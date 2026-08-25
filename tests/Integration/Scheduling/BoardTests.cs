using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Scheduling.Contracts;
using Cracra.Tests.Integration.Directory;
using Cracra.Tests.Integration.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Scheduling;

/// <summary>
/// The five boards and the assignment round-trip, through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// The claim this slice makes is that a board is a join of already-authorized sets — so the tests worth writing
/// are the ones that would catch that claim being false: a member asking for the department board and getting
/// somebody else's unit, or an assignment landing on a row its own module would have refused.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class BoardTests(PostgresFixture postgres)
{
    private static readonly DateOnly Monday = new(2026, 8, 17);
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid DevRole = Guid.Parse("f0000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task My_board_has_a_category_per_activity_bucket()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 3);

        var board = await BoardAsync(factory, SeedOrganisation.Camille, "my");

        board.BoardType.ShouldBe("my");
        board.Archetype.ShouldBe(BoardArchetypes.TaskProgress);

        // The canonical four are always there, so an empty week still renders as a board rather than as nothing.
        var categories = board.Resources.Where(row => row.Kind == "category").ToList();

        categories.Select(row => row.Id).ShouldContain("project-build");
        categories.ShouldAllBe(row => row.ParentId == null);

        // Two levels since v2: the bucket is a heading, and the hours draw on a row beneath it. A flat bucket row
        // answers the wrong question — "22 hours on BUILD" is a number nobody acts on.
        board.Events.ShouldHaveSingleItem().ResourceId.ShouldBe("quality-of-life:quality-of-life");
    }

    [Fact]
    public async Task A_project_bucket_opens_onto_the_projects_the_week_was_spent_on()
    {
        await using var factory = await SeededAsync();
        var projectId = await CrossDepartmentProjectAsync(factory);

        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 3, projectId);

        var board = await BoardAsync(factory, SeedOrganisation.Camille, "my");

        var line = board.Resources.Single(row => row.Id == $"project-build:{projectId}");

        // "14 on the SI rewrite, 8 on the HR portal" is the sentence the board exists to say, so BUILD and RUN
        // open onto projects rather than onto subtypes.
        line.Kind.ShouldBe("project-line");
        line.ParentId.ShouldBe("project-build");
        line.Name.ShouldBe("PRJ-BOARD");

        // The code is the name and must not be run through the dictionary — a project code is not a key.
        line.SubtitleKey.ShouldBeNull();

        board.Events.ShouldHaveSingleItem().ResourceId.ShouldBe(line.Id);
    }

    [Fact]
    public async Task A_non_project_bucket_opens_onto_the_types_the_week_actually_used()
    {
        await using var factory = await SeededAsync();

        await ConfigureTaxonomyAsync(
            factory,
            """{"types":[{"code":"payroll-run","parent":"recruitment-admin","labelKey":"hr.payroll"}]}""");

        await LogAsync(factory, SeedOrganisation.Camille, "payroll-run", 2);

        var board = await BoardAsync(factory, SeedOrganisation.Camille, "my");

        var lane = board.Resources.Single(row => row.Id == "recruitment-admin:payroll-run");

        // A department subtype resolves through its parent, so the hours land under the canonical bucket rather
        // than inventing a fifth one — and the row carries the department's own label.
        lane.ParentId.ShouldBe("recruitment-admin");
        lane.SubtitleKey.ShouldBe("hr.payroll");

        board.Events.ShouldHaveSingleItem().ResourceId.ShouldBe(lane.Id);
    }

    [Fact]
    public async Task An_empty_category_still_gets_a_row_to_click_on()
    {
        await using var factory = await SeededAsync();

        // Nothing was booked to BUILD this week, so its category has no project to open onto.
        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 3);

        var board = await BoardAsync(factory, SeedOrganisation.Camille, "my");

        var placeholder = board.Resources.Single(row => row.Id == "project-build:none");

        // An empty category still gets one row, so the week reads as a board waiting for entries rather than as
        // four headings over blank space — and so there is somewhere to click.
        placeholder.Kind.ShouldBe("lane");
        placeholder.ParentId.ShouldBe("project-build");
    }

    [Fact]
    public async Task Every_event_lands_on_a_row_the_board_actually_emitted()
    {
        await using var factory = await SeededAsync();
        var projectId = await CrossDepartmentProjectAsync(factory);

        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 3, projectId);
        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 2,
            start: MondayMorning.AddDays(1));

        var board = await BoardAsync(factory, SeedOrganisation.Camille, "my");
        var rows = board.Resources.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);

        // The failure this guards against is silent: Mobiscroll renders an event whose resource does not exist as
        // nothing at all, and an hour somebody logged vanishing from their own board is this screen's worst bug.
        board.Events.ShouldNotBeEmpty();
        board.Events.ShouldAllBe(row => rows.Contains(row.ResourceId));
    }

    [Fact]
    public async Task A_node_board_shows_the_people_attached_there()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Mehdi, "quality-of-life", 3);

        // Camille and Mehdi hang off the same node, and the matrix says peers see each other.
        var board = await BoardAsync(
            factory, SeedOrganisation.Camille, "node", SeedOrganisation.Units.Infrastructure);

        board.Resources.Select(row => row.Id).ShouldContain(SeedOrganisation.Mehdi.UserId.ToString());
        board.Events.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_node_board_does_not_show_a_foreign_branch()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Sofia, "quality-of-life", 3);

        var board = await BoardAsync(
            factory, SeedOrganisation.Camille, "node", SeedOrganisation.Units.Infrastructure);

        // Sofia hangs off another branch entirely. Nothing about asking for a board widens what RLS allows.
        board.Resources.Select(row => row.Id).ShouldNotContain(SeedOrganisation.Sofia.UserId.ToString());
    }

    [Fact]
    public async Task A_member_cannot_drag_on_their_node_board()
    {
        await using var factory = await SeededAsync();

        var member = await BoardAsync(
            factory, SeedOrganisation.Camille, "node", SeedOrganisation.Units.Infrastructure);
        var head = await BoardAsync(
            factory, SeedOrganisation.Thomas, "node", SeedOrganisation.Units.Infrastructure);

        // The server answers this rather than the client guessing: a board offering a gesture the server will
        // reject feels broken, and one hiding a gesture the server would allow feels arbitrary.
        member.CanAssign.ShouldBeFalse();
        head.CanAssign.ShouldBeTrue();
    }

    [Fact]
    public async Task A_node_board_over_children_has_a_row_per_child()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 3);

        var board = await BoardAsync(
            factory, SeedOrganisation.Olivier, "node", SeedOrganisation.Departments.InformationSystems);

        board.Resources.ShouldContain(row => row.Kind == "node");

        // Read-only: a head correcting one person's hour expands to people, where the rows are people.
        board.Events.ShouldAllBe(row => !row.Editable);
        board.CanAssign.ShouldBeFalse();
    }

    [Fact]
    public async Task A_node_board_is_empty_for_someone_outside_the_branch()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 3);

        // Laurent heads another branch. Asking for this one is not refused — it simply contains nothing he may
        // see, which is what "the scope narrows, it never lifts" means in practice.
        var board = await BoardAsync(
            factory,
            SeedOrganisation.Laurent,
            "node",
            SeedOrganisation.Departments.InformationSystems);

        board.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_project_board_groups_the_team_by_department()
    {
        await using var factory = await SeededAsync();
        var projectId = await CrossDepartmentProjectAsync(factory);

        var board = await BoardAsync(factory, SeedOrganisation.Olivier, "project", projectId);

        // A department row, then the people beneath it — S3's grouping, reused rather than recomputed.
        var departmentRows = board.Resources.Where(row => row.Kind == "department").ToList();

        departmentRows.Count.ShouldBe(2);

        board.Resources
            .Where(row => row.Kind == "person")
            .ShouldAllBe(row => row.ParentId != null);
    }

    [Fact]
    public async Task A_project_board_needs_a_project()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient()
            .GetAsync("/api/scheduling/board?type=project", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_project_board_for_an_invisible_project_is_a_404()
    {
        await using var factory = await SeededAsync();
        var projectId = await CrossDepartmentProjectAsync(factory);

        factory.AsUser(SeedOrganisation.Mehdi);

        var response = await factory.CreateClient().GetAsync(
            $"/api/scheduling/board?type=project&scopeId={projectId}",
            TestContext.Current.CancellationToken);

        // Mehdi is in the lead department but not on the project. RLS removed the row, so the board genuinely
        // cannot tell "may not see" from "does not exist" — and neither can a prober.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_node_board_of_people_breaks_activity_out_by_project()
    {
        await using var factory = await SeededAsync();
        var projectId = await CrossDepartmentProjectAsync(factory);

        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 3, projectId);
        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 2);

        var board = await BoardAsync(
            factory, SeedOrganisation.Thomas, "node", SeedOrganisation.Units.Infrastructure);

        // A person row with project lines nested beneath it, so somebody on four projects still reads as one
        // person rather than as four unrelated rows.
        var personRow = board.Resources.Single(row =>
            row.Kind == "person" && row.Id == SeedOrganisation.Camille.UserId.ToString());

        board.Resources.ShouldContain(row => row.Kind == "project-line" && row.ParentId == personRow.Id);

        // Non-project work stays on the person's own row: it belongs to them, not to a project line.
        board.Events.ShouldContain(row => row.ResourceId == personRow.Id);
    }

    [Fact]
    public async Task Only_planned_slots_are_draggable()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 3, kind: "planned");
        await LogAsync(factory, SeedOrganisation.Camille, "recruitment-admin", 2, kind: "actual",
            start: MondayMorning.AddDays(1));

        var board = await BoardAsync(factory, SeedOrganisation.Camille, "my");

        // A board arranges intent. What somebody recorded they actually did is not something a drag should
        // quietly rewrite.
        board.Events.Single(row => row.Kind == "planned").Editable.ShouldBeTrue();
        board.Events.Single(row => row.Kind == "actual").Editable.ShouldBeFalse();
    }

    // --- Work orders ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_lead_creates_a_work_order_and_it_lands_in_the_pool()
    {
        await using var factory = await SeededAsync();

        await CreateWorkOrderAsync(factory);

        var pool = await PoolAsync(factory, SeedOrganisation.Thomas);

        pool.ShouldHaveSingleItem().State.ShouldBe("unassigned");
    }

    [Fact]
    public async Task A_member_cannot_create_a_work_order()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/scheduling/work-orders",
            new
            {
                reference = "WO-1",
                title = "Nope",
                unitId = SeedOrganisation.Units.Infrastructure,
                estimatedHours = 1m,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Assigning_a_work_order_creates_the_planned_activity()
    {
        await using var factory = await SeededAsync();
        var workOrderId = await CreateWorkOrderAsync(factory);

        var assignment = await AssignAsync(factory, workOrderId, SeedOrganisation.Camille.UserId);

        assignment.ActivityEntryId.ShouldNotBe(Guid.Empty);

        // The point of the archetype: what the lead scheduled shows up on the person's own week.
        var board = await BoardAsync(factory, SeedOrganisation.Camille, "my");

        board.Events.ShouldContain(row => row.Id == assignment.ActivityEntryId.ToString());

        // And it left the pool.
        (await PoolAsync(factory, SeedOrganisation.Thomas)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unassigning_removes_the_planned_activity_it_created()
    {
        await using var factory = await SeededAsync();
        var workOrderId = await CreateWorkOrderAsync(factory);
        var assignment = await AssignAsync(factory, workOrderId, SeedOrganisation.Camille.UserId);

        factory.AsUser(SeedOrganisation.Thomas);

        var response = await factory.CreateClient().PostAsync(
            $"/api/scheduling/work-orders/{workOrderId}/unassign",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Returning the card to the pool while its hours stay on somebody's week is the failure this ordering
        // exists to prevent.
        var board = await BoardAsync(factory, SeedOrganisation.Camille, "my");

        board.Events.ShouldNotContain(row => row.Id == assignment.ActivityEntryId.ToString());
        (await PoolAsync(factory, SeedOrganisation.Thomas)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_assigned_order_cannot_be_handed_straight_to_someone_else()
    {
        await using var factory = await SeededAsync();
        var workOrderId = await CreateWorkOrderAsync(factory);

        await AssignAsync(factory, workOrderId, SeedOrganisation.Camille.UserId);

        factory.AsUser(SeedOrganisation.Thomas);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/scheduling/work-orders/{workOrderId}/assign",
            new { personId = SeedOrganisation.Mehdi.UserId, start = MondayMorning },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Assigning_a_project_typed_order_with_no_project_is_refused_with_a_reason()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Thomas);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var created = await client.PostAsJsonAsync(
            "/api/scheduling/work-orders",
            new
            {
                reference = "WO-9",
                title = "Sans projet",
                unitId = SeedOrganisation.Units.Infrastructure,
                activityTypeCode = "project-run",
                estimatedHours = 1m,
            },
            ct);

        var workOrderId = (await created.Content.ReadFromJsonAsync<CreatedResponse>(ct))!.Id;

        var response = await client.PostAsJsonAsync(
            $"/api/scheduling/work-orders/{workOrderId}/assign",
            new { personId = SeedOrganisation.Camille.UserId, start = MondayMorning },
            ct);

        // 422 from Activities' own rule, not a 500 and not a silently mislabelled hour. The message tells the
        // lead the two things that would fix it: link a project, or pick a type that does not need one.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Refreshing_the_pool_is_idempotent()
    {
        await using var factory = await SeededAsync();

        // Since S10 the pool's external half is the mirror, so this places three unassigned tickets in the
        // Infrastructure queue rather than turning on a sample source. What is under test is unchanged: pulling
        // twice must not duplicate the pool.
        await ExternalMirrorSeed.SeedAsync(
            factory,
            "servicenow",
            SeedOrganisation.Departments.InformationSystems,
            new ExternalMirrorSeed.Item("0010023", "Poste bloqué", UnitId: SeedOrganisation.Units.Infrastructure),
            new ExternalMirrorSeed.Item("0010024", "Imprimante HS", UnitId: SeedOrganisation.Units.Infrastructure),
            new ExternalMirrorSeed.Item("0010025", "Accès VPN", UnitId: SeedOrganisation.Units.Infrastructure));

        factory.AsUser(SeedOrganisation.Thomas);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var first = await client.PostAsJsonAsync(
            "/api/scheduling/work-orders/pool/refresh",
            new { unitId = SeedOrganisation.Units.Infrastructure },
            ct);

        (await first.Content.ReadFromJsonAsync<RefreshResponse>(ct))!.Created.ShouldBe(3);

        var second = await client.PostAsJsonAsync(
            "/api/scheduling/work-orders/pool/refresh",
            new { unitId = SeedOrganisation.Units.Infrastructure },
            ct);

        // Refreshing is something a lead does repeatedly. A pull that duplicated the pool each time would make the
        // board useless within an afternoon.
        (await second.Content.ReadFromJsonAsync<RefreshResponse>(ct))!.Created.ShouldBe(0);

        (await PoolAsync(factory, SeedOrganisation.Thomas)).Count.ShouldBe(3);
    }

    // --- Shifts --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_default_shift_slots_are_offered_to_a_department_that_configured_none()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var templates = await factory.CreateClient().GetFromJsonAsync<List<ShiftTemplate>>(
            "/api/scheduling/shifts/templates",
            TestContext.Current.CancellationToken);

        templates.ShouldNotBeNull();
        templates.Select(template => template.Code).ShouldBe(["morning", "afternoon", "on-call"]);
    }

    [Fact]
    public async Task A_lead_rosters_someone_and_the_shift_appears_on_the_node_board()
    {
        await using var factory = await SeededAsync(boardLayout: "shifts");

        await PlanShiftAsync(factory, SeedOrganisation.Camille.UserId, "morning", Monday);

        var board = await BoardAsync(
            factory, SeedOrganisation.Thomas, "node", SeedOrganisation.Units.Infrastructure);

        board.Archetype.ShouldBe(BoardArchetypes.Shifts);
        board.Events.ShouldContain(row => row.Kind == "shift");
    }

    [Fact]
    public async Task Double_booking_someone_is_refused()
    {
        await using var factory = await SeededAsync();

        await PlanShiftAsync(factory, SeedOrganisation.Camille.UserId, "morning", Monday);

        factory.AsUser(SeedOrganisation.Thomas);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/scheduling/shifts",
            new { personId = SeedOrganisation.Camille.UserId, templateCode = "morning", day = Monday },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_coverage_gap_is_reported_and_clears_once_it_is_filled()
    {
        await using var factory = await SeededAsync(
            boardLayout: "shifts",
            shiftTemplates: """
                {"shifts":[{"code":"morning","labelKey":"shift.morning","start":"08:00","end":"12:30","minimumStaff":2}]}
                """);

        await PlanShiftAsync(factory, SeedOrganisation.Camille.UserId, "morning", Monday);

        var short_ = await BoardAsync(
            factory, SeedOrganisation.Thomas, "node", SeedOrganisation.Units.Infrastructure);

        var gap = short_.Coverage.Single(warning => warning.Day == Monday);

        gap.Required.ShouldBe(2);
        gap.Scheduled.ShouldBe(1);

        await PlanShiftAsync(factory, SeedOrganisation.Mehdi.UserId, "morning", Monday);

        var covered = await BoardAsync(
            factory, SeedOrganisation.Thomas, "node", SeedOrganisation.Units.Infrastructure);

        covered.Coverage.ShouldNotContain(warning => warning.Day == Monday);
    }

    [Fact]
    public async Task A_member_cannot_roster_themselves()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/scheduling/shifts",
            new { personId = SeedOrganisation.Camille.UserId, templateCode = "morning", day = Monday },
            TestContext.Current.CancellationToken);

        // Putting yourself on the on-call slot is a conversation, not a self-service action.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Reschedule ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_planned_slot_can_be_dragged_to_a_new_time()
    {
        await using var factory = await SeededAsync();

        var entryId = await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 2, kind: "planned");

        factory.AsUser(SeedOrganisation.Camille);

        var moved = MondayMorning.AddDays(1);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/scheduling/tasks/{entryId}/schedule",
            new { start = moved, end = moved.AddHours(2) },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var board = await BoardAsync(factory, SeedOrganisation.Camille, "my");

        board.Events.Single(row => row.Id == entryId.ToString()).Start.ShouldBe(moved);
    }

    [Fact]
    public async Task An_actual_slot_cannot_be_dragged()
    {
        await using var factory = await SeededAsync();

        var entryId = await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 2);

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/scheduling/tasks/{entryId}/schedule",
            new { start = MondayMorning.AddDays(1), end = MondayMorning.AddDays(1).AddHours(2) },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // --- Fixture -------------------------------------------------------------------------------------------------

    private sealed record CreatedResponse(Guid Id);

    private sealed record RefreshResponse(int Created);

    private sealed record LogResponse(Guid Id);

    private static async Task<BoardPayload> BoardAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string type,
        Guid? scopeId = null,
        bool expandPeople = false)
    {
        factory.AsUser(person);

        var scope = scopeId is { } id ? $"&scopeId={id}" : string.Empty;
        var expand = expandPeople ? "&expandPeople=true" : string.Empty;

        return (await factory.CreateClient().GetFromJsonAsync<BoardPayload>(
            $"/api/scheduling/board?type={type}&from=2026-08-17&to=2026-08-23{scope}{expand}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<WorkOrderView>> PoolAsync(CracraApplicationFactory factory, UserContext person)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<List<WorkOrderView>>(
            $"/api/scheduling/work-orders/pool?unitId={SeedOrganisation.Units.Infrastructure}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<Guid> CreateWorkOrderAsync(CracraApplicationFactory factory)
    {
        factory.AsUser(SeedOrganisation.Thomas);
        var ct = TestContext.Current.CancellationToken;

        var created = await factory.CreateClient().PostAsJsonAsync(
            "/api/scheduling/work-orders",
            new
            {
                reference = "INC-4821",
                title = "Imprimante hors service",
                description = "Bourrage papier récurrent au 3e étage.",
                unitId = SeedOrganisation.Units.Infrastructure,
                // A helpdesk ticket against no project. project-run — the obvious choice, and the one the spec
                // names — requires a project under S5's taxonomy, so an unattached queue books its RUN work to a
                // non-project bucket instead. See WorkOrder.ActivityTypeCode for why that is the honest answer.
                activityTypeCode = "quality-of-life",
                estimatedHours = 1m,
            },
            ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await created.Content.ReadFromJsonAsync<CreatedResponse>(ct))!.Id;
    }

    private static async Task<AssignmentPayload> AssignAsync(
        CracraApplicationFactory factory,
        Guid workOrderId,
        Guid personId)
    {
        factory.AsUser(SeedOrganisation.Thomas);
        var ct = TestContext.Current.CancellationToken;

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/scheduling/work-orders/{workOrderId}/assign",
            new { personId, start = MondayMorning },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<AssignmentPayload>(ct))!;
    }

    private sealed record AssignmentPayload(Guid WorkOrderId, Guid PersonId, Guid ActivityEntryId);

    private static async Task PlanShiftAsync(
        CracraApplicationFactory factory,
        Guid personId,
        string templateCode,
        DateOnly day)
    {
        factory.AsUser(SeedOrganisation.Thomas);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/scheduling/shifts",
            new { personId, templateCode, day },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<Guid> LogAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string type,
        decimal hours,
        Guid? projectId = null,
        string kind = "actual",
        DateTimeOffset? start = null)
    {
        factory.AsUser(person);

        var from = start ?? MondayMorning;
        var ct = TestContext.Current.CancellationToken;

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            new
            {
                activityTypeCode = type,
                projectId,
                kind,
                source = "manual",
                slotStart = from,
                slotEnd = from.AddHours((double)hours),
                hours,
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<LogResponse>(ct))!.Id;
    }

    /// <summary>Led by IS, with Camille from IS and Sofia from Finance on it — the cross-department case.</summary>
    private static async Task<Guid> CrossDepartmentProjectAsync(CracraApplicationFactory factory)
    {
        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var created = await client.PostAsJsonAsync(
            "/api/projects",
            new
            {
                code = "PRJ-BOARD",
                name = "Migration M365",
                classification = "build",
                costAmount = 0m,
                costCurrency = "EUR",
                leadDepartmentId = SeedOrganisation.Departments.InformationSystems,
                contributingDepartmentIds = new[] { SeedOrganisation.Departments.Finance },
            },
            ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var projectId = (await created.Content.ReadFromJsonAsync<CreatedResponse>(ct))!.Id;

        foreach (var (person, department) in new[]
                 {
                     (SeedOrganisation.Camille, SeedOrganisation.Departments.InformationSystems),
                     (SeedOrganisation.Sofia, SeedOrganisation.Departments.Finance),
                 })
        {
            var added = await client.PostAsJsonAsync(
                $"/api/projects/{projectId}/members",
                new { personId = person.UserId, departmentId = department, functionalRoleId = DevRole },
                ct);

            added.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        return projectId;
    }

    private async Task<CracraApplicationFactory> SeededAsync(
        string? boardLayout = null,
        string? shiftTemplates = null)
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

        if (boardLayout is not null || shiftTemplates is not null)
        {
            await ConfigureAsync(factory, boardLayout ?? "week", shiftTemplates ?? "{}");
        }

        return factory;
    }

    /// <summary>Rewrites the IS department's taxonomy as its head, which is who is allowed to.</summary>
    private static async Task ConfigureTaxonomyAsync(CracraApplicationFactory factory, string taxonomyJson)
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
                enforceWeeklyTarget = false,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task ConfigureAsync(
        CracraApplicationFactory factory,
        string boardLayout,
        string shiftTemplates)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/departments/{SeedOrganisation.Departments.InformationSystems}/config",
            new
            {
                activityTaxonomyJson = "{}",
                roleLabelsJson = "{}",
                kudoRulesJson = "{}",
                defaultBoardLayout = boardLayout,
                iterationPresetsJson = """["1w","2w","1m"]""",
                weeklyTargetHours = 35m,
                enforceWeeklyTarget = false,
                shiftTemplatesJson = shiftTemplates,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;

        var scheduling = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Scheduling.Infrastructure.SchedulingDbContext>();

        await scheduling.Shifts.ExecuteDeleteAsync(ct);
        await scheduling.WorkOrders.ExecuteDeleteAsync(ct);

        var activities = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Activities.Infrastructure.ActivitiesDbContext>();

        await activities.Entries.ExecuteDeleteAsync(ct);

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

        // Reset rather than delete: sync only creates a config alongside its department, and the departments
        // survive this. See the same note in the Activities suite.
        await directory.DepartmentConfigs.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(config => config.ActivityTaxonomyJson, "{}")
                .SetProperty(config => config.ShiftTemplatesJson, "{}")
                .SetProperty(config => config.DefaultBoardLayout, "week")
                .SetProperty(config => config.WeeklyTargetHours, 35m)
                .SetProperty(config => config.EnforceWeeklyTarget, false),
            ct);
    }
}
