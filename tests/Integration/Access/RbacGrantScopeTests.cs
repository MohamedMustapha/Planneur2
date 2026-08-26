using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Directory.Services;
using Cracra.Modules.Directory.Sync;
using Cracra.Tests.Integration.Directory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Access;

/// <summary>
/// Who may hand out a role, and over what (v2 §08.1, §08.2, §08.4).
/// </summary>
/// <remarks>
/// <para>
/// The whole slice rests on one sentence — "a head can never grant at or above their own node" — and it is the
/// sentence with teeth, because a granted <c>node-head</c> becomes an entry in <c>app.headed_nodes</c> on the
/// caller's next request. A head who could write that row for a node above them would not be widening their admin
/// rights; they would be reading the organisation. So the negative cases here matter more than the positive one,
/// and every one of them goes through HTTP so the answer comes from the policy rather than from a service that
/// could later be called another way.
/// </para>
/// <para>
/// Two heads carry the assertions. Olivier heads the IS department and so has branches beneath him; Thomas heads
/// one unit inside it and has none, which is the case that separates "is a head" from "heads something this is
/// inside of".
/// </para>
/// <para>
/// Every test hands its work to <see cref="RunAsync"/>, which revokes whatever the body granted before it returns.
/// This is the one test class in the assembly whose writes change what the seeded people <em>are</em> rather than
/// what they own: a grant left behind turns Camille into a PMO for every class that runs afterwards, and the
/// failures surface somewhere else entirely — in Kudos, in Scheduling — as tests that assert an ordinary member is
/// refused and are quietly told otherwise. The collection's "tests do not depend on each other's rows" holds only
/// while this one cleans up after itself.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class RbacGrantScopeTests(PostgresFixture postgres)
{
    [Fact]
    public Task The_PMO_grants_at_any_scope_including_globally() => RunAsync(async factory =>
    {
        var response = await GrantAsync(
            factory,
            SeedOrganisation.Nadia,
            SeedOrganisation.Camille.UserId,
            ContextualRole.Pmo,
            "Global",
            scopeId: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    });

    [Fact]
    public Task A_head_grants_on_a_branch_beneath_their_own() => RunAsync(async factory =>
    {
        var response = await GrantAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Camille.UserId,
            ContextualRole.NodeHead,
            "Unit",
            SeedOrganisation.Units.Infrastructure);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    });

    [Fact]
    public Task A_head_does_not_grant_on_their_own_node() => RunAsync(async factory =>
    {
        // The escalation that looks like housekeeping: Olivier already heads IS, so re-granting it to a member of
        // his own department reads like tidying. It is not — it is handing his whole branch to somebody else, and
        // §08.1 puts the granter's own seat outside what the granter may hand out.
        var response = await GrantAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Camille.UserId,
            ContextualRole.NodeHead,
            "Department",
            SeedOrganisation.Departments.InformationSystems);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    });

    [Fact]
    public Task A_head_does_not_grant_above_their_own_node() => RunAsync(async factory =>
    {
        var response = await GrantAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Camille.UserId,
            ContextualRole.NodeHead,
            "Department",
            OrgTreeSql.UnclassifiedRootId);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    });

    [Fact]
    public Task A_head_does_not_grant_into_a_branch_that_is_not_theirs() => RunAsync(async factory =>
    {
        var response = await GrantAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Sofia.UserId,
            ContextualRole.NodeHead,
            "Unit",
            SeedOrganisation.Units.Accounting);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    });

    [Fact]
    public Task A_head_does_not_grant_a_role_to_somebody_outside_their_branch() => RunAsync(async factory =>
    {
        // The scope is squarely inside Olivier's branch; the person is not. Granting it would seat somebody from
        // Finance on an IS unit, which is a way of handing IS's rows across the boundary.
        var response = await GrantAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Sofia.UserId,
            ContextualRole.NodeHead,
            "Unit",
            SeedOrganisation.Units.Infrastructure);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    });

    [Fact]
    public Task A_head_of_a_leaf_has_nothing_beneath_them_to_grant_on() => RunAsync(async factory =>
    {
        // Thomas heads Infrastructure and nothing sits under it, so the only node he could name is his own — which
        // is the case that separates "holds a head role" from "heads something this is inside of".
        var response = await GrantAsync(
            factory,
            SeedOrganisation.Thomas,
            SeedOrganisation.Camille.UserId,
            ContextualRole.NodeHead,
            "Unit",
            SeedOrganisation.Units.Infrastructure);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    });

    [Fact]
    public Task A_head_does_not_grant_themselves_the_PMO() => RunAsync(async factory =>
    {
        // The plainest form of the escalation, and why the role is checked as well as the scope: a role that stops
        // at no branch cannot be handed out by somebody whose authority does.
        var response = await GrantAsync(
            factory,
            SeedOrganisation.Thomas,
            SeedOrganisation.Thomas.UserId,
            ContextualRole.Pmo,
            "Global",
            scopeId: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    });

    [Fact]
    public Task A_head_does_not_grant_the_global_administrator_either() => RunAsync(async factory =>
    {
        var response = await GrantAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Camille.UserId,
            ContextualRole.Admin,
            "Unit",
            SeedOrganisation.Units.Infrastructure);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    });

    [Fact]
    public Task A_head_does_not_grant_a_branch_scoped_role_globally() => RunAsync(async factory =>
    {
        var response = await GrantAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Camille.UserId,
            ContextualRole.NodeHead,
            "Global",
            scopeId: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    });

    [Fact]
    public Task A_member_grants_nothing_at_all() => RunAsync(async factory =>
    {
        var response = await GrantAsync(
            factory,
            SeedOrganisation.Camille,
            SeedOrganisation.Mehdi.UserId,
            ContextualRole.NodeHead,
            "Unit",
            SeedOrganisation.Units.Infrastructure);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    });

    [Fact]
    public Task A_refused_grant_leaves_nothing_behind() => RunAsync(async factory =>
    {
        await GrantAsync(
            factory,
            SeedOrganisation.Thomas,
            SeedOrganisation.Thomas.UserId,
            ContextualRole.Pmo,
            "Global",
            scopeId: null);

        // Read as the PMO, who is refused nothing: a row the granter cannot see but that still counts would be the
        // worst of both answers.
        var all = await ListAsync(factory, SeedOrganisation.Nadia, SeedOrganisation.Thomas.UserId);

        all.ShouldBeEmpty();
    });

    [Fact]
    public Task A_head_does_not_revoke_a_grant_made_above_them() => RunAsync(async factory =>
    {
        var created = await GrantedAsync(
            factory,
            SeedOrganisation.Nadia,
            SeedOrganisation.Camille.UserId,
            ContextualRole.Pmo,
            "Global",
            scopeId: null);

        factory.AsUser(SeedOrganisation.Olivier);

        var refused = await factory.CreateClient().DeleteAsync(
            $"/api/access/overrides/{created.Id}",
            TestContext.Current.CancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // And it is still in force, which is the point: a refusal reported as a success is worse than a refusal,
        // because the person who asked for it stops watching.
        var still = await ListAsync(factory, SeedOrganisation.Nadia, SeedOrganisation.Camille.UserId);

        still.Single(item => item.Id == created.Id).IsActive.ShouldBeTrue();
    });

    [Fact]
    public Task A_head_revokes_what_they_could_have_granted() => RunAsync(async factory =>
    {
        var created = await GrantedAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Camille.UserId,
            ContextualRole.NodeHead,
            "Unit",
            SeedOrganisation.Units.Infrastructure);

        factory.AsUser(SeedOrganisation.Olivier);

        var revoked = await factory.CreateClient().DeleteAsync(
            $"/api/access/overrides/{created.Id}",
            TestContext.Current.CancellationToken);

        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await ListAsync(factory, SeedOrganisation.Nadia, SeedOrganisation.Camille.UserId);

        after.Single(item => item.Id == created.Id).IsActive.ShouldBeFalse();
    });

    [Fact]
    public Task A_head_reads_the_overrides_in_their_own_branch_and_not_another_heads() => RunAsync(async factory =>
    {
        await GrantedAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Camille.UserId,
            ContextualRole.NodeHead,
            "Unit",
            SeedOrganisation.Units.Infrastructure);

        await GrantedAsync(
            factory,
            SeedOrganisation.Laurent,
            SeedOrganisation.Sofia.UserId,
            ContextualRole.NodeHead,
            "Unit",
            SeedOrganisation.Units.Accounting);

        var seen = await ListAsync(factory, SeedOrganisation.Olivier, personId: null);

        seen.Select(item => item.PersonId).ShouldContain(SeedOrganisation.Camille.UserId);
        seen.Select(item => item.PersonId).ShouldNotContain(SeedOrganisation.Sofia.UserId);
    });

    /// <summary>
    /// Runs one test against a freshly synced stack, then puts the seeded people back as they were found.
    /// </summary>
    /// <remarks>
    /// The reset runs whether the body passed or threw. A failing test that also left a global PMO grant behind
    /// would report one broken assertion here and a scattering of unrelated ones in whatever ran next, which is
    /// the most expensive kind of failure to read.
    /// </remarks>
    private async Task RunAsync(Func<CracraApplicationFactory, Task> body)
    {
        await using var factory = await SeededAsync();

        try
        {
            await body(factory);
        }
        finally
        {
            await ResetAsync(factory);
        }
    }

    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        // As the PMO, who both reads every override and may revoke any of them. Revoked rather than deleted, for
        // the same reason the service revokes: the row and its trail are how the grant stays answerable.
        var outstanding = await ListAsync(factory, SeedOrganisation.Nadia, personId: null);

        factory.AsUser(SeedOrganisation.Nadia);

        var client = factory.CreateClient();

        foreach (var item in outstanding.Where(candidate => candidate.IsActive))
        {
            await client.DeleteAsync($"/api/access/overrides/{item.Id}", TestContext.Current.CancellationToken);
        }
    }

    private static async Task<RbacOverrideDto> GrantedAsync(
        CracraApplicationFactory factory,
        UserContext actor,
        Guid personId,
        string role,
        string scopeType,
        Guid? scopeId)
    {
        var response = await GrantAsync(factory, actor, personId, role, scopeType, scopeId);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<RbacOverrideDto>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<HttpResponseMessage> GrantAsync(
        CracraApplicationFactory factory,
        UserContext actor,
        Guid personId,
        string role,
        string scopeType,
        Guid? scopeId)
    {
        factory.AsUser(actor);

        return await factory.CreateClient().PostAsJsonAsync(
            "/api/access/overrides",
            new
            {
                personId,
                role,
                scopeType,
                scopeId,
                isGrant = true,
                reason = "Covering an absence.",
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<RbacOverrideDto>> ListAsync(
        CracraApplicationFactory factory,
        UserContext actor,
        Guid? personId)
    {
        factory.AsUser(actor);

        var route = personId is { } id ? $"/api/access/overrides?personId={id}" : "/api/access/overrides";

        return (await factory.CreateClient().GetFromJsonAsync<List<RbacOverrideDto>>(
            route,
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
