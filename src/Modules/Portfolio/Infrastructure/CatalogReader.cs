using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Portfolio.Application;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Portfolio.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Portfolio.Infrastructure;

/// <summary>
/// The catalog's read side.
/// </summary>
/// <remarks>
/// <para>
/// Every query here runs on the caller's connection, so the catalog is whatever RLS lets through and this class
/// filters no further. The one thing it must not do is a query per card: a grid of two hundred items that fetched
/// its team and its dependency counts row by row would be four hundred round trips to draw one page.
/// </para>
/// <para>
/// Counts are therefore grouped in their own passes and stitched in memory, which is three queries for any number
/// of cards.
/// </para>
/// </remarks>
internal sealed class CatalogReader(PortfolioDbContext context, IDirectoryReader directory) : ICatalogReader
{
    /// <summary>
    /// The catalog grid, read through the discovery projection (v2 §01 §3.1).
    /// </summary>
    /// <remarks>
    /// Two reads, not one. The projection answers "what exists", org-wide, because a duplicate nobody outside the
    /// owning branch can see is a duplicate that gets built twice. The item table then answers "and what may this
    /// caller actually know about it", and RLS silently drops the rest — so an item a stranger may only discover
    /// comes back named and placed, with the estimate, the lead and the team absent rather than blanked.
    /// </remarks>
    public async Task<IReadOnlyList<CatalogCard>> BrowseAsync(CatalogFilter filter, CancellationToken ct)
    {
        var query = context.Discovery.AsNoTracking();

        if (filter.Type is { Length: > 0 } type)
        {
            var parsed = ItemTypes.Parse(type).ToString();
            query = query.Where(row => row.Type == parsed);
        }

        if (filter.Classification is { Length: > 0 } classification)
        {
            var parsed = ItemClassifications.Parse(classification).ToString();
            query = query.Where(row => row.Classification == parsed);
        }

        if (filter.State is { Length: > 0 } state)
        {
            var parsed = ParseState(state).ToString();
            query = query.Where(row => row.State == parsed);
        }

        if (filter.Category is { Length: > 0 } category)
        {
            query = query.Where(row => row.Category == category);
        }

        if (filter.OwnerNodeId is { } owner)
        {
            query = query.Where(row => row.OwnerNodeId == owner);
        }

        if (filter.SharedOnly)
        {
            // Shared means somebody else leans on it. Expressed as "has an incoming edge" rather than as a type,
            // because a run-service other services consume is every bit as shared as a platform is.
            query = query.Where(row => context.Dependencies.Any(edge => edge.DependsOnItemId == row.ItemId));
        }

        var rows = await query
            .OrderBy(row => row.Type)
            .ThenBy(row => row.Code)
            .Take(filter.Limit)
            .ToListAsync(ct);

        return await CardsAsync(rows, ct);
    }

    public async Task<IReadOnlyList<CatalogCard>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";

        // Also the projection: "does something like this already exist" is the question the wizard asks on the way
        // to creating a duplicate, and it is worth least to the person best placed to ask it if it only searches
        // their own branch.
        var rows = await context.Discovery
            .AsNoTracking()
            .Where(row =>
                EF.Functions.ILike(row.Name, pattern)
                || EF.Functions.ILike(row.Code, pattern)
                || (row.Category != null && EF.Functions.ILike(row.Category, pattern))
                || (row.Summary != null && EF.Functions.ILike(row.Summary, pattern)))
            .OrderBy(row => row.Name)
            .Take(limit)
            .ToListAsync(ct);

