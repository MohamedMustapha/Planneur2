namespace Cracra.Modules.Finance.Domain;

/// <summary>Capex, opex and what the rule excluded, kept apart because the page asks for them apart.</summary>
public readonly record struct CostSplit(decimal Capex, decimal Opex, decimal Excluded)
{
    public static readonly CostSplit Zero = new(0m, 0m, 0m);

    public decimal Total => Capex + Opex;

    public static CostSplit operator +(CostSplit left, CostSplit right) => new(
        left.Capex + right.Capex,
        left.Opex + right.Opex,
        left.Excluded + right.Excluded);

    public static CostSplit Of(string treatment, decimal amount) => treatment switch
    {
        Treatments.Capex => new CostSplit(amount, 0m, 0m),
        Treatments.Opex => new CostSplit(0m, amount, 0m),
        _ => new CostSplit(0m, 0m, amount),
    };
}

/// <summary>One node in the tree, as the consolidation walks it.</summary>
public sealed record ConsolidationNode(Guid Id, Guid? ParentId, int LevelNo, string Code, string Name);

/// <summary>What one node owns directly: its own components, plus the items it sponsors.</summary>
public sealed record NodeOwnCost(Guid NodeId, CostSplit Direct, CostSplit Items);

/// <summary>
/// The recursive definition from §04.1, as one fold.
/// </summary>
/// <remarks>
/// <para>
/// <c>node_subtree_cost(n) == node_own_cost(n) + Σ node_subtree_cost(children(n))</c>, computed once from
/// per-node totals rather than by asking the database a recursive question per level. The invariant holds by
/// construction because both halves come out of the same fold — two separate aggregations that agree today are
/// two aggregations that will disagree the first time somebody adds a level.
/// </para>
/// <para>
/// Pure, so the arithmetic that decides what a directorate is told it spent can be argued with in a unit test.
/// </para>
/// </remarks>
public static class Consolidation
{
    public static ConsolidatedNode Fold(
        Guid rootId,
        IReadOnlyCollection<ConsolidationNode> nodes,
        IReadOnlyDictionary<Guid, NodeOwnCost> own,
        IReadOnlyDictionary<Guid, decimal> budgets,
        int depth)
    {
        var root = nodes.SingleOrDefault(node => node.Id == rootId)
                   ?? new ConsolidationNode(rootId, null, 0, string.Empty, string.Empty);

        var childrenOf = nodes
            .Where(node => node.ParentId is not null && node.Id != rootId)
            .GroupBy(node => node.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return Build(root, childrenOf, own, budgets, depth);
    }

    private static ConsolidatedNode Build(
        ConsolidationNode node,
        IReadOnlyDictionary<Guid, ConsolidationNode[]> childrenOf,
        IReadOnlyDictionary<Guid, NodeOwnCost> own,
        IReadOnlyDictionary<Guid, decimal> budgets,
        int remainingDepth)
    {
        var mine = own.TryGetValue(node.Id, out var found)
            ? found
            : new NodeOwnCost(node.Id, CostSplit.Zero, CostSplit.Zero);

        var children = childrenOf.TryGetValue(node.Id, out var below)
            ? below.OrderBy(child => child.Code, StringComparer.Ordinal)
                .Select(child => Build(child, childrenOf, own, budgets, remainingDepth - 1))
                .ToArray()
            : [];

        var ownTotal = mine.Direct + mine.Items;
        var subtree = children.Aggregate(ownTotal, (running, child) => running + child.Subtree);

        budgets.TryGetValue(node.Id, out var planned);

        return new ConsolidatedNode(
            node.Id,
            node.ParentId,
            node.LevelNo,
            node.Code,
            node.Name,
            mine.Direct,
            mine.Items,
            ownTotal,
            subtree,
            planned == 0 ? null : planned,
            planned == 0 ? null : planned - subtree.Total,
            remainingDepth > 0 ? children : []);
    }
}

/// <param name="Own">Everything attached at this node: its own components plus the items it sponsors.</param>
/// <param name="Subtree">
/// <paramref name="Own"/> plus every descendant's. What a parent's envelope is compared against.
/// </param>
/// <param name="Variance">
/// Planned minus spent, and null where nobody set an envelope. Zero would read as "on budget", which is a very
/// different statement from "nobody drew a budget around this".
/// </param>
public sealed record ConsolidatedNode(
    Guid NodeId,
    Guid? ParentId,
    int LevelNo,
    string Code,
    string Name,
    CostSplit Direct,
    CostSplit Items,
    CostSplit Own,
    CostSplit Subtree,
    decimal? PlannedAmount,
    decimal? Variance,
    IReadOnlyList<ConsolidatedNode> Children);
