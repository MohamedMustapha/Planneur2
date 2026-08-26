using Cracra.Modules.Finance.Domain;

namespace Cracra.Tests.Unit.Finance;

/// <summary>
/// The rollup arithmetic (v2 §04.1).
/// </summary>
/// <remarks>
/// The invariant — a node's subtree cost is its own plus its children's subtree costs — is what lets a head at any
/// depth hand their table upward. It is asserted at every node rather than at the root, because an off-by-one in
/// the fold still balances at the top; and against a tree with a skipped level, because that is the shape a real
/// deployment has and the one a fixed three-rung ladder would get wrong.
/// </remarks>
public sealed class ConsolidationTests
{
    private static readonly Guid Top = Guid.Parse("e2000000-0000-0000-0000-000000000001");
    private static readonly Guid Left = Guid.Parse("e2000000-0000-0000-0000-000000000002");
    private static readonly Guid Right = Guid.Parse("e2000000-0000-0000-0000-000000000003");
    private static readonly Guid Pole = Guid.Parse("e2000000-0000-0000-0000-000000000004");

    /// <summary>Top, two branches, and a pôle under the left one only — the right branch skips a level.</summary>
    private static readonly ConsolidationNode[] Tree =
    [
        new(Top, null, 1, "TOP", "Top"),
        new(Left, Top, 2, "LEFT", "Left"),
        new(Right, Top, 2, "RIGHT", "Right"),
        new(Pole, Left, 3, "POLE", "Pôle"),
    ];

    [Fact]
    public void A_leaf_owns_everything_it_carries()
    {
        var folded = Fold(Own(Pole, capex: 100m, itemOpex: 50m));

        var pole = Find(folded, Pole);

        pole.Own.Capex.ShouldBe(100m);
        pole.Own.Opex.ShouldBe(50m);
        pole.Subtree.ShouldBe(pole.Own);
    }

    [Fact]
    public void A_parent_carries_what_its_children_carry()
    {
        var folded = Fold(Own(Pole, capex: 100m), Own(Left, capex: 10m));

        Find(folded, Left).Own.Capex.ShouldBe(10m);
        Find(folded, Left).Subtree.Capex.ShouldBe(110m);
    }

    [Fact]
    public void The_invariant_holds_at_every_node_including_the_branch_that_skips_a_level()
    {
        var folded = Fold(
            Own(Pole, capex: 100m, itemOpex: 25m),
            Own(Left, capex: 10m),
            Own(Right, capex: 7m, itemOpex: 3m));

        AssertRollup(folded);
    }

    [Fact]
    public void A_node_nobody_spent_anything_at_is_zero_rather_than_missing()
    {
        // The consolidated page draws a row per node whatever happened there. A missing entry would render as a
        // gap and read as "we do not know", which is a different claim from "nothing was spent".
        var folded = Fold(Own(Pole, capex: 100m));

        Find(folded, Right).Own.ShouldBe(CostSplit.Zero);
        Find(folded, Right).Subtree.ShouldBe(CostSplit.Zero);
    }

    [Fact]
    public void What_the_rule_excluded_is_carried_rather_than_dropped()
    {
        // Excluded is neither capex nor opex, and it still happened. A total that quietly omitted it would not
        // reconcile against the hours the same period reports.
        var folded = Fold([new NodeOwnCost(Pole, new CostSplit(0m, 0m, 40m), CostSplit.Zero)]);

        Find(folded, Pole).Subtree.Excluded.ShouldBe(40m);
        Find(folded, Pole).Subtree.Total.ShouldBe(0m);
    }

    [Fact]
    public void Variance_is_planned_minus_spent_at_the_node_that_set_an_envelope()
    {
        var folded = Consolidation.Fold(
            Top,
            Tree,
            Dict(Own(Pole, capex: 100m)),
            new Dictionary<Guid, decimal> { [Top] = 250m },
            int.MaxValue);

        folded.PlannedAmount.ShouldBe(250m);
        folded.Variance.ShouldBe(150m);
    }

