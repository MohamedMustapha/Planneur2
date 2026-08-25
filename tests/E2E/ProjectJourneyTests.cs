using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// S3's acceptance journey: create a cross-department project, staff it from two departments, and confirm the
/// team panel groups those people by department for a head who can see it.
/// </summary>
/// <remarks>
/// The whole slice exists to answer "who from which department is doing what". This drives that through the real
/// stack — real login, real RLS, real grouping computed server-side — rather than asserting on a shape the client
/// assembled for itself.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class ProjectJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    [Fact]
    public async Task A_head_sees_the_projects_screen()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/projects");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Projets", new() { Timeout = TimeoutMs });

        // The classification filters come straight from the design's activity-type accents.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "BUILD", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_sees_the_projects_screen_with_nothing_they_are_not_on()
    {
        var page = await stack.SignInAsync("mehdi.sadaoui");

        await page.GotoAsync("/projects");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Projets", new() { Timeout = TimeoutMs });

        // Mehdi is on PRJ-2026-001 and not on PRJ-2026-002, and the IS department leads both. That pair is what
        // makes this a test of the rule rather than of the seed: being in the lead department is not enough — RLS
        // wants membership or a head role — so the project he is not on has to be absent, not merely further down.
        //
        // It used to assert an empty list against somebody on no project at all. The dev seed now puts every
        // seeded person on a team, so that arrangement is no longer reachable; asserting the boundary between two
        // projects tests the same predicate and does not depend on somebody staying unassigned.
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "PRJ-2026-001" }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "PRJ-2026-002" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_member_reaches_a_project_by_link_rather_than_by_rail()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        var rail = page.GetByRole(AriaRole.Navigation);

        // The rail entry is gone with v2 §02.1: portfolio governance is not a member's day, and the catalog is
        // the v2 surface for "does this already exist". The route still resolves, because the portfolio board
        // links into a project's detail and a deep link that 404s is worse than a list nobody navigates to.
        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Projets" })).ToHaveCountAsync(0);

        await page.GotoAsync("/projects");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Projets", new() { Timeout = TimeoutMs });
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
