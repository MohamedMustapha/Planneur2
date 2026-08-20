using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Kudos.Contracts;
using Cracra.Modules.Kudos.Domain;
using Cracra.Modules.Kudos.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Kudos.Application;

// =================================================================================================================
// Queries read the module's one table straight off the DbContext. None of them filters by role: RLS removed what
// the caller may not see before the rows arrived, and a second filter here would either duplicate the policy or
// disagree with it.
//
// The one thing they do check is the department's mode — and that is not a row decision. Whether a leaderboard may
// be drawn at all is a question about the department's settings, not about who is asking, and a mode of "counter"
// means nobody sees a ranking, including the head who set it.
// =================================================================================================================

public sealed record ListKudosQuery(
    string? Scope,
    string? Direction,
    string? Period,
    int? Year,
    int? Month) : IRequest<IReadOnlyList<KudoView>>;

/// <summary>What the give-kudo form needs, for the person it is about to be given to.</summary>
public sealed record GetKudoRulesQuery(Guid? PersonId) : IRequest<KudoRulesView>;

public sealed record GetEligiblePeersQuery : IRequest<IReadOnlyList<EligiblePeer>>;

public sealed record GetKudosSummaryQuery(
    string? Scope,
    Guid? ScopeId,
    string? Period,
    int? Year,
    int? Month) : IRequest<KudosSummary>;

public sealed record GetLeaderboardQuery(
    string? Scope,
    Guid? ScopeId,
    string? Period,
    int? Year,
    int? Month) : IRequest<LeaderboardView>;

public sealed record GetAnnualKudosQuery(int? Year, Guid? PersonId) : IRequest<AnnualKudosView>;

// --- Shared scope resolution ---------------------------------------------------------------------------------

/// <summary>A unit or a department, and the rules that govern recognition inside it.</summary>
internal sealed record ResolvedScope(
    string Scope,
    Guid? ScopeId,
    Guid? UnitId,
    Guid? DepartmentId,
    KudoRules Rules);

/// <summary>
/// Turns <c>?scope=unit&amp;scopeId=…</c> into something to filter on.
/// </summary>
/// <remarks>
/// Naming a scope is a narrowing, never a widening: asking for a department the caller does not head returns their
/// own rows within it rather than the department's, because RLS answers the row question and this only decides
/// which rows to ask about. That is the same shape S5's activity feed uses, and it is why neither needs to know
/// anything about roles.
/// </remarks>
internal sealed class ScopeResolver(IDirectoryPort directory, IUserContext user)
{
    public const string UnitScope = "unit";
    public const string DepartmentScope = "department";
    public const string MeScope = "me";

    public async Task<ResolvedScope> ResolveAsync(string? scope, Guid? scopeId, CancellationToken ct)
    {
        if (string.Equals(scope, DepartmentScope, StringComparison.OrdinalIgnoreCase))
        {
            var departmentId = scopeId ?? user.DepartmentIds.FirstOrDefault();

            return new ResolvedScope(
                DepartmentScope,
                departmentId == Guid.Empty ? null : departmentId,
                null,
                departmentId == Guid.Empty ? null : departmentId,
                await RulesForAsync(departmentId, ct));
        }

        var unitId = scopeId ?? user.UnitId;

        // The unit's own department, not the caller's: a head reading another unit's board must see it under the
        // rules that unit is actually run by, or the numbers on it mean something different from what they say.
        var unitDepartment = unitId is { } unit
            ? await directory.GetUnitDepartmentAsync(unit, ct)
            : null;

        return new ResolvedScope(
            UnitScope,
            unitId,
            unitId,
            unitDepartment,
            await RulesForAsync(unitDepartment ?? Guid.Empty, ct));
    }

    private async Task<KudoRules> RulesForAsync(Guid departmentId, CancellationToken ct) =>
        departmentId == Guid.Empty ? KudoRules.Default : await directory.GetRulesAsync(departmentId, ct);
}

// --- Handlers ----------------------------------------------------------------------------------------------------

