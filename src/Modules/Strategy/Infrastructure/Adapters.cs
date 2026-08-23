using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Problems.Contracts;
using Cracra.Modules.Strategy.Application;

namespace Cracra.Modules.Strategy.Infrastructure;

/// <summary>
/// The Portfolio port, implemented against Portfolio's contract.
/// </summary>
/// <remarks>
/// Strategy never touches the portfolio schema — an architecture test asserts it cannot even reference that
/// assembly. Every call runs on the caller's own connection, which is what makes an objective card show exactly
/// the items its reader could have opened themselves and no more.
/// </remarks>
internal sealed class PortfolioFactsAdapter(ICatalogLookupReader catalog) : IPortfolioFactsPort
{
    public async Task<IReadOnlyDictionary<Guid, ContributionFacts>> GetItemsAsync(
        IReadOnlyList<Guid> itemIds,
        CancellationToken ct) =>
        (await catalog.GetByIdsAsync(itemIds, ct)).ToDictionary(item => item.Id, Facts);

    public async Task<IReadOnlyList<ContributionFacts>> GetItemsInScopeAsync(Guid nodeId, CancellationToken ct) =>
        [.. (await catalog.GetInScopeAsync(nodeId, ct)).Select(Facts)];

    private static ContributionFacts Facts(CatalogCardRef item) =>
        new(item.Id, item.Code, item.Name, item.Type, item.State, item.OwnerNodeId);
}

/// <summary>The Problems port. Same rules as its Portfolio neighbour, and the same reason for existing.</summary>
internal sealed class ProblemFactsAdapter(IProblemLookupReader problems) : IProblemFactsPort
{
    public async Task<IReadOnlyDictionary<Guid, ContributionFacts>> GetProblemsAsync(
        IReadOnlyList<Guid> problemIds,
        CancellationToken ct) =>
        (await problems.GetByIdsAsync(problemIds, ct)).ToDictionary(
            problem => problem.Id,
            problem => new ContributionFacts(
                problem.Id,
                problem.Code,
                problem.Title,
                Contracts.ContributionSources.Problem,
                problem.Status,
                problem.NodeId));
}
