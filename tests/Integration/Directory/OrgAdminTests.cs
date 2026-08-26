using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Directory.Services;
using Cracra.Modules.Directory.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Directory;

/// <summary>
/// Administering the org structure (v2 §08), through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// Two claims carry the slice. A head runs their own branch and everything beneath it and nothing at or above it,
/// which is one predicate rather than a rule restated per endpoint. And every act is recorded under the person
/// who performed it — an admin surface whose actions run as 'system' would leave a trail saying nobody did it.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class OrgAdminTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Everybody_reads_the_tree_because_you_cannot_pick_a_parent_you_cannot_see()
    {
        await using var factory = await SeededAsync();

        var tree = await TreeAsync(factory, SeedOrganisation.Camille);

        tree.Select(node => node.Code).ShouldContain("dsi");

        // Depth-first with the depth already computed, so no client has to recurse over a shape whose height is
        // the deployment's.
        tree[0].Depth.ShouldBe(0);
        tree.Single(node => node.Code == "dsi").Depth.ShouldBe(1);
    }

    [Fact]
    public async Task The_PMO_creates_a_branch_anywhere()
    {
        await using var factory = await SeededAsync();
        var code = Unique("data");

        var created = await CreateAsync(
            factory,
            SeedOrganisation.Nadia,
            SeedOrganisation.Departments.InformationSystems,
            code,
            "Données",
            level: 3);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await TreeAsync(factory, SeedOrganisation.Nadia)).Select(node => node.Code).ShouldContain(code);
    }

    [Fact]
    public async Task A_head_creates_a_branch_beneath_their_own()
    {
        await using var factory = await SeededAsync();

        var created = await CreateAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Departments.InformationSystems,
            Unique("data"),
            "Données",
            level: 3);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_head_cannot_reshape_a_branch_that_is_not_beneath_them()
    {
        await using var factory = await SeededAsync();

        var finance = (await TreeAsync(factory, SeedOrganisation.Olivier)).Single(node => node.Code == "daf");

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/admin/org/nodes/{finance.Id}",
            new { name = "Finance et compagnie" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_head_cannot_rename_their_own_branch_either()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Olivier);

        // §08.2: never at or above their own node. A node's ancestor list ends with the node itself, so the
        // predicate that grants the subtree excludes the row the head sits on rather than relying on the overlap.
        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/admin/org/nodes/{SeedOrganisation.Departments.InformationSystems}",
            new { name = "DSI renommée" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_member_reshapes_nothing()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/admin/org/nodes/{SeedOrganisation.Units.Infrastructure}",
            new { name = "Mon équipe" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Re_parenting_moves_the_whole_subtree_and_re_scopes_it()
    {
        await using var factory = await SeededAsync();
        var ct = TestContext.Current.CancellationToken;

        factory.AsUser(SeedOrganisation.Nadia);

        // Infrastructure moves from IS to Finance. Laurent heads Finance and could not read it a moment ago.
        var moved = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/admin/org/nodes/{SeedOrganisation.Units.Infrastructure}",
            new { reparent = true, parentId = SeedOrganisation.Departments.Finance },
            ct);

        moved.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var tree = await TreeAsync(factory, SeedOrganisation.Nadia);

        tree.Single(node => node.Id == SeedOrganisation.Units.Infrastructure)
            .ParentId.ShouldBe(SeedOrganisation.Departments.Finance);

        // And the ancestry followed, which is what every module's predicate reads. Laurent may now reshape it.
        factory.AsUser(SeedOrganisation.Laurent);

        var renamed = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/admin/org/nodes/{SeedOrganisation.Units.Infrastructure}",
            new { name = "Infra (Finance)" },
            ct);

        renamed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_branch_cannot_be_moved_under_its_own_descendant()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Nadia);

        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/admin/org/nodes/{SeedOrganisation.Departments.InformationSystems}",
            new { reparent = true, parentId = SeedOrganisation.Units.Infrastructure },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_branch_cannot_hang_off_itself()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Nadia);

        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/admin/org/nodes/{SeedOrganisation.Units.Infrastructure}",
            new { reparent = true, parentId = SeedOrganisation.Units.Infrastructure },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_branch_cannot_be_created_at_or_above_its_parents_level()
    {
        await using var factory = await SeededAsync();

        var response = await CreateAsync(
            factory,
            SeedOrganisation.Nadia,
            SeedOrganisation.Departments.InformationSystems,
            "impossible",
            "Impossible",
            level: 1);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Every_act_lands_in_the_trail_under_whoever_performed_it()
    {
        await using var factory = await SeededAsync();

        await CreateAsync(
            factory,
            SeedOrganisation.Nadia,
            SeedOrganisation.Departments.InformationSystems,
            Unique("data"),
            "Données",
            level: 3);

        var trail = await AuditAsync(factory, SeedOrganisation.Nadia);
        var entry = trail.First(row => row.Action == AuditActions.NodeCreated);

        entry.ActorPersonId.ShouldBe(SeedOrganisation.Nadia.UserId);
        entry.Detail.ShouldContain("Données");
    }

    [Fact]
    public async Task A_head_reads_the_trail_for_their_own_branches_and_no_further()
    {
        await using var factory = await SeededAsync();

        // Laurent renames a unit of his own; Olivier heads a different department entirely.
        factory.AsUser(SeedOrganisation.Laurent);

        (await factory.CreateClient().PatchAsJsonAsync(
                $"/api/admin/org/nodes/{SeedOrganisation.Units.Accounting}",
                new { name = "Comptabilité générale" },
                TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await AuditAsync(factory, SeedOrganisation.Laurent))
            .Select(entry => entry.NodeId)
            .ShouldContain(SeedOrganisation.Units.Accounting);

        (await AuditAsync(factory, SeedOrganisation.Olivier))
            .Select(entry => entry.NodeId)
            .ShouldNotContain(SeedOrganisation.Units.Accounting);
    }

    [Fact]
    public async Task A_member_reads_no_trail_at_all()
    {
        await using var factory = await SeededAsync();

        await CreateAsync(
            factory,
            SeedOrganisation.Nadia,
            SeedOrganisation.Departments.InformationSystems,
            "data",
            "Données",
            level: 3);

        (await AuditAsync(factory, SeedOrganisation.Camille)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deactivating_a_branch_keeps_it_and_says_so()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Nadia);

        (await factory.CreateClient().PatchAsJsonAsync(
                $"/api/admin/org/nodes/{SeedOrganisation.Units.Infrastructure}",
                new { active = false },
                TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var tree = await TreeAsync(factory, SeedOrganisation.Nadia);

        tree.Single(node => node.Id == SeedOrganisation.Units.Infrastructure).Active.ShouldBeFalse();
    }

    [Fact]
    public async Task The_levels_are_rows_a_global_administrator_edits()
    {
        await using var factory = await SeededAsync();
        var ct = TestContext.Current.CancellationToken;

        factory.AsUser(Administrator);

        var response = await factory.CreateClient().PutAsJsonAsync(
            "/api/admin/org/levels",
            new
            {
                levelNo = 5,
                code = "cellule",
                labelKey = "org.level.cell",
                labelPluralKey = "org.level.cells",
                headLabelKey = "org.head.cell",
                peopleAllowed = true,
                isOptional = true,
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var levels = await factory.CreateClient().GetFromJsonAsync<List<OrgLevelView>>(
            "/api/admin/org/levels",
            ct);

        levels!.Select(level => level.Code).ShouldContain("cellule");
    }

    [Fact]
    public async Task A_head_moves_somebody_between_branches_they_run()
    {
        await using var factory = await SeededAsync();

        (await MoveAsync(factory, SeedOrganisation.Olivier, SeedOrganisation.Camille.UserId,
                SeedOrganisation.Units.Development))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MembersAsync(factory, SeedOrganisation.Olivier, SeedOrganisation.Units.Development))
            .Select(member => member.PersonId)
            .ShouldContain(SeedOrganisation.Camille.UserId);
    }

    [Fact]
    public async Task A_head_moves_nobody_out_of_a_branch_they_do_not_run()
    {
        await using var factory = await SeededAsync();

        // Not forbidden — not found. Laurent is Finance's, and a DSI head cannot read his row in the first place,
        // so the move fails at the lookup. That is the right answer rather than a near miss: telling somebody
        // "you may not move that person" confirms the person, and RLS is what decided they do not exist here.
        (await MoveAsync(factory, SeedOrganisation.Olivier, SeedOrganisation.Laurent.UserId,
                SeedOrganisation.Units.Development))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_head_moves_nobody_into_a_branch_they_do_not_run()
    {
        await using var factory = await SeededAsync();

        // Camille is his and Comptabilité is not. Both ends are checked, because moving one of your own people
        // into somebody else's branch would be a way of reading that branch.
        (await MoveAsync(factory, SeedOrganisation.Olivier, SeedOrganisation.Camille.UserId,
                SeedOrganisation.Units.Accounting))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_move_is_a_correction_the_next_sync_leaves_alone()
    {
        await using var factory = await SeededAsync();

        await MoveAsync(factory, SeedOrganisation.Nadia, SeedOrganisation.Camille.UserId,
            SeedOrganisation.Units.Development);

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        // §08.1's whole point: the directory still says Infrastructure, and the administrator still wins.
        (await MembersAsync(factory, SeedOrganisation.Nadia, SeedOrganisation.Units.Development))
            .Single(member => member.PersonId == SeedOrganisation.Camille.UserId)
            .FromDirectory.ShouldBeFalse();
    }

    [Fact]
    public async Task A_move_lands_in_the_trail()
    {
        await using var factory = await SeededAsync();

        await MoveAsync(factory, SeedOrganisation.Nadia, SeedOrganisation.Camille.UserId,
            SeedOrganisation.Units.Development);

        (await AuditAsync(factory, SeedOrganisation.Nadia))
            .Where(entry => entry.Action == AuditActions.MemberMoved)
            .Select(entry => entry.TargetId)
            .ShouldContain(SeedOrganisation.Camille.UserId);
    }

    // --- Fixture --------------------------------------------------------------------------------------------------

    /// <summary>A code no other test in this shared database has already taken.</summary>
    private static string Unique(string stem) => $"{stem}-{Guid.CreateVersion7().ToString("N")[^8..]}";

    /// <summary>
    /// A global administrator, which the seeded organisation deliberately has none of.
    /// </summary>
    /// <remarks>
    /// The seed is a company, and companies do not employ somebody whose job is the org chart. The role is real
    /// all the same — the levels are its alone — so the test names one rather than widening what the PMO may do.
    /// </remarks>
    private static readonly UserContext Administrator = SeedOrganisation.Nadia with
    {
        UserName = "admin",
        Roles = [ContextualRole.Member, ContextualRole.Admin],
    };

    private static async Task<HttpResponseMessage> MoveAsync(
        CracraApplicationFactory factory,
        UserContext person,
        Guid personId,
        Guid nodeId)
    {
        factory.AsUser(person);

        return await factory.CreateClient().PatchAsJsonAsync(
            $"/api/admin/org/people/{personId}",
            new { nodeId },
            TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<OrgMemberView>> MembersAsync(
        CracraApplicationFactory factory,
        UserContext person,
        Guid nodeId)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<List<OrgMemberView>>(
            $"/api/admin/org/nodes/{nodeId}/members",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<HttpResponseMessage> CreateAsync(
        CracraApplicationFactory factory,
        UserContext person,
        Guid parentId,
        string code,
        string name,
        int level)
    {
        factory.AsUser(person);

        return await factory.CreateClient().PostAsJsonAsync(
            "/api/admin/org/nodes",
            new { parentId, levelNo = level, code, name },
            TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<OrgNodeAdminView>> TreeAsync(
        CracraApplicationFactory factory,
        UserContext person)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<List<OrgNodeAdminView>>(
            "/api/admin/org/nodes",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<IReadOnlyList<AdminAuditDto>> AuditAsync(
        CracraApplicationFactory factory,
        UserContext person)
    {
        factory.AsUser(person);

        return (await factory.CreateClient().GetFromJsonAsync<List<AdminAuditDto>>(
            "/api/admin/audit",
            TestContext.Current.CancellationToken))!;
    }

    private async Task<CracraApplicationFactory> SeededAsync()
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

        return factory;
    }
}
