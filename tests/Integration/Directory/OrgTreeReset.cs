using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Services;
using Cracra.Modules.Directory.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Tests.Integration.Directory;

/// <summary>
/// Puts the org tree back to what the directory sync produces.
/// </summary>
/// <remarks>
/// <para>
/// The administration surface (v2 §08) writes two things that outlive the test that wrote them: branches created
/// through <c>POST /admin/org/nodes</c>, and the home-node override a move records. Both are durable by design —
/// a correction that the next sync undid would not be a correction — and the database is shared by every class in
/// the collection, so a class that counts branches or reads a person's hours is asserting against whatever the
/// administration tests happened to leave.
/// </para>
/// <para>
/// That is order-dependent rather than wrong, which is the worst way for it to be: it passes until somebody adds
/// a test and shifts the interleaving, and then fails somewhere unrelated to the change. Classes that depend on
/// the seeded shape call this the way they already clear activity rows they did not write.
/// </para>
/// </remarks>
internal static class OrgTreeReset
{
    public static async Task ApplyAsync(CracraApplicationFactory factory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

            var directory = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

            // Anything an administrator invented, which by construction is a node with no legacy twin. The
            // placeholder root is kept: it is seeded rather than authored, and the projection hangs orphans off
            // it.
            var projected = await directory.Departments.Select(department => department.Id)
                .Union(directory.Units.Select(unit => unit.Id))
                .ToListAsync(ct);

            projected.Add(OrgTreeSql.UnclassifiedRootId);

            await directory.People
                .Where(person => person.HomeNodeOverrideId != null)
                .ExecuteUpdateAsync(person => person.SetProperty(row => row.HomeNodeOverrideId, (Guid?)null), ct);

            await directory.OrgNodes
                .Where(node => !projected.Contains(node.Id))
                .ExecuteDeleteAsync(ct);
        }

        // Re-derives every home node from the legacy placement the override was hiding.
        await factory.Services.GetRequiredService<IDirectorySynchronizer>().SynchronizeAsync(ct);
    }
}