/// <summary>The wall: kudos in a scope over a period, newest first.</summary>
internal sealed class ListKudosHandler(
    KudosDbContext context,
    IDirectoryPort directory,
    IUserContext user) : IRequestHandler<ListKudosQuery, IReadOnlyList<KudoView>>
{
    /// <summary>A wall is scrolled, not paged. Past this, nobody is reading — and S9's cursor is the period.</summary>
    private const int MaximumRows = 500;

    public async Task<IReadOnlyList<KudoView>> Handle(ListKudosQuery request, CancellationToken ct)
    {
        var period = KudoPeriod.Resolve(request.Period, request.Year, request.Month, DateTimeOffset.UtcNow);

        var query = context.Kudos.AsQueryable();

        query = (request.Scope ?? ScopeResolver.MeScope).Trim().ToLowerInvariant() switch
        {
            ScopeResolver.UnitScope => query.Where(kudo => kudo.UnitId == user.UnitId),
            ScopeResolver.DepartmentScope => query.Where(kudo => user.DepartmentIds.Contains(kudo.DepartmentId)),
            _ => Mine(query, request.Direction, user.UserId),
        };

        var from = period.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = period.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await query
            .Where(kudo => kudo.CreatedAt >= from && kudo.CreatedAt < to)
            .OrderByDescending(kudo => kudo.CreatedAt)
            .Take(MaximumRows)
            .ToListAsync(ct);

        return await KudoViews.RenderAsync(rows, directory, ct);
    }

    /// <summary>
    /// Received, given, or both.
    /// </summary>
    /// <remarks>
    /// Received by default. "My kudos" means the ones somebody gave me in every language the design speaks; the
    /// ones I gave are a separate, smaller question and the form asks it explicitly.
    /// </remarks>
    private static IQueryable<Kudo> Mine(IQueryable<Kudo> query, string? direction, Guid me) =>
        (direction ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "given" => query.Where(kudo => kudo.FromPersonId == me),
            "all" => query.Where(kudo => kudo.FromPersonId == me || kudo.ToPersonId == me),
            _ => query.Where(kudo => kudo.ToPersonId == me),
        };
}

internal sealed class GetKudoRulesHandler(
    IKudoRepository repository,
    IDirectoryPort directory,
    KudoEligibility eligibility,
    IUserContext user) : IRequestHandler<GetKudoRulesQuery, KudoRulesView>
{
    public async Task<KudoRulesView> Handle(GetKudoRulesQuery request, CancellationToken ct)
    {
        // The receiver's department governs the vocabulary and the price; the caller's own governs the cap. Where
        // no receiver is named — the settings screen asking what its own department does — both are the caller's.
        var receiverDepartment = await ReceiverDepartmentAsync(request.PersonId, ct);

        var departmentId = receiverDepartment ?? user.DepartmentIds.FirstOrDefault();

        var rules = departmentId == Guid.Empty
            ? KudoRules.Default
            : await directory.GetRulesAsync(departmentId, ct);

        var giverRules = user.DepartmentIds.Count > 0
            ? await directory.GetRulesAsync(user.DepartmentIds[0], ct)
            : KudoRules.Default;

        var given = await repository.GivenInMonthAsync(user.UserId, KudoMonth.Of(DateTimeOffset.UtcNow), ct);

        return new KudoRulesView(
            departmentId,
            KudoRules.ToCode(rules.Mode),
            rules.ShowsPoints,
            rules.ShowsLeaderboard,
            giverRules.MonthlyCapPerGiver,
            given,
            Math.Max(0, giverRules.MonthlyCapPerGiver - given),
            [
                .. rules.Categories
                    .OrderBy(category => category.Points)
                    .ThenBy(category => category.Code, StringComparer.Ordinal)
                    .Select(category => new KudoCategoryOption(
                        category.Code,
                        category.LabelKey,
                        // Same promise as everywhere else: a department that counts never sees a number, so the
                        // form cannot accidentally render a price list it was told not to.
                        rules.ShowsPoints ? category.Points : 0)),
            ]);
    }

    /// <summary>
    /// The receiver's department, but only for somebody the caller could actually recognise.
    /// </summary>
    /// <remarks>
    /// Placement is resolved through the reference reader, which by design answers outside the caller's own
    /// visibility — that is what lets a kudo be stamped with the unit of a project teammate in a department the
    /// directory scopes away. Handing that answer back unconditionally would turn this endpoint into a lookup for
    /// "which department is this person in", asked with nothing but a person id. So an ineligible target simply
    /// gets the caller's own rules; the form then refuses on submit, which it was going to do anyway.
    /// </remarks>
    private async Task<Guid?> ReceiverDepartmentAsync(Guid? personId, CancellationToken ct)
    {
        if (personId is not { } receiver)
        {
            return null;
        }

        if (await directory.GetPlacementAsync(receiver, ct) is not { } placement)
        {
            return null;
        }

        var relation = await eligibility.ResolveAsync(receiver, placement.UnitId, placement.DepartmentId, ct);

        return relation is KudoRelation.None ? null : placement.DepartmentId;
    }
}

