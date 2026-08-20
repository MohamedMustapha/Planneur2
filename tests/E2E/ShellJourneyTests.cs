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
    public async Task A_member_is_not_offered_the_department_or_finance_screens()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        var rail = page.GetByRole(AriaRole.Navigation);

        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Mon tableau" })).ToBeVisibleAsync();

        // Hiding these is a tidiness measure, not the control — RLS is. But the rail should still reflect the
        // matrix, because offering someone a screen that will always be empty is its own kind of bug.
        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Finance" })).ToHaveCountAsync(0);
        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Département" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_department_head_is_offered_the_wider_screens()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        var rail = page.GetByRole(AriaRole.Navigation);

        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Département" })).ToBeVisibleAsync();
        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Finance" })).ToBeVisibleAsync();
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
