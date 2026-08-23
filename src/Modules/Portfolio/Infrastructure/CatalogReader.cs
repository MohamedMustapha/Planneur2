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
    public async Task<IReadOnlyList<CatalogCard>> BrowseAsync(CatalogFilter filter, CancellationToken ct)
    {
        var query = context.Items.AsNoTracking();

        if (filter.Type is { Length: > 0 } type)
        {
            var parsed = ItemTypes.Parse(type);
            query = query.Where(item => item.Type == parsed);
        }

        if (filter.Classification is { Length: > 0 } classification)
        {
            var parsed = ItemClassifications.Parse(classification);
            query = query.Where(item => item.Classification == parsed);
        }

        if (filter.State is { Length: > 0 } state)
        {
            var parsed = ParseState(state);
            query = query.Where(item => item.State == parsed);
        }

        if (filter.Category is { Length: > 0 } category)
        {
            query = query.Where(item => item.Category == category);
        }

        if (filter.OwnerNodeId is { } owner)
        {
            query = query.Where(item => item.OwnerNodeId == owner);
        }

        if (filter.SharedOnly)
        {
            // Shared means somebody else leans on it. Expressed as "has an incoming edge" rather than as a type,
            // because a run-service other services consume is every bit as shared as a platform is.
            query = query.Where(item => context.Dependencies.Any(edge => edge.DependsOnItemId == item.Id));
        }

        var items = await query
            .OrderBy(item => item.Type)
            .ThenBy(item => item.Code)
            .Take(filter.Limit)
            .ToListAsync(ct);

        return await CardsAsync(items, ct);
    }

    public async Task<IReadOnlyList<CatalogCard>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";

        var items = await context.Items
            .AsNoTracking()
            .Where(item =>
                EF.Functions.ILike(item.Name, pattern)
                || EF.Functions.ILike(item.Code, pattern)
                || (item.Category != null && EF.Functions.ILike(item.Category, pattern))
                || (item.Summary != null && EF.Functions.ILike(item.Summary, pattern)))
            .OrderBy(item => item.Name)
            .Take(limit)
            .ToListAsync(ct);

        return await CardsAsync(items, ct);
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