    [Fact]
    public void A_node_with_no_envelope_reports_no_variance_rather_than_zero()
    {
        // Zero reads as "on budget". "Nobody drew a budget around this" is a different statement, and the page has
        // to be able to make it.
        var folded = Fold(Own(Pole, capex: 100m));

        Find(folded, Left).PlannedAmount.ShouldBeNull();
        Find(folded, Left).Variance.ShouldBeNull();
    }

    [Fact]
    public void An_envelope_is_compared_against_the_whole_subtree_and_not_just_the_node()
    {
        // "Budget par direction" is the same mechanism as "budget par équipe": what a parent set is measured
        // against everything beneath it, which is the only reading that makes a directorate's envelope mean
        // anything.
        var folded = Consolidation.Fold(
            Top,
            Tree,
            Dict(Own(Pole, capex: 60m), Own(Right, capex: 30m)),
            new Dictionary<Guid, decimal> { [Top] = 100m },
            int.MaxValue);

        folded.Subtree.Capex.ShouldBe(90m);
        folded.Variance.ShouldBe(10m);
    }

    [Fact]
    public void Depth_one_renders_the_children_and_stops_without_losing_their_totals()
    {
        // The property that lets a head read one line per child: the rendering stops, the arithmetic does not.
        var folded = Consolidation.Fold(Top, Tree, Dict(Own(Pole, capex: 100m)), Empty, depth: 1);

        folded.Children.ShouldAllBe(child => child.Children.Count == 0);
        Find(folded, Left).Subtree.Capex.ShouldBe(100m);
    }

    [Fact]
    public void Items_and_direct_costs_are_kept_apart_but_both_count_as_the_nodes_own()
    {
        // A node-wide licence and a product's cloud bill are different lines on the page and the same money in
        // the total.
        var folded = Fold([new NodeOwnCost(Pole, new CostSplit(5m, 0m, 0m), new CostSplit(0m, 20m, 0m))]);

        var pole = Find(folded, Pole);

        pole.Direct.Capex.ShouldBe(5m);
        pole.Items.Opex.ShouldBe(20m);
        pole.Own.Total.ShouldBe(25m);
    }

    private static void AssertRollup(ConsolidatedNode node)
    {
        var expected = node.Children.Aggregate(node.Own, (running, child) => running + child.Subtree);

        node.Subtree.Capex.ShouldBe(expected.Capex, $"capex at {node.Code}");
        node.Subtree.Opex.ShouldBe(expected.Opex, $"opex at {node.Code}");
        node.Subtree.Excluded.ShouldBe(expected.Excluded, $"excluded at {node.Code}");

        foreach (var child in node.Children)
        {
            AssertRollup(child);
        }
    }

    private static ConsolidatedNode Find(ConsolidatedNode root, Guid nodeId) =>
        Search(root, nodeId) ?? throw new InvalidOperationException($"No node {nodeId} in the folded tree.");

    private static ConsolidatedNode? Search(ConsolidatedNode root, Guid nodeId) =>
        root.NodeId == nodeId
            ? root
            : root.Children.Select(child => Search(child, nodeId)).FirstOrDefault(found => found is not null);

    private static ConsolidatedNode Fold(params NodeOwnCost[] own) =>
        Consolidation.Fold(Top, Tree, Dict(own), Empty, int.MaxValue);

    private static IReadOnlyDictionary<Guid, NodeOwnCost> Dict(params NodeOwnCost[] own) =>
        own.ToDictionary(entry => entry.NodeId);

    private static IReadOnlyDictionary<Guid, decimal> Empty => new Dictionary<Guid, decimal>();

    private static NodeOwnCost Own(Guid nodeId, decimal capex = 0m, decimal itemOpex = 0m) =>
        new(nodeId, new CostSplit(capex, 0m, 0m), new CostSplit(0m, itemOpex, 0m));
}
