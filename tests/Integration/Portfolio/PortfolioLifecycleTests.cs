using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Portfolio;

/// <summary>
/// The portfolio lifecycle end to end, through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// The guards are unit-tested exhaustively without a database; what only a real stack can prove is that the
/// transitions survive the transaction boundary, that the audit trail is genuinely append-only under FORCE ROW
/// LEVEL SECURITY, and that the board shows each role what the matrix says it should.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PortfolioLifecycleTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_head_registers_a_candidate_and_it_lands_in_the_considered_lane()
    {
        await using var factory = await SeededAsync();
        var itemId = await ConsiderAsync(factory);

        var board = await BoardAsync(factory, SeedOrganisation.Olivier);

        Lane(board, "considered").Items.Select(item => item.Id).ShouldContain(itemId);
    }

    [Fact]
    public async Task Committing_provisions_a_project_and_moves_the_item()
    {
        await using var factory = await SeededAsync();
        var itemId = await ConsiderAsync(factory);

        var projectId = await CommitAsync(factory, itemId);

        projectId.ShouldNotBe(Guid.Empty);

        var detail = await DetailAsync(factory, SeedOrganisation.Olivier, itemId);

        detail.Item.State.ShouldBe("committed");
        detail.Item.ProjectId.ShouldBe(projectId);

        // The provisioned project's cost and classification show on the card, which is why Portfolio asks Projects
        // for them rather than storing its own copy.
        detail.Item.Classification.ShouldBe("build");
    }

    [Fact]
    public async Task The_provisioned_project_is_readable_through_the_projects_api()
    {
        await using var factory = await SeededAsync();
        var itemId = await ConsiderAsync(factory);
        var projectId = await CommitAsync(factory, itemId);

        factory.AsUser(SeedOrganisation.Olivier);

        // Provisioning runs on the committer's own connection, so the project belongs to them and behaves exactly
        // like one created through the Projects API — including who may edit it afterwards.
        var response = await factory.CreateClient()
            .GetAsync($"/api/projects/{projectId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Activating_without_a_plan_is_refused()
    {
        await using var factory = await SeededAsync();
        var itemId = await ConsiderAsync(factory);
        await CommitAsync(factory, itemId);

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsync(
            $"/api/portfolio/{itemId}/activate",
            null,
            TestContext.Current.CancellationToken);

        // 422: the request was well formed and the caller was allowed; the domain said no.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Activating_with_a_plan_and_a_team_starts_the_first_iteration()
    {
        await using var factory = await SeededAsync();
        var itemId = await ActiveItemAsync(factory);

        var detail = await DetailAsync(factory, SeedOrganisation.Olivier, itemId);

        detail.Item.State.ShouldBe("active");
        detail.Item.CurrentIteration.ShouldNotBeNull();
        detail.Item.CurrentIteration.State.ShouldBe("active");
    }

    [Fact]
    public async Task Archiving_cancels_the_open_iterations_in_the_same_act()
    {
        await using var factory = await SeededAsync();
        var itemId = await ActiveItemAsync(factory);

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/portfolio/{itemId}/archive",
            new { reason = "Superseded by the group platform." },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var detail = await DetailAsync(factory, SeedOrganisation.Olivier, itemId);

        detail.Item.State.ShouldBe("dephase");
        detail.Iterations.ShouldAllBe(iteration => iteration.State == "cancelled");
    }

    [Fact]
    public async Task Every_transition_is_recorded_with_who_and_why()
    {
        await using var factory = await SeededAsync();
        var itemId = await ActiveItemAsync(factory);

        var detail = await DetailAsync(factory, SeedOrganisation.Olivier, itemId);

        // Considered, committed, active. "Why is this where it is, and who decided" is the question the trail
        // exists to keep answerable.
        detail.History.Select(record => record.ToState).ShouldBe(["active", "committed", "considered"]);
        detail.History.ShouldAllBe(record => record.Reason.Length > 0);
        detail.History.ShouldAllBe(record => record.DecidedBy == SeedOrganisation.Olivier.UserId);
    }

    [Fact]
    public async Task The_trail_cannot_be_rewritten_even_by_the_PMO()
    {
        await using var factory = await SeededAsync();
        var itemId = await ConsiderAsync(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = SeedOrganisation.Nadia;

        var context = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Portfolio.Infrastructure.PortfolioDbContext>();

        // No update or delete policy exists, and FORCE ROW LEVEL SECURITY makes the absence bite even for the
        // table owner. An audit trail anyone can edit is not one.
        var deleted = await context.Transitions
            .Where(transition => transition.PortfolioItemId == itemId)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        deleted.ShouldBe(0);
    }

    [Fact]
    public async Task Only_the_PMO_can_move_an_item_backwards()
    {
        await using var factory = await SeededAsync();
        var itemId = await ActiveItemAsync(factory);

        factory.AsUser(SeedOrganisation.Olivier);

        var refused = await factory.CreateClient().PostAsJsonAsync(
            $"/api/portfolio/{itemId}/revert",
            new { targetState = "committed", reason = "Paused." },
            TestContext.Current.CancellationToken);

        // A head runs delivery; rewriting where an item sits in the portfolio is one desk's call.
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        factory.AsUser(SeedOrganisation.Nadia);

        var allowed = await factory.CreateClient().PostAsJsonAsync(
            $"/api/portfolio/{itemId}/revert",
            new { targetState = "committed", reason = "Paused pending the budget review." },
            TestContext.Current.CancellationToken);

        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_reversal_is_flagged_as_one_in_the_trail()
    {
        await using var factory = await SeededAsync();
        var itemId = await ActiveItemAsync(factory);

        factory.AsUser(SeedOrganisation.Nadia);

        await factory.CreateClient().PostAsJsonAsync(
            $"/api/portfolio/{itemId}/revert",
            new { targetState = "committed", reason = "Paused pending the budget review." },
            TestContext.Current.CancellationToken);

        var detail = await DetailAsync(factory, SeedOrganisation.Nadia, itemId);

        detail.History[0].IsReversal.ShouldBeTrue();
        detail.History[0].ToState.ShouldBe("committed");
    }

    [Fact]
    public async Task A_candidate_from_another_department_is_invisible()
    {
        await using var factory = await SeededAsync();
        var itemId = await ConsiderAsync(factory);

        // Laurent heads Finance; the candidate is sponsored by IS and has no project yet, so there is nothing to
        // derive visibility from beyond the sponsoring department.
        factory.AsUser(SeedOrganisation.Laurent);

        var response = await factory.CreateClient()
            .GetAsync($"/api/portfolio/{itemId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anyone_in_the_sponsoring_department_sees_its_candidates()
    {
        await using var factory = await SeededAsync();
        var itemId = await ConsiderAsync(factory);

        // Mehdi is a plain IS member on no project at all. The portfolio is the department's shared plan, so he
        // sees what it is considering — unlike a project, which needs membership.
        var board = await BoardAsync(factory, SeedOrganisation.Mehdi);

        Lane(board, "considered").Items.Select(item => item.Id).ShouldContain(itemId);
    }

    [Fact]
    public async Task A_plain_member_cannot_register_a_candidate()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Mehdi);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/portfolio/considered",
            new { name = "Nope", departmentId = SeedOrganisation.Departments.InformationSystems, priority = 10 },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_unit_head_can_start_delivery_in_their_own_department()
    {
        await using var factory = await SeededAsync();
        var itemId = await ConsiderAsync(factory);
        var projectId = await CommitAsync(factory, itemId);

        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/members",
            new
            {
                personId = SeedOrganisation.Camille.UserId,
                departmentId = SeedOrganisation.Departments.InformationSystems,
                functionalRoleId = Guid.Parse("f0000000-0000-0000-0000-000000000001"),
            },
            ct);

        await client.PostAsJsonAsync(
            $"/api/portfolio/{itemId}/iterations",
            new { name = "Sprint 1", length = "twoweeks", startsOn = "2026-08-31" },
            ct);

        // Thomas heads a unit inside IS but is not the department head. The endpoint policy admits him, so the RLS
        // write predicate must too — a policy that lets someone through the door only for RLS to refuse them shows
        // up as a 404 on an item sitting in front of them on the board.
        factory.AsUser(SeedOrganisation.Thomas);

        var response = await factory.CreateClient()
            .PostAsync($"/api/portfolio/{itemId}/activate", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_head_of_another_department_cannot_move_an_item()
    {
        await using var factory = await SeededAsync();
        var itemId = await ConsiderAsync(factory);

        // Laurent heads Finance and passes the any-head policy at the door. The candidate is sponsored by IS, so
        // RLS never shows it to him — 404, the same answer a non-existent id gets.
        factory.AsUser(SeedOrganisation.Laurent);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/portfolio/{itemId}/commit",
            new { projectCode = "PRJ-NOPE", decisionNotes = "Taking this over." },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_plain_member_cannot_activate_an_item()
    {
        await using var factory = await SeededAsync();
        var itemId = await ActiveItemAsync(factory);

        factory.AsUser(SeedOrganisation.Mehdi);

        var response = await factory.CreateClient().PostAsync(
            $"/api/portfolio/{itemId}/activate",
            null,
            TestContext.Current.CancellationToken);

        // Refused at the door by the endpoint policy; it never reaches RLS.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_PMO_can_archive_an_item_it_does_not_own()
    {
        await using var factory = await SeededAsync();
        var itemId = await ActiveItemAsync(factory);

        factory.AsUser(SeedOrganisation.Nadia);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/portfolio/{itemId}/archive",
            new { reason = "Consolidated into the group programme." },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_archived_item_refuses_further_planning()
    {
        await using var factory = await SeededAsync();
        var itemId = await ActiveItemAsync(factory);

        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync(
            $"/api/portfolio/{itemId}/archive",
            new { reason = "Retired." },
            ct);

        // Read-only from here: this is the signal S6 freezes its boards on, and the item itself has to honour it
        // too or the two views disagree about whether the work is still running.
        var response = await client.PostAsJsonAsync(
            $"/api/portfolio/{itemId}/iterations",
            new { name = "Sprint 2", length = "twoweeks", startsOn = "2026-09-14" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_planned_iteration_can_be_cancelled_but_stays_in_the_timeline()
    {
        await using var factory = await SeededAsync();
        var itemId = await ActiveItemAsync(factory);

        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync(
            $"/api/portfolio/{itemId}/iterations",
            new { name = "Sprint 2", length = "twoweeks", startsOn = "2026-09-14" },
            ct);

        var iterations = await client.GetFromJsonAsync<List<IterationSummary>>(
            $"/api/portfolio/{itemId}/iterations",
            ct);

        var planned = iterations!.Single(iteration => iteration.State == "planned");

        var response = await client.DeleteAsync($"/api/portfolio/{itemId}/iterations/{planned.Id}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<List<IterationSummary>>(
            $"/api/portfolio/{itemId}/iterations",
            ct);

        // Cancelled, not erased. "We scheduled three sprints and dropped one" is a different story from "we always
        // planned two", and only the row knows which is true.
        after!.Count.ShouldBe(2);
        after.Single(iteration => iteration.Id == planned.Id).State.ShouldBe("cancelled");
    }

    [Fact]
    public async Task The_mine_scope_narrows_the_board_to_what_the_caller_raised()
    {
        await using var factory = await SeededAsync();
        await ConsiderAsync(factory);

        factory.AsUser(SeedOrganisation.Mehdi);

        var board = (await factory.CreateClient().GetFromJsonAsync<PortfolioBoard>(
            "/api/portfolio?scope=mine",
            TestContext.Current.CancellationToken))!;

        // Mehdi can see the candidate on the full board — it is his department's. "Mine" is a convenience filter
        // over what RLS already allowed, not a second boundary.
        board.Lanes.SelectMany(lane => lane.Items).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_board_always_returns_every_lane()
    {
        await using var factory = await SeededAsync();

        var board = await BoardAsync(factory, SeedOrganisation.Olivier);

        // Including the empty ones: a board missing its considered column because nothing is in it reads as broken,
        // and it is also where new candidates get dropped.
        board.Lanes.Select(lane => lane.State).ShouldBe(["considered", "committed", "active", "dephase"]);
    }

    [Fact]
    public async Task Closing_an_iteration_starts_the_next_planned_one()
    {
        await using var factory = await SeededAsync();
        var itemId = await ActiveItemAsync(factory);

        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync(
            $"/api/portfolio/{itemId}/iterations",
            new { name = "Sprint 2", length = "twoweeks", startsOn = "2026-09-14" },
            ct);

        var before = await DetailAsync(factory, SeedOrganisation.Olivier, itemId);
        var current = before.Item.CurrentIteration!;

        var closed = await client.PostAsync(
            $"/api/portfolio/{itemId}/iterations/{current.Id}/close",
            null,
            ct);

        closed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await DetailAsync(factory, SeedOrganisation.Olivier, itemId);

        after.Item.CurrentIteration!.Sequence.ShouldBe(2);
        after.Item.CurrentIteration.State.ShouldBe("active");
    }

    [Fact]
    public async Task A_preset_length_fills_in_the_end_date()
    {
        await using var factory = await SeededAsync();
        var itemId = await ConsiderAsync(factory);
        await CommitAsync(factory, itemId);

        factory.AsUser(SeedOrganisation.Olivier);

        await factory.CreateClient().PostAsJsonAsync(
            $"/api/portfolio/{itemId}/iterations",
            new { name = "Sprint 1", length = "twoweeks", startsOn = "2026-08-31" },
            TestContext.Current.CancellationToken);

        var detail = await DetailAsync(factory, SeedOrganisation.Olivier, itemId);

        // Fourteen days inclusive: Monday the 31st through Sunday the 13th.
        detail.Iterations[0].EndsOn.ShouldBe(new DateOnly(2026, 9, 13));
    }

    // --- Fixture -------------------------------------------------------------------------------------------------

    private sealed record CreatedResponse(Guid Id);

    private sealed record CommittedResponse(Guid ProjectId);

    private static PortfolioLane Lane(PortfolioBoard board, string state) =>
        board.Lanes.Single(lane => lane.State == state);

    private static async Task<Guid> ConsiderAsync(CracraApplicationFactory factory)
    {
        factory.AsUser(SeedOrganisation.Olivier);
        var ct = TestContext.Current.CancellationToken;

        var created = await factory.CreateClient().PostAsJsonAsync(
            "/api/portfolio/considered",
            new
            {
                name = "Refonte du portail",
                departmentId = SeedOrganisation.Departments.InformationSystems,
                priority = 10,
                notes = "Raised at the steering committee.",
            },
            ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await created.Content.ReadFromJsonAsync<CreatedResponse>(ct))!.Id;
    }

    private static async Task<Guid> CommitAsync(CracraApplicationFactory factory, Guid itemId)
    {
        factory.AsUser(SeedOrganisation.Olivier);
        var ct = TestContext.Current.CancellationToken;

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/portfolio/{itemId}/commit",
            new { projectCode = "PRJ-PORTAIL", decisionNotes = "Budget approved by the steering committee." },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<CommittedResponse>(ct))!.ProjectId;
    }

    /// <summary>Considered, committed, planned, staffed and started — the state most scenarios need.</summary>
    private static async Task<Guid> ActiveItemAsync(CracraApplicationFactory factory)
    {
        var itemId = await ConsiderAsync(factory);
        var projectId = await CommitAsync(factory, itemId);

        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        // Activation needs a team, and only Projects can answer that — so the team goes on through the Projects
        // API, exactly as it would in use.
        var added = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/members",
            new
            {
                personId = SeedOrganisation.Camille.UserId,
                departmentId = SeedOrganisation.Departments.InformationSystems,
                functionalRoleId = Guid.Parse("f0000000-0000-0000-0000-000000000001"),
                allocationPercent = 50,
            },
            ct);

        added.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var iteration = await client.PostAsJsonAsync(
            $"/api/portfolio/{itemId}/iterations",
            new { name = "Sprint 1", length = "twoweeks", startsOn = "2026-08-31" },
            ct);

        iteration.StatusCode.ShouldBe(HttpStatusCode.Created);

        var activated = await client.PostAsync($"/api/portfolio/{itemId}/activate", null, ct);

        activated.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        return itemId;
    }

    private static async Task<PortfolioBoard> BoardAsync(CracraApplicationFactory factory, UserContext user)
    {
        factory.AsUser(user);

        return (await factory.CreateClient().GetFromJsonAsync<PortfolioBoard>(
            "/api/portfolio",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<PortfolioItemDetail> DetailAsync(
        CracraApplicationFactory factory,
        UserContext user,
        Guid itemId)
    {
        factory.AsUser(user);

        return (await factory.CreateClient().GetFromJsonAsync<PortfolioItemDetail>(
            $"/api/portfolio/{itemId}",
            TestContext.Current.CancellationToken))!;
    }

    private async Task<CracraApplicationFactory> SeededAsync()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();

        keycloak.Users.Add(FakeKeycloakDirectory.User(
            SeedOrganisation.Laurent, "Laurent", "Bouchard", "expert-comptable"));

        var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
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
    /// Clears the portfolio, projects, the access projection and the directory. The Postgres container is shared
    /// for speed, so anything left behind would be counted by the board assertions.
    /// </summary>
    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        // As the system job: the trail has no delete policy at all, and this is the one context that bypasses it.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;

        var portfolio = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Portfolio.Infrastructure.PortfolioDbContext>();

        await portfolio.Transitions.ExecuteDeleteAsync(ct);
        await portfolio.Iterations.ExecuteDeleteAsync(ct);
        await portfolio.Items.ExecuteDeleteAsync(ct);

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
    }
}
