using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// v2 §03's acceptance journey: the catalog is what the portfolio opens on, and a search finds work somebody
/// else's branch owns.
/// </summary>
/// <remarks>
/// The RLS is integration-tested and the identity card's rules are unit-tested. What only a browser can show is
/// that the question people actually arrive with — "does this already exist somewhere" — is answered by the
/// screen the rail lands on, after a real login, across a branch boundary.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class CatalogJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    [Fact]
    public async Task The_portfolio_opens_on_the_catalog()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GetByRole(AriaRole.Navigation)
            .GetByRole(AriaRole.Link, new() { Name = "Portefeuille" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Catalogue", new() { Timeout = TimeoutMs });

        // The facets are the browse, and they are on the screen rather than behind a menu: §03.2's claim is that
        // you narrow by what a thing *is*, not by where it sits in a process.
        await Expect(page.GetByLabel("Type")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
        await Expect(page.GetByLabel("Cycle de vie")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_head_registers_an_item_and_a_member_of_another_branch_finds_it()
    {
        var owner = await stack.SignInAsync("olivier.marchand");

        await owner.GotoAsync("/portfolio");

        await owner.GetByRole(AriaRole.Button, new() { Name = "Nouveau" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var name = $"Bus applicatif {Guid.CreateVersion7().ToString("N")[^12..]}";
        var dialog = owner.GetByRole(AriaRole.Dialog, new() { Name = "Nouveau" });

        // Two steps, because §03.6 asks what a thing *is* before what it is called — which is also what makes the
        // duplicate hint on the second step worth reading.
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Suivant" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // By role, not by label. Both steps render a <label class="field"><span>…</span> wrapping their control,
        // and Angular patches that shape in place rather than replacing it — so for one frame after "Suivant"
        // the label already reads "Nom" while the control beneath it is still step one's <select>. GetByLabel
        // resolved to that select and fill failed with "Element is not an <input>", intermittently and only on a
        // slow machine. Asking for the textbox makes the wait part of the locator: a combobox never satisfies it,
        // so Playwright retries until the step has actually swapped.
        await dialog.GetByRole(AriaRole.Textbox, new() { Name = "Nom" })
            .FillAsync(name, new() { Timeout = TimeoutMs });

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Créer" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(owner.GetByText(name).First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // Sofia is in Finance and has nothing to do with this. §03.1 is explicit that a non-confidential item is
        // discoverable across the organisation — that is the whole point of a catalog rather than a board.
        var stranger = await stack.SignInAsync("sofia.navarro");

        await stranger.GotoAsync("/portfolio");

        await Expect(stranger.GetByText(name).First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task The_flux_board_is_still_one_route_away()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/portfolio/flux");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Portefeuille", new() { Timeout = TimeoutMs });
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
