using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// S8's acceptance journey, end to end.
/// </summary>
/// <remarks>
/// The slice's acceptance criteria are three sentences: the report adapts to whoever opens it and never exceeds
/// their visibility, the narrative is written by the on-prem model in the reader's own language, and the export
/// produces a PDF. These run all three through the real BFF against the real stack — including the dev box's
/// stub model, which is what makes the summary assertion stable rather than a guess at generated prose.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class ReportJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    /// <summary>
    /// A head lands on the widest scope a head holds, and the figures are there (v2 §01.2, §07.3).
    /// </summary>
    /// <remarks>
    /// This asserted "a unit head opens on their unit" while the four level-named head roles still existed. v2
    /// collapsed them into one, so nothing in a role distinguishes a unit head from a service head: a head is
    /// offered both scopes and RLS decides what each returns. The numbers also moved — §07.3 made the Brief the
    /// default rendering, and the metric tiles now live behind "Détails" where somebody consulting alone finds
    /// them.
    /// </remarks>
    [Fact]
    public async Task A_head_opens_the_report_on_the_widest_scope_they_hold_and_the_numbers_are_there()
    {
        var page = await stack.SignInAsync("thomas.berthier");

        await page.GotoAsync("/reports");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Rapport", new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Mon département", Exact = true }))
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = TimeoutMs });

        // The narrower scope stays one click away rather than being taken off the screen.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Mon unité", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // Deterministic figures, rendered before any model is involved.
        await ShowDetailsAsync(page);

        await Expect(page.Locator(".metric__value").First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // A department report stacks its units; it does not carry the RUN load, which is a question about one
        // team's tickets and shifts. This asserted "Charge RUN" here while the page still opened on the unit --
        // §07.3 moved the landing to the widest scope and the section stayed behind, so the assertion was reading
        // the old default rather than the new one.
        // The section header, not just the text: "Par unité" is also the caption of the table inside that section,
        // and an unqualified match is a strict-mode violation rather than an assertion.
        await Expect(page.Locator(".card__header").Filter(new() { HasText = "Par unité" }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // And the narrower scope, which is where the RUN figures live, is genuinely one click away.
        await page.GetByRole(AriaRole.Button, new() { Name = "Mon unité", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // No second "Détails": the rendering is a preference that survives the scope change, and the button that
        // sets it only exists while the brief is showing.
        await Expect(page.GetByText("Charge RUN")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_is_not_offered_a_scope_beyond_their_role()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/reports");

        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Mon travail", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // The tabs are drawn from what the server says this viewer may ask for. What actually protects the
        // department report is the 403 below; this is the half that stops somebody trying.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Mon département", Exact = true }))
            .ToHaveCountAsync(0, new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_asking_the_API_for_a_department_report_is_denied()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        // With the anti-forgery header, so the 403 below is the API's refusal and not the BFF's. Without it every
        // proxied request is refused before the API is asked, and this test would pass whatever the report did.
        var response = await page.APIRequest.GetAsync(
            "/api/reports?scope=department",
            new APIRequestContextOptions { Headers = new Dictionary<string, string> { ["X-Cracra-Csrf"] = "1" } });

        // 403 rather than an empty department: an empty one would tell Camille her department did nothing.
        response.Status.ShouldBe(403);
    }

    [Fact]
    public async Task The_narrative_streams_in_and_is_labelled_as_machine_written()
    {
        var page = await stack.SignInAsync("thomas.berthier");

        await page.GotoAsync("/reports");

        // The synthèse belongs to the detailed rendering; the Brief the page opens on is the meeting-ready one.
        await ShowDetailsAsync(page);

        // The card's own header. "Synthèse" alone also matches the button and the empty-state sentence, and a
        // strict-mode violation is a test failing for a reason that has nothing to do with the feature.
        await Expect(page.Locator(".summary .card__header"))
            .ToContainTextAsync("Synthèse", new() { Timeout = TimeoutMs });

        // Labelled before anything is generated, and it stays labelled afterwards. A reader forwarding this
        // screen or its PDF has to be able to tell which half a model wrote.
        await Expect(page.Locator(".summary__disclaimer")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // Located by position rather than by label: the Aspire stack persists between runs, so a narrative may
        // already be cached and the same button then reads "Régénérer". Both paths run the model and both end in
        // the assertion below, so the label is exactly the detail this test should not depend on.
        await page.Locator(".summary .card__header button")
            .ClickAsync(new() { Timeout = TimeoutMs });

        // The dev box's stub echoes the language it was told to answer in, which is what makes this assertion
        // about our plumbing rather than about a model's mood.
        await Expect(page.Locator(".summary__text"))
            .ToContainTextAsync("[stub:fr]", new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_report_exports_to_a_PDF_and_the_link_comes_back()
    {
        var page = await stack.SignInAsync("thomas.berthier");

        await page.GotoAsync("/reports");

        await ShowDetailsAsync(page);

        await Expect(page.Locator(".metric__value").First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await page.GetByRole(AriaRole.Button, new() { Name = "Exporter en PDF" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // Shown rather than downloaded silently: the link is short-lived, and a download that starts by itself
        // gives the reader nothing to copy when they meant to send it to somebody.
        var link = page.GetByRole(AriaRole.Link, new() { Name = "Télécharger le PDF" });

        await Expect(link).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // Stored in RustFS under the reports prefix, which is the acceptance criterion rather than the download.
        var href = await link.GetAttributeAsync("href");

        href.ShouldNotBeNull();
        href.ShouldContain("reports/");
    }

    [Fact]
    public async Task The_report_follows_the_readers_language()
    {
        var page = await stack.SignInAsync("thomas.berthier");

        await page.GotoAsync("/reports");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Rapport", new() { Timeout = TimeoutMs });

        // Section titles arrive as keys and are rendered by Transloco, so switching language re-labels the whole
        // report without another request — which is the point of keying them server-side.
        await page.GetByRole(AriaRole.Button, new() { Name = "EN", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Status report", new() { Timeout = TimeoutMs });
    }

    /// <summary>
    /// Switches the page from the Brief to the detailed rendering (v2 §07.3).
    /// </summary>
    /// <remarks>
    /// The Brief is what the page opens on, deliberately: the raw table with its decimals is a consultation view,
    /// and handing somebody that when they asked how the week went is why the synthèse went unread. Everything
    /// this suite asserts about metric tiles and the narrative lives on the other side of that toggle.
    /// </remarks>
    private static async Task ShowDetailsAsync(IPage page) =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Détails", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
