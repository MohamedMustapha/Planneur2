using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// v2 §08's acceptance journey: an administrator reshapes the tree, and the trail says who did it.
/// </summary>
/// <remarks>
/// The scope rules are proven against real rows in the integration suite. What a browser adds is that the screen
/// which reshapes the organisation is reachable, that it renders a tree of unknown depth as one indented list,
/// and that a head who is refused is told so rather than left looking at a control that quietly did nothing.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class OrgAdminJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    [Fact]
    public async Task The_PMO_reaches_the_structure_screen_and_reads_the_tree()
    {
        var page = await stack.SignInAsync("nadia.kessler");

        await page.GotoAsync("/settings/org");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Structure", new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Arbre", Level = 2 }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // Whatever the deployment's depth, the tree arrives as one flat list the server has already walked.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Exploitation & Production" }).First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task The_PMO_creates_a_branch_and_the_trail_records_it()
    {
        var page = await stack.SignInAsync("nadia.kessler");

        await page.GotoAsync("/settings/org");

        await page.GetByRole(AriaRole.Button, new() { Name = "Exploitation & Production" }).First
            .ClickAsync(new() { Timeout = TimeoutMs });

        var drawer = page.GetByRole(AriaRole.Dialog);
        var code = $"cell{Guid.CreateVersion7().ToString("N")[^6..]}";

        await drawer.GetByPlaceholder("Code").FillAsync(code);

        await drawer.GetByRole(AriaRole.Button, new() { Name = "Créer" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByText(code).First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // §08.2: the trail is the point. An admin surface whose acts leave no record is a surface nobody can
        // answer questions about six months later.
        await Expect(page.GetByText("Branche créée").First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_head_is_refused_on_a_branch_that_is_not_beneath_them()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/settings/org");

        // Comptabilité is Finance's. Olivier can see it — he can see the whole tree, and must, because you cannot
        // pick a parent you cannot see — and the server refuses the moment he tries to rename it.
        await page.GetByRole(AriaRole.Button, new() { Name = "Comptabilité" }).First
            .ClickAsync(new() { Timeout = TimeoutMs });

        var drawer = page.GetByRole(AriaRole.Dialog);

        await drawer.GetByRole(AriaRole.Button, new() { Name = "Renommer" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Alert))
            .ToContainTextAsync("pas la modifier", new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_head_moves_somebody_into_another_branch_of_their_own()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/settings/org");

        await page.GetByRole(AriaRole.Button, new() { Name = "Exploitation & Production" }).First
            .ClickAsync(new() { Timeout = TimeoutMs });

        var drawer = page.GetByRole(AriaRole.Dialog);
        var member = drawer.GetByRole(AriaRole.Listitem).First;

        await Expect(member).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        var moved = (await member.Locator(".member__name").InnerTextAsync()).Trim();

        // The select offers every branch Olivier can *read*, which is a wider set than the branches he may move
        // somebody into -- it includes the placeholder top he sits under. Picking by position walked straight into
        // one of those and asserted that a refusal was a move. So the destination is a sibling beneath his own
        // node, which is what "another branch of their own" means, and the label is read back afterwards because
        // where the person landed is where the rest of this has to look.
        var destination = member.GetByLabel("Déplacer vers…");

        await destination.SelectOptionAsync(
            new SelectOptionValue { Label = "Études & Développement" },
            new() { Timeout = TimeoutMs });

        var landing = (await destination.Locator("option:checked").InnerTextAsync()).Trim();

        await member.GetByRole(AriaRole.Button, new() { Name = "Déplacer" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // The person is somebody else's now, so this branch stops listing them. Asserting the chip here would be
        // asserting that the move did not happen.
        await Expect(drawer.GetByRole(AriaRole.Listitem).Filter(new() { HasText = moved }))
            .ToHaveCountAsync(0, new() { Timeout = TimeoutMs });

        await page.GetByRole(AriaRole.Button, new() { Name = landing }).First
            .ClickAsync(new() { Timeout = TimeoutMs });

        // §08.1's team-member management, moved off the boards: the correction is a fact about the person from
        // now on, which is why the row says so rather than looking like whatever LDAP last said.
        var landed = page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Listitem)
            .Filter(new() { HasText = moved });

        await Expect(landed).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
        await Expect(landed.GetByText("Déplacé")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