internal sealed class GetEligiblePeersHandler(KudoEligibility eligibility)
    : IRequestHandler<GetEligiblePeersQuery, IReadOnlyList<EligiblePeer>>
{
    public async Task<IReadOnlyList<EligiblePeer>> Handle(GetEligiblePeersQuery request, CancellationToken ct) =>
        await eligibility.ListAsync(ct);
}

/// <summary>
/// The counter, in every mode. The team board's monthly widget reads this.
/// </summary>
/// <remarks>
/// Ordered by name, never by score. This is the one kudos surface everybody in a unit sees whether they went
/// looking for it or not, and a ranking here would be the leaderboard arriving in departments that declined it.
/// </remarks>
internal sealed class GetKudosSummaryHandler(
    KudosDbContext context,
    IKudoRepository repository,
    ScopeResolver scopes,
    IDirectoryPort directory,
    IUserContext user) : IRequestHandler<GetKudosSummaryQuery, KudosSummary>
{
    public async Task<KudosSummary> Handle(GetKudosSummaryQuery request, CancellationToken ct)
    {
        var period = KudoPeriod.Resolve(request.Period, request.Year, request.Month, DateTimeOffset.UtcNow);
        var scope = await scopes.ResolveAsync(request.Scope, request.ScopeId, ct);

        var totals = await KudoTotals.InScopeAsync(context, scope, period, ct);

        var names = await directory.GetPersonNamesAsync([.. totals.Select(total => total.PersonId)], ct);

        var badges = await KudoTotals.BadgesAsync(
            repository,
            scope.Rules,
            [.. totals.Select(total => total.PersonId)],
            ct);

        return new KudosSummary(
            scope.Scope,
            scope.ScopeId,
            KudoRules.ToCode(scope.Rules.Mode),
            scope.Rules.ShowsPoints,
            period.From,
            period.To,
            totals.Sum(total => total.Count),
            totals.FirstOrDefault(total => total.PersonId == user.UserId)?.Count ?? 0,
            await KudoTotals.GivenByAsync(context, scope, period, user.UserId, ct),
            [
                .. totals
                    .Select(total => new KudoPersonTotal(
                        total.PersonId,
                        names.GetValueOrDefault(total.PersonId),
                        total.Count,
                        scope.Rules.ShowsPoints ? total.Points : 0,
                        badges.GetValueOrDefault(total.PersonId, [])))
                    .OrderBy(total => total.PersonName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase),
            ]);
    }
}

/// <summary>
/// The ranked view, where a department asked for one.
/// </summary>
/// <remarks>
/// A 403 rather than an empty list when the mode does not enable it. An empty leaderboard says "nobody has been
/// recognised here", which is a different and much more damaging statement than "this department does not rank
/// people" — and the client would have no way to tell them apart.
/// </remarks>
internal sealed class GetLeaderboardHandler(
    KudosDbContext context,
    IKudoRepository repository,
    ScopeResolver scopes,
    IDirectoryPort directory) : IRequestHandler<GetLeaderboardQuery, LeaderboardView>
{
    public async Task<LeaderboardView> Handle(GetLeaderboardQuery request, CancellationToken ct)
    {
        var period = KudoPeriod.Resolve(request.Period, request.Year, request.Month, DateTimeOffset.UtcNow);
        var scope = await scopes.ResolveAsync(request.Scope, request.ScopeId, ct);

        if (!scope.Rules.ShowsLeaderboard)
        {
            throw new UnauthorizedAccessException(
                "This department has not enabled the kudos leaderboard.");
        }

        var totals = await KudoTotals.InScopeAsync(context, scope, period, ct);

        var names = await directory.GetPersonNamesAsync([.. totals.Select(total => total.PersonId)], ct);

        var ranked = totals
            .OrderByDescending(total => total.Points)
            .ThenByDescending(total => total.Count)
            .ThenBy(total => names.GetValueOrDefault(total.PersonId) ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaximumRanked)
            .ToList();

        var badges = await KudoTotals.BadgesAsync(repository, scope.Rules, [.. ranked.Select(row => row.PersonId)], ct);

        var rows = new List<LeaderboardRow>(ranked.Count);
        var rank = 0;
        var seen = 0;
        (int Points, int Count) previous = (-1, -1);

        foreach (var total in ranked)
        {
            seen++;

            // Ties share a rank, and the next one skips. Two people on nine points are not first and second, and
            // being told you are second on the same score as the winner is worse than not being ranked at all.
            if ((total.Points, total.Count) != previous)
            {
                rank = seen;
                previous = (total.Points, total.Count);
            }

            rows.Add(new LeaderboardRow(
                rank,
                total.PersonId,
                names.GetValueOrDefault(total.PersonId),
                total.Count,
                total.Points,
                badges.GetValueOrDefault(total.PersonId, [])));
        }

        return new LeaderboardView(
            scope.Scope,
            scope.ScopeId,
            KudoRules.ToCode(scope.Rules.Mode),
            period.From,
            period.To,
            rows);
    }

    /// <summary>
    /// How far down the board goes.
    /// </summary>
    /// <remarks>
    /// Twenty-five. Deep enough to cover a unit and most departments, shallow enough that nobody discovers they
    /// are ninetieth — which is a fact recognition software has no business delivering.
    /// </remarks>
    private const int MaximumRanked = 25;
}

