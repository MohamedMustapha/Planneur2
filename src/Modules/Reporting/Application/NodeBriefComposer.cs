using Cracra.BuildingBlocks.Web.Users;
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
    /// <param name="nodeId">Empty means "wherever I hang off the tree", which is what the Reports page asks for.</param>
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
internal sealed class NodeBriefComposer(
    IOrgNodeQueries nodes,
    IActivityQueries activities,
    IMeetingQueries meetings,
    IUserContext user) : INodeBriefComposer
{
    /// <summary>How many activity types a headline names before it stops being a headline (v2 §07.3).</summary>
    private const int HighlightCount = 3;

    /// <summary>How far ahead the brief looks for the next COPIL or freeze. A fortnight, as everywhere else.</summary>
    private const int UpcomingDays = 14;

    public async Task<NodeBriefView?> ComposeAsync(
        Guid nodeId,
        ReportPeriod period,
        string depth,
        CancellationToken ct)
    {
        if (nodeId == Guid.Empty)
        {
            if (await nodes.HomeNodeAsync(user.UserId, ct) is not { } home)
            {
                return null;
            }

            nodeId = home;
        }

        var subtree = await nodes.SubtreeAsync(nodeId, ct);

        var root = subtree.FirstOrDefault(node => node.Id == nodeId);

        if (root is null)
        {
            return null;
        }

        var slices = await activities.HoursByNodeAsync(nodeId, period.From, period.To, ct);
        var byType = await activities.HighlightsByNodeAsync(nodeId, period.From, period.To, ct);

        var own = slices.ToDictionary(
            slice => slice.NodeId,
            slice => new BriefTotals(
                slice.ActualHours,
                slice.PlannedHours,
                slice.EntryCount,
                slice.PeopleCount));

        var highlights = byType
            .GroupBy(slice => slice.NodeId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, decimal>)group
                    .GroupBy(slice => slice.ActivityTypeCode, StringComparer.Ordinal)
                    .ToDictionary(
                        types => types.Key,
                        types => types.Sum(slice => slice.ActualHours),
                        StringComparer.Ordinal));

        var childrenOf = subtree
            .Where(node => node.ParentId is not null)
            .GroupBy(node => node.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var block = Fold(root, childrenOf, own, highlights, depth == BriefDepths.All ? int.MaxValue : 1);

        return new NodeBriefView(nodeId, period.ToView(), depth, block, await UpcomingAsync(ct));
    }

    /// <summary>
    /// What is dated and close enough to mention.
    /// </summary>
    /// <remarks>
    /// Read through the meetings port, so it is already narrowed to what the caller may see — a brief cannot
    /// announce a COPIL its reader has no business knowing about. From today rather than from the period, because
    /// the brief is presented forward: what a COPIL wants under the numbers is what is coming, not what was.
    /// </remarks>
    private async Task<IReadOnlyList<BriefUpcoming>> UpcomingAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        var entries = await meetings.InWindowAsync(today, today.AddDays(UpcomingDays), ct);

        return
        [
            .. entries
                .OrderBy(entry => entry.At)
                .Select(entry => new BriefUpcoming(entry.Kind, entry.NameKey, entry.At, entry.Severity)),
        ];
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
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, decimal>> highlights,
        int remainingDepth)
    {
        var mine = own.TryGetValue(node.Id, out var found) ? found : BriefTotals.Zero;

        var children = childrenOf.TryGetValue(node.Id, out var below)
            ? below.OrderBy(child => child.Code, StringComparer.Ordinal)
                .Select(child => Fold(child, childrenOf, own, highlights, remainingDepth - 1))
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
            remainingDepth > 0 ? children : [],
            TopTypes(node.Id, childrenOf, highlights));
    }

    /// <summary>
    /// The subtree's three biggest activity types.
    /// </summary>
    /// <remarks>
    /// Over the subtree rather than the node's own hours, and that is the property that makes a brief mergeable:
    /// a head reading one line per child sees what that whole branch spent its week on, which is the same answer
    /// the child's own brief gives. A node's own hours are already on the block beside this.
    /// </remarks>
    private static IReadOnlyList<BriefHighlight> TopTypes(
        Guid nodeId,
        IReadOnlyDictionary<Guid, OrgNodeSummary[]> childrenOf,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, decimal>> highlights)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);

        Accumulate(nodeId, childrenOf, highlights, totals);

        return
        [
            .. totals
                .Where(pair => pair.Value > 0m)
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(HighlightCount)
                .Select(pair => new BriefHighlight(pair.Key, pair.Value)),
        ];
    }

    private static void Accumulate(
        Guid nodeId,
        IReadOnlyDictionary<Guid, OrgNodeSummary[]> childrenOf,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, decimal>> highlights,
        Dictionary<string, decimal> into)
    {
        if (highlights.TryGetValue(nodeId, out var mine))
        {
            foreach (var (type, hours) in mine)
            {
                into[type] = into.GetValueOrDefault(type) + hours;
            }
        }

        if (!childrenOf.TryGetValue(nodeId, out var below))
        {
            return;
        }

        foreach (var child in below)
        {
            Accumulate(child.Id, childrenOf, highlights, into);
        }
    }
}
