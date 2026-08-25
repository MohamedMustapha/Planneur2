using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Portfolio.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Portfolio.Infrastructure;

/// <summary>
/// Item references for other modules (v2 §06.2).
/// </summary>
/// <remarks>
/// Separate from <see cref="CatalogReader"/> and much smaller, on purpose. That one draws the catalog and pays for
/// team counts, dependency counts and iteration names; a strategy listing thirty contributions needs none of that
/// and should not make thirty cards' worth of queries to render thirty lines.
/// </remarks>
internal sealed class CatalogLookupReader(PortfolioDbContext context) : ICatalogLookupReader
{
    public async Task<IReadOnlyList<CatalogCardRef>> GetByIdsAsync(
        IReadOnlyList<Guid> itemIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Count == 0)
        {
            return [];
        }

        return Project(
            await context.Items
                .AsNoTracking()
                .Where(item => itemIds.Contains(item.Id))
                .ToListAsync(ct));
    }

    public async Task<IReadOnlyList<CatalogCardRef>> GetInScopeAsync(Guid nodeId, CancellationToken ct) =>
        Project(
            await context.Items
                .AsNoTracking()
                .Where(item => item.NodeAncestorIds.Contains(nodeId))
                .OrderBy(item => item.Code)
                .ToListAsync(ct));

    private static IReadOnlyList<CatalogCardRef> Project(IReadOnlyList<PortfolioItem> items) =>
    [
        .. items.Select(item => new CatalogCardRef(
            item.Id,
            item.Code,
            item.Name,
            WireType(item.Type),
            WireState(item.State),
            item.OwnerNodeId,
            item.ProjectId)),
    ];

    private static string WireState(PortfolioState state) => state switch
    {
        PortfolioState.AwaitingVnext => LifecycleStates.AwaitingVnext,
        _ => state.ToString().ToLowerInvariant(),
    };

    private static string WireType(ItemType type) => type switch
    {
        ItemType.RunService => ItemTypes.RunService,
        ItemType.BusinessInitiative => ItemTypes.BusinessInitiative,
        _ => type.ToString().ToLowerInvariant(),
    };
}
