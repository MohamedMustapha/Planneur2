using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Contracts;
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
    IActivityTaxonomyReader taxonomy,
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
    /// My board: the activity buckets as categories, the things I actually worked on as rows beneath them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rows are activity, not days, because the design's board is a timeline: days are the horizontal axis, and
    /// making them rows too would produce a grid with the same thing on both axes.
    /// </para>
    /// <para>
    /// Two levels rather than one, because a flat bucket row answers the wrong question. "You spent 22 hours on
    /// BUILD" is a number nobody acts on; "14 on the SI rewrite, 8 on the HR portal" is the sentence people
    /// actually want the board to say. So BUILD and RUN open onto the projects the week was spent against, while
    /// the two non-project buckets open onto the department's own subtypes — which is where their detail lives.
    /// </para>
    /// </remarks>
    private async Task<BoardPayload> MyAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var entries = await activities.GetForPeopleAsync([user.UserId], from, to, ct);
        // Null, not the caller's department id: the reader already resolves "mine" that way, and passing one of
        // several department ids by hand would pick the wrong taxonomy for anyone who spans two.
        var types = await taxonomy.GetTypesAsync(null, ct);

        var rows = MyRows(entries, types);

        return new BoardPayload(
            BoardTypes.My,
            BoardArchetypes.TaskProgress,
            "board.my",
            from,
            to,
            [.. rows.Resources],
            [.. entries.Select(entry => EventFor(entry, rows.RowIdFor(entry), CanEdit(entry)))],
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

        // Resolved once and handed to both consumers below. The walk is a couple of queries, and doing it twice
        // per board would also make it possible for the archetype and the capabilities on one screen to come
        // from two different reads.
        var profile = departmentId is { } profiled
            ? await directory.GetNodeProfileAsync(scope, profiled, ct)
            : null;

        var archetype = departmentId is { } department
            ? await ArchetypeForAsync(profile, department, ct)
            : BoardArchetypes.TaskProgress;

        // Archetype and capability are checked separately because they answer different questions: the archetype
        // is what this board looks like, the capability is whether this branch does that work at all. A profile
        // that names the work-order board but switches the pool off gets the board without the unassigned row,
        // which is a coherent thing for a unit that receives its orders already assigned.
        var pool = archetype == BoardArchetypes.WorkOrders && Allows(profile, NodeCapabilities.WorkOrderPool)
            ? await PoolForAsync(scope, ct)
            : [];

        var unitShifts = archetype == BoardArchetypes.Shifts && Allows(profile, NodeCapabilities.ShiftScheduling)
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
            CanAssign: user.HasAnyRole(ContextualRole.NodeHead, ContextualRole.Pmo));
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
            CanAssign: user.HasAnyRole(ContextualRole.NodeHead, ContextualRole.Pmo));
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
                ContextualRole.NodeHead,
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

    /// <summary>
    /// Which board this node renders with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The profile decides, and it decides by naming an archetype rather than by having a code this method
    /// recognises — which is the difference v2 §10.0 draws between behaviour that is data and behaviour that is a
    /// branch. Two sibling units under one department reach here with different profiles and leave with different
    /// boards, and nothing in this file knows or cares what either of them does for a living.
    /// </para>
    /// <para>
    /// First entry wins where a profile names several. The list is ordered by preference precisely so an
    /// administrator can express "work orders, or task progress if that is not available" without the platform
    /// having an opinion about which is more appropriate for them.
    /// </para>
    /// <para>
    /// The fallback is the pre-v2 department layout, kept because a deployment that has authored no profiles must
    /// keep the boards it had. It maps the two legacy layout values onto archetypes and is the only place that
    /// still reads them.
    /// </para>
    /// </remarks>
    private async Task<string> ArchetypeForAsync(
        NodeProfileSnapshot? profile,
        Guid departmentId,
        CancellationToken ct)
    {
        if (profile is not null)
        {
            foreach (var archetype in profile.BoardArchetypes)
            {
                // Mapping archetype names onto the components that exist is a rendering concern, not an
                // organizational one, so it is allowed to be a branch — what v2 §10.6 forbids is branching on the
                // profile's *code*. "week-grid" is the plain canvas, which is the task-progress board with
                // neither a pool nor shifts on it; there is no fourth component to render.
                var renderable = archetype.Trim().ToLowerInvariant() switch
                {
                    BoardArchetypes.WorkOrders => BoardArchetypes.WorkOrders,
                    BoardArchetypes.Shifts => BoardArchetypes.Shifts,
                    BoardArchetypes.TaskProgress or "week-grid" => BoardArchetypes.TaskProgress,
                    _ => null,
                };

                if (renderable is not null)
                {
                    return renderable;
                }
            }
        }

        var layout = await directory.GetDefaultBoardLayoutAsync(departmentId, ct);

        return layout.Trim().ToLowerInvariant() switch
        {
            "work-orders" or "run" => BoardArchetypes.WorkOrders,
            "shifts" => BoardArchetypes.Shifts,
            _ => BoardArchetypes.TaskProgress,
        };
    }

    /// <summary>
    /// Whether a capability is on for a node, defaulting to on where no profile is in force.
    /// </summary>
    /// <remarks>
    /// On by default so this slice never removes a control from a deployment that has authored nothing. A branch
    /// loses the work-order pool because an administrator switched it off, not because profiles shipped.
    /// </remarks>
    private static bool Allows(NodeProfileSnapshot? profile, string capability) =>
        profile is null || profile.Allows(capability);

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
    /// Progress prefers what somebody said, and falls back to what the hours imply. A plan its actual has
    /// reconciled is finished whether or not anyone dragged the handle, so the derived answer stays; but a task
    /// that spans three days is half done long before any of its hours are logged, and only the person doing it
    /// can say so. The explicit figure therefore wins where it exists, and nothing invents one where it does not.
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
            entry.PercentComplete ?? (entry.Kind == "planned" && entry.Reconciled ? 100 : null),
            editable,
            entry.ActivityTypeCode,
            entry.ProjectId,
            entry.ExternalRef,
            entry.Note);

    /// <summary>
    /// Whether the caller may move this block.
    /// </summary>
    /// <remarks>
    /// Only planned slots, ever. A board is where intent is arranged; what somebody recorded they actually did is
    /// not something a drag gesture should quietly rewrite.
    /// </remarks>
    private static bool CanEdit(ActivityEntryView entry) => entry.Kind == "planned";

    /// <summary>
    /// The personal board's two levels, and the mapping from an entry to the row it belongs on.
    /// </summary>
    /// <remarks>
    /// Built together and returned together, because the rows and the lookup are the same decision seen twice: a
    /// row exists precisely because some entry resolves to it. Computing them separately is how a board ends up
    /// with events pointing at rows that were never emitted, which Mobiscroll renders as nothing at all.
    /// </remarks>
    private static MyBoardRows MyRows(
        IReadOnlyList<ActivityEntryView> entries,
        IReadOnlyList<ActivityTypeOption> types)
    {
        // Which bucket a code belongs to. A department subtype resolves through its parent; a canonical bucket is
        // its own. Anything the taxonomy does not know falls back to itself, so an entry logged against a type
        // that has since been removed from the configuration still lands somewhere visible.
        var bucketOf = types.ToDictionary(
            type => type.Code,
            type => type.ParentCode ?? type.Code,
            StringComparer.OrdinalIgnoreCase);

        string Bucket(string code) => bucketOf.GetValueOrDefault(code, code);

        var resources = new List<BoardResource>();
        var rowIds = new Dictionary<string, string>(StringComparer.Ordinal);

        // The canonical four always, plus any bucket this week's entries reached that is not one of them, so a
        // department with its own top-level bucket still sees it.
        var buckets = BucketColors.Keys
            .Concat(entries.Select(entry => Bucket(entry.ActivityTypeCode)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        foreach (var bucket in buckets)
        {
            resources.Add(new BoardResource(
                bucket,
                bucket,
                Kind: "category",
                ParentId: null,
                ColorFor(bucket),
                SubtitleKey: LabelFor(entries, bucket)));

            var children = ProjectBuckets.Contains(bucket)
                ? ProjectRowsFor(entries, bucket, Bucket)
                : SubtypeRowsFor(entries, types, bucket, Bucket);

            foreach (var (key, child) in children)
            {
                resources.Add(child);
                rowIds[key] = child.Id;
            }

            // A category with nothing under it still gets one row, so an empty week reads as a board waiting for
            // entries rather than as four headers over blank space — and so there is somewhere to click.
            if (children.Count == 0)
            {
                var placeholder = new BoardResource(
                    $"{bucket}:none",
                    bucket,
                    Kind: "lane",
                    ParentId: bucket,
                    ColorFor(bucket),
                    SubtitleKey: LabelFor(entries, bucket));

                resources.Add(placeholder);
            }
        }

        return new MyBoardRows(resources, rowIds, Bucket);
    }

    /// <summary>One row per project the week was spent against, plus one for the hours that named none.</summary>
    private static List<(string Key, BoardResource Row)> ProjectRowsFor(
        IReadOnlyList<ActivityEntryView> entries,
        string bucket,
        Func<string, string> bucketOf) =>
        [.. entries
            .Where(entry => string.Equals(bucketOf(entry.ActivityTypeCode), bucket, StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry => entry.ProjectId)
            .OrderBy(group => group.Key is null)
            .ThenBy(group => group.First().ProjectCode, StringComparer.Ordinal)
            .Select(group =>
            {
                var id = $"{bucket}:{group.Key?.ToString() ?? "none"}";

                return (
                    Key: $"{bucket}|{group.Key?.ToString() ?? string.Empty}",
                    Row: new BoardResource(
                        id,
                        // The code is the name — a project code is not a translation key and must not be run
                        // through the dictionary. SubtitleKey stays null for exactly that reason.
                        group.First().ProjectCode ?? string.Empty,
                        Kind: group.Key is null ? "lane" : "project-line",
                        ParentId: bucket,
                        ColorFor(bucket),
                        SubtitleKey: group.Key is null ? "board.noProject" : null));
            })];

    /// <summary>One row per configured subtype the week actually used, falling back to the bucket itself.</summary>
    private static List<(string Key, BoardResource Row)> SubtypeRowsFor(
        IReadOnlyList<ActivityEntryView> entries,
        IReadOnlyList<ActivityTypeOption> types,
        string bucket,
        Func<string, string> bucketOf) =>
        [.. entries
            .Where(entry => string.Equals(bucketOf(entry.ActivityTypeCode), bucket, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.ActivityTypeCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal)
            .Select(code => (
                Key: $"{bucket}|{code}",
                Row: new BoardResource(
                    $"{bucket}:{code}",
                    code,
                    Kind: "lane",
                    ParentId: bucket,
                    ColorFor(bucket),
                    SubtitleKey: types.FirstOrDefault(type =>
                            string.Equals(type.Code, code, StringComparison.OrdinalIgnoreCase))?.LabelKey
                        ?? LabelFor(entries, code))))];

    /// <summary>
    /// The buckets whose detail is a project rather than a subtype.
    /// </summary>
    /// <remarks>
    /// The same two the taxonomy marks <c>requiresProject</c>, but stated here rather than read from it: a
    /// department may add a subtype that requires a project under quality-of-life, and that must not turn the
    /// training bucket into a project list.
    /// </remarks>
    private static readonly HashSet<string> ProjectBuckets =
        new(["project-build", "project-run"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The personal board's rows, and which one an entry belongs on.</summary>
    private sealed record MyBoardRows(
        IReadOnlyList<BoardResource> Resources,
        IReadOnlyDictionary<string, string> RowIds,
        Func<string, string> BucketOf)
    {
        /// <summary>
        /// Where this entry draws.
        /// </summary>
        /// <remarks>
        /// Falls back to the category itself rather than to nothing: an event with no row silently disappears,
        /// and an hour somebody logged vanishing from their own board is the worst failure this screen has.
        /// </remarks>
        public string RowIdFor(ActivityEntryView entry)
        {
            var bucket = BucketOf(entry.ActivityTypeCode);

            var key = ProjectBuckets.Contains(bucket)
                ? $"{bucket}|{entry.ProjectId?.ToString() ?? string.Empty}"
                : $"{bucket}|{entry.ActivityTypeCode}";

            return RowIds.GetValueOrDefault(key, bucket);
        }
    }

    private static string? ColorFor(string code) => BucketColors.GetValueOrDefault(code);

    private static string? LabelFor(IReadOnlyList<ActivityEntryView> entries, string code) =>
        entries.FirstOrDefault(entry =>
            string.Equals(entry.ActivityTypeCode, code, StringComparison.OrdinalIgnoreCase))?.ActivityTypeLabelKey
        ?? $"activity.type.{code}";
}
