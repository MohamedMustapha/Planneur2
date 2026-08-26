using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cracra.Tests.Integration.Directory;

/// <summary>
/// Supervision over a tree four levels deep, one branch of which skips level 3 (v2 §01 Tests).
/// </summary>
/// <remarks>
/// <para>
/// The claim §01 makes is that depth, labels and semantics are data, and that one array overlap carries the whole
/// hierarchy. The only way to hold that claim to account is a tree that is not uniform: <c>Left</c> has a level-3
/// pôle with people under it, <c>Right</c> has none and attaches its people directly at level 2. If any predicate
/// secretly counted levels, the right-hand branch is where it would show.
/// </para>
/// <para>
/// Predicates are evaluated directly rather than through rows, for the reason the S2 fixture gives: calling the
/// predicate asks exactly one question, where inserting and selecting back would test a module's schema as much
/// as the rule.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class OrgTreeSupervisionTests(PostgresFixture postgres)
{
    private static readonly Guid Top = Guid.Parse("e1000000-0000-0000-0000-000000000001");
    private static readonly Guid Left = Guid.Parse("e1000000-0000-0000-0000-000000000002");
    private static readonly Guid Right = Guid.Parse("e1000000-0000-0000-0000-000000000003");
    private static readonly Guid LeftPole = Guid.Parse("e1000000-0000-0000-0000-000000000004");
    private static readonly Guid LeftOtherPole = Guid.Parse("e1000000-0000-0000-0000-000000000005");

    private static readonly Guid[] InLeftPole = [Top, Left, LeftPole];
    private static readonly Guid[] InLeftOtherPole = [Top, Left, LeftOtherPole];

    /// <summary>The skipped-level branch: its people hang off level 2, so their path has no level-3 entry.</summary>
    private static readonly Guid[] InRight = [Top, Right];

    [Fact]
    public async Task A_member_sees_their_own_node_and_no_further()
    {
        await using var probe = await ProbeAsync();

        var member = Person("member", LeftPole, InLeftPole);

        (await CanReadActivity(probe, member, LeftPole, InLeftPole)).ShouldBeTrue();
        (await CanReadActivity(probe, member, LeftOtherPole, InLeftOtherPole)).ShouldBeFalse();
        (await CanReadActivity(probe, member, Right, InRight)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_head_of_a_pole_sees_their_pole_and_not_its_sibling()
    {
        await using var probe = await ProbeAsync();

        var head = Person("pole-head", LeftPole, InLeftPole, LeftPole);

        (await CanReadActivity(probe, head, LeftPole, InLeftPole)).ShouldBeTrue();
        (await CanReadActivity(probe, head, LeftOtherPole, InLeftOtherPole)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_head_one_level_up_sees_every_pole_beneath_them()
    {
        await using var probe = await ProbeAsync();

        var head = Person("branch-head", Left, [Top, Left], Left);

        (await CanReadActivity(probe, head, LeftPole, InLeftPole)).ShouldBeTrue();
        (await CanReadActivity(probe, head, LeftOtherPole, InLeftOtherPole)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_head_of_the_branch_that_skips_a_level_sees_the_people_attached_directly_to_it()
    {
        // The case the whole design exists for. Nothing here is a special case in the schema: Right simply has no
        // level-3 child, and the same overlap that reaches through two levels on the left reaches through one here.
        await using var probe = await ProbeAsync();

        var head = Person("right-head", Right, InRight, Right);

        (await CanReadActivity(probe, head, Right, InRight)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_head_does_not_reach_across_into_a_sibling_branch()
    {
        await using var probe = await ProbeAsync();

        var left = Person("branch-head", Left, [Top, Left], Left);
        var right = Person("right-head", Right, InRight, Right);

        (await CanReadActivity(probe, left, Right, InRight)).ShouldBeFalse();
        (await CanReadActivity(probe, right, LeftPole, InLeftPole)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_head_at_the_top_sees_every_branch_whatever_its_shape()
    {
        await using var probe = await ProbeAsync();

        var top = Person("top-head", Top, [Top], Top);

        (await CanReadActivity(probe, top, LeftPole, InLeftPole)).ShouldBeTrue();
        (await CanReadActivity(probe, top, LeftOtherPole, InLeftOtherPole)).ShouldBeTrue();
        (await CanReadActivity(probe, top, Right, InRight)).ShouldBeTrue();
    }

    [Fact]
    public async Task Supervision_reaches_a_level_the_deployment_added_after_the_fact()
    {
        // Depth-independence, asserted against the predicate rather than the trigger: a fifth level is a row, and
        // the head three levels above it still reads what happens there without a line of code changing.
        await using var probe = await ProbeAsync();

        var deep = Guid.NewGuid();
        Guid[] inDeep = [Top, Left, LeftPole, deep];

        var top = Person("top-head", Top, [Top], Top);
        var sibling = Person("right-head", Right, InRight, Right);

        (await CanReadActivity(probe, top, deep, inDeep)).ShouldBeTrue();
        (await CanReadActivity(probe, sibling, deep, inDeep)).ShouldBeFalse();
    }

    private static Task<bool> CanReadActivity(
        RlsMatrixProbe probe,
        IUserContext viewer,
        Guid node,
        Guid[] ancestors) =>
        probe.EvaluateAsync(
            viewer,
            "access.can_read_activity(@p0, @p1, @p2, @p3)",
            [Guid.NewGuid(), node, ancestors, null],
            TestContext.Current.CancellationToken);

    private static UserContext Person(string name, Guid node, Guid[] path, params Guid[] heads) => new()
    {
        IsAuthenticated = true,
        UserId = Guid.NewGuid(),
        UserName = name,
        NodeId = node,
        NodePath = path,
        HeadedNodes = heads,
        Roles = heads.Length > 0 ? [ContextualRole.Member, ContextualRole.NodeHead] : [ContextualRole.Member],
    };

    private async Task<RlsMatrixProbe> ProbeAsync()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

            var directory = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
            var ct = TestContext.Current.CancellationToken;

            foreach (var (id, parent, level, code) in Shape())
            {
                if (!await directory.OrgNodes.AnyAsync(node => node.Id == id, ct))
                {
                    directory.OrgNodes.Add(new OrgNode
                    {
                        Id = id,
                        ParentId = parent,
                        LevelNo = level,
                        Code = code,
                        Name = code,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ModifiedAt = DateTimeOffset.UtcNow,
                    });

                    await directory.SaveChangesAsync(ct);
                }
            }
        }

        return new RlsMatrixProbe(
            factory.Services.GetRequiredService<IOptions<CracraDatabaseOptions>>().Value.RuntimeConnectionString);
    }

    private static IEnumerable<(Guid Id, Guid? Parent, int Level, string Code)> Shape()
    {
        yield return (Top, null, 1, "E-TOP");
        yield return (Left, Top, 2, "E-LEFT");
        yield return (Right, Top, 2, "E-RIGHT");
        yield return (LeftPole, Left, 3, "E-LEFT-A");
        yield return (LeftOtherPole, Left, 3, "E-LEFT-B");
    }
}
