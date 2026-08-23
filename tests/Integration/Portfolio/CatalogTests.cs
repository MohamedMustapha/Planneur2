using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Portfolio.Infrastructure;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Portfolio;

/// <summary>
/// The catalog end to end (v2 §03), through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// The claim §03 makes is that the portfolio becomes a complete, browsable map of everything the organisation
/// runs — including the things that were never projects — and that somebody can find out whether a thing exists
/// before asking for it to be built. Both halves need real rows and a real session to mean anything.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class CatalogTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_platform_is_a_first_class_item_rather_than_a_project_in_disguise()
    {
        await using var factory = await SeededAsync();

        var id = await CreateAsync(factory, SeedOrganisation.Olivier, "K8s cluster", "platform");

        var card = await CardAsync(factory, SeedOrganisation.Olivier, id);

        card.Type.ShouldBe("platform");
        card.State.ShouldBe("considered");

        // Mixed by default for a platform: one under active extension is genuinely both, and classification stayed
        // a separate axis precisely so the type does not decide it.
        card.Classification.ShouldBe("mixed");
    }

    [Fact]
    public async Task Creating_an_item_needs_only_a_name_and_a_type()
    {
        // §03.3. The v1 complaint was that you could not create a project; the fix is not a longer form.
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/portfolio/items",
            new { name = "Bilateral summit 2027", type = "business-initiative" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_code_is_derived_rather_than_demanded()
    {
        await using var factory = await SeededAsync();

        var id = await CreateAsync(factory, SeedOrganisation.Olivier, "Messaging", "product");

        (await CardAsync(factory, SeedOrganisation.Olivier, id)).Code.ShouldBe("MESSAGING");
    }

    [Fact]
    public async Task Two_items_with_the_same_name_get_different_codes()
    {
        await using var factory = await SeededAsync();

        var first = await CreateAsync(factory, SeedOrganisation.Olivier, "Portal", "product");
        var second = await CreateAsync(factory, SeedOrganisation.Olivier, "Portal", "product");

        var one = await CardAsync(factory, SeedOrganisation.Olivier, first);
        var two = await CardAsync(factory, SeedOrganisation.Olivier, second);

        one.Code.ShouldNotBe(two.Code);
    }

    // --- Dependencies ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dependency_reads_from_both_ends()
    {
        // Stored once, rendered as "consumes" on the project and "consumed by" on the platform. Two tables here is
        // how the two pages start disagreeing about whether the dependency exists.
        await using var factory = await SeededAsync();

        var platform = await CreateAsync(factory, SeedOrganisation.Olivier, "K8s", "platform");
        var project = await CreateAsync(factory, SeedOrganisation.Olivier, "New portal", "project");

        await DependAsync(factory, project, platform);

        var fromProject = await DetailAsync(factory, SeedOrganisation.Olivier, project);
        var fromPlatform = await DetailAsync(factory, SeedOrganisation.Olivier, platform);

        fromProject.Dependencies.Single().Direction.ShouldBe("consumes");
        fromProject.Dependencies.Single().ItemId.ShouldBe(platform);

        fromPlatform.Dependencies.Single().Direction.ShouldBe("consumed-by");
        fromPlatform.Dependencies.Single().ItemId.ShouldBe(project);
    }

    [Fact]
    public async Task A_dependency_that_would_close_a_loop_is_refused()
    {
        await using var factory = await SeededAsync();

        var first = await CreateAsync(factory, SeedOrganisation.Olivier, "Alpha", "platform");
        var second = await CreateAsync(factory, SeedOrganisation.Olivier, "Beta", "project");

        await DependAsync(factory, second, first);

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/portfolio/items/{first}/dependencies",
            new { dependsOnItemId = second },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_catalog_can_be_filtered_to_the_things_others_lean_on()
    {
        await using var factory = await SeededAsync();

        var platform = await CreateAsync(factory, SeedOrganisation.Olivier, "Shared cluster", "platform");
        var lonely = await CreateAsync(factory, SeedOrganisation.Olivier, "Nobody uses this", "product");
        var project = await CreateAsync(factory, SeedOrganisation.Olivier, "Consumer", "project");

        await DependAsync(factory, project, platform);

        var shared = await BrowseAsync(factory, SeedOrganisation.Olivier, "sharedOnly=true");

        shared.Select(card => card.Id).ShouldContain(platform);
        shared.Select(card => card.Id).ShouldNotContain(lonely);
    }

    // --- Awaiting v2 ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_live_product_can_be_marked_awaiting_its_next_version_once_the_scope_exists()
    {
        await using var factory = await SeededAsync();

        var id = await LiveItemAsync(factory);

        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        // Refused while the backlog is empty: the pill must never claim more than the list behind it.
        (await client.PostAsJsonAsync($"/api/portfolio/{id}/await-next", new { version = "v2" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await client.PostAsJsonAsync(
                $"/api/portfolio/{id}/epics",
                new { name = "Bulk import", status = "deferred", targetVersion = "v2" },
                ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PostAsJsonAsync($"/api/portfolio/{id}/await-next", new { version = "v2" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var card = await CardAsync(factory, SeedOrganisation.Olivier, id);

        card.State.ShouldBe("awaiting-vnext");
        card.AwaitingVersion.ShouldBe("v2");
        card.QueuedEpicCount.ShouldBe(1);
    }

    // --- Visibility -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_head_finds_a_similar_item_in_another_branch_before_asking_for_a_new_one()
    {
        // The catalog's reason to exist. Laurent heads Finance; the cluster belongs to IS. Without the cross-branch
        // read for heads, he would file a request for a thing that already runs two doors down.
        await using var factory = await SeededAsync();

        await CreateAsync(factory, SeedOrganisation.Olivier, "Kubernetes cluster", "platform");

        var found = await SearchAsync(factory, SeedOrganisation.Laurent, "kubernetes");

        found.Select(card => card.Name).ShouldContain("Kubernetes cluster");
    }

    [Fact]
    public async Task A_member_does_not_read_another_branchs_catalog()
    {
        await using var factory = await SeededAsync();

        await CreateAsync(factory, SeedOrganisation.Olivier, "Kubernetes cluster", "platform");

        // Sofia is a member in Finance. Discovery is for heads; a member sees their own branch.
        var found = await SearchAsync(factory, SeedOrganisation.Sofia, "kubernetes");

        found.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_item_marked_confidential_drops_out_of_the_cross_branch_view()
    {
        // The opt-out §01 §3.1 promises. Discovery is wide by default because duplicates are expensive; the
        // deployment can still say no for one item without narrowing the rule for every other.
        await using var factory = await SeededAsync();

        var id = await CreateAsync(factory, SeedOrganisation.Olivier, "Sensitive programme", "project");

        (await SearchAsync(factory, SeedOrganisation.Laurent, "Sensitive")).ShouldNotBeEmpty();

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/portfolio/items/{id}",
            new { confidential = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SearchAsync(factory, SeedOrganisation.Laurent, "Sensitive")).ShouldBeEmpty();

        // Its own branch still reads it. Confidential means "not org-wide", not "invisible".
        (await SearchAsync(factory, SeedOrganisation.Olivier, "Sensitive")).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Somebody_on_the_item_reads_it_wherever_they_sit()
    {
        await using var factory = await SeededAsync();

        var id = await CreateAsync(factory, SeedOrganisation.Olivier, "Cross-branch build", "project");

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/portfolio/items/{id}/members",
            new { personId = SeedOrganisation.Sofia.UserId, nodeId = SeedOrganisation.Units.Accounting },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Sofia is a member in another department entirely, and reads it because she is on it.
        (await CardAsync(factory, SeedOrganisation.Sofia, id)).Name.ShouldBe("Cross-branch build");
    }

    // --- Fixture --------------------------------------------------------------------------------------------------

    private static async Task<Guid> LiveItemAsync(CracraApplicationFactory factory)
    {
        var ct = TestContext.Current.CancellationToken;

        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();

        // Owned at the department node rather than at Olivier's own, because that is where a head owns a product
        // — and because the project the commitment provisions takes its department from the item's owning node.
        var id = await CreateAsync(
            factory,
            SeedOrganisation.Olivier,
            "Messaging platform",
            "product",
            SeedOrganisation.Departments.InformationSystems);

        var commit = await client.PostAsJsonAsync(
            $"/api/portfolio/{id}/commit",
            new { projectCode = $"MSG-{Guid.NewGuid():N}"[..12], decisionNotes = "Approved at the COPIL." },
            ct);

        commit.StatusCode.ShouldBe(HttpStatusCode.OK);

        var projectId = (await commit.Content.ReadFromJsonAsync<Committed>(ct))!.ProjectId;

        (await client.PostAsJsonAsync(
                $"/api/portfolio/{id}/iterations",
                new { name = "Sprint 1", length = "twoweeks", startsOn = "2026-08-31" },
                ct))
            .IsSuccessStatusCode.ShouldBeTrue();

        var member = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/members",
            new
            {
                personId = SeedOrganisation.Camille.UserId,
                departmentId = SeedOrganisation.Departments.InformationSystems,
                functionalRoleId = Guid.Parse("f0000000-0000-0000-0000-000000000001"),
            },
            ct);

        member.IsSuccessStatusCode.ShouldBeTrue(await member.Content.ReadAsStringAsync(ct));

        (await client.PostAsync($"/api/portfolio/{id}/activate", null, ct)).IsSuccessStatusCode.ShouldBeTrue();

        return id;
    }

    private static async Task<Guid> CreateAsync(
        CracraApplicationFactory factory,
        UserContext author,
        string name,
        string type,
        Guid? ownerNodeId = null)
    {
        factory.AsUser(author);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/portfolio/items",
            new { name, type, ownerNodeId },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreatedItem>(TestContext.Current.CancellationToken);

        return created!.Id;
    }

    private static async Task DependAsync(CracraApplicationFactory factory, Guid item, Guid dependsOn)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/portfolio/items/{item}/dependencies",
            new { dependsOnItemId = dependsOn, kind = "consumes" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<CatalogCard> CardAsync(
        CracraApplicationFactory factory,
        UserContext viewer,
        Guid itemId) =>
        (await DetailAsync(factory, viewer, itemId)).Card;

    private static async Task<CatalogItemDetail> DetailAsync(
        CracraApplicationFactory factory,
        UserContext viewer,
        Guid itemId)
    {
        factory.AsUser(viewer);

        return (await factory.CreateClient().GetFromJsonAsync<CatalogItemDetail>(
            $"/api/portfolio/items/{itemId}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<IReadOnlyList<CatalogCard>> BrowseAsync(
        CracraApplicationFactory factory,
        UserContext viewer,
        string query)
    {
        factory.AsUser(viewer);

        return (await factory.CreateClient().GetFromJsonAsync<List<CatalogCard>>(
            $"/api/portfolio/catalog?{query}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<IReadOnlyList<CatalogCard>> SearchAsync(
        CracraApplicationFactory factory,
        UserContext viewer,
        string term)
    {
        factory.AsUser(viewer);

        return (await factory.CreateClient().GetFromJsonAsync<List<CatalogCard>>(
            $"/api/portfolio/catalog/search?q={Uri.EscapeDataString(term)}",
            TestContext.Current.CancellationToken))!;
    }

    private sealed record CreatedItem(Guid Id);

    private sealed record Committed(Guid ItemId, Guid ProjectId);

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

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

            var context = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            var ct = TestContext.Current.CancellationToken;

            // The container is shared, so items another scenario left behind would satisfy a "nobody else can see
            // this" assertion by accident — or break a search that expects one hit.
            await context.Dependencies.ExecuteDeleteAsync(ct);
            await context.Members.ExecuteDeleteAsync(ct);
            await context.Epics.ExecuteDeleteAsync(ct);
            await context.Iterations.ExecuteDeleteAsync(ct);
            await context.Transitions.ExecuteDeleteAsync(ct);
            await context.Items.ExecuteDeleteAsync(ct);
        }

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        return factory;
    }
}
