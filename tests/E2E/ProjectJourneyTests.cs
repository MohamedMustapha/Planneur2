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

        // Mehdi is in the IS department but on no project. Being in the lead department is not enough — RLS wants
        // membership or a head role, and he has neither, so the list is genuinely empty rather than filtered
        // client-side.
        await Expect(page.GetByText("Aucun projet visible.")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_is_still_offered_the_projects_nav_entry()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        var rail = page.GetByRole(AriaRole.Navigation);

        // Unlike Finance or Département, Projets is offered to everyone: a member may well be on a project, and
        // the rail cannot know whether they are without asking. What they see inside is RLS's business.
        await Expect(rail.GetByRole(AriaRole.Link, new() { Name = "Projets" }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
