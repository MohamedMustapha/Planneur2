namespace Cracra.Modules.Strategy.Application;

/// <summary>
/// What a contributing thing looks like from here.
/// </summary>
/// <remarks>
/// Deliberately the smallest shape that renders a line on an objective card: enough to recognise it and to show
/// its own state in its own words. Anything more and Strategy would be re-rendering somebody else's card.
/// </remarks>
public sealed record ContributionFacts(Guid Id, string Code, string Name, string Type, string State, Guid NodeId);

/// <summary>What Strategy needs from Portfolio to draw a rollup and find alignment gaps.</summary>
public interface IPortfolioFactsPort
{
    Task<IReadOnlyDictionary<Guid, ContributionFacts>> GetItemsAsync(
        IReadOnlyList<Guid> itemIds,
        CancellationToken ct);

    /// <summary>Every item the caller may see under a node, so the alignment view can subtract the linked ones.</summary>
    Task<IReadOnlyList<ContributionFacts>> GetItemsInScopeAsync(Guid nodeId, CancellationToken ct);
}

/// <summary>What Strategy needs from Problems. Narrower: a problem is never an alignment gap, only a source.</summary>
public interface IProblemFactsPort
{
    Task<IReadOnlyDictionary<Guid, ContributionFacts>> GetProblemsAsync(
        IReadOnlyList<Guid> problemIds,
        CancellationToken ct);
}
