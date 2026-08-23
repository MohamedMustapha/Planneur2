using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Problems.Contracts;
using Cracra.Modules.Problems.Data;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Problems;

/// <summary>
/// Problems end to end (v2 §05), through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// Two claims are worth proving against real rows. The first is that a branch which declares it solves a category
/// reads those problems from anywhere — that is how a need reaches IT instead of becoming somebody's private
/// workaround. The second is that converting leaves a link in both directions, which is what "no shadow IT" means
/// once the enthusiasm has worn off.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ProblemTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Anybody_can_report_an_irritant()
    {
        await using var factory = await SeededAsync();

        var filed = await FileAsync(factory, SeedOrganisation.Camille, "Too many manual ticket steps");

        filed.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Filing_hands_back_what_already_sounds_like_it()
    {
        // §05.3's dedup helper. Offered after filing rather than blocking it: a form that argues before accepting
        // anything is a form nobody uses twice.
        await using var factory = await SeededAsync();

        await FileAsync(factory, SeedOrganisation.Camille, "Ticket closing takes too many steps");

        var second = await FileAsync(factory, SeedOrganisation.Mehdi, "Ticket closing is slow");

        second.Suggestions.Select(card => card.Title).ShouldContain("Ticket closing takes too many steps");
    }

    [Fact]
    public async Task The_reporter_and_their_peers_see_it_and_another_branch_does_not()
    {
        await using var factory = await SeededAsync();

        var filed = await FileAsync(factory, SeedOrganisation.Camille, "Coffee machine on the wrong floor");

        (await ListAsync(factory, SeedOrganisation.Camille)).Select(card => card.Id).ShouldContain(filed.Id);
        (await ListAsync(factory, SeedOrganisation.Mehdi)).Select(card => card.Id).ShouldContain(filed.Id);

        // Sofia is in Finance, and quality-of-life is nobody's declared speciality.
        (await ListAsync(factory, SeedOrganisation.Sofia)).Select(card => card.Id).ShouldNotContain(filed.Id);
    }

    [Fact]
    public async Task A_branch_that_declares_it_solves_tooling_reads_tooling_problems_from_anywhere()
    {
        // The intake pipeline, and the reason the whole slice is not four hundred private complaint boxes.
        await using var factory = await SeededAsync();

        await DeclareSolverAsync(factory, SeedOrganisation.Departments.InformationSystems, "tooling");

        var filed = await FileAsync(
            factory,
            SeedOrganisation.Sofia,
            "Our export tool loses accents",
            category: "tooling");

        // Olivier heads IS, which is not Sofia's branch. He reads it because the branch he runs has declared it
        // picks tooling up.
        (await ListAsync(factory, SeedOrganisation.Olivier)).Select(card => card.Id).ShouldContain(filed.Id);
    }

    [Fact]
    public async Task The_same_branch_does_not_get_a_free_read_of_everything_else()
    {
        // Bounded by the profile: declaring you solve tooling is not declaring you read the whole organisation.
        await using var factory = await SeededAsync();

        await DeclareSolverAsync(factory, SeedOrganisation.Departments.InformationSystems, "tooling");

        var filed = await FileAsync(
            factory,
            SeedOrganisation.Sofia,
            "Our office is too cold",
            category: "quality-of-life");

        (await ListAsync(factory, SeedOrganisation.Olivier)).Select(card => card.Id).ShouldNotContain(filed.Id);
    }

    // --- Participation ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_colleague_proposes_a_fix_and_votes_once()
    {
        await using var factory = await SeededAsync();

        var filed = await FileAsync(factory, SeedOrganisation.Camille, "Too many manual ticket steps");
        var ct = TestContext.Current.CancellationToken;

        factory.AsUser(SeedOrganisation.Mehdi);
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync(
                $"/api/problems/{filed.Id}/proposals",
                new { description = "Script the last three steps.", effortGuess = 2m },
                ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var first = await client.PostAsJsonAsync($"/api/problems/{filed.Id}/vote", new { }, ct);
        var second = await client.PostAsJsonAsync($"/api/problems/{filed.Id}/vote", new { }, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await first.Content.ReadFromJsonAsync<Voted>(ct))!.Counted.ShouldBeTrue();
        (await second.Content.ReadFromJsonAsync<Voted>(ct))!.Counted.ShouldBeFalse();

        var detail = await DetailAsync(factory, SeedOrganisation.Camille, filed.Id);

        detail.Card.VoteCount.ShouldBe(1);
        detail.Proposals.Single().Description.ShouldBe("Script the last three steps.");
    }

    // --- Triage and conversion ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_member_cannot_triage()
    {
        await using var factory = await SeededAsync();

        var filed = await FileAsync(factory, SeedOrganisation.Camille, "Too many manual ticket steps");

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/problems/{filed.Id}/triage",
            new { decision = "accepted" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_head_accepts_and_converts_and_the_two_ends_stay_linked()
    {
        await using var factory = await SeededAsync();

        var filed = await FileAsync(factory, SeedOrganisation.Camille, "Too many manual ticket steps");
        var ct = TestContext.Current.CancellationToken;

        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync($"/api/problems/{filed.Id}/triage", new { decision = "accepted" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var converted = await client.PostAsJsonAsync(
            $"/api/problems/{filed.Id}/convert",
            new { type = "project", ownerNodeId = SeedOrganisation.Departments.InformationSystems },
            ct);

        converted.StatusCode.ShouldBe(HttpStatusCode.OK);

        var itemId = (await converted.Content.ReadFromJsonAsync<Saved>(ct))!.Id;

        var detail = await DetailAsync(factory, SeedOrganisation.Olivier, filed.Id);

        detail.Card.Status.ShouldBe("converted");
        detail.Card.ConvertedItemId.ShouldBe(itemId);

        // And the item is real, pre-filled from the pain rather than an empty stub.
        var item = await factory.CreateClient().GetFromJsonAsync<CatalogItemDetail>(
            $"/api/portfolio/items/{itemId}",
            ct);

        item!.Card.Name.ShouldBe("Too many manual ticket steps");
        item.Card.Type.ShouldBe("project");
    }

    [Fact]
    public async Task A_declined_problem_cannot_be_converted_and_leaves_no_item_behind()
    {
        await using var factory = await SeededAsync();

        var filed = await FileAsync(factory, SeedOrganisation.Camille, "Something out of scope");
        var ct = TestContext.Current.CancellationToken;

        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync(
                $"/api/problems/{filed.Id}/triage",
                new { decision = "declined", reason = "Belongs to the landlord." },
                ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var before = await CatalogCountAsync(factory);

        (await client.PostAsJsonAsync($"/api/problems/{filed.Id}/convert", new { type = "project" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The refusal is asked before anything is created, so a rejected conversion does not litter the catalog.
        (await CatalogCountAsync(factory)).ShouldBe(before);
    }

    [Fact]
    public async Task The_board_ranks_by_what_the_pain_costs()
    {
        await using var factory = await SeededAsync();

        await FileAsync(factory, SeedOrganisation.Camille, "Minor annoyance", timeLoss: 0.1m, frequency: "monthly");
        await FileAsync(factory, SeedOrganisation.Mehdi, "Daily grind", timeLoss: 1m, frequency: "daily");

        var ranked = await ListAsync(factory, SeedOrganisation.Camille);

        ranked[0].Title.ShouldBe("Daily grind");
    }

    // --- Fixture --------------------------------------------------------------------------------------------------

    private static async Task DeclareSolverAsync(CracraApplicationFactory factory, Guid departmentId, string category)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var directory = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
        var ct = TestContext.Current.CancellationToken;

        var profile = await directory.NodeProfiles
            .AsTracking()
            .FirstAsync(candidate => candidate.Code == "DELIVERY", ct);

        profile.SolvesCategories = [category];

        await directory.Departments
            .Where(department => department.Id == departmentId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(department => department.ProfileId, profile.Id), ct);

        // The tree carries the profile pointer the predicate reads, and the projection is what puts it there.
        await directory.SaveChangesAsync(ct);

        await scope.ServiceProvider.GetRequiredService<Modules.Directory.Services.IOrgTreeProjection>()
            .ProjectAsync(directory, ct);
    }

    private static async Task<int> CatalogCountAsync(CracraApplicationFactory factory)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var cards = await factory.CreateClient().GetFromJsonAsync<List<CatalogCard>>(
            "/api/portfolio/catalog",
            TestContext.Current.CancellationToken);

        return cards!.Count;
    }

    private static async Task<Filed> FileAsync(
        CracraApplicationFactory factory,
        UserContext reporter,
        string title,
        string category = "tooling",
        decimal? timeLoss = 1m,
        string frequency = "weekly")
    {
        factory.AsUser(reporter);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/problems",
            new
            {
                title,
                description = "It adds up.",
                category,
                impactTimeLoss = timeLoss,
                impactFrequency = frequency,
                affectedPeopleEstimate = 3,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<Filed>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<IReadOnlyList<ProblemCard>> ListAsync(
        CracraApplicationFactory factory,
        UserContext viewer)
    {
        factory.AsUser(viewer);

        return (await factory.CreateClient().GetFromJsonAsync<List<ProblemCard>>(
            "/api/problems",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<ProblemDetail> DetailAsync(
        CracraApplicationFactory factory,
        UserContext viewer,
        Guid problemId)
    {
        factory.AsUser(viewer);

        return (await factory.CreateClient().GetFromJsonAsync<ProblemDetail>(
            $"/api/problems/{problemId}",
            TestContext.Current.CancellationToken))!;
    }

    private sealed record Filed(Guid Id, IReadOnlyList<ProblemCard> Suggestions);

    private sealed record Saved(Guid Id);

    private sealed record Voted(bool Counted);

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

            var ct = TestContext.Current.CancellationToken;
            var problems = scope.ServiceProvider.GetRequiredService<ProblemsDbContext>();

            await problems.Comments.ExecuteDeleteAsync(ct);
            await problems.Votes.ExecuteDeleteAsync(ct);
            await problems.Proposals.ExecuteDeleteAsync(ct);
            await problems.Problems.ExecuteDeleteAsync(ct);

            // Profiles are shared vocabulary; a solves-categories left behind by one scenario would hand another
            // one a cross-branch read it never asked for.
            var directory = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

            await directory.Units.ExecuteUpdateAsync(
                setters => setters.SetProperty(unit => unit.ProfileId, (Guid?)null),
                ct);

            await directory.Departments.ExecuteUpdateAsync(
                setters => setters.SetProperty(department => department.ProfileId, (Guid?)null),
                ct);

            await directory.NodeProfiles
                .Where(profile => profile.Code == "DELIVERY")
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(profile => profile.SolvesCategories, (string[]?)null),
                    ct);
        }

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        return factory;
    }
}
