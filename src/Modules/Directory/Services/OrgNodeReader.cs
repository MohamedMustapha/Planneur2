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
}
