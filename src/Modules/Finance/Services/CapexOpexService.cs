using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Finance.Domain;
using Cracra.Modules.Projects.Contracts;

namespace Cracra.Modules.Finance.Services;

/// <summary>What the caller asked to see.</summary>
public sealed record CapexOpexRequest(string? Scope, Guid? ScopeId, string? Period, DateOnly? From, DateOnly? To);

public static class FinanceScopes
{
    public const string Department = "department";
    public const string Project = "project";
    public const string Portfolio = "portfolio";

    public static readonly IReadOnlyList<string> All = [Department, Project, Portfolio];
}

/// <summary>One project's contribution to the split.</summary>
public sealed record ProjectLine(
    Guid ProjectId,
    string Code,
    string Name,
    string Classification,
    decimal ManualCost,
    string CostCurrency,

    /// <summary>False where the project's cost is in another currency than the view's, and so is not in its totals.</summary>
    bool CostCounted,
    decimal BuildHours,
    decimal RunHours,
    decimal EffortCost,
    decimal CapexAmount,
    decimal OpexAmount);

/// <summary>One month's effort, for the "by period" breakdown.</summary>
/// <remarks>
/// Effort only, deliberately. A manual cost is a budget figure with no date on it, so spreading it across the
/// months of a period would be inventing a schedule — and a schedule for a capitalized cost is an amortization
/// table, which the spec puts out of scope in as many words.
/// </remarks>
public sealed record PeriodLine(
    string Month,
    decimal CapexHours,
    decimal OpexHours,
    decimal ExcludedHours,
    decimal CapexCost,
    decimal OpexCost);

/// <summary>The capitalization view, as the screen and the export both render it.</summary>
public sealed record CapexOpexView(
    string Scope,
    Guid? ScopeId,
    string ScopeLabel,
    string PeriodKind,
    DateOnly From,
    DateOnly To,

    /// <summary>The currency every amount below is in. See <see cref="ProjectLine.CostCounted"/>.</summary>
    string Currency,

    /// <summary>Projects whose cost is in another currency, and is therefore reported but not summed.</summary>
    int OtherCurrencyProjects,
    SplitTotals Totals,
    IReadOnlyList<ProjectLine> Projects,
    IReadOnlyList<PeriodLine> Periods,
    CapexOpexRuleView Rule);

public interface ICapexOpexService
{
    Task<CapexOpexView> GetAsync(CapexOpexRequest request, CancellationToken ct);
}