/// <summary>
/// The annual-review claim.
/// </summary>
/// <remarks>
/// Somebody's own year by default. A head may ask for a person in their scope — RLS decides whether the rows come
/// back, so a member naming their director gets an empty year rather than a refusal, which is the correct answer
/// to a question they were free to ask.
/// </remarks>
internal sealed class GetAnnualKudosHandler(
    KudosDbContext context,
    IDirectoryPort directory,
    IUserContext user) : IRequestHandler<GetAnnualKudosQuery, AnnualKudosView>
{
    public async Task<AnnualKudosView> Handle(GetAnnualKudosQuery request, CancellationToken ct)
    {
        var personId = request.PersonId ?? user.UserId;
        var year = request.Year ?? DateTimeOffset.UtcNow.UtcDateTime.Year;
        var period = KudoPeriod.Resolve(KudoPeriod.YearKind, year, null, DateTimeOffset.UtcNow);

        var placement = await directory.GetPlacementAsync(personId, ct);

        var rules = placement is { } found
            ? await directory.GetRulesAsync(found.DepartmentId, ct)
            // Somebody who has left still has a year worth claiming. The canonical rules are the honest answer
            // rather than a failure.
            : KudoRules.Default;

        var from = period.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = period.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await context.Kudos
            .Where(kudo => kudo.ToPersonId == personId && kudo.CreatedAt >= from && kudo.CreatedAt < to)
            .OrderByDescending(kudo => kudo.CreatedAt)
            .ToListAsync(ct);

        var views = await KudoViews.RenderAsync(rows, directory, ct);

        // Badges are derived from the whole record, not from this year alone: a badge earned in March 2025 is
        // still held in 2026, and a claim view that dropped it would understate the person to make the query
        // simpler.
        var everything = await context.Kudos
            .Where(kudo => kudo.ToPersonId == personId)
            .Select(kudo => new { kudo.Category, kudo.Points, kudo.CreatedAt })
            .ToListAsync(ct);

        var badges = BadgeLadder.Earned(
            rules,
            everything.Select(kudo => new KudoRecord(kudo.Category, kudo.Points, kudo.CreatedAt)));

        var names = await directory.GetPersonNamesAsync([personId], ct);

        return new AnnualKudosView(
            personId,
            names.GetValueOrDefault(personId),
            year,
            KudoRules.ToCode(rules.Mode),
            rules.ShowsPoints,
            rows.Count,
            rules.ShowsPoints ? rows.Sum(kudo => kudo.Points) : 0,
            [
                .. views
                    .GroupBy(view => view.Category, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new AnnualCategoryGroup(
                        group.Key,
                        rules.LabelFor(group.Key),
                        group.Count(),
                        group.Sum(view => view.Points),
                        [.. group]))
                    .OrderByDescending(group => group.Count)
                    .ThenBy(group => group.Category, StringComparer.Ordinal),
            ],
            [.. badges.Select(badge => new BadgeView(badge.Code, badge.LabelKey, badge.EarnedAt))]);
    }
}

// --- Shared projection helpers -------------------------------------------------------------------------------

/// <summary>One person's tally inside a scope and period.</summary>
internal sealed record ScopeTotal(Guid PersonId, int Count, int Points);

internal static class KudoTotals
{
    /// <summary>
    /// How many people a scope report will describe.
    /// </summary>
    /// <remarks>
    /// A widget listing everyone in a unit is useful; one listing four hundred people in a department is a
    /// scrollbar. The cap bites on the largest tallies, so what is dropped is the tail.
    /// </remarks>
    private const int MaximumPeople = 100;