        return await CardsAsync(rows, ct);
    }

    public async Task<CatalogItemDetail?> DetailAsync(Guid itemId, CancellationToken ct)
    {
        var item = await context.Items
            .AsNoTracking()
            .Include(candidate => candidate.Iterations)
            .Include(candidate => candidate.Epics)
            .Include(candidate => candidate.Members)
            .AsSplitQuery()
            .SingleOrDefaultAsync(candidate => candidate.Id == itemId, ct);

        if (item is null)
        {
            return null;
        }

        var card = (await CardsAsync([item], ct)).Single();

        var names = await directory.GetPersonNamesAsync(
            [.. item.Members.Select(member => member.PersonId).Distinct()],
            ct);

        var team = item.Members
            .OrderBy(member => member.NodeId)
            .ThenBy(member => names.TryGetValue(member.PersonId, out var name) ? name : string.Empty)
            .Select(member => new ItemTeamMember(
                member.PersonId,
                names.TryGetValue(member.PersonId, out var name) ? name : null,
                member.NodeId,
                null,
                member.FunctionalRoleId,
                member.AllocationPercent,
                member.From,
                member.To))
            .ToArray();

        var iterations = item.Iterations
            .OrderBy(iteration => iteration.Sequence)
            .Select(iteration => new IterationSummary(
                iteration.Id,
                iteration.Sequence,
                iteration.Name,
                iteration.Length.ToString().ToLowerInvariant(),
                iteration.StartsOn,
                iteration.EndsOn,
                iteration.State.ToString().ToLowerInvariant()))
            .ToArray();

        var epics = item.Epics
            .OrderBy(epic => epic.Sequence)
            .Select(epic => new ItemEpicView(
                epic.Id,
                epic.Name,
                epic.Description,
                Wire(epic.Status),
                epic.TargetVersion,
                epic.IterationId,
                epic.Sequence))
            .ToArray();

        var history = await context.Transitions
            .AsNoTracking()
            .Where(transition => transition.PortfolioItemId == itemId)
            .OrderByDescending(transition => transition.DecidedAt)
            .Select(transition => new TransitionRecord(
                transition.FromState.ToString()!.ToLowerInvariant(),
                transition.ToState.ToString().ToLowerInvariant(),
                transition.IsReversal,
                transition.Reason,
                transition.DecidedBy,
                transition.DecidedAt))
            .ToListAsync(ct);

        return new CatalogItemDetail(
            card,
            team,
            iterations,
            epics,
            await DependencyViewsAsync(itemId, ct),
            history);
    }

    public async Task<bool> CodeExistsAsync(string code, CancellationToken ct) =>
        await context.Items.AsNoTracking().AnyAsync(item => item.Code == code, ct);

    public async Task<bool> ItemExistsAsync(Guid itemId, CancellationToken ct) =>
        await context.Items.AsNoTracking().AnyAsync(item => item.Id == itemId, ct);

    public async Task<IReadOnlyList<DependencyEdge>> GetDependencyEdgesAsync(CancellationToken ct) =>
        await context.Dependencies
            .AsNoTracking()
            .Select(edge => new DependencyEdge(edge.ItemId, edge.DependsOnItemId))
            .ToListAsync(ct);

    /// <summary>
    /// Both directions of the graph, from one table.
    /// </summary>
    /// <remarks>
    /// "Consumes" and "consumed by" are the same rows read from opposite ends, which is exactly why the edge is
    /// stored once. Rendering them from two tables is how the platform page and the project page start disagreeing
    /// about whether a dependency exists.
    /// </remarks>
    private async Task<IReadOnlyList<ItemDependencyView>> DependencyViewsAsync(Guid itemId, CancellationToken ct)
    {
        var outgoing = await context.Dependencies
            .AsNoTracking()
            .Where(edge => edge.ItemId == itemId)
            .ToListAsync(ct);

        var incoming = await context.Dependencies
            .AsNoTracking()
            .Where(edge => edge.DependsOnItemId == itemId)
            .ToListAsync(ct);

        var otherIds = outgoing.Select(edge => edge.DependsOnItemId)
            .Concat(incoming.Select(edge => edge.ItemId))
            .Distinct()
            .ToArray();

        var others = await context.Items
            .AsNoTracking()
            .Where(item => otherIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Code, item.Name, item.Type })
            .ToDictionaryAsync(item => item.Id, ct);

        var views = new List<ItemDependencyView>();

        foreach (var edge in outgoing)
        {
            if (others.TryGetValue(edge.DependsOnItemId, out var other))
            {
                views.Add(new ItemDependencyView(
                    edge.Id,
                    other.Id,
                    other.Code,
                    other.Name,
                    Wire(other.Type),
                    Wire(edge.Kind),
                    "consumes",
                    edge.Note));
            }
        }

        foreach (var edge in incoming)
        {
            if (others.TryGetValue(edge.ItemId, out var other))
            {
                views.Add(new ItemDependencyView(
                    edge.Id,
                    other.Id,
                    other.Code,
                    other.Name,
                    Wire(other.Type),
                    Wire(edge.Kind),
                    "consumed-by",
                    edge.Note));
            }
        }

        return views;
    }

    /// <summary>
    /// Turns discovered rows into cards, filled in as far as this caller is entitled to (v2 §01 §3.1).
    /// </summary>
    /// <remarks>
    /// The item query is not filtered here; RLS filters it. Whatever comes back is what the caller may read in
    /// full, and every discovered row it did not cover falls through to the narrow card. That is the whole
    /// "discovery yes, detail no" rule, and it is expressed as the difference between two result sets rather than
    /// as a permission check in C# — so it cannot drift from <c>can_read_item</c>, because it *is*
    /// <c>can_read_item</c>.
    /// </remarks>
    private async Task<IReadOnlyList<CatalogCard>> CardsAsync(
        IReadOnlyList<ItemDiscovery> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.ItemId).ToArray();

        var readable = await context.Items
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(ct);

        var full = (await CardsAsync(readable, ct)).ToDictionary(card => card.Id);

        return [.. rows.Select(row => full.TryGetValue(row.ItemId, out var card) ? card : Narrow(row))];
    }

    /// <summary>The discovery projection as a card: what it is and whose, and nothing that would cost anything.</summary>
    private static CatalogCard Narrow(ItemDiscovery row) => new(
        row.ItemId,
        row.Code,
        row.Name,
        Wire(Enum.Parse<ItemType>(row.Type)),
        row.Category,
        Wire(Enum.Parse<ItemClassification>(row.Classification)),
        Wire(Enum.Parse<PortfolioState>(row.State)),
        // Deliberately absent rather than zeroed-with-meaning: a stranger learning that an item has eleven people
        // and a €400k estimate has learned most of what the card was withholding.
        AwaitingVersion: null,
        row.OwnerNodeId,
        LeadPersonId: null,
        PoPersonId: null,
        row.Summary,
        EstimateAmount: null,
        Currency: string.Empty,
        TeamHeadcount: 0,
        DependencyCount: 0,
        ConsumedByCount: 0,
        QueuedEpicCount: 0,
        CurrentIterationName: null,
        row.Confidential);

    private async Task<IReadOnlyList<CatalogCard>> CardsAsync(
        IReadOnlyList<PortfolioItem> items,
        CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var ids = items.Select(item => item.Id).ToArray();

        var headcount = await context.Members
            .AsNoTracking()
            .Where(member => ids.Contains(member.ItemId) && member.To == null)
            .GroupBy(member => member.ItemId)
            .Select(group => new { ItemId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.ItemId, entry => entry.Count, ct);

        var dependsOn = await context.Dependencies
            .AsNoTracking()
            .Where(edge => ids.Contains(edge.ItemId))
            .GroupBy(edge => edge.ItemId)
            .Select(group => new { ItemId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.ItemId, entry => entry.Count, ct);

        var consumedBy = await context.Dependencies
            .AsNoTracking()
            .Where(edge => ids.Contains(edge.DependsOnItemId))
            .GroupBy(edge => edge.DependsOnItemId)
            .Select(group => new { ItemId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.ItemId, entry => entry.Count, ct);

        var queued = await context.Epics
            .AsNoTracking()
            .Where(epic => ids.Contains(epic.ItemId)
                           && (epic.Status == EpicStatus.Planned
                               || epic.Status == EpicStatus.Deferred
                               || epic.Status == EpicStatus.InProgress))
            .GroupBy(epic => epic.ItemId)
            .Select(group => new { ItemId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.ItemId, entry => entry.Count, ct);

        var current = await context.Iterations
            .AsNoTracking()
            .Where(iteration => ids.Contains(iteration.PortfolioItemId)
                                && iteration.State == IterationState.Active)
            .ToDictionaryAsync(iteration => iteration.PortfolioItemId, iteration => iteration.Name, ct);

        return
        [
            .. items.Select(item => new CatalogCard(
                item.Id,
                item.Code,
                item.Name,
                Wire(item.Type),
                item.Category,
                Wire(item.Classification),
                Wire(item.State),
                item.AwaitingVersion,
                item.OwnerNodeId,
                item.LeadPersonId,
                item.PoPersonId,
                item.Summary,
                item.EstimateAmount,
                item.Currency,
                headcount.GetValueOrDefault(item.Id),
                dependsOn.GetValueOrDefault(item.Id),
                consumedBy.GetValueOrDefault(item.Id),
                queued.GetValueOrDefault(item.Id),
                current.GetValueOrDefault(item.Id),
                item.Confidential)),
        ];
    }

    private static PortfolioState ParseState(string state) => state.Trim().ToLowerInvariant() switch
    {
        LifecycleStates.Considered => PortfolioState.Considered,
        LifecycleStates.Committed => PortfolioState.Committed,
        LifecycleStates.Active => PortfolioState.Active,
        LifecycleStates.AwaitingVnext => PortfolioState.AwaitingVnext,
        LifecycleStates.Dephase => PortfolioState.Dephase,
        _ => throw new BuildingBlocks.Abstractions.DomainRuleViolationException(
            $"'{state}' is not a lifecycle state."),
    };

    /// <summary>
    /// Enum to wire string.
    /// </summary>
    /// <remarks>
    /// Hyphenated rather than lower-cased, because <c>AwaitingVnext</c> and <c>RunService</c> are two words on the
    /// wire and the client's chips key off them. Done once here so no caller has to remember which ones differ.
    /// </remarks>
    private static string Wire(PortfolioState state) => state switch
    {
        PortfolioState.AwaitingVnext => LifecycleStates.AwaitingVnext,
        _ => state.ToString().ToLowerInvariant(),
    };

    private static string Wire(ItemType type) => type switch
    {
        ItemType.RunService => ItemTypes.RunService,
        ItemType.BusinessInitiative => ItemTypes.BusinessInitiative,
        _ => type.ToString().ToLowerInvariant(),
    };

    private static string Wire(ItemClassification classification) => classification.ToString().ToLowerInvariant();

    private static string Wire(DependencyKind kind) => kind.ToString().ToLowerInvariant();

    private static string Wire(EpicStatus status) => status switch
    {
        EpicStatus.InProgress => EpicStatuses.InProgress,
        _ => status.ToString().ToLowerInvariant(),
    };
}

internal sealed class DirectoryNodeAdapter(IOrgNodeReader nodes) : IDirectoryNodePort
{
    public async Task<Guid?> HomeNodeOfAsync(Guid personId, CancellationToken ct) =>
        (await nodes.GetHomeScopeAsync(personId, ct))?.NodeId;
}
