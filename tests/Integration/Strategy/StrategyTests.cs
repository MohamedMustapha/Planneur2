using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Strategy.Contracts;
using Cracra.Modules.Strategy.Data;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Strategy;

/// <summary>
/// Strategy end to end (v2 §06), through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// Three claims are worth proving against real rows. A member reads the objectives of the branch they sit in —
/// without that, the spine is a poster only management can see. A member cannot write one. And a contribution
/// links the two ends: an objective shows the item's own state, and the item is discoverable from the objective,
/// which is what stops "strategy" and "the work" being two documents that agree by coincidence.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class StrategyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_head_sets_out_a_strategy()
    {
        await using var factory = await SeededAsync();

        var strategyId = await OpenAsync(factory, SeedOrganisation.Olivier);

        strategyId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_member_cannot_set_one()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/strategy",
            new { title = "My own plan" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_member_reads_the_strategy_of_the_branch_they_sit_in()
    {
        // The whole point of §06.4's read rule. A strategy nobody below the head can open is a poster.
        await using var factory = await SeededAsync();

        var strategyId = await OpenAsync(factory, SeedOrganisation.Olivier);

        (await ListAsync(factory, SeedOrganisation.Camille))
            .Select(strategy => strategy.Id)
            .ShouldContain(strategyId);
    }

    [Fact]
    public async Task An_objective_starts_where_the_baseline_says_and_moves_when_it_is_measured()
    {
        await using var factory = await SeededAsync();
        var ct = TestContext.Current.CancellationToken;

        var strategyId = await OpenAsync(factory, SeedOrganisation.Olivier);
        var objectiveId = await AddObjectiveAsync(factory, strategyId, baseline: 40m, target: 32m);

        (await RollupAsync(factory, SeedOrganisation.Olivier, strategyId))
            .Objectives.Single().Progress.ShouldBe(0m);

        factory.AsUser(SeedOrganisation.Olivier);

        (await factory.CreateClient().PatchAsJsonAsync(
                $"/api/strategy/objectives/{objectiveId}",
                new { current = 36m },
                ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RollupAsync(factory, SeedOrganisation.Olivier, strategyId))
            .Objectives.Single().Progress.ShouldBe(0.5m);
    }

    [Fact]
    public async Task A_member_cannot_record_a_reading()
    {
        await using var factory = await SeededAsync();

        var strategyId = await OpenAsync(factory, SeedOrganisation.Olivier);
        var objectiveId = await AddObjectiveAsync(factory, strategyId, baseline: 40m, target: 32m);

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PatchAsJsonAsync(
            $"/api/strategy/objectives/{objectiveId}",
            new { current = 32m },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Linking_a_problem_shows_its_own_status_on_the_objective()
    {
        // §06.1: solving this pain advances the objective. The state on the card is the problem's own word for
        // it, never a copy taken when somebody linked it.
        await using var factory = await SeededAsync();
        var ct = TestContext.Current.CancellationToken;

        var strategyId = await OpenAsync(factory, SeedOrganisation.Olivier);
        var objectiveId = await AddObjectiveAsync(factory, strategyId, baseline: 40m, target: 32m);

        factory.AsUser(SeedOrganisation.Camille);

        var filed = await factory.CreateClient().PostAsJsonAsync(
            "/api/problems",
            new
            {
                title = "Ticket closing takes too many steps",
                category = "tooling",
                impactTimeLoss = 1m,
                impactFrequency = "weekly",
            },
            ct);

        var problemId = (await filed.Content.ReadFromJsonAsync<Filed>(ct))!.Id;

        factory.AsUser(SeedOrganisation.Olivier);

        (await factory.CreateClient().PostAsJsonAsync(
                $"/api/strategy/objectives/{objectiveId}/contributions",
                new { sourceType = "problem", sourceId = problemId },
                ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var contribution = (await RollupAsync(factory, SeedOrganisation.Olivier, strategyId))
            .Objectives.Single().Contributions.Single();

        contribution.SourceId.ShouldBe(problemId);
        contribution.State.ShouldBe("new");
    }

    [Fact]
    public async Task Linking_something_the_caller_cannot_see_is_refused()
    {
        // Deliberately the same answer as "does not exist": anything else turns the link picker into a way of
        // probing for confidential items by id.
        await using var factory = await SeededAsync();

        var strategyId = await OpenAsync(factory, SeedOrganisation.Olivier);
        var objectiveId = await AddObjectiveAsync(factory, strategyId, baseline: 0m, target: 10m);

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/strategy/objectives/{objectiveId}/contributions",
            new { sourceType = "item", sourceId = Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_objective_nobody_is_working_on_shows_up_as_a_gap()
    {
        await using var factory = await SeededAsync();

        var strategyId = await OpenAsync(factory, SeedOrganisation.Olivier);
        var objectiveId = await AddObjectiveAsync(factory, strategyId, baseline: 0m, target: 10m);

        factory.AsUser(SeedOrganisation.Olivier);

        var gaps = await factory.CreateClient().GetFromJsonAsync<AlignmentGaps>(
            "/api/strategy/alignment",
            TestContext.Current.CancellationToken);

        gaps!.UnlinkedObjectives.Select(objective => objective.Id).ShouldContain(objectiveId);
    }

    [Fact]
    public async Task Another_branch_does_not_read_a_strategy_it_has_nothing_to_do_with()
    {
        // Heads read across for knowledge flow; members do not. Sofia is in Finance and heads nothing.
        await using var factory = await SeededAsync();

        var strategyId = await OpenAsync(factory, SeedOrganisation.Olivier);

        (await ListAsync(factory, SeedOrganisation.Sofia))
            .Select(strategy => strategy.Id)
            .ShouldNotContain(strategyId);
    }

    // --- Fixture --------------------------------------------------------------------------------------------------

    private static async Task<Guid> OpenAsync(CracraApplicationFactory factory, UserContext owner)
    {
        factory.AsUser(owner);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/strategy",
            new
            {
                title = "Cut ticket handling time",
                narrative = "Fewer steps, fewer tickets.",
                periodFrom = "2026-01-01",
                periodTo = "2026-12-31",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<Saved>(TestContext.Current.CancellationToken))!.Id;
    }

    private static async Task<Guid> AddObjectiveAsync(
        CracraApplicationFactory factory,
        Guid strategyId,
        decimal baseline,
        decimal target)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/strategy/{strategyId}/objectives",
            new
            {
                title = "Cut handling time 20%",
                metricKind = "number",
                baseline,
                target,
                unit = "min",
                due = "2026-12-31",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<Saved>(TestContext.Current.CancellationToken))!.Id;
    }

    private static async Task<IReadOnlyList<StrategyView>> ListAsync(
        CracraApplicationFactory factory,
        UserContext viewer)
    {
        factory.AsUser(viewer);

        return (await factory.CreateClient().GetFromJsonAsync<List<StrategyView>>(
            "/api/strategy",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<StrategyRollup> RollupAsync(
        CracraApplicationFactory factory,
        UserContext viewer,
        Guid strategyId)
    {
        factory.AsUser(viewer);

        return (await factory.CreateClient().GetFromJsonAsync<StrategyRollup>(
            $"/api/strategy/{strategyId}/rollup",
            TestContext.Current.CancellationToken))!;
    }

    private sealed record Saved(Guid Id);

    private sealed record Filed(Guid Id);

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
            var strategies = scope.ServiceProvider.GetRequiredService<StrategyDbContext>();

            await strategies.Contributions.ExecuteDeleteAsync(ct);
            await strategies.KeyResults.ExecuteDeleteAsync(ct);
            await strategies.Objectives.ExecuteDeleteAsync(ct);
            await strategies.Strategies.ExecuteDeleteAsync(ct);
        }

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        return factory;
    }
}
