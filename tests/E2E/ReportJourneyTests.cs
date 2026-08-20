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

    [Fact]
    public async Task A_unit_head_opens_their_unit_report_and_the_numbers_are_there()
    {
        var page = await stack.SignInAsync("thomas.berthier");

        await page.GotoAsync("/reports");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Rapport", new() { Timeout = TimeoutMs });

        // A unit head's widest scope is their unit, and the report opens on it without being asked.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Mon unité", Exact = true }))
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = TimeoutMs });

        // Deterministic figures, rendered before any model is involved.
        await Expect(page.Locator(".metric__value").First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
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

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
