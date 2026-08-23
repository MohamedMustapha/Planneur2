using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Mediator;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Portfolio.Domain;

namespace Cracra.Modules.Portfolio.Application;

/// <summary>
/// What the catalog reads. Separate from <see cref="IPortfolioRepository"/> on purpose: that one loads an
/// aggregate to change it, this one projects rows to draw them, and conflating the two is how a list view ends up
/// loading every iteration of every item to render a grid.
/// </summary>
public interface ICatalogReader
{
    Task<IReadOnlyList<CatalogCard>> BrowseAsync(CatalogFilter filter, CancellationToken ct);

    Task<CatalogItemDetail?> DetailAsync(Guid itemId, CancellationToken ct);

    Task<IReadOnlyList<CatalogCard>> SearchAsync(string query, int limit, CancellationToken ct);

    Task<bool> CodeExistsAsync(string code, CancellationToken ct);

    Task<bool> ItemExistsAsync(Guid itemId, CancellationToken ct);

    Task<IReadOnlyList<DependencyEdge>> GetDependencyEdgesAsync(CancellationToken ct);
}

/// <summary>The node a person hangs off, for recording where a contributor contributes from.</summary>
public interface IDirectoryNodePort
{
    Task<Guid?> HomeNodeOfAsync(Guid personId, CancellationToken ct);
}

public sealed record CatalogFilter(
    string? Type,
    string? Category,
    string? Classification,
    string? State,
    Guid? OwnerNodeId,
    bool SharedOnly,
    int Limit);

public sealed record BrowseCatalogQuery(
    string? Type,
    string? Category,
    string? Classification,
    string? State,
    Guid? Owner,
    bool SharedOnly,
    int? Limit) : IRequest<IReadOnlyList<CatalogCard>>;

internal sealed class BrowseCatalogHandler(ICatalogReader catalog)
    : IRequestHandler<BrowseCatalogQuery, IReadOnlyList<CatalogCard>>
{
    private const int DefaultLimit = 200;

    public async Task<IReadOnlyList<CatalogCard>> Handle(BrowseCatalogQuery request, CancellationToken ct)
    {
        Reject(request.Type, ItemTypes.All, "type");
        Reject(request.Classification, ItemClassifications.All, "classification");
        Reject(request.State, LifecycleStates.All, "lifecycle state");

        return await catalog.BrowseAsync(
            new CatalogFilter(
                request.Type,
                request.Category,
                request.Classification,
                request.State,
                request.Owner,
                request.SharedOnly,
                Math.Clamp(request.Limit ?? DefaultLimit, 1, 500)),
            ct);
    }

    private static void Reject(string? value, IReadOnlyList<string> allowed, string what)
    {
        if (value is { Length: > 0 } given && !allowed.Contains(given, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                $"'{given}' is not a {what}. Expected one of: {string.Join(", ", allowed)}.");
        }
    }
}

public sealed record GetCatalogItemQuery(Guid ItemId) : IRequest<CatalogItemDetail>;

internal sealed class GetCatalogItemHandler(ICatalogReader catalog)
    : IRequestHandler<GetCatalogItemQuery, CatalogItemDetail>
{
    public async Task<CatalogItemDetail> Handle(GetCatalogItemQuery request, CancellationToken ct) =>
        await catalog.DetailAsync(request.ItemId, ct)
        ?? throw new ResourceNotFoundException($"No portfolio item {request.ItemId}.");
}

/// <summary>
/// "Does something similar already exist?"
/// </summary>
/// <remarks>
/// The question §03 exists to make answerable before somebody asks for a new build. It runs under the caller's own
/// session like everything else, which is why the catalog's read rule lets heads see across branches: a duplicate
/// nobody can see is a duplicate that gets built twice.
/// </remarks>
public sealed record SearchCatalogQuery(string? Q, int? Limit) : IRequest<IReadOnlyList<CatalogCard>>;

internal sealed class SearchCatalogHandler(ICatalogReader catalog)
    : IRequestHandler<SearchCatalogQuery, IReadOnlyList<CatalogCard>>
{
    public async Task<IReadOnlyList<CatalogCard>> Handle(SearchCatalogQuery request, CancellationToken ct)
    {
        var query = request.Q?.Trim() ?? string.Empty;

        return query.Length < 2 ? [] : await catalog.SearchAsync(query, Math.Clamp(request.Limit ?? 20, 1, 100), ct);
    }
}
