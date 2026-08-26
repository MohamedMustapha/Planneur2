namespace Cracra.Modules.Portfolio.Domain;

/// <summary>One edge of the catalog's dependency graph, as the cycle check sees it.</summary>
public readonly record struct DependencyEdge(Guid ItemId, Guid DependsOnItemId);

/// <summary>
/// Whether a proposed dependency closes a loop.
/// </summary>
/// <remarks>
/// <para>
/// Pure and static so the rule can be argued with in a unit test rather than against a database. A cycle is a
/// property of the whole graph, which no single aggregate can see, so this takes the edges as an argument and the
/// caller loads them.
/// </para>
/// <para>
/// A cycle here is not a theoretical concern. The catalog renders "consumes" and "consumed by" from the same
/// edges, and a loop makes both views infinite; worse, it makes "what would break if we retired the cluster"
/// unanswerable, which is the question the graph exists for.
/// </para>
/// </remarks>
public static class DependencyGraph
{
    /// <summary>
    /// True when <paramref name="dependsOn"/> already reaches <paramref name="item"/>, so adding the edge would
    /// close a loop.
    /// </summary>
    public static bool WouldCycle(IReadOnlyCollection<DependencyEdge> edges, Guid item, Guid dependsOn)
    {
        if (item == dependsOn)
        {
            return true;
        }

        var outgoing = edges
            .GroupBy(edge => edge.ItemId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.DependsOnItemId).ToArray());

        var seen = new HashSet<Guid>();
        var pending = new Stack<Guid>();

        pending.Push(dependsOn);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (current == item)
            {
                return true;
            }

            if (!seen.Add(current) || !outgoing.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var candidate in next)
            {
                pending.Push(candidate);
            }
        }

        return false;
    }
}
