using Cracra.Modules.Directory.Data;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Directory.Services;

public interface IOrgTreeProjection
{
    Task ProjectAsync(DirectoryDbContext context, CancellationToken ct);
}

internal sealed class OrgTreeProjection : IOrgTreeProjection
{
    public async Task ProjectAsync(DirectoryDbContext context, CancellationToken ct)
    {
        await context.Database.ExecuteSqlRawAsync(OrgTreeSql.Project, ct);
    }
}
