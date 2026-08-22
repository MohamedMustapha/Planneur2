using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Scheduling.Contracts;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Scheduling;

/// <summary>
/// Drawing a task on the 6c canvas, and dragging its progress handle (v2 §02, S6 6c).
/// </summary>
/// <remarks>
/// Both writes go straight through to Activities rather than restating its rules, so what only a real stack can
/// prove is that they land: that a rectangle drawn on the canvas becomes a planned entry the person's own week
/// counts, carrying the sentence they typed, and that the percentage they set is the one the board reads back
/// rather than the one plan-versus-actual would have inferred.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class TaskPlanningTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_task_drawn_on_the_canvas_lands_on_the_persons_own_week()
    {
        await using var factory = await SeededAsync();

        var id = await PlanAsync(factory, SeedOrganisation.Camille, note: "Reprise du socle");

        var board = await BoardAsync(factory, SeedOrganisation.Camille);
        var drawn = board.Events.ShouldHaveSingleItem();

        drawn.Id.ShouldBe(id.ToString());
        drawn.Kind.ShouldBe("planned");

        // Planned, not actual: drawing a rectangle on a schedule states an intention. Whether the hours were in
        // fact spent stays the owner's to claim in S5.
        drawn.Editable.ShouldBeTrue();
    }

    [Fact]
    public async Task The_sentence_typed_into_the_popup_comes_back_on_the_block()
    {
        await using var factory = await SeededAsync();

        await PlanAsync(factory, SeedOrganisation.Camille, note: "Reprise du socle réseau");

        var board = await BoardAsync(factory, SeedOrganisation.Camille);

        // Drawn under the title on 6c. Stored as the entry's note, so the same sentence shows up in S5's feed
        // rather than living only on the board that created it.
        board.Events.ShouldHaveSingleItem().Note.ShouldBe("Reprise du socle réseau");
    }

    [Fact]
    public async Task A_percentage_given_at_creation_is_the_one_the_board_reads_back()
    {
        await using var factory = await SeededAsync();

        await PlanAsync(factory, SeedOrganisation.Camille, percentComplete: 40);

        var board = await BoardAsync(factory, SeedOrganisation.Camille);

        board.Events.ShouldHaveSingleItem().Progress.ShouldBe(40);
    }

    [Fact]
    public async Task A_task_drawn_without_a_percentage_claims_none()
    {
        await using var factory = await SeededAsync();

        await PlanAsync(factory, SeedOrganisation.Camille);

        var board = await BoardAsync(factory, SeedOrganisation.Camille);

        // Null rather than zero. Nobody has said how far along this is, and drawing an empty progress bar would
        // be a claim the person never made.
        board.Events.ShouldHaveSingleItem().Progress.ShouldBeNull();
    }

    [Fact]
    public async Task Dragging_the_progress_handle_moves_the_percentage()
    {
        await using var factory = await SeededAsync();

        var id = await PlanAsync(factory, SeedOrganisation.Camille, percentComplete: 25);

        (await SetProgressAsync(factory, SeedOrganisation.Camille, id, 75)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        var board = await BoardAsync(factory, SeedOrganisation.Camille);

        board.Events.ShouldHaveSingleItem().Progress.ShouldBe(75);
    }

    [Fact]
    public async Task Clearing_the_percentage_hands_the_answer_back_to_the_hours()
    {
        await using var factory = await SeededAsync();

        var id = await PlanAsync(factory, SeedOrganisation.Camille, percentComplete: 60);

        await SetProgressAsync(factory, SeedOrganisation.Camille, id, null);

        var board = await BoardAsync(factory, SeedOrganisation.Camille);

        // Back to what plan-versus-actual implies, which for an unreconciled plan is "no answer" — not the 60 the
        // person just erased.
        board.Events.ShouldHaveSingleItem().Progress.ShouldBeNull();
    }

    [Fact]
    public async Task A_reconciled_plan_still_reads_as_finished_without_anyone_saying_so()
    {
        await using var factory = await SeededAsync();

        var plannedId = await PlanAsync(factory, SeedOrganisation.Camille, percentComplete: null);

        var actual = await LogActualAsync(factory, SeedOrganisation.Camille);

        actual.ReconciledPlanId.ShouldBe(plannedId);

        var board = await BoardAsync(factory, SeedOrganisation.Camille);
        var plan = board.Events.Single(row => row.Id == plannedId.ToString());

        // The derived answer survives v2: a plan its actual has reconciled is done whether or not anybody dragged
        // the handle. The explicit figure wins where it exists; nothing invents one where it does not.
        plan.Progress.ShouldBe(100);
    }

    [Fact]
    public async Task An_explicit_percentage_wins_over_what_the_hours_imply()
    {
        await using var factory = await SeededAsync();

        var plannedId = await PlanAsync(factory, SeedOrganisation.Camille, percentComplete: 30);

        (await LogActualAsync(factory, SeedOrganisation.Camille)).ReconciledPlanId.ShouldBe(plannedId);

        var board = await BoardAsync(factory, SeedOrganisation.Camille);

        // Reconciled, and therefore 100 by the derived rule — but somebody said 30, and the person doing the work
        // is the authority on how far along it is.
        board.Events.Single(row => row.Id == plannedId.ToString()).Progress.ShouldBe(30);
    }

    [Fact]
    public async Task Progress_belongs_to_a_plan_and_not_to_an_actual()
    {
        await using var factory = await SeededAsync();

        var actual = await LogActualAsync(factory, SeedOrganisation.Camille);

        var response = await SetProgressAsync(factory, SeedOrganisation.Camille, actual.Id, 50);

        // An actual is a claim about time already spent: it is complete by definition, and a percentage on it
        // would be a second, contradictory answer to the same question.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_percentage_off_the_scale_is_refused_at_the_door()
    {
        await using var factory = await SeededAsync();

        var id = await PlanAsync(factory, SeedOrganisation.Camille);

        (await SetProgressAsync(factory, SeedOrganisation.Camille, id, 101)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);

        (await SetProgressAsync(factory, SeedOrganisation.Camille, id, -1)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_block_that_ends_before_it_starts_is_refused()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/scheduling/tasks",
            new
            {
                personId = SeedOrganisation.Camille.UserId,
                activityTypeCode = "quality-of-life",
                start = MondayMorning,
                end = MondayMorning.AddHours(-1),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Project_work_drawn_with_no_project_is_refused_by_the_taxonomy()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/scheduling/tasks",
            new
            {
                personId = SeedOrganisation.Camille.UserId,
                activityTypeCode = "project-build",
                start = MondayMorning,
                end = MondayMorning.AddHours(2),
            },
            TestContext.Current.CancellationToken);

        // Activities' ruling, reached rather than restated: the handler passes the request straight through, so a
        // second copy of this rule in Scheduling could only ever disagree with it.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_lead_may_draw_a_task_on_somebody_elses_row()
    {
        await using var factory = await SeededAsync();

        // Authenticated rather than lead-only, because planning your own week is ordinary. Who else may be planned
        // for is RLS's ruling, and a unit head over their own unit is exactly who it allows.
        var id = await PlanAsync(factory, SeedOrganisation.Thomas, forPerson: SeedOrganisation.Camille);

        var board = await BoardAsync(factory, SeedOrganisation.Camille);

        board.Events.ShouldContain(row => row.Id == id.ToString());
    }

    [Fact]
    public async Task Drawing_a_task_on_somebody_elses_row_is_refused_by_the_database()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/scheduling/tasks",
            new
            {
                personId = SeedOrganisation.Sofia.UserId,
                activityTypeCode = "quality-of-life",
                start = MondayMorning,
                end = MondayMorning.AddHours(2),
            },
            TestContext.Current.CancellationToken);

        // Sofia is in another department, and Camille is nobody's head. The endpoint is Authenticated because
        // planning your own week is ordinary; who else may be planned for is Postgres's ruling, and it refuses
        // the insert outright rather than the module re-deciding it here.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Fixture -----------------------------------------------------------------------------------------------

    private sealed record PlannedResponse(Guid Id);

    private sealed record LogResponse(Guid Id, string GuardrailStatus, decimal WeekHours, decimal TargetHours, decimal Overtime, Guid? ReconciledPlanId);

    private static async Task<Guid> PlanAsync(
        CracraApplicationFactory factory,
        UserContext caller,
        UserContext? forPerson = null,
        string? note = null,
        int? percentComplete = null)
    {
        factory.AsUser(caller);

        var ct = TestContext.Current.CancellationToken;

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/scheduling/tasks",
            new
            {
                personId = (forPerson ?? caller).UserId,
                activityTypeCode = "quality-of-life",
                start = MondayMorning,
                end = MondayMorning.AddHours(3),
                note,
                percentComplete,
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<PlannedResponse>(ct))!.Id;
    }

    private static async Task<HttpResponseMessage> SetProgressAsync(
        CracraApplicationFactory factory,
        UserContext caller,
        Guid entryId,
        int? percentComplete)
    {
        factory.AsUser(caller);

        return await factory.CreateClient().PutAsJsonAsync(
            $"/api/scheduling/tasks/{entryId}/progress",
            new { percentComplete },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Logs the hours that were actually spent, in the same slot the plan occupies.
    /// </summary>
    /// <remarks>
    /// Nothing names the plan: reconciliation is automatic where one is sitting in the window, which is why these
    /// scenarios can put an actual on the canvas and then ask the board what the plan now says.
    /// </remarks>
    private static async Task<LogResponse> LogActualAsync(CracraApplicationFactory factory, UserContext person)
    {
        factory.AsUser(person);

        var ct = TestContext.Current.CancellationToken;

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            new
            {
                activityTypeCode = "quality-of-life",
                kind = "actual",
                source = "manual",
                slotStart = MondayMorning,
                slotEnd = MondayMorning.AddHours(3),
                hours = 3m,
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<LogResponse>(ct))!;
    }

    private static async Task<BoardPayload> BoardAsync(CracraApplicationFactory factory, UserContext person)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<BoardPayload>(
            "/api/scheduling/board?type=my&from=2026-08-17&to=2026-08-23",
            TestContext.Current.CancellationToken))!;
    }

    private async Task<CracraApplicationFactory> SeededAsync()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();

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

    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;

        var activities = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Activities.Infrastructure.ActivitiesDbContext>();

        await activities.Entries.ExecuteDeleteAsync(ct);

        var directory = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Directory.Data.DirectoryDbContext>();

        await directory.PersonFunctionalRoles.ExecuteDeleteAsync(ct);
        await directory.PersonUnits.ExecuteDeleteAsync(ct);
        await directory.People.ExecuteDeleteAsync(ct);

        // Reset rather than delete: sync creates a config only alongside its department, and the departments
        // survive this. A working day left over from another suite would move this board's axis.
        await directory.DepartmentConfigs.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(config => config.ActivityTaxonomyJson, "{}")
                .SetProperty(config => config.WorkingDayJson, "{}")
                .SetProperty(config => config.WeeklyTargetHours, 35m)
                .SetProperty(config => config.EnforceWeeklyTarget, false),
            ct);
    }
}
