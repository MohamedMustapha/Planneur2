using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Domain;
using Cracra.Modules.Directory.Services;
using Cracra.Modules.Directory.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Directory;

[Collection(DatabaseCollection.Name)]
public sealed class OrgTreeProjectionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Every_department_and_unit_reaches_the_tree_under_one_placeholder_top()
    {
        await using var factory = await SyncedAsync();
        await using var scope = SystemScope(factory);

        var nodes = await NodesAsync(scope);

        nodes.ShouldContainKey(OrgTreeSql.UnclassifiedRootId);

        foreach (var department in await Directory(scope).Departments.ToListAsync(TestContext.Current.CancellationToken))
        {
            nodes.ShouldContainKey(department.Id);
            nodes[department.Id].LevelNo.ShouldBe(2);
            nodes[department.Id].ParentId.ShouldBe(OrgTreeSql.UnclassifiedRootId);
        }

        foreach (var unit in await Directory(scope).Units.ToListAsync(TestContext.Current.CancellationToken))
        {
            nodes.ShouldContainKey(unit.Id);
            nodes[unit.Id].LevelNo.ShouldBe(3);
            nodes[unit.Id].ParentId.ShouldBe(unit.DepartmentId);
        }
    }

    [Fact]
    public async Task A_units_ancestry_reads_root_department_unit()
    {
        await using var factory = await SyncedAsync();
        await using var scope = SystemScope(factory);

        var nodes = await NodesAsync(scope);
        var infrastructure = nodes[SeedOrganisation.Units.Infrastructure];

        infrastructure.AncestorIds.ShouldBe([
            OrgTreeSql.UnclassifiedRootId,
            SeedOrganisation.Departments.InformationSystems,
            SeedOrganisation.Units.Infrastructure,
        ]);
    }

    [Fact]
    public async Task A_second_sync_adds_nothing_and_changes_nothing()
    {
        await using var factory = await SyncedAsync();

        await using (var first = SystemScope(factory))
        {
            var before = await NodesAsync(first);

            await factory.Services.GetRequiredService<IDirectorySynchronizer>()
                .SynchronizeAsync(TestContext.Current.CancellationToken);

            await using var second = SystemScope(factory);
            var after = await NodesAsync(second);

            after.Count.ShouldBe(before.Count);
            after.Keys.ShouldBe(before.Keys, ignoreOrder: true);

            foreach (var (id, node) in after)
            {
                node.AncestorIds.ShouldBe(before[id].AncestorIds);
                node.ParentId.ShouldBe(before[id].ParentId);
            }
        }
    }

    [Fact]
    public async Task A_node_count_that_matches_the_org_it_was_projected_from()
    {
        await using var factory = await SyncedAsync();
        await using var scope = SystemScope(factory);

        var ct = TestContext.Current.CancellationToken;
        var directory = Directory(scope);

        var departments = await directory.Departments.CountAsync(ct);
        var units = await directory.Units.CountAsync(ct);
        var nodes = await directory.OrgNodes.CountAsync(ct);

        nodes.ShouldBe(departments + units + 1);
    }

    [Fact]
    public async Task Everybody_is_attached_to_the_node_their_branch_actually_has()
    {
        await using var factory = await SyncedAsync();
        await using var scope = SystemScope(factory);

        var ct = TestContext.Current.CancellationToken;
        var nodes = await NodesAsync(scope);

        var people = await Directory(scope).People.ToListAsync(ct);

        people.ShouldNotBeEmpty();

        foreach (var person in people)
        {
            person.HomeNodeId.ShouldNotBe(Guid.Empty);
            person.HomeNodeId.ShouldBe(person.PrimaryUnitId!.Value);
            person.NodeAncestorIds.ShouldBe(nodes[person.HomeNodeId].AncestorIds);
        }
    }

    [Fact]
    public async Task Moving_a_branch_moves_the_path_every_person_beneath_it_carries()
    {
        await using var factory = await SyncedAsync();
        await using var scope = SystemScope(factory);

        var ct = TestContext.Current.CancellationToken;
        var directory = Directory(scope);

        var second = new OrgNode
        {
            Id = Guid.NewGuid(),
            LevelNo = 1,
            Code = $"SECOND-{Guid.NewGuid():N}",
            Name = "Second top",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        };

        directory.OrgNodes.Add(second);
        await directory.SaveChangesAsync(ct);

        var department = await directory.OrgNodes
            .AsTracking()
            .SingleAsync(node => node.Id == SeedOrganisation.Departments.InformationSystems, ct);

        department.ParentId = second.Id;
        await directory.SaveChangesAsync(ct);

        directory.ChangeTracker.Clear();

        var camille = await directory.People.SingleAsync(person => person.Id == SeedOrganisation.Camille.UserId, ct);

        camille.NodeAncestorIds.ShouldBe([
            second.Id,
            SeedOrganisation.Departments.InformationSystems,
            SeedOrganisation.Units.Infrastructure,
        ]);

        directory.ChangeTracker.Clear();

        var moved = await directory.OrgNodes
            .AsTracking()
            .SingleAsync(node => node.Id == SeedOrganisation.Departments.InformationSystems, ct);

        moved.ParentId = OrgTreeSql.UnclassifiedRootId;
        await directory.SaveChangesAsync(ct);

        await directory.OrgNodes.Where(node => node.Id == second.Id).ExecuteDeleteAsync(ct);
    }

    private static async Task<Dictionary<Guid, OrgNode>> NodesAsync(AsyncServiceScope scope)
    {
        var directory = Directory(scope);

        directory.ChangeTracker.Clear();

        return await directory.OrgNodes.ToDictionaryAsync(
            node => node.Id,
            TestContext.Current.CancellationToken);
    }

    private static DirectoryDbContext Directory(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

    private static AsyncServiceScope SystemScope(CracraApplicationFactory factory)
    {
        var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        return scope;
    }

    private async Task<CracraApplicationFactory> SyncedAsync()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();

        var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IKeycloakDirectoryClient>();
                services.AddSingleton<IKeycloakDirectoryClient>(keycloak);
            },
        };

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        // The seeded shape, not whatever the administration tests left in the shared database.
        await OrgTreeReset.ApplyAsync(factory, TestContext.Current.CancellationToken);

        return factory;
    }
}
