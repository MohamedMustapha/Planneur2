using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// v2 §04's acceptance journey: finance opens on a number rather than on a question.
/// </summary>
/// <remarks>
/// v1's finance page opened empty and asked which project you meant, which is why nobody used it. The claim this
/// journey exists to hold is small and load-bearing: a head who clicks Finance lands on the highest branch they
/// run, already totalled, without choosing anything.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class ConsolidatedJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    [Fact]
    public async Task Finance_lands_a_head_on_the_branch_they_run()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GetByRole(AriaRole.Navigation)
            .GetByRole(AriaRole.Link, new() { Name = "Finance" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Budget consolidé", new() { Timeout = TimeoutMs });

        // The server picked the branch, and says so rather than leaving the reader wondering what they are
        // looking at.
        await Expect(page.GetByText("l’entité la plus haute que vous dirigez"))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_head_records_an_envelope_against_the_branch_they_are_looking_at()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/finance");

        await page.GetByRole(AriaRole.Button, new() { Name = "Gérer" }).First
            .ClickAsync(new() { Timeout = TimeoutMs });

        var drawer = page.GetByRole(AriaRole.Dialog);

        await drawer.GetByLabel("Montant prévu").FillAsync("125000");

        await drawer.GetByRole(AriaRole.Button, new() { Name = "Enregistrer" }).First
            .ClickAsync(new() { Timeout = TimeoutMs });

        // The envelope lands on the row it was typed against, which is the whole reason the editor is a drawer on
        // the table rather than a settings page somewhere else.
        await Expect(page.GetByText("125 000").First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_is_offered_neither_the_rail_entry_nor_the_page()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await Expect(page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new() { Name = "Finance" }))
            .ToHaveCountAsync(0, new() { Timeout = TimeoutMs });
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
