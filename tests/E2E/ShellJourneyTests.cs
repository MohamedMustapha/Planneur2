using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// The S0 acceptance journey: log in through Keycloak, land on the shell, switch language, see the timeline render.
/// </summary>
[Collection(AspireStackCollection.Name)]
public sealed class ShellJourneyTests(AspireStackFixture stack)
{
    [Fact]
    public async Task A_member_can_sign_in_and_reach_their_board()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Mon tableau");

        // The shell reads the identity from /bff/user, which reads it from the token Keycloak issued — so an
        // avatar with the right initials means the realm's mappers did their job.
        await Expect(page.Locator(".avatar")).ToHaveTextAsync("CV");
    }

    [Fact]
    public async Task The_language_switcher_retranslates_without_a_reload()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Mon tableau");

        // Camille is a member, and v2 §02 starts members in Focus mode, which folds the language switcher away
        // with the rest of the top bar's secondary controls. Leaving it is what she would do to reach them.
        await AspireStackFixture.LeaveFocusModeAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "EN", Exact = true }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("My board");

        await page.GetByRole(AriaRole.Button, new() { Name = "ES", Exact = true }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Mi tablero");
    }

    [Fact]
    public async Task The_licensed_timeline_renders()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        // Mobiscroll is a commercial dependency pinned to an old Angular target; that it still mounts is exactly
        // the kind of thing that is cheap to check now and expensive to discover in S6.
        await Expect(page.Locator("mbsc-eventcalendar")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task A_member_is_not_offered_governance_at_all()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        var rail = page.GetByRole(AriaRole.Navigation);

        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Ma semaine" })).ToBeVisibleAsync();

        // Hiding these is a tidiness measure, not the control — RLS is. But the rail should still reflect the
        // matrix, because offering someone a screen that will always be empty is its own kind of bug. Not even
        // under "Plus": these two a member may not read at all, unlike the catalog or a CR.
        await page.GetByRole(AriaRole.Button, new() { Name = "Plus" }).ClickAsync();

        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Finance" })).ToHaveCountAsync(0);
        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Rapports" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_head_is_offered_the_wider_screens()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        var rail = page.GetByRole(AriaRole.Navigation);

        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Mon périmètre" })).ToBeVisibleAsync();
        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Finance" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Each_role_lands_on_its_own_intent()
    {
        // Through the empty path rather than the sign-in landing: the fixture signs everybody in at /board, so
        // asking it what a role lands on would be asking the fixture rather than the app.
        var member = await stack.SignInAsync("camille.villeneuve");

        await member.GotoAsync("/");

        // Whatever the server says this viewer's day is about (§02.1). A member's is their own week; a head's is
        // the screen that compares what is beneath them.
        await Expect(member).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex("/board"),
            new() { Timeout = 30_000 });

        var head = await stack.SignInAsync("olivier.marchand");

        await head.GotoAsync("/");

        await Expect(head).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex("/node"),
            new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task Focus_mode_leaves_the_primary_panel_and_its_action()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        // A member starts in Focus mode by default (§02.2), so the secondary controls should already be gone.
        await Expect(page.Locator("button.topbar__focus")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Saisir", Exact = true }))
            .ToHaveCountAsync(0);

        await AspireStackFixture.LeaveFocusModeAsync(page);

        // And back: the toggle is never a trap, and leaving restores what it folded away.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Changer de thème" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_deep_link_out_of_focus_renders_in_full_and_offers_the_way_back()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await Expect(page.Locator("button.topbar__focus")).ToHaveAttributeAsync("aria-pressed", "true");

        await page.GotoAsync($"{stack.WebBaseUrl}/problems");

        // Pierced, not switched off: the chip says so and puts them back where they were (§02.2).
        var chip = page.GetByRole(AriaRole.Button, new() { Name = "Retour au mode focus" });

        await Expect(chip).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(page.Locator("button.topbar__focus")).ToHaveAttributeAsync("aria-pressed", "true");

        await chip.ClickAsync();

        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/board"));
    }

    [Fact]
    public async Task Every_landing_page_says_what_to_do_next()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        // "No empty grids, ever" applies to the guidance strip: a landing page with nothing to say has failed to
        // answer the question people arrive with (§02.5).
        var banner = page.Locator("app-guidance-banner .guidance__message");

        await Expect(banner.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(banner.First).Not.ToHaveTextAsync(string.Empty);
    }

    [Fact]
    public async Task The_week_grid_is_reachable_from_the_keyboard_alone()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Mon tableau");

        // Tabbing has to reach the board's primary action without a pointer. Bounded rather than unbounded, so a
        // focus trap fails the test instead of hanging it.
        for (var press = 0; press < 40; press++)
        {
            if (await page.EvaluateAsync<bool>(
                    "() => document.activeElement?.textContent?.includes('Ajout rapide') ?? false"))
            {
                return;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        Assert.Fail("The quick-add action was not reachable by tabbing.");
    }

    [Fact]
    public async Task The_dark_theme_toggle_repaints_the_shell()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GetByRole(AriaRole.Button, new() { Name = "Changer de thème" }).ClickAsync();

        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
