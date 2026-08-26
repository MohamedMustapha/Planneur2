using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// v2 §05's acceptance journey: somebody says what wastes their week, a colleague agrees, a head accepts it and
/// turns it into work.
/// </summary>
/// <remarks>
/// The whole slice rests on the intake being usable by a person with no hat and no training, so the assertions
/// here are deliberately about what a plain member can reach: the rail entry, the form, and the vote. If any of
/// those needed a role the feature would be a suggestion box nobody uses.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class ProblemJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    [Fact]
    public async Task Anybody_reaches_the_intake_from_the_rail()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GetByRole(AriaRole.Navigation)
            .GetByRole(AriaRole.Link, new() { Name = "Problèmes" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Problèmes", new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Signaler un problème" }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_reports_an_irritant_and_a_colleague_says_me_too()
    {
        var reporter = await stack.SignInAsync("camille.villeneuve");

        var title = await ReportAsync(reporter, $"Trop d'étapes pour clore un ticket {Suffix()}");

        // A peer in the same unit sees it and can add their weight without any role at all. §05.2's ranking is
        // impact times agreement, and the agreement half has to cost one click.
        var peer = await stack.SignInAsync("mehdi.sadaoui");

        await peer.GotoAsync("/problems");

        var card = peer.GetByRole(AriaRole.Listitem).Filter(new() { HasText = title });

        await Expect(card).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await card.GetByRole(AriaRole.Button, new() { Name = "Moi aussi" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(card.GetByText("Vous avez voté")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_is_not_offered_the_triage_controls()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await ReportAsync(page, $"Machine à café au mauvais étage {Suffix()}");

        // Tidiness rather than security — the endpoint refuses a member whatever the screen shows.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Accepter" }))
            .ToHaveCountAsync(0, new() { Timeout = TimeoutMs });
    }

    private static async Task<string> ReportAsync(IPage page, string title)
    {
        await page.GotoAsync("/problems");

        await page.GetByRole(AriaRole.Button, new() { Name = "Signaler un problème" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await page.GetByLabel("Que se passe-t-il ?").FillAsync(title);
        await page.GetByLabel("Détails").FillAsync("Cela s'accumule toutes les semaines.");

        await page.GetByRole(AriaRole.Button, new() { Name = "Envoyer" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByText(title).First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        return title;
    }

    private static string Suffix() => Guid.CreateVersion7().ToString("N")[^8..];

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
