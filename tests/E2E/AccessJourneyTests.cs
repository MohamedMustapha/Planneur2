using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// S2's journey: a member and a department head see different people, and an override changes what someone sees.
/// </summary>
/// <remarks>
/// The whole slice exists so that authorization is decided once, in the database, from roles resolved server-side.
/// This is where that claim is tested against a real login rather than a swapped-in context — the realm's group
/// mappers, sync's materialization, the resolver's merge and the RLS policies all have to agree for these to pass.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class AccessJourneyTests(AspireStackFixture stack)
{
    private const int DirectoryTimeoutMs = 60_000;

    [Fact]
    public async Task A_department_head_sees_both_units_of_their_department()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/unit");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Annuaire", new() { Timeout = DirectoryTimeoutMs });

        // Olivier heads the IS department. Camille is in Infrastructure, Julie in Études — two different units,
        // both his.
        await Expect(page.GetByText("Camille Villeneuve")).ToBeVisibleAsync(new() { Timeout = DirectoryTimeoutMs });
        await Expect(page.GetByText("Julie Ondracek")).ToBeVisibleAsync(new() { Timeout = DirectoryTimeoutMs });
    }

    [Fact]
    public async Task A_member_does_not_reach_the_roles_admin()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/settings/access");

        // The route is reachable by URL — guards are UX, not security. What stops her is the endpoint policy and
        // RLS behind it, so the screen renders with nothing in it rather than with somebody else's roles.
        await Expect(page.GetByText("Sofia Navarro")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_head_can_open_the_roles_admin_and_read_effective_roles()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/settings/access");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Rôles", new() { Timeout = DirectoryTimeoutMs });

        await page.GetByRole(AriaRole.Button, new() { Name = "Camille Villeneuve" })
            .ClickAsync(new() { Timeout = DirectoryTimeoutMs });

        // "member", sourced from the directory rather than from an override — the distinction the screen exists to
        // make visible.
        await Expect(page.GetByText("Annuaire").First).ToBeVisibleAsync(new() { Timeout = DirectoryTimeoutMs });
    }

    [Fact]
    public async Task The_PMO_sees_every_department_in_the_switcher()
    {
        var page = await stack.SignInAsync("nadia.kessler");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Mon tableau", new() { Timeout = DirectoryTimeoutMs });

        // The switcher is populated from /api/directory/departments, which RLS filtered. Every directorate in the
        // realm means the PMO's global role survived the whole chain: Keycloak group, sync, resolver, GUC, policy.
        // The count follows the realm — it was two before Communication was added beside DSI and DAF.
        await page.GetByRole(AriaRole.Button, new() { Name = "Direction" }).First.ClickAsync();

        await Expect(page.GetByRole(AriaRole.Menuitemradio)).ToHaveCountAsync(3, new() { Timeout = DirectoryTimeoutMs });
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
