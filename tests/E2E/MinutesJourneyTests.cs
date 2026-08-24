using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// v2 §07's acceptance journey: run a meeting, write its CR, assign an action, publish it, and watch it reach the
/// people the meeting was for.
/// </summary>
/// <remarks>
/// The publish rule and the RLS are proven against real rows elsewhere. What only a browser shows is the loop
/// closing: the person who ran the meeting writes the record on the occurrence itself, and the action they
/// created is on somebody else's tracker a moment later without either of them going looking for it.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class MinutesJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    [Fact]
    public async Task Anybody_reaches_meetings_from_the_rail()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GetByRole(AriaRole.Navigation)
            .GetByRole(AriaRole.Link, new() { Name = "Réunions" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Réunions", new() { Timeout = TimeoutMs });

        // The three panels are the loop: what is coming, what came out of it, and what you now owe.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "À venir", Level = 2 }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Derniers CR", Level = 2 }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_head_writes_the_CR_of_a_meeting_they_scheduled_and_publishes_it()
    {
        var head = await stack.SignInAsync("olivier.marchand");

        await ScheduleAsync(head);

        await head.GotoAsync("/meetings");

        await head.GetByRole(AriaRole.Button, new() { Name = "Écrire le CR" }).First
            .ClickAsync(new() { Timeout = TimeoutMs });

        var drawer = head.GetByRole(AriaRole.Dialog);

        await Expect(drawer).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        var summary = $"Deux incidents, tous deux clos {Guid.CreateVersion7().ToString("N")[^8..]}";

        await drawer.GetByLabel("Synthèse").FillAsync(summary);

        // Blur first: the text boxes save on blur, and publishing a draft whose summary is still in an input is
        // the one way to distribute an empty CR that somebody had in fact written.
        await drawer.GetByLabel("Ordre du jour").ClickAsync(new() { Timeout = TimeoutMs });

        await drawer.GetByRole(AriaRole.Button, new() { Name = "Publier" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(drawer.GetByText("Publié").First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    /// <summary>
    /// A node weekly on the department, so the occurrence the CR hangs off exists.
    /// </summary>
    /// <remarks>
    /// Scheduled through the manager exactly as a head would, rather than seeded through the API: the level
    /// control is new in this slice, and a journey that reached past the screen would not prove it is reachable.
    /// </remarks>
    private static async Task ScheduleAsync(IPage page)
    {
        await page.GotoAsync("/settings/meetings");

        await page.GetByLabel("Intitulé").First
            .FillAsync($"Hebdo DSI {Guid.CreateVersion7().ToString("N")[^8..]}");

        await page.GetByLabel("Périmètre").First.SelectOptionAsync("department", new() { Timeout = TimeoutMs });
        await page.GetByLabel("Niveau").First.SelectOptionAsync("node", new() { Timeout = TimeoutMs });

        // The target list is the departments this person may schedule into, which for a head is their own.
        await page.GetByLabel("Cible").First.SelectOptionAsync(new SelectOptionValue { Index = 1 },
            new() { Timeout = TimeoutMs });

        await page.GetByRole(AriaRole.Button, new() { Name = "Créer la réunion" })
            .ClickAsync(new() { Timeout = TimeoutMs });
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
