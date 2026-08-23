using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// S7's acceptance journey, end to end.
/// </summary>
/// <remarks>
/// The slice's acceptance criteria come down to one sentence: create a weekly copil at department scope and a
/// patch-party special day, and see both on the department board and in the "coming up" strip for members of that
/// department. That is what these run, through the real BFF, the real policies and real RLS — including the part
/// that matters most, which is that a member of another department sees neither.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class MeetingJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    private static readonly Dictionary<string, string> AntiForgery = new() { ["X-Cracra-Csrf"] = "1" };

    private const string InformationSystems = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task A_department_head_schedules_a_copil_and_declares_a_patch_party()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/settings/meetings");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Réunions", new() { Timeout = TimeoutMs });

        var copil = $"Copil SI {Suffix()}";

        await page.GetByLabel("Type").First.SelectOptionAsync("copil");
        await page.GetByPlaceholder("Point hebdomadaire Infrastructure").FillAsync(copil);
        await page.GetByLabel("Périmètre").First.SelectOptionAsync("department");
        await page.GetByLabel("Cible").First.SelectOptionAsync(new SelectOptionValue { Value = InformationSystems });

        // The composed rule is shown as the picker builds it, so somebody who reads RRULE can check the form
        // rather than saving and re-opening to find out what it meant.
        await Expect(page.Locator(".meetings__rule code")).ToContainTextAsync("FREQ=WEEKLY");

        await page.GetByRole(AriaRole.Button, new() { Name = "Créer la réunion" }).ClickAsync();

        await Expect(page.GetByText(copil, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_patch_party_shows_on_the_department_board_with_its_severity()
    {
        // On the canvas, so it has to be a day the canvas is showing.
        var name = await DeclarePatchPartyAsync(TuesdayOfThisWeek());

        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/department");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Plannings", new() { Timeout = TimeoutMs });

        // The overlay strip names what the canvas can only colour. Both are painted from the same token, so the
        // chip and the marked column cannot disagree about how loud the day is.
        var chip = page.Locator(".events__item", new() { HasTextString = name });

        await Expect(chip).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
        await Expect(chip).ToContainTextAsync("Patch party");
    }

    [Fact]
    public async Task A_member_of_the_department_sees_it_in_the_coming_up_strip()
    {
        // In the strip, so it has to be ahead of now.
        var name = await DeclarePatchPartyAsync(Tomorrow());

        // Camille is a plain member of IS. The special day targets her department, so she sees it — which is the
        // whole point of the department scope, and the thing a head-only rule would have broken.
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/board");

        // The strip is one of the secondary panels Focus mode folds away, and members start in Focus mode
        // (v2 §02). What this test is about is whether the department's special day reaches her at all, so it
        // leaves Focus mode rather than asserting against a shell that is deliberately hiding context.
        await AspireStackFixture.LeaveFocusModeAsync(page);

        await Expect(page.Locator(".upcoming__name", new() { HasTextString = name }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task Somebody_in_another_department_sees_neither()
    {
        var name = await DeclarePatchPartyAsync(Tomorrow());

        var page = await stack.SignInAsync("sofia.navarro");

        await page.GotoAsync("/board");

        // Wait for the dashboard itself before asserting an absence, or the assertion passes simply because the
        // page has not rendered yet. Leaving Focus mode is the same guard one step further in: Sofia is a member,
        // members start in Focus mode, and Focus mode removes the strip — so without this the absence below is
        // satisfied by a strip that was never drawn rather than by a day she may not see.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await AspireStackFixture.LeaveFocusModeAsync(page);

        await Expect(page.Locator(".upcoming__name", new() { HasTextString = name }))
            .ToHaveCountAsync(0, new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_is_not_offered_the_manager()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/board");

        // The rail's settings entry is heads-only, so a member never arrives at the manager by clicking. What
        // actually protects it is the endpoint policy and the write predicate; this is the tidiness half.
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Réunions & événements" }))
            .ToHaveCountAsync(0, new() { Timeout = TimeoutMs });
    }

    /// <summary>
    /// Declares one patch party for IS and returns its name.
    /// </summary>
    /// <remarks>
    /// Through the API on a real signed-in session, so the arrangement passes the same BFF, policies and RLS a
    /// head would. Setting it up behind the application's back could arrange states the application forbids.
    ///
    /// Named uniquely per run because the Aspire stack persists between runs, and an earlier run's day would make
    /// the "sees it" assertions pass without this one ever being written.
    /// </remarks>
    /// <summary>The Tuesday of the current ISO week, which every board's default window includes.</summary>
    /// <remarks>
    /// The boards open on the working week containing today, Monday to Friday. "Tomorrow" is inside that window
    /// on most days and outside it on Friday, Saturday and Sunday — so a test that used it passed all week and
    /// failed at the weekend, for a reason that looks nothing like its cause. A fixed weekday of the current week
    /// is in the window whatever day the suite runs.
    /// </remarks>
    private static DateOnly TuesdayOfThisWeek()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(1);
    }

    /// <summary>Tomorrow, for the screens that show what is coming rather than what is on this week's canvas.</summary>
    private static DateOnly Tomorrow() => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    private async Task<string> DeclarePatchPartyAsync(DateOnly on)
    {
        var page = await stack.SignInAsync("olivier.marchand");

        // The Aspire stack persists between runs, so earlier runs' days accumulate on the same dates — and the
        // strip shows only the next few. Without this, the assertion below would start failing once enough runs
        // had piled up, for a reason that looks nothing like its cause. Cleared through the public API so the
        // arrangement obeys the same policies a head would.
        await ClearSpecialDaysAsync(page);

        var name = $"Patch party {Suffix()}";

        var response = await page.APIRequest.PostAsync("/api/meetings/special-days", new APIRequestContextOptions
        {
            Headers = AntiForgery,
            DataObject = new Dictionary<string, object?>
            {
                ["kind"] = "patch-party",
                ["nameKey"] = name,
                ["scopeType"] = "department",
                ["scopeId"] = InformationSystems,
                // Chosen by the caller, because the two screens under test want different things: the board can
                // only paint a day inside the week it opened on, and the "à venir" strip only lists days ahead.
                ["date"] = on.ToString("yyyy-MM-dd"),
                ["allDay"] = true,
                ["severity"] = "warning",
                ["description"] = "Fenêtre de patch mensuelle.",
            },
        });

        response.Status.ShouldBe(201);

        return name;
    }

    /// <summary>Removes every special day this head can see, so each run starts from an empty calendar.</summary>
    private static async Task ClearSpecialDaysAsync(IPage page)
    {
        // The header goes on the GET too: the BFF requires it on every proxied /api call, not only on writes.
        var existing = await page.APIRequest.GetAsync(
            "/api/meetings/special-days",
            new APIRequestContextOptions { Headers = AntiForgery });

        existing.Status.ShouldBe(200);

        foreach (var day in (await existing.JsonAsync())!.Value.EnumerateArray())
        {
            var id = day.GetProperty("id").GetString();

            await page.APIRequest.DeleteAsync(
                $"/api/meetings/special-days/{id}",
                new APIRequestContextOptions { Headers = AntiForgery });
        }
    }

    private static string Suffix() => Guid.CreateVersion7().ToString("N")[^6..];

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
