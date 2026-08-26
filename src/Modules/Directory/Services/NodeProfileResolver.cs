using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Directory.Services;

/// <summary>
/// Loads a node's ancestry chain and hands it to <see cref="NodeProfileResolution"/>.
/// </summary>
/// <remarks>
/// <para>
/// Split from the walk itself so this type only does I/O and that one only does the rule. What lives here is the
/// unglamorous half — read the unit, read its department, follow <c>parent_department_id</c> up — and the reason
/// it is worth its own type is the loop guard: a directory whose parent pointers form a cycle is a real
/// possibility after a bad re-parent, and the walk must terminate rather than hang the request that discovered it.
/// </para>
/// <para>
/// Caller-scoped: the reads run under the caller's RLS session, so a department the caller cannot see contributes
/// nothing to the chain. That is deliberately the same answer as "that ancestor has no profile" — the resolution
/// degrades to what the caller may know about, rather than leaking the existence of a node above them.
/// </para>
/// </remarks>
internal sealed class NodeProfileResolver(DirectoryDbContext context) : INodeProfileReader
{
    /// <summary>
    /// How far up the walk will follow parent pointers before giving up.
    /// </summary>
    /// <remarks>
    /// Not a statement about how deep an org may be — the visited set already makes cycles terminate. It is a
    /// second belt for the case where the chain is not cyclic but is absurd, so a corrupted directory produces a
    /// bounded answer instead of an unbounded query loop.
    /// </remarks>
    private const int MaxDepth = 64;

    public async Task<NodeProfileSnapshot?> ResolveForUnitAsync(Guid unitId, CancellationToken ct)
    {
        var unit = await context.Units
            .AsNoTracking()
            .Where(candidate => candidate.Id == unitId)
            .Select(candidate => new { candidate.ProfileId, candidate.DepartmentId })
            .SingleOrDefaultAsync(ct);

        if (unit is null)
        {
            return null;
        }

        var attached = new List<Guid>();

        if (unit.ProfileId is { } unitProfile)
        {
            attached.Add(unitProfile);
        }

        attached.AddRange(await DepartmentChainAsync(unit.DepartmentId, ct));

        return NodeProfileResolution.Resolve(await LoadAsync(attached, ct));
    }

    public async Task<NodeProfileSnapshot?> ResolveForDepartmentAsync(Guid departmentId, CancellationToken ct) =>
        NodeProfileResolution.Resolve(await LoadAsync(await DepartmentChainAsync(departmentId, ct), ct));

    /// <summary>
    /// The profile in force at a node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy rows are consulted first, and that order is load-bearing rather than a preference. During the
    /// shim <c>org_node.profile_id</c> is a <em>projection</em> of <c>unit.profile_id</c>, refreshed when the
    /// projection sweeps — while an administrator attaching a profile writes the unit and nothing else. Walking
    /// the tree first therefore answers with whatever the last sweep copied, which is stale the moment somebody
    /// attaches anything, and a capability that reads as on because a sweep has not run yet is exactly the kind
    /// of wrong nobody notices.
    /// </para>
    /// <para>
    /// The tree walk is what answers for a node with no legacy twin — one created through the org admin — and it
    /// becomes the only path once the legacy tables go.
    /// </para>
    /// </remarks>
    public async Task<NodeProfileSnapshot?> ResolveForNodeAsync(Guid nodeId, CancellationToken ct)
    {
        if (await context.Units.AsNoTracking().AnyAsync(unit => unit.Id == nodeId, ct))
        {
            return await ResolveForUnitAsync(nodeId, ct);
        }

        if (await context.Departments.AsNoTracking().AnyAsync(department => department.Id == nodeId, ct))
        {
            return await ResolveForDepartmentAsync(nodeId, ct);
        }

        var path = await context.OrgNodes
            .AsNoTracking()
            .Where(node => node.Id == nodeId)
            .Select(node => node.AncestorIds)
            .SingleOrDefaultAsync(ct);

        if (path is null || path.Length == 0)
        {
            return null;
        }

        var attached = await context.OrgNodes
            .AsNoTracking()
            .Where(node => path.Contains(node.Id) && node.ProfileId != null)
            .Select(node => new { node.Id, ProfileId = node.ProfileId!.Value })
            .ToDictionaryAsync(node => node.Id, node => node.ProfileId, ct);

        var nearestFirst = path.Reverse().Where(attached.ContainsKey).Select(id => attached[id]).ToList();

        return NodeProfileResolution.Resolve(await LoadAsync(nearestFirst, ct));
    }

    /// <summary>Profile ids attached along a department's ancestry, nearest first.</summary>
    private async Task<List<Guid>> DepartmentChainAsync(Guid departmentId, CancellationToken ct)
    {
        var attached = new List<Guid>();
        var visited = new HashSet<Guid>();
        Guid? current = departmentId;

        for (var depth = 0; current is { } id && depth < MaxDepth && visited.Add(id); depth++)
        {
            var department = await context.Departments
                .AsNoTracking()
                .Where(candidate => candidate.Id == id)
                .Select(candidate => new { candidate.ProfileId, candidate.ParentDepartmentId })
                .SingleOrDefaultAsync(ct);

            if (department is null)
            {
                break;
            }

            if (department.ProfileId is { } profileId)
            {
                attached.Add(profileId);
            }

            current = department.ParentDepartmentId;
        }

        return attached;
    }

    /// <summary>
    /// Materialises the chain, preserving order.
    /// </summary>
    /// <remarks>
    /// One query rather than one per level, then re-ordered in memory: the chain is a handful of rows and the
    /// order is what the whole walk means, so it is worth being explicit that the database's row order is not
    /// being trusted to carry it.
    /// </remarks>
    private async Task<IReadOnlyList<NodeProfile>> LoadAsync(List<Guid> attached, CancellationToken ct)
    {
        if (attached.Count == 0)
        {
            return [];
        }

        var profiles = await context.NodeProfiles
            .AsNoTracking()
            .Where(profile => attached.Contains(profile.Id))
            .ToDictionaryAsync(profile => profile.Id, ct);

        return [.. attached.Where(profiles.ContainsKey).Select(id => profiles[id])];
    }
}
