using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Guidance;
using Cracra.Tests.Integration.Directory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Guidance;

/// <summary>
/// The shell the server computes for a viewer, and the one action it suggests (v2 §02.1, §02.5).
/// </summary>
/// <remarks>
/// The claim being tested is that hiding is UX and RLS is security: a member is not shown Budget, and a member
/// who types the URL anyway is refused by Postgres rather than by the menu. Both halves have to be true — the
/// first alone is a UI that lies, the second alone is the cluttered app this slice exists to fix.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class GuidanceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_member_lands_on_their_own_week()
    {
        await using var factory = await SeededAsync();

        var shell = await NavigationAsync(factory, SeedOrganisation.Camille);

        shell.Position.ShouldBe(ShellPositions.Member);
        shell.LandingId.ShouldBe(ShellSections.Week);
        shell.FocusId.ShouldBe(ShellSections.Week);
    }

    [Fact]
    public async Task A_member_is_not_offered_the_budget()
    {
        await using var factory = await SeededAsync();

        var shell = await NavigationAsync(factory, SeedOrganisation.Camille);

        shell.Primary.ShouldNotContain(ShellSections.Finance);
        shell.Secondary.ShouldNotContain(ShellSections.Finance);
    }

    [Fact]
    public async Task A_member_who_types_the_url_anyway_is_refused_by_the_database()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().GetAsync(
            "/api/finance/consolidated",
            TestContext.Current.CancellationToken);

        // The nav hid it, and that hiding is cosmetic. What actually protects the numbers is the policy, and it
        // is asked again on every call — so a viewer who edits the menu in their browser gains nothing.
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_head_over_children_lands_on_the_node_and_is_offered_governance()
    {
        await using var factory = await SeededAsync();

        var shell = await NavigationAsync(factory, SeedOrganisation.Olivier);

        shell.Position.ShouldBeOneOf(ShellPositions.HeadBranch, ShellPositions.HeadTop);
        shell.LandingId.ShouldBe(ShellSections.Node);
        shell.Primary.ShouldContain(ShellSections.Reports);
    }

    [Fact]
    public async Task Every_viewer_gets_a_next_action_rather_than_a_blank_banner()
    {
        await using var factory = await SeededAsync();

        foreach (var viewer in new[] { SeedOrganisation.Camille, SeedOrganisation.Thomas, SeedOrganisation.Olivier })
        {
            var action = await NextActionAsync(factory, viewer);

            // "No empty grids, ever" applies to the guidance strip too: a landing page with nothing to say has
            // failed to answer the one question people arrive with.
            action.Key.ShouldStartWith("guidance.action.");
            action.ActionKey.ShouldBe($"{action.Key}.cta");
            action.Section.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task An_unfilled_week_is_what_a_member_is_told_to_do()
    {
        await using var factory = await SeededAsync();

        var action = await NextActionAsync(factory, SeedOrganisation.Camille);

        action.Key.ShouldBe("guidance.action.fillWeek");
        action.Section.ShouldBe(ShellSections.Week);
        action.Params.ShouldContainKey("hours");
    }

    [Fact]
    public async Task A_problem_awaiting_triage_outranks_the_week_for_whoever_triages()
    {
        await using var factory = await SeededAsync();

        await FileProblemAsync(factory, SeedOrganisation.Camille);

        // A member filed it, so a member is not told to triage it — but their head is, ahead of their own hours.
        (await NextActionAsync(factory, SeedOrganisation.Camille)).Key.ShouldBe("guidance.action.fillWeek");

        var head = await NextActionAsync(factory, SeedOrganisation.Olivier);

        head.Key.ShouldBe("guidance.action.triageProblems");
        head.Section.ShouldBe(ShellSections.Problems);
    }

    [Fact]
    public async Task A_problem_awaiting_triage_raises_an_obligation_for_whoever_owes_it()
    {
        await using var factory = await SeededAsync();

        await FileProblemAsync(factory, SeedOrganisation.Camille);

        var head = await ObligationsAsync(factory, SeedOrganisation.Olivier);
        var member = await ObligationsAsync(factory, SeedOrganisation.Camille);

        // An obligation is something *you* owe. Somebody else's triage queue is not a chip on your banner.
        head.ShouldContain(obligation => obligation.Id == "awaiting-triage");
        member.ShouldNotContain(obligation => obligation.Id == "awaiting-triage");
    }

    [Fact]
    public async Task A_viewer_who_owes_nothing_gets_no_chips()
    {
        await using var factory = await SeededAsync();

        (await ObligationsAsync(factory, SeedOrganisation.Camille)).ShouldBeEmpty();
    }

    private static async Task<ShellNavigation> NavigationAsync(
        CracraApplicationFactory factory,
        UserContext viewer)
    {
        factory.AsUser(viewer);

        return (await factory.CreateClient().GetFromJsonAsync<ShellNavigation>(
            "/api/guidance/navigation",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<NextAction> NextActionAsync(CracraApplicationFactory factory, UserContext viewer)
    {
        factory.AsUser(viewer);

        return (await factory.CreateClient().GetFromJsonAsync<NextAction>(
            "/api/guidance/next-action",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<Obligation>> ObligationsAsync(
        CracraApplicationFactory factory,
        UserContext viewer)
    {
        factory.AsUser(viewer);

        return (await factory.CreateClient().GetFromJsonAsync<List<Obligation>>(
            "/api/guidance/obligations",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task FileProblemAsync(CracraApplicationFactory factory, UserContext reporter)
    {
        factory.AsUser(reporter);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/problems",
            new { title = "Too many manual ticket steps", category = "work-process" },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
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