    public static async Task<IReadOnlyList<ScopeTotal>> InScopeAsync(
        KudosDbContext context,
        ResolvedScope scope,
        KudoPeriod period,
        CancellationToken ct)
    {
        var from = period.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = period.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var query = Filtered(context, scope)
            .Where(kudo => kudo.CreatedAt >= from && kudo.CreatedAt < to);

        // Grouped into an anonymous type rather than straight into the record: EF cannot translate a positional
        // constructor inside a GroupBy projection, and the failure is a runtime one.
        var grouped = await query
            .GroupBy(kudo => kudo.ToPersonId)
            .Select(group => new
            {
                PersonId = group.Key,
                Count = group.Count(),
                Points = group.Sum(kudo => kudo.Points),
            })
            .OrderByDescending(total => total.Points)
            .ThenByDescending(total => total.Count)
            .Take(MaximumPeople)
            .ToListAsync(ct);

        return [.. grouped.Select(total => new ScopeTotal(total.PersonId, total.Count, total.Points))];
    }

    /// <summary>How many the caller gave inside the same scope and period — the other half of the widget.</summary>
    public static async Task<int> GivenByAsync(
        KudosDbContext context,
        ResolvedScope scope,
        KudoPeriod period,
        Guid personId,
        CancellationToken ct)
    {
        var from = period.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = period.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        return await Filtered(context, scope)
            .CountAsync(
                kudo => kudo.FromPersonId == personId && kudo.CreatedAt >= from && kudo.CreatedAt < to,
                ct);
    }

    /// <summary>
    /// Badges for a set of people, derived from their whole record.
    /// </summary>
    /// <remarks>
    /// One query for everybody rather than one per person: a leaderboard of twenty-five would otherwise open
    /// twenty-five round trips to render an icon each. Empty by construction where the department counts rather
    /// than scores, and the query is skipped entirely in that case.
    /// </remarks>
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<BadgeView>>> BadgesAsync(
        IKudoRepository repository,
        KudoRules rules,
        IReadOnlyList<Guid> personIds,
        CancellationToken ct)
    {
        if (!rules.ShowsPoints || personIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<BadgeView>>();
        }

        var records = await repository.RecordsForAsync(personIds, ct);

        return records.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<BadgeView>)
            [
                .. BadgeLadder.Earned(rules, entry.Value)
                    .Select(badge => new BadgeView(badge.Code, badge.LabelKey, badge.EarnedAt)),
            ]);
    }

    private static IQueryable<Kudo> Filtered(KudosDbContext context, ResolvedScope scope) =>
        scope switch
        {
            { UnitId: { } unitId } => context.Kudos.Where(kudo => kudo.UnitId == unitId),
            { DepartmentId: { } departmentId } => context.Kudos.Where(kudo => kudo.DepartmentId == departmentId),
            // Somebody with neither a unit nor a department asked about a scope they do not have. Nothing is the
            // honest answer; RLS would have produced the same one a moment later.
            _ => context.Kudos.Where(_ => false),
        };
}

/// <summary>Turns rows into the DTO the wall renders, resolving names and per-department labels once.</summary>
internal static class KudoViews
{
    public static async Task<IReadOnlyList<KudoView>> RenderAsync(
        IReadOnlyList<Kudo> rows,
        IDirectoryPort directory,
        CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var names = await directory.GetPersonNamesAsync(
            [.. rows.SelectMany(row => new[] { row.FromPersonId, row.ToPersonId }).Distinct()],
            ct);

        // The rules are per department, and a departmental or project-crossing feed can span several, so the
        // labels and the points promise are resolved per department rather than once.
        var rules = new Dictionary<Guid, KudoRules>();

        foreach (var departmentId in rows.Select(row => row.DepartmentId).Distinct())
        {
            rules[departmentId] = await directory.GetRulesAsync(departmentId, ct);
        }

        return
        [
            .. rows.Select(row =>
            {
                var applicable = rules.GetValueOrDefault(row.DepartmentId) ?? KudoRules.Default;

                return new KudoView(
                    row.Id,
                    row.FromPersonId,
                    names.GetValueOrDefault(row.FromPersonId),
                    row.ToPersonId,
                    names.GetValueOrDefault(row.ToPersonId),
                    row.UnitId,
                    row.DepartmentId,
                    row.Category,
                    applicable.LabelFor(row.Category),
                    row.Message,
                    applicable.ShowsPoints ? row.Points : 0,
                    row.CreatedAt);
            }),
        ];
    }
}
