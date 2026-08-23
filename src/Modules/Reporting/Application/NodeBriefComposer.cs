using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Reporting.Contracts;
using Cracra.Modules.Reporting.Domain;

namespace Cracra.Modules.Reporting.Application;

public static class BriefDepths
{
    public const string Direct = "1";
    public const string All = "all";

    public static readonly IReadOnlyList<string> Supported = [Direct, All];
}

public interface INodeBriefComposer
{
    Task<NodeBriefView?> ComposeAsync(Guid nodeId, ReportPeriod period, string depth, CancellationToken ct);
}

/// <summary>
/// The rollup, folded once from per-node slices (v2 §01.4).
/// </summary>
/// <remarks>
/// <para>
/// Two reads and no recursion in SQL: the subtree's nodes, and the hours grouped by the node each entry hangs off.
/// Everything else is arithmetic over a dictionary, which is what makes the invariant — a node's subtree total
/// equals its own plus its children's subtree totals — true by construction rather than by two queries agreeing.
/// </para>
/// <para>
/// Both reads run in the caller's session, so a head sees their subtree and nothing beside it, and the brief needs
/// no scope check of its own. A node the caller cannot read comes back as a null view, which the endpoint turns
/// into the same 404 a non-existent id gets.
/// </para>
/// </remarks>
internal sealed class NodeBriefComposer(IOrgNodeQueries nodes, IActivityQueries activities) : INodeBriefComposer
{
    public async Task<NodeBriefView?> ComposeAsync(
        Guid nodeId,
        ReportPeriod period,
        string depth,
        CancellationToken ct)
    {
        var subtree = await nodes.SubtreeAsync(nodeId, ct);

        var root = subtree.FirstOrDefault(node => node.Id == nodeId);

        if (root is null)
        {
            return null;
        }

        var slices = await activities.HoursByNodeAsync(nodeId, period.From, period.To, ct);

        var own = slices.ToDictionary(
            slice => slice.NodeId,
            slice => new BriefTotals(
                slice.ActualHours,
                slice.PlannedHours,
                slice.EntryCount,
                slice.PeopleCount));

        var childrenOf = subtree
            .Where(node => node.ParentId is not null)
            .GroupBy(node => node.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var block = Fold(root, childrenOf, own, depth == BriefDepths.All ? int.MaxValue : 1);

        return new NodeBriefView(nodeId, period.ToView(), depth, block);
    }

    /// <summary>
    /// Builds a block and its descendants.
    /// </summary>
    /// <param name="remainingDepth">
    /// How many more generations to render. The totals never stop at it — a child block reports its whole subtree
    /// however deep the rendering goes, which is the property that lets a head read one line per child instead of
    /// four hundred rows and still see the real number.
    /// </param>
    private static NodeBriefBlock Fold(
        OrgNodeSummary node,
        IReadOnlyDictionary<Guid, OrgNodeSummary[]> childrenOf,
        IReadOnlyDictionary<Guid, BriefTotals> own,
        int remainingDepth)
    {
        var mine = own.TryGetValue(node.Id, out var found) ? found : BriefTotals.Zero;

        var children = childrenOf.TryGetValue(node.Id, out var below)
            ? below.OrderBy(child => child.Code, StringComparer.Ordinal)
                .Select(child => Fold(child, childrenOf, own, remainingDepth - 1))
                .ToArray()
            : [];

        var subtree = children.Aggregate(mine, (running, child) => running + child.Subtree);

        return new NodeBriefBlock(
            node.Id,
            node.ParentId,
            node.LevelNo,
            node.Code,
            node.Name,
            mine,
            subtree,
            remainingDepth > 0 ? children : []);
    }
}
