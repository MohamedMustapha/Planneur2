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
            ReportScopes.Team => await TeamAsync(period, ct),
            ReportScopes.Unit => await UnitAsync(period, ct),
            ReportScopes.Department => await DepartmentAsync(period, ct),
            ReportScopes.Project => await ProjectAsync(descriptor.ScopeId, period, ct),
            ReportScopes.Portfolio => await PortfolioAsync(period, ct),
            _ => await MyAsync(period, ct),
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
            DateTimeOffset.UtcNow);
    }

    // --- My work ---------------------------------------------------------------------------------------------

    /// <summary>Hours by bucket, where that leaves me against the target, where the work came from, what is next.</summary>
    private async Task<(IReadOnlyList<ReportSection> Sections, string Label)> MyAsync(
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

    // --- My project team -------------------------------------------------------------------------------------

    /// <summary>
    /// The teams of the projects the viewer is on.
    /// </summary>
    /// <remarks>
    /// "Every project I can read" is exactly "every project I am on" for a member, because that is what
    /// <c>access.can_read_project</c> says — so this needs no membership filter of its own. For a head it widens
    /// to their department's, which is the right answer to the same question asked by a different person.
    /// </remarks>
    private async Task<(IReadOnlyList<ReportSection> Sections, string Label)> TeamAsync(
        ReportPeriod period,
        CancellationToken ct)
    {
        var visible = await projects.VisibleAsync(ct);
        var rows = new List<ReportRow>();
        var iterationRows = new List<ReportRow>();

        foreach (var project in visible.Take(MaximumProjects))
        {
            var entries = await activities.ForProjectAsync(project.Id, period.From, period.To, ct);

            rows.Add(new ReportRow(
                project.Id.ToString(),
                $"{project.Code} — {project.Name}",
                [Actual(entries), Planned(entries), project.ActiveMemberCount]));

            foreach (var iteration in await portfolio.IterationsAsync(project.Id, ct))
            {
                // Only the ones that touch the window. A project's whole history would bury the sprint the report
                // is actually about.
                if (iteration.EndsOn < period.From || iteration.StartsOn > period.To)
                {
                    continue;
                }

                iterationRows.Add(new ReportRow(
                    iteration.Id.ToString(),
                    $"{project.Code} · {iteration.Name}",
                    [iteration.Sequence, DaysElapsed(iteration.StartsOn, iteration.EndsOn, period.To)]));
            }
        }

        return (
            [
                new ReportSection(
                    "teamActivity",
                    "reports.section.teamActivity",
                    [new ReportMetric("projects", rows.Count, "count")],
                    [new ReportTable(
                        "reports.table.projectActivity",
                        ["reports.column.actualHours", "reports.column.plannedHours", "reports.column.members"],
                        rows)],
                    // Said out loud rather than truncated in silence. A capped list presented as a complete one
                    // is the kind of wrong that gets quoted in a meeting.
                    visible.Count > MaximumProjects
                        ? [new ReportNote(
                            "reports.note.truncated",
                            (visible.Count - MaximumProjects).ToString(System.Globalization.CultureInfo.InvariantCulture),
                            "info")]
                        : []),
                new ReportSection(
                    "iterations",
                    "reports.section.iterations",
                    [],
                    [new ReportTable(
                        "reports.table.iterations",
                        ["reports.column.sequence", "reports.column.daysElapsed"],
                        iterationRows)],
                    []),
                await AgendaSectionAsync(period, ct),
            ],
            "reports.scope.team");
    }

    // --- My unit ---------------------------------------------------------------------------------------------

    private async Task<(IReadOnlyList<ReportSection> Sections, string Label)> UnitAsync(
        ReportPeriod period,
        CancellationToken ct)
    {
        var unitId = user.UnitId;

        if (unitId is not { } unit)
        {
            throw new DomainRuleViolationException("You are not attached to a unit, so there is no unit report.");
        }

        var people = await directory.PeopleAsync(unit, null, ct);
        var entries = await activities.ForPeopleAsync([.. people.Select(person => person.Id)], period.From, period.To, ct);

        var byPerson = entries.GroupBy(entry => entry.PersonId).ToDictionary(group => group.Key, group => group.ToList());

        var rows = people
            .Take(MaximumRows)
            .Select(person =>
            {
                var theirs = byPerson.GetValueOrDefault(person.Id, []);

                return new ReportRow(
                    person.Id.ToString(),
                    person.DisplayName,
                    [Actual(theirs), Planned(theirs), HoursOf(theirs, QualityOfLifeCode)]);
            })
            .ToList();

        var load = await schedule.LoadAsync(unit, period.From, period.To, ct);
        var kudoCount = await kudos.CountAsync(unit, null, period.From, period.To, ct);

        var units = await directory.UnitsAsync(null, ct);

        return (
            [
                new ReportSection(
                    "members",
                    "reports.section.members",
                    [new ReportMetric("headcount", people.Count, "count")],
                    [new ReportTable(
                        "reports.table.memberActivity",
                        ["reports.column.actualHours", "reports.column.plannedHours", "reports.column.qolHours"],
                        rows,
                        // The one table in the whole report whose labels are people. Everything downstream — the
                        // prompt's pseudonymizer above all — keys off this flag rather than guessing.
                        IdentifiesPeople: true)],
                    []),
                new ReportSection(
                    "runLoad",
                    "reports.section.runLoad",
                    [
                        new ReportMetric("openWorkOrders", load.OpenWorkOrders, "count"),
                        new ReportMetric("assignedWorkOrders", load.AssignedWorkOrders, "count"),
                        new ReportMetric("workOrderHours", load.EstimatedHours, "hours"),
                        new ReportMetric("shiftHours", load.ShiftHours, "hours"),
                        new ReportMetric("coverageGaps", load.CoverageGaps, "count"),
                    ],
                    [],
                    load.CoverageGaps > 0
                        ? [new ReportNote("reports.note.coverageGaps", null, "warning")]
                        : []),
                new ReportSection(
                    "qol",
                    "reports.section.qol",
                    [
                        new ReportMetric("qolHours", HoursOf(entries, QualityOfLifeCode), "hours"),
                        // Rendered from S8 onward, zero until S9 gave it something to count — because a section
                        // that appeared the week kudos shipped would have looked like a new feature rather than a
                        // blank being filled in.
                        new ReportMetric("kudos", kudoCount, "count"),
                    ],
                    [],
                    []),
                await AgendaSectionAsync(period, ct),
            ],
            units.FirstOrDefault(candidate => candidate.Id == unit)?.Name ?? "reports.scope.unit");
    }

    // --- My department ---------------------------------------------------------------------------------------

    private async Task<(IReadOnlyList<ReportSection> Sections, string Label)> DepartmentAsync(
        ReportPeriod period,
        CancellationToken ct)
    {
        var departmentId = user.DepartmentIds.FirstOrDefault();

        if (departmentId == Guid.Empty)
        {
            throw new DomainRuleViolationException("You belong to no department, so there is no department report.");
        }

        var units = await directory.UnitsAsync(departmentId, ct);
        var unitRows = new List<ReportRow>();

        foreach (var unit in units.Take(MaximumRows))
        {
            var people = await directory.PeopleAsync(unit.Id, null, ct);
            var entries = await activities.ForPeopleAsync(
                [.. people.Select(person => person.Id)],
                period.From,
                period.To,
                ct);

            unitRows.Add(new ReportRow(
                unit.Id.ToString(),
                unit.Name,
                [people.Count, Actual(entries), HoursOf(entries, QualityOfLifeCode)]));
        }

        var visible = await projects.VisibleAsync(ct);
        var board = await portfolio.BoardAsync(ct);

        var stateByProject = board.Lanes
            .SelectMany(lane => lane.Items.Where(item => item.ProjectId is not null)
                .Select(item => (item.ProjectId!.Value, lane.State)))
            .GroupBy(pair => pair.Item1)
            .ToDictionary(group => group.Key, group => group.First().State);

        var projectRows = visible
            .Take(MaximumRows)
            .Select(project => new ReportRow(
                project.Id.ToString(),
                $"{project.Code} — {project.Name}",
                [project.CostAmount, project.ActiveMemberCount]))
            .ToList();

        var names = await directory.DepartmentNameKeysAsync([departmentId], ct);

        return (
            [
                new ReportSection(
                    "units",
                    "reports.section.units",
                    [new ReportMetric("units", unitRows.Count, "count")],
                    [new ReportTable(
                        "reports.table.unitRollup",
                        ["reports.column.headcount", "reports.column.actualHours", "reports.column.qolHours"],
                        unitRows)],
                    []),
                new ReportSection(
                    "projects",
                    "reports.section.projects",
                    [
                        new ReportMetric("projects", visible.Count, "count"),
                        new ReportMetric("cost", visible.Sum(project => project.CostAmount), "currency"),
                    ],
                    [new ReportTable(
                        "reports.table.projects",
                        ["reports.column.cost", "reports.column.members"],
                        projectRows)],
                    // The lifecycle states, plus a truncation note where the list was capped.
                    // The lifecycle states are notes rather than a column, because a project without a portfolio
                    // item has no state at all and an empty cell reads as a missing value rather than as "not
                    // tracked in the portfolio".
                    [
                        .. stateByProject
                            .Where(pair => visible.Any(project => project.Id == pair.Key))
                            .GroupBy(pair => pair.Value)
                            .Select(group => new ReportNote(
                                $"reports.note.state.{group.Key}",
                                group.Count().ToString(System.Globalization.CultureInfo.InvariantCulture),
                                null)),
                        .. visible.Count > MaximumRows
                            ? new[]
                            {
                                new ReportNote(
                                    "reports.note.truncated",
                                    (visible.Count - MaximumRows).ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    "info"),
                            }
                            : [],
                    ]),
                await AgendaSectionAsync(period, ct),
            ],
            names.GetValueOrDefault(departmentId, "reports.scope.department"));
    }

    // --- My project ------------------------------------------------------------------------------------------

    private async Task<(IReadOnlyList<ReportSection> Sections, string Label)> ProjectAsync(
        Guid? scopeId,
        ReportPeriod period,
        CancellationToken ct)
    {
        if (scopeId is not { } projectId)
        {
            throw new DomainRuleViolationException("A project report needs a project.");
        }

        var project = await projects.ProjectAsync(projectId, ct)
            // RLS hid it or it does not exist, and the report cannot tell the two apart — which is the point.
            ?? throw new ResourceNotFoundException("That project does not exist.");

        var entries = await activities.ForProjectAsync(projectId, period.From, period.To, ct);
        var team = await projects.TeamAsync(projectId, ct);
        var iterations = await portfolio.IterationsAsync(projectId, ct);

        var byDepartment = entries
            .GroupBy(entry => entry.DepartmentId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var departmentNames = await directory.DepartmentNameKeysAsync([.. byDepartment.Keys], ct);

        var contribution = byDepartment
            .Select(pair => new ReportRow(
                pair.Key.ToString(),
                departmentNames.GetValueOrDefault(pair.Key, pair.Key.ToString()),
                [
                    Actual(pair.Value),
                    // The split as a percentage, computed here rather than left to the reader. A report that
                    // makes you divide two of its own numbers has not finished its job.
                    Percent(Actual(pair.Value), Actual(entries)),
                    team.Count(member => member.DepartmentId == pair.Key),
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
            $"{project.Code} — {project.Name}");
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
