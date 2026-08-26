using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// The S1 journey, end to end through the real stack: Keycloak issues a token, sync reconciles the directory out
/// of that same Keycloak, and the shell renders what RLS lets the signed-in person see.
/// </summary>
/// <remarks>
/// This is the only suite where the realm's protocol mappers, the sync service account and the RLS policies are
/// all exercised together. The integration tests fake Keycloak deliberately — they are about reconciliation logic
/// — which leaves exactly one thing untested until here: whether the seeded org in the realm actually becomes the
/// directory the boards read.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class DirectoryJourneyTests(AspireStackFixture stack)
{
    /// <summary>
    /// Sync runs shortly after the API starts, and the stack may still be reconciling when the browser arrives.
    /// Generous rather than flaky: this waits for a real event, it does not paper over a broken one.
    /// </summary>
    private const int DirectoryTimeoutMs = 60_000;

    [Fact]
    public async Task The_org_explorer_lists_colleagues_from_the_synced_directory()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/directory");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Annuaire", new() { Timeout = DirectoryTimeoutMs });

        // Camille is in Infrastructure; Thomas is her unit head. Both came from Keycloak by way of sync, so seeing
        // them here proves the whole chain rather than any one link of it.
        await Expect(page.GetByText("Thomas Berthier"))
            .ToBeVisibleAsync(new() { Timeout = DirectoryTimeoutMs });
    }

    [Fact]
    public async Task A_member_does_not_see_another_departments_people()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/directory");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Annuaire", new() { Timeout = DirectoryTimeoutMs });

        // Sofia is in Finance. The directory is the most open module in the system and still stops at the
        // department boundary — and it is RLS that stops it, not this page.
        await Expect(page.GetByText("Sofia Navarro")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_department_head_can_open_and_save_their_department_settings()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/settings");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Paramètres", new() { Timeout = DirectoryTimeoutMs });

        var target = page.GetByLabel("Objectif hebdomadaire (heures)");

        await Expect(target).ToBeVisibleAsync(new() { Timeout = DirectoryTimeoutMs });
        await target.FillAsync("37");

        await page.GetByRole(AriaRole.Button, new() { Name = "Enregistrer", Exact = true }).ClickAsync();

        // The confirmation only appears on a 2xx, so it stands in for "the RLS write policy accepted this head for
        // this department" — which is the actual assertion.
        await Expect(page.GetByText("Configuration enregistrée."))
            .ToBeVisibleAsync(new() { Timeout = DirectoryTimeoutMs });
    }

    [Fact]
    public async Task A_member_is_not_offered_department_settings()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        var rail = page.GetByRole(AriaRole.Navigation);

        // The org chart is a member's to read, but it is not what their day is about — so it sits under "Plus"
        // rather than in the primary group (§02.1). Settings is not offered at all.
        await page.GetByRole(AriaRole.Button, new() { Name = "Plus" }).ClickAsync();

        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Organigramme" })).ToBeVisibleAsync();
        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Paramètres" })).ToHaveCountAsync(0);
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