/// <summary>
/// The derivation: classification and cost from S3, effort from S5, valued through this module's rate cards.
/// </summary>
/// <remarks>
/// <para>
/// Every read below runs in the caller's own RLS session, through each module's own contract. That is the whole
/// security model of this slice, and it is stronger than it looks: the view is a projection over rows the viewer
/// could already have read one at a time, so no combination of scope parameters can make it exceed their rights.
/// The endpoint policy decides whether they may ask; the finance rule and rate card rows have their own policy;
/// the numbers come from whatever the other modules were willing to hand over.
/// </para>
/// <para>
/// The three scopes count deliberately different things, because "what did this cost" has three different
/// meanings here. A <em>project</em> is its own cost and every hour booked to it. A <em>department</em> is the
/// cost of the projects it leads plus every hour its own people logged, wherever they logged it — you capitalize
/// what you own and you pay the people you employ. A <em>portfolio</em> is the projects, without the
/// people-shaped buckets: quality-of-life and administration belong to a department's headcount rather than to a
/// portfolio, and rolling them up organization-wide would answer a question nobody asked.
/// </para>
/// </remarks>
internal sealed class CapexOpexService(
    IFinanceConfigService config,
    IProjectProvisioner projects,
    IActivityScheduler activities,
    IActivityTaxonomyReader taxonomy,
    IDirectoryReader directory,
    IUserContext user,
    TimeProvider clock) : ICapexOpexService
{
    public async Task<CapexOpexView> GetAsync(CapexOpexRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var period = FinancePeriod.Resolve(request.Period, request.From, request.To, today);
        var scope = Scope(request.Scope);

        var departmentId = scope == FinanceScopes.Department
            ? request.ScopeId ?? user.DepartmentIds.FirstOrDefault()
            : request.ScopeId is { } id && scope == FinanceScopes.Project
                ? await LeadDepartmentAsync(id, ct)
                : user.DepartmentIds.FirstOrDefault();

        // The rule and the rate cards are the department's, even for a project scope: a project's hours are
        // capitalized by whoever owns the delivery, not by whoever happens to be looking.
        var rule = await config.GetRuleAsync(departmentId, ct);
        var rates = await config.GetRateCardsAsync(departmentId, ct);

        var summaries = await ProjectsInScopeAsync(scope, request.ScopeId, departmentId, ct);
        var entries = await EffortAsync(scope, request.ScopeId, departmentId, period, ct);

        var valuation = await Valuation.BuildAsync(rates, entries, directory, ct);
        var buckets = await BucketsByCodeAsync(departmentId, ct);

        var currency = Currency(summaries, rates);

        var split = new CapexOpexSplit(rule.ToDomain());

        foreach (var entry in entries)
        {
            split.AddEffort(buckets.GetValueOrDefault(entry.ActivityTypeCode), entry.Hours, valuation.Value(entry));
        }

        var lines = ProjectLines(summaries, entries, buckets, valuation, rule, currency);

        foreach (var line in lines.Where(line => line.CostCounted))
        {
            split.AddManualCost(line.Classification, line.ManualCost, line.BuildHours, line.RunHours);
        }

        return new CapexOpexView(
            scope,
            request.ScopeId ?? (scope == FinanceScopes.Department ? departmentId : null),
            await ScopeLabelAsync(scope, request.ScopeId, departmentId, summaries, ct),
            period.Kind,
            period.From,
            period.To,
            currency,
            lines.Count(line => !line.CostCounted && line.ManualCost != 0m),
            split.Build(),
            lines,
            Periods(period, entries, buckets, valuation, rule),
            rule);
    }

    private static string Scope(string? requested)
    {
        var candidate = (requested ?? FinanceScopes.Department).Trim().ToLowerInvariant();

        return FinanceScopes.All.Contains(candidate, StringComparer.Ordinal)
            ? candidate
            : throw new DomainRuleViolationException($"'{requested}' is not a capex/opex scope.");
    }

    private async Task<Guid> LeadDepartmentAsync(Guid projectId, CancellationToken ct)
    {
        var summaries = await projects.GetSummariesAsync([projectId], ct);

        // Falls back to the caller's own department rather than throwing. A project RLS hid is a 404 the caller
        // is about to get from the empty view anyway, and a different status here would confirm it exists.
        return summaries.TryGetValue(projectId, out var summary)
            ? summary.LeadDepartmentId
            : user.DepartmentIds.FirstOrDefault();
    }

    private async Task<IReadOnlyList<ProjectSummary>> ProjectsInScopeAsync(
        string scope,
        Guid? scopeId,
        Guid departmentId,
        CancellationToken ct)
    {
        var visible = await projects.GetVisibleProjectIdsAsync(ct);
        var summaries = await projects.GetSummariesAsync(visible, ct);

        return scope switch
        {
            FinanceScopes.Project => scopeId is { } projectId && summaries.TryGetValue(projectId, out var one)
                ? [one]
                : [],

            // Led by this department. A project another department leads is capitalized there, once — the
            // alternative is the same investment appearing in two departments' capex and in neither's reconciled.
            FinanceScopes.Department =>
                [.. summaries.Values.Where(summary => summary.LeadDepartmentId == departmentId)],

            _ => [.. summaries.Values],
        };
    }

    private async Task<IReadOnlyList<ActivityEntryView>> EffortAsync(
        string scope,
        Guid? scopeId,
        Guid departmentId,
        FinancePeriod period,
        CancellationToken ct)
    {
        if (scope == FinanceScopes.Project)
        {
            return scopeId is { } projectId
                ? await activities.GetForProjectAsync(projectId, period.From, period.To, ct)
                : [];
        }

        if (scope == FinanceScopes.Department)
        {
            var people = await directory.GetPeopleAsync(null, departmentId, ct);

            // Every hour these people logged, on their department's projects or anyone else's. A department pays
            // its people whatever they are working on, and the quality-of-life and administration buckets exist
            // in this view precisely because they are real cost that belongs to nobody's project.
            return people.Count == 0
                ? []
                : await activities.GetForPeopleAsync(
                    [.. people.Select(person => person.Id)], period.From, period.To, ct);
        }

        var visible = await projects.GetVisibleProjectIdsAsync(ct);
        var all = new List<ActivityEntryView>();

        foreach (var projectId in visible)
        {
            all.AddRange(await activities.GetForProjectAsync(projectId, period.From, period.To, ct));
        }

        return all;
    }

    /// <summary>
    /// Which canonical bucket each of the department's activity codes belongs to.
    /// </summary>
    /// <remarks>
    /// Walks to the root of the hierarchy, because a department may put subtypes under a subtype. Guarded against
    /// a cycle in configuration somebody hand-edited: a code whose parent chain loops is reported as unclassified
    /// rather than hanging the request.
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, string?>> BucketsByCodeAsync(
        Guid departmentId,
        CancellationToken ct)
    {
        var types = await taxonomy.GetTypesAsync(departmentId == Guid.Empty ? null : departmentId, ct);

        var parents = types.ToDictionary(
            type => type.Code,
            type => type.ParentCode,
            StringComparer.OrdinalIgnoreCase);

        var buckets = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in types)
        {
            var code = type.Code;
            var depth = 0;

            while (parents.TryGetValue(code, out var parent) && parent is not null && depth++ < 8)
            {
                code = parent;
            }

            buckets[type.Code] = ActivityBucket.For(code);
        }

        return buckets;
    }

    /// <summary>
    /// The currency the totals are stated in.
    /// </summary>
    /// <remarks>
    /// The platform has no exchange rates and this slice is not the place to introduce them, so the view picks one
    /// currency and says so. The rate cards decide it where there are any — they are what the department entered
    /// most recently and deliberately — and the projects' most common currency otherwise. A project priced in
    /// another currency keeps its own row, with its cost visible and excluded from the totals.
    /// </remarks>
    private static string Currency(IReadOnlyList<ProjectSummary> summaries, IReadOnlyList<RateCardView> rates)
    {
        if (rates.Count > 0)
        {
            return rates[0].Currency;
        }

        return summaries
            .GroupBy(summary => summary.CostCurrency, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault() ?? "EUR";
    }

    private static IReadOnlyList<ProjectLine> ProjectLines(
        IReadOnlyList<ProjectSummary> summaries,
        IReadOnlyList<ActivityEntryView> entries,
        IReadOnlyDictionary<string, string?> buckets,
        Valuation valuation,
        CapexOpexRuleView rule,
        string currency)
    {
        var byProject = entries
            .Where(entry => entry.ProjectId is not null)
            .GroupBy(entry => entry.ProjectId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var domainRule = rule.ToDomain();
        var lines = new List<ProjectLine>(summaries.Count);

        foreach (var summary in summaries)
        {
            var mine = byProject.GetValueOrDefault(summary.Id, []);
            var counted = string.Equals(summary.CostCurrency, currency, StringComparison.Ordinal);

            var buildHours = Hours(mine, buckets, Domain.Buckets.Build);
            var runHours = Hours(mine, buckets, Domain.Buckets.Run);

            // A per-project split, computed the same way the totals are — same rule, same calculator — so a row
            // and the total it contributes to cannot disagree about how a mixed project was apportioned.
            var split = new CapexOpexSplit(domainRule);

            foreach (var entry in mine)
            {
                split.AddEffort(buckets.GetValueOrDefault(entry.ActivityTypeCode), entry.Hours, valuation.Value(entry));
            }

            if (counted)
            {
                split.AddManualCost(summary.Classification, summary.CostAmount, buildHours, runHours);
            }

            var totals = split.Build();

            lines.Add(new ProjectLine(
                summary.Id,
                summary.Code,
                summary.Name,
                summary.Classification,
                summary.CostAmount,
                summary.CostCurrency,
                counted,
                buildHours,
                runHours,
                totals.EffortCapex + totals.EffortOpex,
                totals.CapexAmount,
                totals.OpexAmount));
        }

        return [.. lines.OrderBy(line => line.Code, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<PeriodLine> Periods(
        FinancePeriod period,
        IReadOnlyList<ActivityEntryView> entries,
        IReadOnlyDictionary<string, string?> buckets,
        Valuation valuation,
        CapexOpexRuleView rule)
    {
        var domainRule = rule.ToDomain();

        return
        [
            .. period.Months().Select(month =>
            {
                var split = new CapexOpexSplit(domainRule);

                foreach (var entry in entries)
                {
                    var day = DateOnly.FromDateTime(entry.SlotStart.UtcDateTime);

                    if (month.Contains(day))
                    {
                        split.AddEffort(
                            buckets.GetValueOrDefault(entry.ActivityTypeCode), entry.Hours, valuation.Value(entry));
                    }
                }

                var totals = split.Build();

                return new PeriodLine(
                    $"{month.From:yyyy-MM}",
                    totals.CapexHours,
                    totals.OpexHours,
                    totals.ExcludedHours,
                    totals.EffortCapex,
                    totals.EffortOpex);
            }),
        ];
    }

    private static decimal Hours(
        IReadOnlyList<ActivityEntryView> entries,
        IReadOnlyDictionary<string, string?> buckets,
        string bucket) =>
        entries
            .Where(entry => string.Equals(buckets.GetValueOrDefault(entry.ActivityTypeCode), bucket, StringComparison.Ordinal))
            .Sum(entry => entry.Hours);

    private async Task<string> ScopeLabelAsync(
        string scope,
        Guid? scopeId,
        Guid departmentId,
        IReadOnlyList<ProjectSummary> summaries,
        CancellationToken ct)
    {
        if (scope == FinanceScopes.Project)
        {
            return summaries.Count > 0 ? $"{summaries[0].Code} — {summaries[0].Name}" : "finance.scope.project";
        }

        if (scope == FinanceScopes.Portfolio)
        {
            return "finance.scope.portfolio";
        }

        // A Transloco key where the directory has one. Server-originated user-facing text is keyed
        // (conventions.md §5); the client renders it, because only the client knows the reader's language.
        var names = await directory.GetDepartmentNameKeysAsync([departmentId], ct);

        return names.GetValueOrDefault(departmentId, "finance.scope.department");
    }
}
