using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Cracra.Tests.Integration.Directory;

[Collection(DatabaseCollection.Name)]
public sealed class OrgNodeTreeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_deployment_arrives_with_its_levels_configured()
    {
        await using var factory = await FreshAsync();
        await using var scope = SystemScope(factory);

        var levels = await Directory(scope).OrgLevels
            .OrderBy(level => level.LevelNo)
            .ToListAsync(TestContext.Current.CancellationToken);

        levels.Count.ShouldBe(4);
        levels.Select(level => level.LevelNo).ShouldBe([1, 2, 3, 4]);
        levels.Single(level => level.LevelNo == 1).IsOptional.ShouldBeFalse();
        levels.Single(level => level.LevelNo == 3).IsOptional.ShouldBeTrue();
    }

    [Fact]
    public async Task A_root_is_its_own_ancestry()
    {
        await using var factory = await FreshAsync();
        await using var scope = SystemScope(factory);

        var root = await AddAsync(scope, null, 1, "ROOT");

        root.AncestorIds.ShouldBe([root.Id]);
    }

    [Fact]
    public async Task A_child_carries_the_path_from_the_root_down_to_itself()
    {
        await using var factory = await FreshAsync();
        await using var scope = SystemScope(factory);

        var root = await AddAsync(scope, null, 1, "ROOT");
        var branch = await AddAsync(scope, root.Id, 2, "BRANCH");
        var leaf = await AddAsync(scope, branch.Id, 3, "LEAF");

        branch.AncestorIds.ShouldBe([root.Id, branch.Id]);
        leaf.AncestorIds.ShouldBe([root.Id, branch.Id, leaf.Id]);
    }

    [Fact]
    public async Task A_branch_may_skip_a_level_its_deployment_made_optional()
    {
        await using var factory = await FreshAsync();
        await using var scope = SystemScope(factory);

        var root = await AddAsync(scope, null, 1, "ROOT");
        var branch = await AddAsync(scope, root.Id, 2, "FLAT");
        var deep = await AddAsync(scope, branch.Id, 4, "DEEP");

        deep.AncestorIds.ShouldBe([root.Id, branch.Id, deep.Id]);
    }

    [Fact]
    public async Task Re_parenting_recomputes_the_whole_subtree_beneath_the_node_that_moved()
    {
        await using var factory = await FreshAsync();
        await using var scope = SystemScope(factory);

        var first = await AddAsync(scope, null, 1, "FIRST");
        var second = await AddAsync(scope, null, 1, "SECOND");
        var branch = await AddAsync(scope, first.Id, 2, "BRANCH");
        var leaf = await AddAsync(scope, branch.Id, 3, "LEAF");
        var deep = await AddAsync(scope, leaf.Id, 4, "DEEP");

        var directory = Directory(scope);
        branch.ParentId = second.Id;
        await directory.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reloaded = await ReloadAsync(scope, branch.Id, leaf.Id, deep.Id);

        reloaded[branch.Id].ShouldBe([second.Id, branch.Id]);
        reloaded[leaf.Id].ShouldBe([second.Id, branch.Id, leaf.Id]);
        reloaded[deep.Id].ShouldBe([second.Id, branch.Id, leaf.Id, deep.Id]);
    }

    [Fact]
    public async Task A_node_cannot_be_moved_underneath_its_own_descendant()
    {
        await using var factory = await FreshAsync();
        await using var scope = SystemScope(factory);

        var root = await AddAsync(scope, null, 1, "ROOT");
        var branch = await AddAsync(scope, root.Id, 2, "BRANCH");

        var directory = Directory(scope);
        root.ParentId = branch.Id;

        var failure = await Should.ThrowAsync<DbUpdateException>(
            async () => await directory.SaveChangesAsync(TestContext.Current.CancellationToken));

        failure.InnerException.ShouldBeOfType<PostgresException>();
    }

    [Fact]
    public async Task A_child_must_sit_strictly_below_its_parent()
    {
        await using var factory = await FreshAsync();
        await using var scope = SystemScope(factory);

        var root = await AddAsync(scope, null, 1, "ROOT");
        var branch = await AddAsync(scope, root.Id, 2, "BRANCH");

        await Should.ThrowAsync<DbUpdateException>(
            async () => await AddAsync(scope, branch.Id, 2, "SIBLING-LEVEL"));

        branch.LevelNo.ShouldBe(2);
    }

    [Fact]
    public async Task A_fifth_level_needs_a_row_rather_than_a_migration()
    {
        await using var factory = await FreshAsync();
        await using var scope = SystemScope(factory);

        var directory = Directory(scope);
        var ct = TestContext.Current.CancellationToken;

        directory.OrgLevels.Add(new OrgLevel
        {
            LevelNo = 5,
            Code = "L5",
            LabelKey = "directory.level.l5",
            LabelPluralKey = "directory.level.l5.plural",
            HeadLabelKey = "directory.level.l5.head",
            IsOptional = true,
        });

        await directory.SaveChangesAsync(ct);

        var root = await AddAsync(scope, null, 1, "ROOT");
        var l2 = await AddAsync(scope, root.Id, 2, "L2");
        var l3 = await AddAsync(scope, l2.Id, 3, "L3");
        var l4 = await AddAsync(scope, l3.Id, 4, "L4");
        var l5 = await AddAsync(scope, l4.Id, 5, "L5");

        l5.AncestorIds.ShouldBe([root.Id, l2.Id, l3.Id, l4.Id, l5.Id]);

        (await directory.OrgLevels.MaxAsync(level => level.LevelNo, ct)).ShouldBe(5);
    }

    private static async Task<OrgNode> AddAsync(
        AsyncServiceScope scope,
        Guid? parentId,
        int levelNo,
        string code)
    {
        var directory = Directory(scope);
        var now = DateTimeOffset.UtcNow;

        var node = new OrgNode
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            LevelNo = levelNo,
            Code = code,
            Name = code,
            CreatedAt = now,
            ModifiedAt = now,
        };

        directory.OrgNodes.Add(node);
        await directory.SaveChangesAsync(TestContext.Current.CancellationToken);

        return node;
    }

    private static async Task<Dictionary<Guid, Guid[]>> ReloadAsync(AsyncServiceScope scope, params Guid[] ids)
    {
        var directory = Directory(scope);

        directory.ChangeTracker.Clear();

        return await directory.OrgNodes
            .Where(node => ids.Contains(node.Id))
            .ToDictionaryAsync(node => node.Id, node => node.AncestorIds, TestContext.Current.CancellationToken);
    }

    private static DirectoryDbContext Directory(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

    private static AsyncServiceScope SystemScope(CracraApplicationFactory factory)
    {
        var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        return scope;
    }

    private async Task<CracraApplicationFactory> FreshAsync()
    {
        var factory = new CracraApplicationFactory(postgres.AdminConnectionString);

        await using var scope = SystemScope(factory);

        await Directory(scope).OrgNodes.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await Directory(scope).OrgLevels
            .Where(level => level.LevelNo > 4)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        return factory;
    }
}
