using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Scheduling.Contracts;
using Cracra.Modules.Scheduling.Domain;

namespace Cracra.Modules.Scheduling.Application;

/// <summary>
/// Builds the five boards.
/// </summary>
/// <remarks>
/// <para>
/// The read side of the slice, and deliberately 2-layer: there is no aggregate here because there is no state
/// here. Every row and every event already exists somewhere else and has already been filtered by that module's
/// own RLS — so a board is a join of already-authorized sets, and this class does the joining and nothing else.
/// </para>
/// <para>
/// That is also why none of the methods below checks a role. Asking for the department board as a plain member is
/// not an error; it returns the units that member can see, which is theirs alone. The scope narrows, it never
/// lifts, and there is exactly one place — Postgres — that decides where the ceiling is.
/// </para>
/// </remarks>
internal sealed class BoardComposer(
    IDirectoryPort directory,
    IProjectsPort projects,
    IActivitiesPort activities,
    IPortfolioPort portfolio,
    IShiftRepository shifts,
    IWorkOrderRepository workOrders,
    ICalendarOverlaySource overlays,
    IUserContext user)
{
    /// <summary>The colours the design fixes for each canonical bucket. Boards and timelines agree by construction.</summary>
    private static readonly Dictionary<string, string> BucketColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["project-build"] = "var(--activity-build)",
        ["project-run"] = "var(--activity-run)",
        ["quality-of-life"] = "var(--activity-qol)",
        ["recruitment-admin"] = "var(--activity-training)",
    };

    public async Task<BoardPayload> ComposeAsync(
        string boardType,
        Guid? scopeId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        (boardType ?? BoardTypes.My).Trim().ToLowerInvariant() switch
        {
            BoardTypes.Team => await TeamAsync(scopeId, from, to, ct),
            BoardTypes.Unit => await UnitAsync(scopeId, from, to, ct),
            BoardTypes.Project => await ProjectAsync(scopeId, from, to, ct),
            BoardTypes.Department => await DepartmentAsync(scopeId, from, to, ct),
            _ => await MyAsync(from, to, ct),
        };

    /// <summary>
    /// My board: one row per activity lane, my slots on them.
    /// </summary>
    /// <remarks>
    /// Rows are lanes rather than days because the design's board is a timeline: days are the horizontal axis, and
    /// making them rows too would produce a grid with the same thing on both axes.
    /// </remarks>
    private async Task<BoardPayload> MyAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var entries = await activities.GetForPeopleAsync([user.UserId], from, to, ct);

        // A lane per bucket that actually has something in it, plus the canonical four so an empty week still
        // renders as a board rather than as nothing.
        var codes = BucketColors.Keys
            .Concat(entries.Select(entry => entry.ActivityTypeCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        var resources = codes
            .Select(code => new BoardResource(
                code,
                code,
                Kind: "lane",
                ParentId: null,
                ColorFor(code),
                SubtitleKey: LabelFor(entries, code)))
            .ToList();

        return new BoardPayload(
            BoardTypes.My,
            BoardArchetypes.TaskProgress,
            "board.my",
            from,
            to,
            resources,
            [.. entries.Select(entry => EventFor(entry, entry.ActivityTypeCode, CanEdit(entry)))],
            [.. await overlays.GetOverlaysAsync(user.UnitId, null, from, to, ct)],
            Pool: [],
            Coverage: [],
            // Your own board is yours to arrange. Which individual blocks may move is still CanEdit's answer —
            // an actual you recorded is not draggable even on your own board.
            CanAssign: true);
    }

    /// <summary>
    /// Team board: my unit's members, their activity.
    /// </summary>
    /// <remarks>
    /// The archetype follows the department's configured layout: a helpdesk unit gets the work-order board, a
    /// delivery unit gets task progress. That is what <c>default_board_layout</c> is for, and it is why a helpdesk
    /// and a dev team can share one platform without either being handed the other's tool.
    /// </remarks>
    private async Task<BoardPayload> TeamAsync(Guid? unitId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var scope = unitId ?? user.UnitId
            ?? throw new DomainRuleViolationException("You are not in a unit, so there is no team board to show.");

        var people = await directory.GetPeopleAsync(scope, null, ct);
        var entries = await activities.GetForPeopleAsync([.. people.Select(person => person.Id)], from, to, ct);

        var departmentId = people.Select(person => person.DepartmentId).FirstOrDefault(id => id is not null);

        var archetype = departmentId is { } department
            ? await ArchetypeForAsync(department, ct)
            : BoardArchetypes.TaskProgress;

        var pool = archetype == BoardArchetypes.WorkOrders
            ? await PoolForAsync(scope, ct)
            : [];

        var unitShifts = archetype == BoardArchetypes.Shifts
            ? await shifts.GetForUnitAsync(scope, from, to, ct)
            : [];

        var coverage = await CoverageForAsync(departmentId, unitShifts, from, to, ct);

        return new BoardPayload(
            BoardTypes.Team,
            archetype,
            "board.team",
            from,
            to,
            [.. people.Select(PersonRow)],
            [
                .. entries.Select(entry => EventFor(entry, entry.PersonId.ToString(), editable: CanEdit(entry))),
                .. unitShifts.Select(ShiftEvent),
            ],
            [.. await overlays.GetOverlaysAsync(scope, null, from, to, ct)],
            pool,
            coverage,
            // A lead may drag; a member looking at their team's board may not. RLS refuses the write either way,
            // but a board that offers a gesture the server will reject is a board that feels broken.
            CanAssign: user.HasAnyRole(ContextualRole.UnitHead, ContextualRole.DepartmentHead, ContextualRole.Pmo));
    }

    /// <summary>Unit board: the unit's people, their activity broken out by the project it was against.</summary>
    private async Task<BoardPayload> UnitAsync(Guid? unitId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var scope = unitId ?? user.UnitId
            ?? throw new DomainRuleViolationException("You are not in a unit, so there is no unit board to show.");

        var people = await directory.GetPeopleAsync(scope, null, ct);
        var entries = await activities.GetForPeopleAsync([.. people.Select(person => person.Id)], from, to, ct);

        var codes = await projects.GetProjectCodesAsync(
            [.. entries.Where(entry => entry.ProjectId is not null).Select(entry => entry.ProjectId!.Value).Distinct()],
            ct);

        // A person row, with a child row per project they worked on. Nesting rather than one row per pair, so
        // somebody on four projects still reads as one person rather than as four unrelated rows.
        var resources = new List<BoardResource>();
        var events = new List<BoardEvent>();

        foreach (var person in people)
        {
            resources.Add(PersonRow(person));

            var theirs = entries.Where(entry => entry.PersonId == person.Id).ToList();

            foreach (var projectId in theirs
                         .Where(entry => entry.ProjectId is not null)
                         .Select(entry => entry.ProjectId!.Value)
                         .Distinct())
            {
                var rowId = $"{person.Id}:{projectId}";

                resources.Add(new BoardResource(
                    rowId,
                    codes.GetValueOrDefault(projectId, "—"),
                    Kind: "project-line",
                    ParentId: person.Id.ToString(),
                    Color: null,
                    SubtitleKey: null));

                events.AddRange(theirs
                    .Where(entry => entry.ProjectId == projectId)
                    .Select(entry => EventFor(entry, rowId, CanEdit(entry))));
            }

            // Non-project work stays on the person's own row: it belongs to them, not to a project line.
            events.AddRange(theirs
                .Where(entry => entry.ProjectId is null)
                .Select(entry => EventFor(entry, person.Id.ToString(), CanEdit(entry))));
        }

        return new BoardPayload(
            BoardTypes.Unit,
            BoardArchetypes.TaskProgress,
            "board.unit",
            from,
            to,
            resources,
            events,
            [.. await overlays.GetOverlaysAsync(scope, null, from, to, ct)],
            Pool: [],
            Coverage: [],
            CanAssign: user.HasAnyRole(ContextualRole.UnitHead, ContextualRole.DepartmentHead, ContextualRole.Pmo));
    }

    /// <summary>
    /// Project board: the team grouped by department, then function.
    /// </summary>
    /// <remarks>
    /// The grouping is S3's own team projection rather than a second one computed here. "Who from which department
    /// is doing what" is the question S3 exists to answer, and asking it twice in two places is how the two
    /// answers start to differ.
    /// </remarks>
    private async Task<BoardPayload> ProjectAsync(Guid? projectId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var scope = projectId
            ?? throw new DomainRuleViolationException("A project board needs a project.");

        var project = await projects.GetProjectAsync(scope, ct)
            // RLS filtered it or it does not exist; the caller cannot tell, by design.
            ?? throw new ResourceNotFoundException($"No project {scope}.");

        var team = await projects.GetTeamAsync(scope, ct);
        var entries = await activities.GetForProjectAsync(scope, from, to, ct);

        var resources = new List<BoardResource>();

        foreach (var group in team.GroupBy(member => (member.DepartmentId, member.DepartmentNameKey)))
        {
            var departmentRow = $"dept:{group.Key.DepartmentId}";

            resources.Add(new BoardResource(
                departmentRow,
                group.Key.DepartmentNameKey,
                Kind: "department",
                ParentId: null,
                Color: null,
                SubtitleKey: null));

            resources.AddRange(group
                .OrderBy(member => member.FunctionCode, StringComparer.Ordinal)
                .ThenBy(member => member.DisplayName, StringComparer.Ordinal)
                .Select(member => new BoardResource(
                    member.PersonId.ToString(),
                    // Anyone RLS hid keeps their row but loses their name: their hours still fill the slot, and
                    // dropping the row would understate how loaded the project is.
                    member.DisplayName ?? "directory.hiddenPerson",
                    Kind: "person",
                    ParentId: departmentRow,
                    Color: null,
                    SubtitleKey: $"directory.functionalRole.{member.FunctionCode}")));
        }

        var iterations = await portfolio.GetIterationsAsync(scope, ct);

        return new BoardPayload(
            BoardTypes.Project,
            BoardArchetypes.TaskProgress,
            project.Code,
            from,
            to,
            resources,
            [.. entries.Select(entry => EventFor(entry, entry.PersonId.ToString(), CanEdit(entry)))],
            [
                // Iteration boundaries as ranges, which is what the spec asks for and what makes a sprint visible
                // on the same canvas as the work inside it.
                .. iterations.Select(iteration => new BoardOverlay(
                    iteration.Id.ToString(),
                    "iteration",
                    iteration.Name,
                    iteration.StartsOn,
                    iteration.EndsOn,
                    iteration.State == "active" ? "var(--state-active-soft)" : "var(--surface-2)")),
                .. await overlays.GetOverlaysAsync(null, null, from, to, ct),
            ],
            Pool: [],
            Coverage: [],
            CanAssign: user.HasAnyRole(
                ContextualRole.ProjectLead,
                ContextualRole.ProductOwner,
                ContextualRole.UnitHead,
                ContextualRole.DepartmentHead,
                ContextualRole.Pmo));
    }

    /// <summary>
    /// Department board: every unit, with each project as a line beneath it.
    /// </summary>
    /// <remarks>
    /// The one board whose rows are not people. A head reading it wants to know where the department's effort is
    /// going, not who individually is doing what — that question is the unit board, one level down.
    /// </remarks>
    private async Task<BoardPayload> DepartmentAsync(Guid? departmentId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var scope = departmentId ?? user.DepartmentIds.FirstOrDefault();

        if (scope == Guid.Empty)
        {
            throw new DomainRuleViolationException("You are not in a department, so there is no department board.");
        }

        var units = await directory.GetUnitsAsync(scope, ct);
        var people = await directory.GetPeopleAsync(null, scope, ct);
        var entries = await activities.GetForPeopleAsync([.. people.Select(person => person.Id)], from, to, ct);

        var unitOf = people.ToDictionary(person => person.Id, person => person.UnitId);

        var codes = await projects.GetProjectCodesAsync(
            [.. entries.Where(entry => entry.ProjectId is not null).Select(entry => entry.ProjectId!.Value).Distinct()],
            ct);

        var resources = new List<BoardResource>();
        var events = new List<BoardEvent>();

        foreach (var unit in units)
        {
            resources.Add(new BoardResource(
                unit.Id.ToString(),
                unit.Name,
                Kind: "unit",
                ParentId: null,
                Color: null,
                SubtitleKey: null));

            var theirs = entries
                .Where(entry => unitOf.GetValueOrDefault(entry.PersonId) == unit.Id)
                .ToList();

            foreach (var projectId in theirs
                         .Where(entry => entry.ProjectId is not null)
                         .Select(entry => entry.ProjectId!.Value)
                         .Distinct())
            {
                var rowId = $"{unit.Id}:{projectId}";

                resources.Add(new BoardResource(
                    rowId,
                    codes.GetValueOrDefault(projectId, "—"),
                    Kind: "project-line",
                    ParentId: unit.Id.ToString(),
                    Color: null,
                    SubtitleKey: null));

                events.AddRange(theirs
                    .Where(entry => entry.ProjectId == projectId)
                    // Read-only from the department board. A head correcting one person's hour does it on the
                    // unit board where they can see whose hour it is; here the rows are units, and dragging a
                    // block would be editing a row that does not belong to one person.
                    .Select(entry => EventFor(entry, rowId, editable: false)));
            }

            events.AddRange(theirs
                .Where(entry => entry.ProjectId is null)
                .Select(entry => EventFor(entry, unit.Id.ToString(), editable: false)));
        }

        return new BoardPayload(
            BoardTypes.Department,
            BoardArchetypes.TaskProgress,
            "board.department",
            from,
            to,
            resources,
            events,
            // Special days and deadlines belong here above all — a patch party or an audit is a department-wide
            // fact, and this is the board where a head plans around them.
            [.. await overlays.GetOverlaysAsync(null, scope, from, to, ct)],
            Pool: [],
            Coverage: [],
            CanAssign: false);
    }

    // --- Shared shaping ----------------------------------------------------------------------------------------

    private async Task<string> ArchetypeForAsync(Guid departmentId, CancellationToken ct)
    {
        var layout = await directory.GetDefaultBoardLayoutAsync(departmentId, ct);

        return layout.Trim().ToLowerInvariant() switch
        {
            "work-orders" or "run" => BoardArchetypes.WorkOrders,
            "shifts" => BoardArchetypes.Shifts,
            _ => BoardArchetypes.TaskProgress,
        };
    }

    private async Task<IReadOnlyList<WorkOrderView>> PoolForAsync(Guid unitId, CancellationToken ct)
    {
        var orders = await workOrders.GetForUnitAsync(unitId, ct);

        var codes = await projects.GetProjectCodesAsync(
            [.. orders.Where(order => order.ProjectId is not null).Select(order => order.ProjectId!.Value).Distinct()],
            ct);

        return
        [
            .. orders
                .Where(order => order.State == WorkOrderState.Unassigned)
                .Select(order => new WorkOrderView(
                    order.Id,
                    order.Reference,
                    order.Title,
                    order.Description,
                    order.Source,
                    order.ExternalRef,
                    order.ProjectId,
                    order.ProjectId is { } id ? codes.GetValueOrDefault(id) : null,
                    order.ActivityTypeCode,
                    order.UnitId,
                    order.AssignedToPersonId,
                    AssignedToName: null,
                    order.ScheduledStart,
                    order.ScheduledEnd,
                    order.EstimatedHours,
                    order.State.ToString().ToLowerInvariant())),
        ];
    }

    private async Task<IReadOnlyList<CoverageWarning>> CoverageForAsync(
        Guid? departmentId,
        IReadOnlyList<Shift> unitShifts,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (departmentId is not { } department)
        {
            return [];
        }

        var templates = await directory.GetShiftTemplatesAsync(department, ct);

        return
        [
            .. ShiftCoverage.Check(templates, unitShifts, from, to)
                .Select(gap => new CoverageWarning(gap.Day, gap.TemplateCode, gap.Required, gap.Scheduled)),
        ];
    }

    private static BoardResource PersonRow(Directory.Contracts.PersonSummary person) =>
        new(
            person.Id.ToString(),
            person.DisplayName,
            Kind: "person",
            ParentId: null,
            Color: null,
            SubtitleKey: null);

    private static BoardEvent ShiftEvent(Shift shift) =>
        new(
            shift.Id.ToString(),
            shift.PersonId.ToString(),
            $"shift.{shift.TemplateCode}",
            shift.Start,
            shift.End,
            Kind: "shift",
            Color: null,
            CssClass: $"shift shift--{shift.TemplateCode}",
            Progress: null,
            Editable: true,
            ActivityTypeCode: null,
            ProjectId: null,
            ExternalRef: null);

    /// <summary>
    /// Turns an activity entry into a timeline event.
    /// </summary>
    /// <remarks>
    /// Progress is set only on a planned slot that an actual has reconciled, and it is the ratio of the two — the
    /// spec's "progress reflects actuals vs plan". A typed-in percentage would be a second number to keep true;
    /// this one cannot drift because it is derived from the hours themselves.
    /// </remarks>
    private static BoardEvent EventFor(ActivityEntryView entry, string resourceId, bool editable) =>
        new(
            entry.Id.ToString(),
            resourceId,
            entry.ActivityTypeLabelKey,
            entry.SlotStart,
            entry.SlotEnd,
            entry.Kind,
            ColorFor(entry.ActivityTypeCode),
            entry.Kind == "planned" ? "event event--planned" : "event event--actual",
            entry.Kind == "planned" && entry.Reconciled ? 100 : null,
            editable,
            entry.ActivityTypeCode,
            entry.ProjectId,
            entry.ExternalRef);

    /// <summary>
    /// Whether the caller may move this block.
    /// </summary>
    /// <remarks>
    /// Only planned slots, ever. A board is where intent is arranged; what somebody recorded they actually did is
    /// not something a drag gesture should quietly rewrite.
    /// </remarks>
    private static bool CanEdit(ActivityEntryView entry) => entry.Kind == "planned";

    private static string? ColorFor(string code) => BucketColors.GetValueOrDefault(code);

    private static string? LabelFor(IReadOnlyList<ActivityEntryView> entries, string code) =>
        entries.FirstOrDefault(entry =>
            string.Equals(entry.ActivityTypeCode, code, StringComparison.OrdinalIgnoreCase))?.ActivityTypeLabelKey
        ?? $"activity.type.{code}";
}
