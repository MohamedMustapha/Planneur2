using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Reporting.Contracts;
using Cracra.Modules.Reporting.Domain;

namespace Cracra.Modules.Reporting.Application;

/// <summary>
/// Assembles a report from the modules that own its numbers.
/// </summary>
/// <remarks>
/// <para>
/// The read side in one class, mirroring what S6's board composer does for timelines and for the same reason: six
/// scopes over six modules' data is exactly the assembly that goes wrong when it is spread out. Here the whole
/// map from "who is asking about what" to "which sections, with which figures" is one file somebody can read.
/// </para>
/// <para>
/// Every figure is computed here, in code. The model never adds anything up — see <see cref="SummaryPrompt"/> for
/// why that division is the point of the slice rather than an implementation choice.
/// </para>
/// <para>
/// Nothing here filters by role. <see cref="ReportScope"/> already decided which question this viewer may ask;
/// RLS already decided which rows answer it. A third check here would be a third place for the matrix to be
/// wrong.
/// </para>
/// </remarks>
internal sealed class ReportComposer(
    IActivityQueries activities,
    IDirectoryQueries directory,
    IOrgNodeQueries nodes,
    IProjectQueries projects,
    IPortfolioQueries portfolio,
    IMeetingQueries meetings,
    IScheduleQueries schedule,
    IKudosQueries kudos,
    IUserContext user)
{
    /// <summary>A table of every person in a unit is useful; a table of four hundred is not.</summary>
    private const int MaximumRows = 100;

    /// <summary>
    /// How many projects the team report examines.
    /// </summary>
    /// <remarks>
    /// Lower than <see cref="MaximumRows"/> because this one costs two queries per project — its activity and its
    /// iterations — and a head with sixty projects would otherwise run a hundred and twenty. Where the cap bites,
    /// the section says so rather than quietly presenting a partial answer as a complete one.
    /// </remarks>
    private const int MaximumProjects = 25;

    /// <summary>Activity buckets whose hours count as quality-of-life work, for the section that credits it.</summary>
    private const string QualityOfLifeCode = "quality-of-life";

    public async Task<ReportView> ComposeAsync(ReportDescriptor descriptor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var period = descriptor.Period;

        var (sections, label) = descriptor.Scope switch
        {
            ReportScopes.Node => await NodeAsync(descriptor.ScopeId, period, ct),
            ReportScopes.Item => await ItemAsync(descriptor.ScopeId, period, ct),
            ReportScopes.Portfolio => await PortfolioAsync(period, ct),
            _ => await MeAsync(period, ct),
        };

        return new ReportView(
            ReportIdentity.Encode(descriptor),
            descriptor.Scope,
            descriptor.ScopeId,
            label,
            period.ToView(),
            descriptor.Language,
            sections,
            ReportScope.Available(user),
            Summary: null,
            DateTimeOffset.UtcNow,
            await HeadlineAsync(descriptor.Scope, sections, ct));
    }

    /// <summary>
    /// The profile's headline sentence, rendered from figures this report already computed (v2 §10.5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read back out of <paramref name="sections"/> rather than recomputed from the entries. Two reasons, and the
    /// second is the important one: it costs no extra query, and it makes a headline that disagrees with the body
    /// of its own report impossible to write. A sentence that says "412 h" above a table totalling 380 destroys
    /// confidence in every other number on the page, and re-deriving it from the same source would only make that
    /// mismatch unlikely rather than unreachable.
    /// </para>
    /// <para>
    /// Only for node-scoped reports. An item or a portfolio spans branches by construction, so there is no one
    /// profile in force over it — rendering some contributor's sentence across a cross-branch item would be
    /// picking a branch's vocabulary arbitrarily and presenting it as the report's own.
    /// </para>
    /// </remarks>
    private async Task<string?> HeadlineAsync(
        string scope,
        IReadOnlyList<ReportSection> sections,
        CancellationToken ct)
    {
        if (scope is ReportScopes.Item or ReportScopes.Portfolio)
        {
            return null;
        }

        var profile = await nodes.HomeNodeAsync(user.UserId, ct) is { } home
            ? await directory.NodeProfileAsync(home, ct)
            : null;

        if (profile?.HeadlinePattern is null)
        {
            return null;
        }

        var hours = sections.FirstOrDefault(section => section.Key == "hours");

        // My-scope reports cover one person and carry no headcount metric, which is the correct answer rather
        // than a missing one.
        var people = (int)(MetricOf(sections, "headcount")
            ?? MetricOf(sections, "members")
            ?? 1m);

        var leading = hours?.Tables
            .FirstOrDefault(table => table.TitleKey == "reports.table.hoursByType")?.Rows
            .Where(row => !CanonicalBuckets.Contains(row.Key))
            .Select(row => (Code: row.Key, LabelKey: row.Label, Hours: row.Values.FirstOrDefault()))
            .OrderByDescending(row => row.Hours)
            .ToList() ?? [];

        return HeadlinePattern.Render(
            profile.HeadlinePattern,
            HeadlinePattern.Values(
                people,
                MetricOf(sections, "actualHours") ?? 0m,
                leading,
                sections.Sum(section => section.Notes.Count(note => note.Severity is "critical" or "warning"))));
    }

    /// <summary>
    /// The four buckets are excluded from the headline's subtypes on purpose.
    /// </summary>
    /// <remarks>
    /// They are the platform's fixed vocabulary and appear in every branch's table, so binding <c>c1</c> to one
    /// would give a delivery branch and a casework branch the same headline — which is exactly the sameness v2
    /// §10 exists to remove. What distinguishes them is what they put <em>underneath</em> those buckets.
    /// </remarks>
    private static readonly HashSet<string> CanonicalBuckets = new(StringComparer.OrdinalIgnoreCase)
    {
        "project-build", "project-run", "quality-of-life", "recruitment-admin",
    };

    private static decimal? MetricOf(IReadOnlyList<ReportSection> sections, string key) =>
        sections
            .SelectMany(section => section.Metrics)
            .FirstOrDefault(metric => metric.Key == key)
            ?.Value;

    // --- My work ---------------------------------------------------------------------------------------------

    /// <summary>Hours by bucket, where that leaves me against the target, where the work came from, what is next.</summary>
    private async Task<(IReadOnlyList<ReportSection> Sections, string Label)> MeAsync(
        ReportPeriod period,
        CancellationToken ct)
    {
        var entries = await activities.ForPeopleAsync([user.UserId], period.From, period.To, ct);
        var (target, enforced) = await activities.TargetAsync(user.DepartmentIds.FirstOrDefault(), ct);

        var me = await directory.PersonAsync(user.UserId, ct);

        return (
            [
                HoursSection(entries, target, enforced, period),
                SourcesSection(entries),
                await AgendaSectionAsync(period, ct),
            ],
            me?.DisplayName ?? user.UserName);
    }

    // --- A branch of the tree -------------------------------------------------------------------------------

    /// <summary>
    /// The node report: child branches where the node has any, the people attached there where it has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One method where there were three. A unit report and a department report asked the same question of two
    /// rungs and differed only in what their rows were — people or units — which is a fact about the node, not
    /// about the level it sits at. Asking the tree makes the fourth level free.
    /// </para>
    /// <para>
    /// Read-only in both shapes and identical in section keys, so a head can hand this upward and their parent's
    /// report accepts it as one block — which is the composability v2 01.4 is after.
    /// </para>
    /// </remarks>
    private async Task<(IReadOnlyList<ReportSection> Sections, string Label)> NodeAsync(
        Guid? scopeId,
        ReportPeriod period,
        CancellationToken ct)
    {
        var nodeId = scopeId
            ?? await nodes.HomeNodeAsync(user.UserId, ct)
            ?? throw new DomainRuleViolationException(
                "You are not attached to a branch, so there is no branch report.");

        var subtree = await nodes.SubtreeAsync(nodeId, ct);
        var self = subtree.FirstOrDefault(node => node.Id == nodeId)
            // RLS hid it or it does not exist, and the report cannot tell the two apart — which is the point.
            ?? throw new ResourceNotFoundException("That branch does not exist.");

        var children = subtree.Where(node => node.ParentId == nodeId && node.Active).ToList();
        var people = await nodes.PeopleInSubtreeAsync(nodeId, ct);
        var entries = await activities.ForPeopleAsync(
            [.. people.Select(person => person.PersonId)],
            period.From,
            period.To,
            ct);

        var rows = children.Count > 0
            ? ChildRows(nodeId, children, people, entries)
            : PersonRows(people, entries);

        var unitId = people.Select(person => person.UnitId).FirstOrDefault(id => id is not null);
        var load = unitId is { } unit
            ? await schedule.LoadAsync(unit, period.From, period.To, ct)
            : null;

        var kudoCount = unitId is { } kudoUnit
            ? await kudos.CountAsync(kudoUnit, null, period.From, period.To, ct)
            : 0;

        return (
            [
                new ReportSection(
                    "members",
                    "reports.section.members",
                    [
                        new ReportMetric("headcount", people.Count, "count"),
                        new ReportMetric("branches", children.Count, "count"),
                    ],
                    [new ReportTable(
                        children.Count > 0 ? "reports.table.branchRollup" : "reports.table.memberActivity",
                        children.Count > 0
                            ?
                            [
                                "reports.column.headcount",
                                "reports.column.actualHours",
                                "reports.column.qolHours",
                            ]
                            :
                            [
                                "reports.column.actualHours",
                                "reports.column.plannedHours",
                                "reports.column.qolHours",
                            ],
                        rows,
                        // Only the people shape names people. Everything downstream — the prompt's pseudonymizer
                        // above all — keys off this flag rather than guessing from the table's title.
                        IdentifiesPeople: children.Count == 0)],
                    []),
                new ReportSection(
                    "runLoad",
                    "reports.section.runLoad",
                    load is null
                        ? []
                        :
                        [
                            new ReportMetric("openWorkOrders", load.OpenWorkOrders, "count"),
                            new ReportMetric("assignedWorkOrders", load.AssignedWorkOrders, "count"),
                            new ReportMetric("workOrderHours", load.EstimatedHours, "hours"),
                            new ReportMetric("shiftHours", load.ShiftHours, "hours"),
                            new ReportMetric("coverageGaps", load.CoverageGaps, "count"),
                        ],
                    [],
                    load is { CoverageGaps: > 0 }
                        ? [new ReportNote("reports.note.coverageGaps", null, "warning")]
                        : []),
                new ReportSection(
                    "qol",
                    "reports.section.qol",
                    [
                        new ReportMetric("qolHours", HoursOf(entries, QualityOfLifeCode), "hours"),
                        new ReportMetric("kudos", kudoCount, "count"),
                    ],
                    [],
                    []),
                await AgendaSectionAsync(period, ct),
            ],
            self.Name);
    }

    /// <summary>One row per direct child, each aggregated over its whole subtree.</summary>
    private static List<ReportRow> ChildRows(
        Guid nodeId,
        IReadOnlyList<Directory.Contracts.OrgNodeSummary> children,
        IReadOnlyList<Directory.Contracts.NodeMember> people,
        IReadOnlyList<ActivityEntryView> entries)
    {
        var branchOf = people.ToDictionary(
            person => person.PersonId,
            person => BranchUnder(nodeId, person.NodeAncestorIds));

        return
        [
            .. children
                .OrderBy(child => child.Name, StringComparer.Ordinal)
                .Take(MaximumRows)
                .Select(child =>
                {
                    var theirs = entries
                        .Where(entry => branchOf.GetValueOrDefault(entry.PersonId) == child.Id)
                        .ToList();

                    return new ReportRow(
                        child.Id.ToString(),
                        child.Name,
                        [
                            people.Count(person => branchOf[person.PersonId] == child.Id),
                            Actual(theirs),
                            HoursOf(theirs, QualityOfLifeCode),
                        ]);
                }),
        ];
    }

    private static List<ReportRow> PersonRows(
        IReadOnlyList<Directory.Contracts.NodeMember> people,
        IReadOnlyList<ActivityEntryView> entries)
    {
        var byPerson = entries
            .GroupBy(entry => entry.PersonId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return
        [
            .. people
                .Take(MaximumRows)
                .Select(person =>
                {
                    var theirs = byPerson.GetValueOrDefault(person.PersonId, []);

                    return new ReportRow(
                        person.PersonId.ToString(),
                        person.DisplayName,
                        [Actual(theirs), Planned(theirs), HoursOf(theirs, QualityOfLifeCode)]);
                }),
        ];
    }

    /// <summary>The child of the scope this path passes through, or the scope itself.</summary>
    private static Guid BranchUnder(Guid scope, IReadOnlyList<Guid> path)
    {
        var index = path.ToList().IndexOf(scope);

        return index >= 0 && index + 1 < path.Count ? path[index + 1] : scope;
    }

    // --- One portfolio item ----------------------------------------------------------------------------------

    /// <summary>
    /// The item report: what one thing in the catalog cost and who is carrying it.
    /// </summary>
    /// <remarks>
    /// Scoped by item id rather than by project id (v2 00 3). An item without a delivery row behind it is a real
    /// state — something the catalog knows about that nobody has started running — and it reports its identity
    /// and says so, rather than 404ing on a thing the caller can plainly see in the catalog.
    /// </remarks>
    private async Task<(IReadOnlyList<ReportSection> Sections, string Label)> ItemAsync(
        Guid? scopeId,
        ReportPeriod period,
        CancellationToken ct)
    {
        if (scopeId is not { } id)
        {
            throw new DomainRuleViolationException("An item report needs an item.");
        }

        var item = await portfolio.ItemAsync(id, ct)
            // RLS hid it or it does not exist, and the report cannot tell the two apart — which is the point.
            ?? throw new ResourceNotFoundException("That item does not exist.");

        var label = $"{item.Code} — {item.Name}";

        if (item.ProjectId is not { } projectId)
        {
            return (
                [
                    new ReportSection(
                        "identity",
                        "reports.section.identity",
                        [],
                        [],
                        [new ReportNote($"reports.note.state.{item.State}", null, null),
                         new ReportNote("reports.note.notRunning", null, "info")]),
                ],
                label);
        }

        var project = await projects.ProjectAsync(projectId, ct)
            ?? throw new ResourceNotFoundException("That item does not exist.");

        var entries = await activities.ForProjectAsync(projectId, period.From, period.To, ct);
        var team = await projects.TeamAsync(projectId, ct);
        var iterations = await portfolio.IterationsAsync(projectId, ct);

        // Grouped by the branch each hour hangs off, not by the department its owner is filed under (v2 00 3).
        // An item is cross-branch by construction, and the branch is the thing a head recognises as theirs.
        var byNode = entries
            .GroupBy(entry => entry.NodeId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var nodeNames = await NodeNamesAsync([.. byNode.Keys], ct);

        var contribution = byNode
            .Select(pair => new ReportRow(
                pair.Key.ToString(),
                nodeNames.GetValueOrDefault(pair.Key, pair.Key.ToString()),
                [
                    Actual(pair.Value),
                    // The split as a percentage, computed here rather than left to the reader. A report that
                    // makes you divide two of its own numbers has not finished its job.
                    Percent(Actual(pair.Value), Actual(entries)),
                    pair.Value.Select(entry => entry.PersonId).Distinct().Count(),
                ]))
            .OrderByDescending(row => row.Values[0])
            .ToList();

        var current = iterations.FirstOrDefault(iteration =>
            iteration.StartsOn <= period.To && iteration.EndsOn >= period.From);

        return (
            [
                new ReportSection(
                    "burn",
                    "reports.section.burn",
                    [
                        new ReportMetric("actualHours", Actual(entries), "hours"),
                        new ReportMetric("plannedHours", Planned(entries), "hours"),
                        new ReportMetric("members", team.Count, "count"),
                        new ReportMetric(
                            "iterationDaysElapsed",
                            current is null ? 0 : DaysElapsed(current.StartsOn, current.EndsOn, period.To),
                            "count"),
                    ],
                    [new ReportTable(
                        "reports.table.iterations",
                        ["reports.column.sequence", "reports.column.daysElapsed"],
                        [
                            .. iterations.Select(iteration => new ReportRow(
                                iteration.Id.ToString(),
                                iteration.Name,
                                [iteration.Sequence, DaysElapsed(iteration.StartsOn, iteration.EndsOn, period.To)])),
                        ])],
                    current is null
                        ? [new ReportNote("reports.note.noIteration", null, "info")]
                        : []),
                new ReportSection(
                    "contribution",
                    "reports.section.contribution",
                    [],
                    [new ReportTable(
                        "reports.table.contribution",
                        ["reports.column.actualHours", "reports.column.share", "reports.column.members"],
                        contribution)],
                    []),
                new ReportSection(
                    "cost",
                    "reports.section.cost",
                    [new ReportMetric("cost", project.CostAmount, "currency")],
                    [],
                    []),
            ],
            label);
    }

    /// <summary>Branch names for a scattered set of node ids, resolved through one subtree read per root.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> NodeNamesAsync(
        IReadOnlyList<Guid> nodeIds,
        CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();

        if (await nodes.HomeNodeAsync(user.UserId, ct) is { } home)
        {
            foreach (var node in await nodes.SubtreeAsync(home, ct))
            {
                names[node.Id] = node.Name;
            }
        }

        // Anything the caller's own subtree does not cover is asked for directly. A cross-branch item is the
        // normal case here, and a row labelled with a raw id is a row nobody can act on.
        foreach (var nodeId in nodeIds.Where(nodeId => !names.ContainsKey(nodeId)))
        {
            var found = (await nodes.SubtreeAsync(nodeId, ct)).FirstOrDefault(node => node.Id == nodeId);

            if (found is not null)
            {
                names[nodeId] = found.Name;
            }
        }

        return names;
    }

    // --- Portfolio -------------------------------------------------------------------------------------------

    private async Task<(IReadOnlyList<ReportSection> Sections, string Label)> PortfolioAsync(
        ReportPeriod period,
        CancellationToken ct)
    {
        var board = await portfolio.BoardAsync(ct);

        var stateRows = board.Lanes
            .Select(lane => new ReportRow(
                lane.State,
                $"portfolio.state.{lane.State}",
                [lane.Items.Count, lane.Items.Sum(item => item.CostAmount ?? 0m)]))
            .ToList();

        var archived = board.Lanes
            .Where(lane => lane.State is "dephase")
            .SelectMany(lane => lane.Items)
            .Take(MaximumRows)
            .ToList();

        var priority = board.Lanes
            .SelectMany(lane => lane.Items)
            .OrderByDescending(item => item.Priority)
            .Take(10)
            .Select(item => new ReportRow(item.Id.ToString(), item.Name, [item.Priority, item.IterationCount]))
            .ToList();

        return (
            [
                new ReportSection(
                    "states",
                    "reports.section.states",
                    [new ReportMetric("items", board.Lanes.Sum(lane => lane.Items.Count), "count")],
                    [new ReportTable(
                        "reports.table.states",
                        ["reports.column.items", "reports.column.cost"],
                        stateRows)],
                    []),
                new ReportSection(
                    "archived",
                    "reports.section.archived",
                    [new ReportMetric("archived", archived.Count, "count")],
                    [],
                    // Named, because "three projects went déphasé" is the sentence somebody actually needs and
                    // the count alone does not carry it. Project names are not PII and are not masked.
                    [.. archived.Select(item => new ReportNote("reports.note.archived", item.Name, "info"))]),
                new ReportSection(
                    "priority",
                    "reports.section.priority",
                    [],
                    [new ReportTable(
                        "reports.table.priority",
                        ["reports.column.priority", "reports.column.iterations"],
                        priority)],
                    []),
                await AgendaSectionAsync(period, ct),
            ],
            "reports.scope.portfolio");
    }

    // --- Shared sections -------------------------------------------------------------------------------------

    private static ReportSection HoursSection(
        IReadOnlyList<ActivityEntryView> entries,
        decimal target,
        bool enforced,
        ReportPeriod period)
    {
        // The target is weekly, so a month's report compares against the weeks it contains rather than against
        // one week's figure. Reporting 150 hours as "115 over target" would be arithmetic nobody trusts again.
        var weeks = Math.Max(1m, Math.Round(period.Days / 7m, 2));
        var scaled = Math.Round(target * weeks, 2);
        var actual = Actual(entries);

        var byType = entries
            .GroupBy(entry => entry.ActivityTypeCode)
            .Select(group => new ReportRow(
                group.Key,
                group.First().ActivityTypeLabelKey,
                [
                    group.Where(IsActual).Sum(entry => entry.Hours),
                    group.Where(entry => !IsActual(entry)).Sum(entry => entry.Hours),
                ]))
            .OrderByDescending(row => row.Values[0])
            .ToList();

        return new ReportSection(
            "hours",
            "reports.section.hours",
            [
                new ReportMetric("targetHours", scaled, "hours"),
                new ReportMetric("actualHours", actual, "hours"),
                new ReportMetric("plannedHours", Planned(entries), "hours"),
                new ReportMetric("overtimeHours", Math.Max(0m, actual - scaled), "hours"),
                new ReportMetric("targetShare", Percent(actual, scaled), "percent"),
            ],
            [new ReportTable(
                "reports.table.hoursByType",
                ["reports.column.actualHours", "reports.column.plannedHours"],
                byType)],
            actual > scaled
                ? [new ReportNote(
                    enforced ? "reports.note.overTargetEnforced" : "reports.note.overTarget",
                    null,
                    enforced ? "critical" : "warning")]
                : []);
    }

    /// <summary>
    /// Where the work came from: typed in, or pulled from a connected system.
    /// </summary>
    /// <remarks>
    /// A small section that answers a question S10 makes interesting — how much of a week arrives through the
    /// integrations rather than through the form. Until those adapters land it reads "all manual", which is both
    /// true and the baseline the comparison will need.
    /// </remarks>
    private static ReportSection SourcesSection(IReadOnlyList<ActivityEntryView> entries)
    {
        var rows = entries
            .GroupBy(entry => entry.Source)
            .Select(group => new ReportRow(
                group.Key,
                $"activity.source.{group.Key}",
                [group.Count(), group.Where(IsActual).Sum(entry => entry.Hours)]))
            .OrderByDescending(row => row.Values[1])
            .ToList();

        return new ReportSection(
            "sources",
            "reports.section.sources",
            [new ReportMetric("entries", entries.Count, "count")],
            [new ReportTable(
                "reports.table.sources",
                ["reports.column.entries", "reports.column.actualHours"],
                rows)],
            []);
    }

    /// <summary>
    /// What is coming: meetings and special days inside the window.
    /// </summary>
    /// <remarks>
    /// Notes rather than a table. These are sentences a reader acts on — "the audit is on the 14th" — and a
    /// two-column grid is the wrong shape for something with one number in it.
    /// </remarks>
    private async Task<ReportSection> AgendaSectionAsync(ReportPeriod period, CancellationToken ct)
    {
        var entries = await meetings.InWindowAsync(period.From, period.To, ct);

        var notes = entries
            .Take(MaximumRows)
            .Select(entry => new ReportNote(
                $"reports.note.agenda.{(entry.Severity is null ? "meeting" : "specialDay")}",
                $"{entry.At:yyyy-MM-dd} · {entry.NameKey}",
                entry.Severity))
            .ToList();

        return new ReportSection(
            "agenda",
            "reports.section.agenda",
            [
                new ReportMetric("meetings", entries.Count(entry => entry.Severity is null), "count"),
                new ReportMetric("specialDays", entries.Count(entry => entry.Severity is not null), "count"),
            ],
            [],
            notes);
    }

    // --- Arithmetic ------------------------------------------------------------------------------------------

    /// <summary>An actual is what happened; a plan is an intention. Only the first counts as hours worked.</summary>
    private static bool IsActual(ActivityEntryView entry) =>
        string.Equals(entry.Kind, "actual", StringComparison.OrdinalIgnoreCase);

    private static decimal Actual(IEnumerable<ActivityEntryView> entries) =>
        entries.Where(IsActual).Sum(entry => entry.Hours);

    private static decimal Planned(IEnumerable<ActivityEntryView> entries) =>
        entries.Where(entry => !IsActual(entry)).Sum(entry => entry.Hours);

    private static decimal HoursOf(IEnumerable<ActivityEntryView> entries, string code) =>
        entries
            .Where(entry => IsActual(entry)
                && entry.ActivityTypeCode.StartsWith(code, StringComparison.OrdinalIgnoreCase))
            .Sum(entry => entry.Hours);

    /// <summary>A share, to one decimal. Zero denominator is zero rather than an exception or a NaN.</summary>
    private static decimal Percent(decimal part, decimal whole) =>
        whole == 0m ? 0m : Math.Round(part / whole * 100m, 1);

    /// <summary>
    /// How far into an iteration the period's end falls.
    /// </summary>
    /// <remarks>
    /// Clamped at both ends: an iteration that has not started reads zero rather than negative, and one already
    /// finished reads its full length rather than continuing to grow.
    /// </remarks>
    private static decimal DaysElapsed(DateOnly startsOn, DateOnly endsOn, DateOnly asOf)
    {
        var length = endsOn.DayNumber - startsOn.DayNumber + 1;
        var elapsed = asOf.DayNumber - startsOn.DayNumber + 1;

        return Math.Clamp(elapsed, 0, length);
    }
}
