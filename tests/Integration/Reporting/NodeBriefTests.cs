using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Services;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Reporting.Contracts;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Reporting;

/// <summary>
/// The rollup brief (v2 §01.4), including the invariant that makes it composable.
/// </summary>
/// <remarks>
/// The claim is that a brief has one shape at every depth, so a head can hand theirs upward and it slots into
/// their parent's as a single block. That only holds if a node's subtree total is exactly its own plus its
/// children's subtree totals — asserted here at every node of a real tree rather than at the root, because an
/// off-by-one in the fold would still balance at the top.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class NodeBriefTests(PostgresFixture postgres)
{
    private static readonly DateOnly From = new(2026, 8, 17);
    private static readonly DateOnly To = new(2026, 8, 21);

    [Fact]
    public async Task A_head_reads_one_block_per_child_rather_than_every_person()
    {
        await using var factory = await SeededAsync();

        var brief = await BriefAsync(factory, SeedOrganisation.Olivier, SeedOrganisation.Departments.InformationSystems);

        brief.ShouldNotBeNull();
        brief.Node.NodeId.ShouldBe(SeedOrganisation.Departments.InformationSystems);

        // Two units under the department, one block each. The department itself has nobody attached directly.
        brief.Node.Children.Count.ShouldBe(2);
        brief.Node.Children.Select(child => child.NodeId).ShouldContain(SeedOrganisation.Units.Infrastructure);
        brief.Node.Children.Select(child => child.NodeId).ShouldContain(SeedOrganisation.Units.Development);
    }

    [Fact]
    public async Task The_sum_of_the_children_plus_its_own_is_the_subtree_at_every_node()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, 3m);
        await LogAsync(factory, SeedOrganisation.Mehdi, 2m);
        await LogAsync(factory, SeedOrganisation.Olivier, 4m);

        var brief = await BriefAsync(
            factory,
            SeedOrganisation.Nadia,
            OrgTreeSql.UnclassifiedRootId,
            depth: "all");

        brief.ShouldNotBeNull();

        AssertRollup(brief.Node);
    }

    [Fact]
    public async Task The_top_of_the_tree_counts_everything_beneath_it()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, 3m);
        await LogAsync(factory, SeedOrganisation.Sofia, 5m);

        var brief = await BriefAsync(
            factory,
            SeedOrganisation.Nadia,
            OrgTreeSql.UnclassifiedRootId,
            depth: "all");

        brief!.Node.Subtree.ActualHours.ShouldBe(8m);

        // Two different departments contributed, so this is a rollup and not one branch's total.
        var departments = brief.Node.Children;
        departments.Count.ShouldBe(2);
        departments.Sum(child => child.Subtree.ActualHours).ShouldBe(8m);
    }

    [Fact]
    public async Task Depth_one_gives_a_head_their_children_and_stops()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, 3m);

        var direct = await BriefAsync(factory, SeedOrganisation.Nadia, OrgTreeSql.UnclassifiedRootId);

        direct!.Node.Children.ShouldAllBe(child => child.Children.Count == 0);

        // The totals do not stop where the rendering does: a child block still reports its whole subtree, which is
        // what lets a head read one line per child and still see the real number.
        direct.Node.Children
            .Single(child => child.NodeId == SeedOrganisation.Departments.InformationSystems)
            .Subtree.ActualHours.ShouldBe(3m);
    }

    [Fact]
    public async Task A_brief_shows_a_viewer_only_what_they_could_have_read_row_by_row()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, 3m);
        await LogAsync(factory, SeedOrganisation.Sofia, 5m);

        // Olivier heads IS. Finance's hours are none of his business, and the brief inherits that from RLS rather
        // than filtering for itself.
        var brief = await BriefAsync(
            factory,
            SeedOrganisation.Olivier,
            OrgTreeSql.UnclassifiedRootId,
            depth: "all");

        brief!.Node.Subtree.ActualHours.ShouldBe(3m);
    }

    [Fact]
    public async Task An_unsupported_depth_is_refused_rather_than_quietly_narrowed()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Nadia);

        var response = await factory.CreateClient().GetAsync(
            $"/api/reports/brief?nodeId={OrgTreeSql.UnclassifiedRootId}&depth=everything",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_node_the_caller_cannot_read_is_the_same_answer_as_one_that_does_not_exist()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().GetAsync(
            $"/api/reports/brief?nodeId={Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static void AssertRollup(NodeBriefBlock block)
    {
        var expected = block.Children.Aggregate(block.Own, (running, child) => running + child.Subtree);

        block.Subtree.ActualHours.ShouldBe(expected.ActualHours, $"actual hours at {block.Code}");
        block.Subtree.PlannedHours.ShouldBe(expected.PlannedHours, $"planned hours at {block.Code}");
        block.Subtree.EntryCount.ShouldBe(expected.EntryCount, $"entries at {block.Code}");

        foreach (var child in block.Children)
        {
            AssertRollup(child);
        }
    }

    private static async Task<NodeBriefView?> BriefAsync(
        CracraApplicationFactory factory,
        UserContext viewer,
        Guid nodeId,
        string depth = "1")
    {
        factory.AsUser(viewer);

        return await factory.CreateClient().GetFromJsonAsync<NodeBriefView>(
            $"/api/reports/brief?nodeId={nodeId}&from={From:yyyy-MM-dd}&to={To:yyyy-MM-dd}&depth={depth}",
            TestContext.Current.CancellationToken);
    }

    private static async Task LogAsync(CracraApplicationFactory factory, UserContext person, decimal hours)
    {
        factory.AsUser(person);

        var day = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            new
            {
                activityTypeCode = "quality-of-life",
                kind = "actual",
                source = "manual",
                slotStart = day,
                slotEnd = day.AddHours((double)hours),
                hours,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
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

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

            var activities = scope.ServiceProvider
                .GetRequiredService<Cracra.Modules.Activities.Infrastructure.ActivitiesDbContext>();

            await activities.Entries.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        return factory;
    }
}
