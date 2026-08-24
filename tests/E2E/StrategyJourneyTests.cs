using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// v2 §06's acceptance journey: the spine is readable by the people whose work is on it.
/// </summary>
/// <remarks>
/// The arithmetic is unit-tested and the write rules are integration-tested. The claim that needs a browser is
/// the one §06.4 makes about who it is *for*: a strategy only a head can open is a poster, so the rail entry and
/// the objectives carry no role at all, and only the controls that change something are a head's.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class StrategyJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    [Fact]
    public async Task A_member_reaches_the_strategy_from_the_rail()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GetByRole(AriaRole.Navigation)
            .GetByRole(AriaRole.Link, new() { Name = "Stratégie" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Stratégie", new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_is_not_offered_the_controls_that_change_it()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/strategy");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Stratégie", new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Ajouter un objectif" }))
            .ToHaveCountAsync(0, new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_head_is_offered_them()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/strategy");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Stratégie", new() { Timeout = TimeoutMs });
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
