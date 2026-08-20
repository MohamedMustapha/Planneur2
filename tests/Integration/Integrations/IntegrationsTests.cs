using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Integrations.Domain;
using Cracra.Modules.Integrations.Providers;
using Cracra.Modules.Integrations.Services;
using Cracra.Modules.Integrations.Sync;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cracra.Tests.Integration.Integrations;

/// <summary>
/// S10 through HTTP, with real RLS: configuring a connection, pulling it, and who sees what came back.
/// </summary>
/// <remarks>
/// This is a 2-layer module, so per conventions.md §6 there is little worth unit-testing beyond the mapping and
/// the guard, and everything else is covered here end to end. The assertions worth having are the ones that would
/// catch the module's central claims being false: that a pull is idempotent, that an item leaving the source is
/// closed rather than lost, that the mirror is only readable by the people the matrix names, and that nobody but
/// the synchronizer can write it.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class IntegrationsTests(PostgresFixture postgres)
{
    private static readonly Guid DevRole = Guid.Parse("f0000000-0000-0000-0000-000000000001");

    // --- Configuration -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_department_head_configures_a_connection()
    {
        await using var factory = await SeededAsync();

        var connection = await CreateConnectionAsync(factory, SeedOrganisation.Olivier);

        connection.Provider.ShouldBe(ExternalProviders.AzureDevOps);
        connection.LastSyncStatus.ShouldBe(SyncStatuses.Never);

        // The name of a secret, not a secret. There is no shape of this response that could leak a token, because
        // the token is not in the module's database to begin with.
        connection.AuthRef.ShouldBe("is-devops");
    }

    [Fact]
    public async Task A_member_cannot_reach_the_connection_list_at_all()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().GetAsync(
            "/api/integrations/connections",
            TestContext.Current.CancellationToken);

        // The policy is the door — a member is not a head, and never gets as far as RLS.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_head_of_another_department_sees_none_of_it()
    {
        await using var factory = await SeededAsync();

        var connection = await CreateConnectionAsync(factory, SeedOrganisation.Olivier);

        factory.AsUser(SeedOrganisation.Laurent);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        (await client.GetFromJsonAsync<List<ConnectionView>>("/api/integrations/connections", ct))
            .ShouldBeEmpty();

        // And asking for it by id is a 404 rather than a 403: telling a Finance head that a connection id exists
        // but is not theirs is exactly the disclosure the policy exists to prevent.
        var byId = await client.GetAsync($"/api/integrations/connections/{connection.Id}", ct);

        byId.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_pmo_sees_every_department_connection()
    {
        await using var factory = await SeededAsync();

        await CreateConnectionAsync(factory, SeedOrganisation.Olivier);
        await CreateConnectionAsync(
            factory,
            SeedOrganisation.Laurent,
            departmentId: SeedOrganisation.Departments.Finance,
            name: "DAF — ServiceNow",
            provider: ExternalProviders.ServiceNow);

        factory.AsUser(SeedOrganisation.Nadia);

        var all = await factory.CreateClient().GetFromJsonAsync<List<ConnectionView>>(
            "/api/integrations/connections",
            TestContext.Current.CancellationToken);

        all!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_provider_the_platform_has_no_adapter_for_is_refused()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/integrations/connections",
            ConnectionPayload(provider: "jira"),
            TestContext.Current.CancellationToken);

        // 422 at configuration time. Accepting it would give an administrator a connection that fails forever
        // with "no provider registered" — true, and discovered at the worst possible moment.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_host_outside_the_allow_list_is_refused_when_the_connection_is_written()
    {
        await using var factory = await SeededAsync(allowedHost: "devops.intranet");
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/integrations/connections",
            ConnectionPayload(baseUrl: "https://elsewhere.example"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_poll_interval_nobody_meant_is_refused()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        // A second is somebody misunderstanding the units, and would be a denial of service against a system
        // that belongs to another team.
        var tooFast = await client.PostAsJsonAsync(
            "/api/integrations/connections",
            ConnectionPayload(pollInterval: "00:00:01"),
            ct);

        tooFast.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Zero is not the same mistake: it is a connection somebody wants to pull by hand, and it is allowed.
        var onDemand = await client.PostAsJsonAsync(
            "/api/integrations/connections",
            ConnectionPayload(pollInterval: "00:00:00"),
            ct);

        onDemand.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_mapping_must_target_what_its_kind_says_it_targets()
    {
        await using var factory = await SeededAsync();

        var connection = await CreateConnectionAsync(factory, SeedOrganisation.Olivier);

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/integrations/connections/{connection.Id}/mappings",
            new
            {
                kind = MappingKinds.AreaPath,
                externalValue = @"CRACRA\Platform",
                unitId = SeedOrganisation.Units.Infrastructure,
            },
            TestContext.Current.CancellationToken);

        // 422 naming the problem, rather than a 500 carrying a check-constraint name. Both guards exist: this one
        // explains, the constraint holds.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // --- Pulling -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pull_mirrors_what_the_source_returned_and_maps_it_to_a_local_project()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, SeedOrganisation.Camille);
        var connection = await ConfiguredDevOpsAsync(factory, projectId);

        DevOps(factory).Items.AddRange(
        [
            FakeWorkItemProvider.DevOpsTask("4301", "Migrer le socle", "camille.villeneuve"),
            FakeWorkItemProvider.DevOpsTask("4304", "Documenter le pipeline", null),
        ]);

        var result = await SyncAsync(factory, connection.Id);

        result.Created.ShouldBe(2);
        result.Updated.ShouldBe(0);

        var mirrored = await WorkItemsAsync(factory, SeedOrganisation.Camille);

        mirrored.Count.ShouldBe(2);
        mirrored.ShouldAllBe(item => item.ProjectId == projectId);

        // The uid the source reported, resolved against the directory under the system context — the one context
        // that can see every department at once, which is why the resolution happens at sync time and not on read.
        mirrored.ShouldContain(item => item.AssignedPersonId == SeedOrganisation.Camille.UserId);
        mirrored.ShouldContain(item => item.AssignedPersonId == null);
    }

    [Fact]
    public async Task Pulling_twice_changes_nothing_the_first_pull_did_not()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, SeedOrganisation.Camille);
        var connection = await ConfiguredDevOpsAsync(factory, projectId);

        DevOps(factory).Items.Add(FakeWorkItemProvider.DevOpsTask("4301", "Migrer le socle", "camille.villeneuve"));

        (await SyncAsync(factory, connection.Id)).Created.ShouldBe(1);

        var second = await SyncAsync(factory, connection.Id);

        // Idempotent by construction: the upsert is keyed on (provider, external_id). It has to be, because a
        // schedule, a startup pass and a button all point at the same reconciliation.
        second.Created.ShouldBe(0);
        second.Updated.ShouldBe(1);

        (await WorkItemsAsync(factory, SeedOrganisation.Camille)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_item_the_source_stopped_returning_is_closed_rather_than_deleted()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, SeedOrganisation.Camille);
        var connection = await ConfiguredDevOpsAsync(factory, projectId);

        var provider = DevOps(factory);

        provider.Items.Add(FakeWorkItemProvider.DevOpsTask("4301", "Migrer le socle", "camille.villeneuve"));

        await SyncAsync(factory, connection.Id);

        provider.Items.Clear();

        (await SyncAsync(factory, connection.Id)).Closed.ShouldBe(1);

        // Gone from the feeds...
        (await WorkItemsAsync(factory, SeedOrganisation.Camille)).ShouldBeEmpty();

        // ...but still on record. An activity logged last month references it by external_ref, and a report that
        // cannot name the ticket somebody worked on is worse than one that names a closed ticket.
        var closed = await WorkItemsAsync(factory, SeedOrganisation.Camille, "&includeClosed=true");

        closed.ShouldHaveSingleItem().MirrorState.ShouldBe(MirrorStates.Closed);
    }

    [Fact]
    public async Task An_item_the_source_starts_returning_again_reopens()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, SeedOrganisation.Camille);
        var connection = await ConfiguredDevOpsAsync(factory, projectId);

        var provider = DevOps(factory);
        var task = FakeWorkItemProvider.DevOpsTask("4301", "Migrer le socle", "camille.villeneuve");

        provider.Items.Add(task);
        await SyncAsync(factory, connection.Id);

        provider.Items.Clear();
        await SyncAsync(factory, connection.Id);

        provider.Items.Add(task);
        await SyncAsync(factory, connection.Id);

        // An incident reopened at the source is live work again. Leaving it closed here would hide it from the
        // very feed that exists to surface it.
        (await WorkItemsAsync(factory, SeedOrganisation.Camille)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_failed_pull_lands_on_the_connection_and_leaves_the_mirror_alone()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, SeedOrganisation.Camille);
        var connection = await ConfiguredDevOpsAsync(factory, projectId);

        var provider = DevOps(factory);

        provider.Items.Add(FakeWorkItemProvider.DevOpsTask("4301", "Migrer le socle", "camille.villeneuve"));
        await SyncAsync(factory, connection.Id);

        provider.Fails = new HttpRequestException("The remote host refused the connection.");

        await SyncAsync(factory, connection.Id);

        var after = await GetConnectionAsync(factory, SeedOrganisation.Olivier, connection.Id);

        after.LastSyncStatus.ShouldBe(SyncStatuses.Failed);
        after.LastSyncError.ShouldNotBeNull().ShouldContain("refused");

        // The mirror keeps what it had. Treating "we could not reach DevOps" as "DevOps has nothing" would close
        // every open item in the department the first time a network blipped.
        (await WorkItemsAsync(factory, SeedOrganisation.Camille)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_connection_whose_secret_is_missing_fails_visibly_rather_than_quietly()
    {
        await using var factory = await SeededAsync();

        var connection = await CreateConnectionAsync(factory, SeedOrganisation.Olivier, authRef: "not-provisioned");

        await SyncAsync(factory, connection.Id);

        var after = await GetConnectionAsync(factory, SeedOrganisation.Olivier, connection.Id);

        // Skipping it silently would be indistinguishable from an external system with nothing to report, and
        // the administrator who has not finished the vault entry would never find out.
        after.LastSyncStatus.ShouldBe(SyncStatuses.Failed);
        after.LastSyncError.ShouldNotBeNull().ShouldContain("not-provisioned");
    }

    [Fact]
    public async Task Only_connections_whose_own_interval_has_come_due_are_pulled()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, SeedOrganisation.Camille);
        var connection = await ConfiguredDevOpsAsync(factory, projectId);

        DevOps(factory).Items.Add(FakeWorkItemProvider.DevOpsTask("4301", "Migrer le socle", "camille.villeneuve"));

        var synchronizer = factory.Services.GetRequiredService<IExternalWorkItemSynchronizer>();
        var ct = TestContext.Current.CancellationToken;

        (await synchronizer.SynchronizeDueAsync(ct)).ShouldHaveSingleItem();

        // The connection's poll interval is fifteen minutes and it has just been pulled, so the next scheduler
        // tick finds nothing to do. Two intervals, two meanings: how often the platform asks, and how often each
        // connection is actually pulled.
        (await synchronizer.SynchronizeDueAsync(ct)).ShouldBeEmpty();

        DevOps(factory).Calls.ShouldBe(1);

        // On demand ignores the schedule entirely, which is what the button is for.
        await synchronizer.SynchronizeAsync(connection.Id, ct);

        DevOps(factory).Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Asking_for_a_pull_over_http_is_accepted_rather_than_awaited()
    {
        await using var factory = await SeededAsync();

        var connection = await CreateConnectionAsync(factory, SeedOrganisation.Olivier);

        factory.AsUser(SeedOrganisation.Olivier);

        // An empty JSON body rather than none at all: the route carries the id, and FastEndpoints binds the
        // request from the body, so a bodyless POST is answered 415 before the endpoint ever runs.
        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/integrations/{connection.Id}/sync",
            new { },
            TestContext.Current.CancellationToken);

        // 202 and a status resource: a pull is a round trip to somebody else's server, and holding an HTTP
        // request open for it makes our responsiveness a function of theirs.
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_head_cannot_pull_another_departments_connection()
    {
        await using var factory = await SeededAsync();

        var connection = await CreateConnectionAsync(factory, SeedOrganisation.Olivier);

        factory.AsUser(SeedOrganisation.Laurent);

        // An empty JSON body rather than none at all: the route carries the id, and FastEndpoints binds the
        // request from the body, so a bodyless POST is answered 415 before the endpoint ever runs.
        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/integrations/{connection.Id}/sync",
            new { },
            TestContext.Current.CancellationToken);

        // Checked before accepting. Otherwise a Finance head would learn nothing from the response — and the
        // pull would run anyway, as the system, against a connection they may not administer.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_a_connection_removes_what_it_mirrored()
    {
        await using var factory = await SeededAsync();

        var connection = await ConfiguredServiceNowAsync(factory);

        ServiceNow(factory).Items.Add(FakeWorkItemProvider.Incident("0010023", "Poste bloqué"));

        await SyncAsync(factory, connection.Id);

        (await WorkItemsAsync(factory, SeedOrganisation.Camille)).ShouldHaveSingleItem();

        factory.AsUser(SeedOrganisation.Olivier);

        var deleted = await factory.CreateClient().DeleteAsync(
            $"/api/integrations/connections/{connection.Id}",
            TestContext.Current.CancellationToken);

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The rows are a cache of somebody else's data and mean nothing without the connection that explains
        // where they came from. Left behind they would also be invisible and still indexed, so reconfiguring the
        // same integration would fail on insert against rows nobody could see.
        (await WorkItemsAsync(factory, SeedOrganisation.Camille, "&includeClosed=true")).ShouldBeEmpty();
    }

    // --- The matrix --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_developer_sees_their_own_devops_items_and_their_projects()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, SeedOrganisation.Camille);
        var connection = await ConfiguredDevOpsAsync(factory, projectId);

        DevOps(factory).Items.Add(FakeWorkItemProvider.DevOpsTask("4301", "Migrer le socle", "camille.villeneuve"));

        await SyncAsync(factory, connection.Id);

        (await WorkItemsAsync(factory, SeedOrganisation.Camille)).ShouldHaveSingleItem();

        // Mehdi is in Camille's unit and on none of her projects. A sprint task is project work, so unit
        // membership grants nothing here — which is the difference between this predicate and the activity one,
        // and the reason they are not the same function.
        (await WorkItemsAsync(factory, SeedOrganisation.Mehdi)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_item_assigned_to_somebody_follows_them_even_off_their_projects()
    {
        await using var factory = await SeededAsync();

        // No mapping at all, so the item resolves to no project: the only thing that can grant sight of it is
        // the assignment itself.
        var connection = await CreateConnectionAsync(factory, SeedOrganisation.Olivier);

        DevOps(factory).Items.Add(FakeWorkItemProvider.DevOpsTask("4310", "Corriger un bug", "mehdi.sadaoui"));

        await SyncAsync(factory, connection.Id);

        (await WorkItemsAsync(factory, SeedOrganisation.Mehdi)).ShouldHaveSingleItem();
        (await WorkItemsAsync(factory, SeedOrganisation.Camille)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_helpdesk_agent_sees_their_units_queue()
    {
        await using var factory = await SeededAsync();

        var connection = await ConfiguredServiceNowAsync(factory);

        ServiceNow(factory).Items.AddRange(
        [
            FakeWorkItemProvider.Incident("0010023", "Poste bloqué au démarrage"),
            FakeWorkItemProvider.Incident("0010024", "Imprimante hors service"),
        ]);

        await SyncAsync(factory, connection.Id);

        // Camille is in Infrastructure, which the assignment group maps to. Everybody in the unit works the
        // queue: the 6a pool is a shared list by design, and a per-person rule would empty it.
        (await WorkItemsAsync(factory, SeedOrganisation.Camille)).Count.ShouldBe(2);
        (await WorkItemsAsync(factory, SeedOrganisation.Mehdi)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Another_departments_people_see_none_of_it()
    {
        await using var factory = await SeededAsync();

        var connection = await ConfiguredServiceNowAsync(factory);

        ServiceNow(factory).Items.Add(FakeWorkItemProvider.Incident("0010023", "Poste bloqué"));

        await SyncAsync(factory, connection.Id);

        (await WorkItemsAsync(factory, SeedOrganisation.Sofia)).ShouldBeEmpty();
        (await WorkItemsAsync(factory, SeedOrganisation.Laurent)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_department_head_sees_their_departments_items_and_the_pmo_sees_everything()
    {
        await using var factory = await SeededAsync();

        var connection = await ConfiguredServiceNowAsync(factory);

        ServiceNow(factory).Items.Add(FakeWorkItemProvider.Incident("0010023", "Poste bloqué"));

        await SyncAsync(factory, connection.Id);

        // Olivier heads IS but sits in Development, not in the unit the queue maps to. He reaches it through the
        // department column the row carries — which is why sync stamps it.
        (await WorkItemsAsync(factory, SeedOrganisation.Olivier)).ShouldHaveSingleItem();
        (await WorkItemsAsync(factory, SeedOrganisation.Nadia)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Nobody_but_the_synchronizer_can_write_the_mirror()
    {
        await using var factory = await SeededAsync();

        var connection = await ConfiguredServiceNowAsync(factory);

        ServiceNow(factory).Items.Add(FakeWorkItemProvider.Incident("0010023", "Poste bloqué"));

        await SyncAsync(factory, connection.Id);

        await using var scope = factory.Services.CreateAsyncScope();

        // As a real person — a department head, the widest human role over these rows — rather than as the
        // system. The module exposes no endpoint that writes a work item, but "there is no endpoint today" is a
        // fact about this deploy; this is the fact about the database.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = SeedOrganisation.Olivier;

        var context = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Integrations.Data.IntegrationsDbContext>();

        var ct = TestContext.Current.CancellationToken;

        var updated = await context.WorkItems.ExecuteUpdateAsync(
            setters => setters.SetProperty(item => item.Title, "Retitled by hand"),
            ct);

        // Postgres filters an UPDATE its policy rejects rather than raising, so the refusal shows up as a row
        // count of zero. A locally edited mirror row would be a lie about somebody else's data that the next
        // pull erases anyway.
        updated.ShouldBe(0);
    }

    [Fact]
    public async Task A_connection_that_cannot_be_pulled_shows_up_as_degraded_on_the_probe()
    {
        await using var factory = await SeededAsync();

        var health = new IntegrationsHealthCheck(factory.Services.GetRequiredService<IServiceScopeFactory>());
        var ct = TestContext.Current.CancellationToken;
        var context = new HealthCheckContext();

        // Nothing configured is the ordinary state of a deployment that does not use the integrations. Reporting
        // degraded for it would train an operator to ignore this check.
        (await health.CheckHealthAsync(context, ct)).Status.ShouldBe(HealthStatus.Healthy);

        var connection = await CreateConnectionAsync(factory, SeedOrganisation.Olivier, authRef: "not-provisioned");

        await SyncAsync(factory, connection.Id);

        var degraded = await health.CheckHealthAsync(context, ct);

        // Degraded, never unhealthy: the platform works perfectly well with a stale mirror, and it must not
        // refuse traffic because a ticketing system is having an afternoon.
        degraded.Status.ShouldBe(HealthStatus.Degraded);
        degraded.Description.ShouldNotBeNull().ShouldContain("SI — Azure DevOps");
    }

    // --- What the mirror feeds ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_pulled_sprint_task_reaches_the_S5_dropdown()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, SeedOrganisation.Camille);
        var connection = await ConfiguredDevOpsAsync(factory, projectId);

        DevOps(factory).Items.Add(FakeWorkItemProvider.DevOpsTask("4301", "Migrer le socle", "camille.villeneuve"));

        await SyncAsync(factory, connection.Id);

        factory.AsUser(SeedOrganisation.Camille);

        var tasks = await factory.CreateClient()
            .GetFromJsonAsync<List<Cracra.Modules.Activities.Contracts.AssignableTask>>(
                "/api/activities/assignable-tasks?source=azure-devops",
                TestContext.Current.CancellationToken);

        var task = tasks.ShouldHaveSingleItem();

        // The reference the source shows its own users, and the project the mapping resolved — which is what
        // pre-fills the entry. The suggested type is the taxonomy's own split: DevOps work is BUILD.
        task.ExternalRef.ShouldBe("AB#4301");
        task.ProjectId.ShouldBe(projectId);
        task.SuggestedActivityTypeCode.ShouldBe("project-build");
    }

    [Fact]
    public async Task Pulled_tickets_fill_the_S6a_work_order_pool()
    {
        await using var factory = await SeededAsync();

        var connection = await ConfiguredServiceNowAsync(factory);

        ServiceNow(factory).Items.AddRange(
        [
            FakeWorkItemProvider.Incident("0010023", "Poste bloqué au démarrage"),
            FakeWorkItemProvider.Incident("0010024", "Imprimante hors service"),

            // Somebody at the source has already picked this one up. It is not free work, so it must not appear
            // in a pool a lead drags from.
            FakeWorkItemProvider.Incident("0010025", "Accès VPN", assignedTo: "thomas.berthier"),
        ]);

        await SyncAsync(factory, connection.Id);

        factory.AsUser(SeedOrganisation.Thomas);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var refreshed = await client.PostAsJsonAsync(
            "/api/scheduling/work-orders/pool/refresh",
            new { unitId = SeedOrganisation.Units.Infrastructure },
            ct);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var pool = await client.GetFromJsonAsync<List<Cracra.Modules.Scheduling.Contracts.WorkOrderView>>(
            $"/api/scheduling/work-orders/pool?unitId={SeedOrganisation.Units.Infrastructure}",
            ct);

        pool!.Count.ShouldBe(2);
        pool.ShouldAllBe(order => order.State == "unassigned");
        pool.ShouldContain(order => order.Reference == "INC0010023");
    }

    [Fact]
    public async Task A_ticket_two_connections_both_mirror_reaches_the_pool_once()
    {
        await using var factory = await SeededAsync();

        // The configuration mistake this guards against: somebody points a second connection at a queue that is
        // already covered. Both mirror the ticket — an external id is only unique within its own connection —
        // and the pool must still show one card, or a lead pressing refresh gets a 500 from the unique index the
        // work order carries.
        foreach (var _ in new[] { 1, 2 })
        {
            var connection = await ConfiguredServiceNowAsync(factory);

            ServiceNow(factory).Items.Clear();
            ServiceNow(factory).Items.Add(FakeWorkItemProvider.Incident("0010023", "Poste bloqué"));

            await SyncAsync(factory, connection.Id);
        }

        factory.AsUser(SeedOrganisation.Thomas);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        // Two mirror rows, one ticket.
        (await WorkItemsAsync(factory, SeedOrganisation.Thomas)).Count.ShouldBe(2);

        factory.AsUser(SeedOrganisation.Thomas);

        var refreshed = await client.PostAsJsonAsync(
            "/api/scheduling/work-orders/pool/refresh",
            new { unitId = SeedOrganisation.Units.Infrastructure },
            ct);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var pool = await client.GetFromJsonAsync<List<Cracra.Modules.Scheduling.Contracts.WorkOrderView>>(
            $"/api/scheduling/work-orders/pool?unitId={SeedOrganisation.Units.Infrastructure}",
            ct);

        pool.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_dropdown_and_the_pool_are_empty_where_nothing_is_connected()
    {
        await using var factory = await SeededAsync();

        await CreateProjectAsync(factory, SeedOrganisation.Camille);

        factory.AsUser(SeedOrganisation.Camille);

        var tasks = await factory.CreateClient()
            .GetFromJsonAsync<List<Cracra.Modules.Activities.Contracts.AssignableTask>>(
                "/api/activities/assignable-tasks",
                TestContext.Current.CancellationToken);

        // The honest answer for a deployment that has connected nothing, and now it is honest for a better
        // reason than before: the mirror is a table, and the table is empty.
        tasks.ShouldBeEmpty();
    }

    // --- Fixture -----------------------------------------------------------------------------------------------

    private static object ConnectionPayload(
        Guid? departmentId = null,
        string provider = ExternalProviders.AzureDevOps,
        string name = "SI — Azure DevOps",
        string baseUrl = "https://devops.intranet",
        string authRef = "is-devops",
        string projectOrQueue = "CRACRA",
        string? currentSprint = "Sprint 42",
        string pollInterval = "00:15:00") => new
        {
            departmentId = departmentId ?? SeedOrganisation.Departments.InformationSystems,
            provider,
            name,
            baseUrl,
            authRef,
            projectOrQueue,
            currentSprint,
            pollInterval,
            active = true,
        };

    private static async Task<ConnectionView> CreateConnectionAsync(
        CracraApplicationFactory factory,
        UserContext person,
        Guid? departmentId = null,
        string provider = ExternalProviders.AzureDevOps,
        string name = "SI — Azure DevOps",
        string authRef = "is-devops",
        string projectOrQueue = "CRACRA")
    {
        factory.AsUser(person);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/integrations/connections",
            ConnectionPayload(departmentId, provider, name, authRef: authRef, projectOrQueue: projectOrQueue),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<ConnectionView>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>A DevOps connection whose area path maps to a local project.</summary>
    private static async Task<ConnectionView> ConfiguredDevOpsAsync(
        CracraApplicationFactory factory,
        Guid projectId)
    {
        var connection = await CreateConnectionAsync(factory, SeedOrganisation.Olivier);

        await AddMappingAsync(factory, connection.Id, new
        {
            kind = MappingKinds.AreaPath,
            externalValue = @"CRACRA\Platform",
            projectId,
        });

        return connection;
    }

    /// <summary>A ServiceNow connection whose assignment group maps to the Infrastructure unit.</summary>
    private static async Task<ConnectionView> ConfiguredServiceNowAsync(CracraApplicationFactory factory)
    {
        var connection = await CreateConnectionAsync(
            factory,
            SeedOrganisation.Olivier,
            provider: ExternalProviders.ServiceNow,
            name: "SI — ServiceNow",
            authRef: "is-servicenow",
            projectOrQueue: "Helpdesk N1");

        await AddMappingAsync(factory, connection.Id, new
        {
            kind = MappingKinds.AssignmentGroup,
            externalValue = "Helpdesk N1",
            unitId = SeedOrganisation.Units.Infrastructure,
        });

        return connection;
    }

    private static async Task AddMappingAsync(CracraApplicationFactory factory, Guid connectionId, object payload)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/integrations/connections/{connectionId}/mappings",
            payload,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<ConnectionView> GetConnectionAsync(
        CracraApplicationFactory factory,
        UserContext person,
        Guid connectionId)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<ConnectionView>(
            $"/api/integrations/connections/{connectionId}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<ExternalWorkItemView>> WorkItemsAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string query = "")
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<List<ExternalWorkItemView>>(
            $"/api/integrations/work-items?limit=100{query}",
            TestContext.Current.CancellationToken))!;
    }

    /// <summary>Drives the synchronizer directly, rather than through the 202 and its background drain.</summary>
    /// <remarks>
    /// The endpoint's own behaviour — accept, authorize, enqueue — is asserted separately. Here what is under
    /// test is the reconciliation, and awaiting it is the difference between an assertion and a race.
    /// </remarks>
    private static async Task<SyncResult> SyncAsync(CracraApplicationFactory factory, Guid connectionId) =>
        await factory.Services.GetRequiredService<IExternalWorkItemSynchronizer>()
            .SynchronizeAsync(connectionId, TestContext.Current.CancellationToken);

    private static FakeWorkItemProvider DevOps(CracraApplicationFactory factory) =>
        Provider(factory, ExternalProviders.AzureDevOps);

    private static FakeWorkItemProvider ServiceNow(CracraApplicationFactory factory) =>
        Provider(factory, ExternalProviders.ServiceNow);

    private static FakeWorkItemProvider Provider(CracraApplicationFactory factory, string provider) =>
        (FakeWorkItemProvider)factory.Services.GetServices<IExternalWorkItemProvider>()
            .Single(candidate => candidate.Provider == provider);

    private static async Task<Guid> CreateProjectAsync(CracraApplicationFactory factory, UserContext member)
    {
        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var created = await client.PostAsJsonAsync(
            "/api/projects",
            new
            {
                code = "PRJ-INT",
                name = "Socle applicatif",
                classification = "build",
                costAmount = 0m,
                costCurrency = "EUR",
                leadDepartmentId = SeedOrganisation.Departments.InformationSystems,
                contributingDepartmentIds = Array.Empty<Guid>(),
            },
            ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var projectId = (await created.Content.ReadFromJsonAsync<CreatedResponse>(ct))!.Id;

        var added = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/members",
            new
            {
                personId = member.UserId,
                departmentId = SeedOrganisation.Departments.InformationSystems,
                functionalRoleId = DevRole,
            },
            ct);

        added.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        return projectId;
    }

    private sealed record CreatedResponse(Guid Id);

    private async Task<CracraApplicationFactory> SeededAsync(string? allowedHost = null)
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();

        keycloak.Users.Add(FakeKeycloakDirectory.User(
            SeedOrganisation.Laurent, "Laurent", "Bouchard", "expert-comptable"));

        var settings = new Dictionary<string, string?>
        {
            // The secret store, as a deployment provides it: environment variables named for the auth_ref a
            // connection carries. Nothing here reaches the database, which is the point.
            ["Cracra:Integrations:Credentials:is-devops"] = "pat:test-token",
            ["Cracra:Integrations:Credentials:is-servicenow"] = "basic:cracra:test-password",
        };

        if (allowedHost is not null)
        {
            settings["Cracra:Integrations:AllowedHosts:0"] = allowedHost;
        }

        var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
            Settings = settings,
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IKeycloakDirectoryClient>();
                services.AddSingleton<IKeycloakDirectoryClient>(keycloak);

                // Both real adapters go, together. Leaving one would mean a test whose ServiceNow connection
                // quietly tried to reach a host that does not exist, and the failure would be a timeout rather
                // than a message.
                services.RemoveAll<IExternalWorkItemProvider>();
                services.AddSingleton<IExternalWorkItemProvider>(
                    new FakeWorkItemProvider(ExternalProviders.AzureDevOps));
                services.AddSingleton<IExternalWorkItemProvider>(
                    new FakeWorkItemProvider(ExternalProviders.ServiceNow));
            },
        };

        await ResetAsync(factory);

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        return factory;
    }

    /// <summary>
    /// Clears everything these scenarios count.
    /// </summary>
    /// <remarks>
    /// The Postgres container is shared for speed, so a connection or a mirror row left behind by another class
    /// would be picked up here — and several of these assertions are about emptiness, which is exactly the shape
    /// of assertion that leftover rows break.
    /// </remarks>
    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;

        var integrations = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Integrations.Data.IntegrationsDbContext>();

        await integrations.WorkItems.ExecuteDeleteAsync(ct);
        await integrations.Mappings.ExecuteDeleteAsync(ct);
        await integrations.Connections.ExecuteDeleteAsync(ct);

        var scheduling = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Scheduling.Infrastructure.SchedulingDbContext>();

        await scheduling.WorkOrders.ExecuteDeleteAsync(ct);

        var projects = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Projects.Infrastructure.ProjectsDbContext>();

        await projects.Projects.ExecuteDeleteAsync(ct);
    }
}
