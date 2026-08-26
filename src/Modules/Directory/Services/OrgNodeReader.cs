using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Directory.Services;

internal sealed class OrgNodeReader(DirectoryDbContext context) : IOrgNodeReader
{
    public async Task<HomeNodeScope?> GetHomeScopeAsync(Guid personId, CancellationToken ct)
    {
        var found = await context.People
            .Where(person => person.Id == personId)
            .Select(person => new { person.HomeNodeId, person.NodeAncestorIds })
            .FirstOrDefaultAsync(ct);

        return found is null || found.HomeNodeId == Guid.Empty
            ? null
            : new HomeNodeScope(found.HomeNodeId, found.NodeAncestorIds);
    }

    public async Task<IReadOnlyList<OrgNodeSummary>> GetSubtreeAsync(Guid nodeId, CancellationToken ct) =>
        await context.OrgNodes
            .Where(node => node.AncestorIds.Contains(nodeId))
            .Select(node => new OrgNodeSummary(
                node.Id,
                node.ParentId,
                node.LevelNo,
                node.Code,
                node.Name,
                node.Active))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetPathAsync(Guid nodeId, CancellationToken ct) =>
        await context.OrgNodes
            .AsNoTracking()
            .Where(node => node.Id == nodeId)
            .Select(node => node.AncestorIds)
            .SingleOrDefaultAsync(ct) ?? [];

    public async Task<IReadOnlyList<NodeMember>> GetPeopleInSubtreeAsync(Guid nodeId, CancellationToken ct) =>
    [
        .. (await context.People
                .AsNoTracking()
                .Where(person => person.NodeAncestorIds.Contains(nodeId) && person.Active)
                .Select(person => new
                {
                    person.Id,
                    person.DisplayName,
                    person.HomeNodeId,
                    person.NodeAncestorIds,
                    person.PrimaryUnitId,
                    person.PrimaryDepartmentId,
                    person.Active,
                })
                .OrderBy(person => person.DisplayName)
                .ToListAsync(ct))
            .Select(person => new NodeMember(
                person.Id,
                person.DisplayName,
                person.HomeNodeId,
                person.NodeAncestorIds,
                person.PrimaryUnitId,
                person.PrimaryDepartmentId,
                person.Active)),
    ];
}
