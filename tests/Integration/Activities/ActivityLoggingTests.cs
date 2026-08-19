using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Sync;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Activities;

/// <summary>
/// Activity logging end to end, through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// The guardrail and taxonomy rules are unit-tested exhaustively without a database. What only a real stack can
/// prove is the visibility matrix: that a member genuinely sees their unit peers' hours and genuinely cannot see a
/// foreign unit's, decided by a policy in Postgres rather than by a filter in a handler.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ActivityLoggingTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Monday = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Anyone_can_log_their_own_hour()
    {
        await using var factory = await SeededAsync();

        var result = await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 3);

        result.GuardrailStatus.ShouldBe("within");
        result.WeekHours.ShouldBe(3m);
        result.TargetHours.ShouldBe(35m);
    }

    [Fact]
    public async Task The_canonical_taxonomy_is_offered_to_a_department_that_configured_nothing()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var types = await factory.CreateClient().GetFromJsonAsync<List<ActivityTypeOption>>(
            "/api/activities/types",
            TestContext.Current.CancellationToken);

        types.ShouldNotBeNull();
        types.Select(type => type.Code).ShouldContain("project-build");
        types.Single(type => type.Code == "project-build").RequiresProject.ShouldBeTrue();
    }

    [Fact]
    public async Task A_department_subtype_is_offered_alongside_the_canonical_buckets()
    {
        await using var factory = await SeededAsync();

        await ConfigureTaxonomyAsync(
            factory,
            """{"types":[{"code":"payroll-run","parent":"recruitment-admin","labelKey":"hr.payroll"}]}""");

        factory.AsUser(SeedOrganisation.Camille);

        var types = await factory.CreateClient().GetFromJsonAsync<List<ActivityTypeOption>>(
            "/api/activities/types",
            TestContext.Current.CancellationToken);

        // Merged, not replaced: adding a subtype must not cost a department the vocabulary S8 reports in.
        types.ShouldNotBeNull();
        types.Select(type => type.Code).ShouldContain("payroll-run");
        types.Select(type => type.Code).ShouldContain("project-build");
    }

    [Fact]
    public async Task An_hour_can_be_logged_against_a_configured_subtype()
    {
        await using var factory = await SeededAsync();

        await ConfigureTaxonomyAsync(
            factory,
            """{"types":[{"code":"payroll-run","parent":"recruitment-admin","labelKey":"hr.payroll"}]}""");

        var result = await LogAsync(factory, SeedOrganisation.Camille, "payroll-run", hours: 2);

        result.WeekHours.ShouldBe(2m);
    }

    [Fact]
    public async Task Booking_to_a_project_the_person_is_not_on_is_refused()
    {
        await using var factory = await SeededAsync();
        var projectId = await CreateProjectAsync(factory);

        factory.AsUser(SeedOrganisation.Camille);

        // Camille can see the project — she is in the lead department and it is hers to read — but she is not on
        // the team. RLS governs reading; this rule governs booking.
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            Entry("project-build", Monday, Monday.AddHours(3), projectId: projectId),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Booking_to_a_project_the_person_is_on_succeeds()
    {
        await using var factory = await SeededAsync();
        var projectId = await CreateProjectAsync(factory, withMember: SeedOrganisation.Camille);

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            Entry("project-build", Monday, Monday.AddHours(3), projectId: projectId),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Going_over_the_week_warns_but_still_records_the_hours()
    {
        await using var factory = await SeededAsync();

        // 32 hours across four days, then six more.
        for (var day = 0; day < 4; day++)
        {
            await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 8,
                start: Monday.AddDays(day));
        }

        var result = await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 6,
            start: Monday.AddDays(4));

        // Flagged, not dropped. A platform that refuses the 38th hour stops knowing it happened.
        result.GuardrailStatus.ShouldBe("warned");
        result.WeekHours.ShouldBe(38m);
        result.Overtime.ShouldBe(3m);
    }

    [Fact]
    public async Task A_department_that_enforces_the_target_blocks_instead()
    {
        await using var factory = await SeededAsync();

        await ConfigureTaxonomyAsync(factory, "{}", enforce: true);

        for (var day = 0; day < 4; day++)
        {
            await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 8,
                start: Monday.AddDays(day));
        }

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            Entry("quality-of-life", Monday.AddDays(4), Monday.AddDays(4).AddHours(6)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Planned_hours_do_not_count_against_the_guardrail()
    {
        await using var factory = await SeededAsync();

        for (var day = 0; day < 5; day++)
        {
            await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 8,
                start: Monday.AddDays(day), kind: "planned");
        }

        // Forty planned hours, and the week is still clean: a plan is an intention, and blocking someone from
        // sketching one out they will then trim helps nobody.
        var result = await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 2);

        result.GuardrailStatus.ShouldBe("within");
        result.WeekHours.ShouldBe(2m);
    }

    [Fact]
    public async Task An_actual_reconciles_the_plan_it_overlaps()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 4,
            start: Monday, kind: "planned");

        var actual = await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 6,
            start: Monday, kind: "actual");

        // Automatic, because making people find and name their own planned slot means most actuals never get
        // linked and the comparison the module exists for quietly stops working.
        actual.ReconciledPlanId.ShouldNotBeNull();

        var feed = await FeedAsync(factory, SeedOrganisation.Camille, "me");

        feed.Single(entry => entry.Kind == "planned").Reconciled.ShouldBeTrue();
        feed.Single(entry => entry.Kind == "actual").SupersedesEntryId.ShouldNotBeNull();
    }

    [Fact]
    public async Task An_actual_with_no_plan_in_the_window_is_still_allowed()
    {
        await using var factory = await SeededAsync();

        var result = await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 2);

        // Most work is not planned in advance. Refusing the hour would just mean the hour goes unrecorded.
        result.ReconciledPlanId.ShouldBeNull();
    }

    [Fact]
    public async Task A_member_sees_a_unit_peers_hours()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Mehdi, "quality-of-life", hours: 3);

        // Camille and Mehdi share the Infrastructure unit. The matrix says unit peers see each other's activity,
        // and that is what makes the unit board — and the kudos in S9 — possible at all.
        var feed = await FeedAsync(factory, SeedOrganisation.Camille, "unit");

        feed.Select(entry => entry.PersonId).ShouldContain(SeedOrganisation.Mehdi.UserId);
    }

    [Fact]
    public async Task A_member_does_not_see_a_foreign_units_hours()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Sofia, "quality-of-life", hours: 3);

        // Sofia is in Accounting, in another department entirely. Asking for the whole feed does not widen what
        // RLS allows — the scope narrows, it never lifts.
        var feed = await FeedAsync(factory, SeedOrganisation.Camille, "all");

        feed.Select(entry => entry.PersonId).ShouldNotContain(SeedOrganisation.Sofia.UserId);
    }

    [Fact]
    public async Task A_department_head_sees_their_departments_hours_across_units()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 3);

        // Olivier heads IS and sits in the Development unit; Camille is in Infrastructure. A head reads the
        // department, not just their own unit.
        var feed = await FeedAsync(factory, SeedOrganisation.Olivier, "department");

        feed.Select(entry => entry.PersonId).ShouldContain(SeedOrganisation.Camille.UserId);
    }

    [Fact]
    public async Task A_head_of_another_department_sees_nothing_of_it()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 3);

        var feed = await FeedAsync(factory, SeedOrganisation.Laurent, "all");

        feed.Select(entry => entry.PersonId).ShouldNotContain(SeedOrganisation.Camille.UserId);
    }

    [Fact]
    public async Task The_PMO_sees_everyones_hours()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 3);
        await LogAsync(factory, SeedOrganisation.Sofia, "quality-of-life", hours: 3);

        var feed = await FeedAsync(factory, SeedOrganisation.Nadia, "all");

        feed.Select(entry => entry.PersonId).ShouldContain(SeedOrganisation.Camille.UserId);
        feed.Select(entry => entry.PersonId).ShouldContain(SeedOrganisation.Sofia.UserId);
    }

    [Fact]
    public async Task A_member_cannot_write_a_unit_peers_row()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            Entry("quality-of-life", Monday, Monday.AddHours(3)) with { PersonId = SeedOrganisation.Mehdi.UserId },
            TestContext.Current.CancellationToken);

        // Seeing a peer's week is not the right to book hours in their name. The read policy is deliberately much
        // wider than the write policy, and this is the gap between them.
        //
        // 403 rather than 404: Camille can see Mehdi and his hours, so there is nothing left to conceal by
        // pretending he does not exist. Reads are the opposite case and RLS answers those by returning no row.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_head_can_log_on_behalf_of_someone_in_their_department()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            Entry("quality-of-life", Monday, Monday.AddHours(3)) with { PersonId = SeedOrganisation.Camille.UserId },
            TestContext.Current.CancellationToken);

        // Which is the point of the write policy being scoped rather than owner-only: someone has to be able to
        // record the week of a colleague who is on leave.
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Someone_can_only_delete_their_own_entry()
    {
        await using var factory = await SeededAsync();

        var mine = await LogAsync(factory, SeedOrganisation.Mehdi, "quality-of-life", hours: 2);

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient()
            .DeleteAsync($"/api/activities/{mine.Id}", TestContext.Current.CancellationToken);

        // Camille can read Mehdi's entry as a unit peer, so the read succeeds and only the write policy stops her.
        // Postgres filters a refused DELETE silently, so the module detects it from the row count rather than
        // reporting a concurrency conflict it would have to invent.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_weekly_summary_breaks_the_week_down_by_type()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 4, start: Monday);
        await LogAsync(factory, SeedOrganisation.Camille, "recruitment-admin", hours: 2,
            start: Monday.AddDays(1));

        factory.AsUser(SeedOrganisation.Camille);

        var summary = await factory.CreateClient().GetFromJsonAsync<WeeklySummary>(
            "/api/activities/weekly-summary/me?week=2026-W34",
            TestContext.Current.CancellationToken);

        summary!.ActualHours.ShouldBe(6m);
        summary.TargetHours.ShouldBe(35m);
        summary.Status.ShouldBe("within");
        summary.Monday.ShouldBe(new DateOnly(2026, 8, 17));
        summary.ByType.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Asking_for_a_summary_of_an_over_target_week_reports_rather_than_throws()
    {
        await using var factory = await SeededAsync();

        for (var day = 0; day < 5; day++)
        {
            await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", hours: 8,
                start: Monday.AddDays(day));
        }

        factory.AsUser(SeedOrganisation.Camille);

        var summary = await factory.CreateClient().GetFromJsonAsync<WeeklySummary>(
            "/api/activities/weekly-summary/me?week=2026-W34",
            TestContext.Current.CancellationToken);

        summary!.Status.ShouldBe("warned");
        summary.Overtime.ShouldBe(5m);
    }

    [Fact]
    public async Task The_assignable_tasks_dropdown_offers_only_the_callers_own_projects()
    {
        await using var factory = await SeededAsync(seedTasks: true);

        await CreateProjectAsync(factory, withMember: SeedOrganisation.Camille);

        factory.AsUser(SeedOrganisation.Camille);

        var tasks = await factory.CreateClient().GetFromJsonAsync<List<AssignableTask>>(
            "/api/activities/assignable-tasks?source=azure-devops",
            TestContext.Current.CancellationToken);

        tasks.ShouldNotBeEmpty();
        tasks!.ShouldAllBe(task => task.Source == "azure-devops");
        tasks.ShouldAllBe(task => task.SuggestedActivityTypeCode == "project-build");

        // Mehdi is on nothing, so his dropdown is empty even though the same source is configured. Handing one
        // person another's assigned tickets is a disclosure the external system never agreed to.
        factory.AsUser(SeedOrganisation.Mehdi);

        var theirs = await factory.CreateClient().GetFromJsonAsync<List<AssignableTask>>(
            "/api/activities/assignable-tasks",
            TestContext.Current.CancellationToken);

        theirs.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_entry_logged_from_a_pulled_task_keeps_its_reference()
    {
        await using var factory = await SeededAsync(seedTasks: true);
        var projectId = await CreateProjectAsync(factory, withMember: SeedOrganisation.Camille);

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            Entry("project-build", Monday, Monday.AddHours(3), projectId: projectId) with
            {
                Source = "azure-devops",
                ExternalRef = "AB-1000",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var feed = await FeedAsync(factory, SeedOrganisation.Camille, "me");

        var entry = feed.ShouldHaveSingleItem();

        entry.Source.ShouldBe("azure-devops");
        entry.ExternalRef.ShouldBe("AB-1000");
    }

    [Fact]
    public async Task The_dropdown_is_empty_where_no_source_is_configured()
    {
        await using var factory = await SeededAsync();
        await CreateProjectAsync(factory, withMember: SeedOrganisation.Camille);

        factory.AsUser(SeedOrganisation.Camille);

        var tasks = await factory.CreateClient().GetFromJsonAsync<List<AssignableTask>>(
            "/api/activities/assignable-tasks",
            TestContext.Current.CancellationToken);

        // The honest answer for a deployment that has not connected Azure DevOps. Inventing tickets here would be
        // worse than showing none.
        tasks.ShouldBeEmpty();
    }

    // --- Fixture -------------------------------------------------------------------------------------------------

    private sealed record LogRequest(
        Guid? PersonId,
        string ActivityTypeCode,
        Guid? ProjectId,
        Guid? IterationId,
        string Kind,
        string Source,
        string? ExternalRef,
        DateTimeOffset SlotStart,
        DateTimeOffset SlotEnd,
        decimal? Hours,
        string? Note);

    private sealed record LogResponse(
        Guid Id,
        string GuardrailStatus,
        decimal WeekHours,
        decimal TargetHours,
        decimal Overtime,
        Guid? ReconciledPlanId);

    private sealed record CreatedResponse(Guid Id);

    private static LogRequest Entry(
        string type,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? projectId = null,
        string kind = "actual",
        decimal? hours = null) =>
        new(null, type, projectId, null, kind, "manual", null, start, end, hours, null);

    private static async Task<LogResponse> LogAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string type,
        decimal hours,
        DateTimeOffset? start = null,
        string kind = "actual")
    {
        factory.AsUser(person);

        var from = start ?? Monday;

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            Entry(type, from, from.AddHours((double)hours), kind: kind, hours: hours),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<LogResponse>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<ActivityEntryView>> FeedAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string scope)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<List<ActivityEntryView>>(
            $"/api/activities?scope={scope}",
            TestContext.Current.CancellationToken))!;
    }

    /// <summary>Rewrites the IS department's configuration as its head, which is who is allowed to.</summary>
    private static async Task ConfigureTaxonomyAsync(
        CracraApplicationFactory factory,
        string taxonomyJson,
        bool enforce = false)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/departments/{SeedOrganisation.Departments.InformationSystems}/config",
            new
            {
                activityTaxonomyJson = taxonomyJson,
                roleLabelsJson = "{}",
                kudoRulesJson = "{}",
                defaultBoardLayout = "week",
                iterationPresetsJson = """["1w","2w","1m"]""",
                weeklyTargetHours = 35m,
                enforceWeeklyTarget = enforce,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<Guid> CreateProjectAsync(
        CracraApplicationFactory factory,
        UserContext? withMember = null)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var created = await client.PostAsJsonAsync(
            "/api/projects",
            new
            {
                code = "PRJ-ACT",
                name = "Migration M365",
                classification = "build",
                costAmount = 0m,
                costCurrency = "EUR",
                leadDepartmentId = SeedOrganisation.Departments.InformationSystems,
                contributingDepartmentIds = Array.Empty<Guid>(),
            },
            ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var projectId = (await created.Content.ReadFromJsonAsync<CreatedResponse>(ct))!.Id;

        if (withMember is { } member)
        {
            var added = await client.PostAsJsonAsync(
                $"/api/projects/{projectId}/members",
                new
                {
                    personId = member.UserId,
                    departmentId = SeedOrganisation.Departments.InformationSystems,
                    functionalRoleId = Guid.Parse("f0000000-0000-0000-0000-000000000001"),
                },
                ct);

            added.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        return projectId;
    }

    private async Task<CracraApplicationFactory> SeededAsync(bool seedTasks = false)
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();

        keycloak.Users.Add(FakeKeycloakDirectory.User(
            SeedOrganisation.Laurent, "Laurent", "Bouchard", "expert-comptable"));

        var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
            Settings = seedTasks
                ? new Dictionary<string, string?>
                {
                    ["Cracra:Activities:AssignableTasks:SeedSampleTasks"] = "true",
                }
                : null,
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IKeycloakDirectoryClient>();
                services.AddSingleton<IKeycloakDirectoryClient>(keycloak);
            },
        };

        await ResetAsync(factory);

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        return factory;
    }

    /// <summary>
    /// Clears everything each scenario counts. The Postgres container is shared for speed, so a row left behind
    /// would be picked up by the feed assertions — several of which assert on emptiness.
    /// </summary>
    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;

        var activities = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Activities.Infrastructure.ActivitiesDbContext>();

        await activities.Entries.ExecuteDeleteAsync(ct);

        var projects = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Projects.Infrastructure.ProjectsDbContext>();

        await projects.ProjectMembers.ExecuteDeleteAsync(ct);
        await projects.ProjectDepartments.ExecuteDeleteAsync(ct);
        await projects.Projects.ExecuteDeleteAsync(ct);

        var access = scope.ServiceProvider.GetRequiredService<Cracra.Modules.Access.Data.AccessDbContext>();

        await access.ProjectMemberships.ExecuteDeleteAsync(ct);

        var directory = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Directory.Data.DirectoryDbContext>();

        await directory.PersonFunctionalRoles.ExecuteDeleteAsync(ct);
        await directory.PersonUnits.ExecuteDeleteAsync(ct);
        await directory.People.ExecuteDeleteAsync(ct);

        // Reset rather than delete: sync creates a config only when it creates the department, and the departments
        // survive this reset. Deleting the rows would leave the next scenario with no config at all — which the
        // module tolerates by falling back to defaults, but which would make every settings write 404.
        await directory.DepartmentConfigs.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(config => config.ActivityTaxonomyJson, "{}")
                .SetProperty(config => config.WeeklyTargetHours, 35m)
                .SetProperty(config => config.EnforceWeeklyTarget, false),
            ct);
    }
}
