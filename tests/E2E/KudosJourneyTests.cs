using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// S9's acceptance journey, end to end.
/// </summary>
/// <remarks>
/// The slice's acceptance criterion is one sentence with a hinge in the middle: in a department set to counter
/// mode a member recognises a unit peer and it shows on the team board's monthly total, and then the department
/// switches to points-badges mode and a leaderboard and a badge appear. Both halves run here against the real
/// stack, through a real login, because the hinge is a department setting and the whole point is that flipping it
/// changes what everybody sees without anything being re-entered.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class KudosJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    private static readonly Dictionary<string, string> AntiForgery = new() { ["X-Cracra-Csrf"] = "1" };

    private const string InformationSystems = "11111111-1111-1111-1111-111111111111";
    private const string Mehdi = "c0000000-0000-0000-0000-000000000002";

    /// <summary>
    /// The two department settings this journey hinges on, both with the monthly cap wound right up.
    /// </summary>
    /// <remarks>
    /// The dev stack keeps its database between runs, so the kudos these tests give accumulate for as long as the
    /// month lasts and the real ten-a-month cap turns into a suite that passes in the morning and fails in the
    /// afternoon. The cap itself is asserted where it can be asserted honestly — against a database reset per
    /// scenario, in the integration suite, and against the aggregate directly in the unit one.
    /// </remarks>
    private const string CounterMode = """{"mode":"counter","monthlyCapPerGiver":200}""";

    private const string PointsMode =
        """{"mode":"points-badges-leaderboard","monthlyCapPerGiver":200}""";

    [Fact]
    public async Task A_member_recognises_a_unit_peer_and_the_counter_moves()
    {
        await ConfigureKudoRulesAsync(CounterMode);

        var page = await stack.SignInAsync("camille.villeneuve");

        var before = await CountAsync(page);

        await GiveAsync(page, "cleanup", "A nettoyé les avertissements de build que personne ne voulait toucher.");

        await page.GotoAsync("/kudos");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Kudos", new() { Timeout = TimeoutMs });

        // The wall carries the message, not just the fact. A category with nothing said about it is a click.
        await Expect(page.Locator(".kudo__message").First)
            .ToContainTextAsync("avertissements de build", new() { Timeout = TimeoutMs });

        // A department that counts shows no score anywhere, including on the card that was just written.
        await Expect(page.Locator(".kudo-badge")).ToHaveCountAsync(0, new() { Timeout = TimeoutMs });

        (await CountAsync(page)).ShouldBe(before + 1);
    }

    [Fact]
    public async Task The_monthly_total_reaches_the_team_board()
    {
        await ConfigureKudoRulesAsync(CounterMode);

        // A different giver in each of these, deliberately. The Aspire stack persists between runs and the
        // monthly cap is real: one person giving four kudos per run would eventually exhaust their own allowance
        // and turn a fixture limit into a mystifying feature failure.
        var page = await stack.SignInAsync("thomas.berthier");

        await GiveAsync(page, "initiative", "A repris l'astreinte au pied levé.");

        await page.GotoAsync("/team");

        // The widget the spec asks for: recognition lands where the team already is, rather than only on a screen
        // somebody has to remember to open.
        await Expect(page.Locator(".kudos-monthly")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
        await Expect(page.Locator(".kudos-monthly__value"))
            .Not.ToHaveTextAsync("0", new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_counting_department_is_offered_no_leaderboard_at_all()
    {
        await ConfigureKudoRulesAsync(CounterMode);

        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/kudos");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Kudos", new() { Timeout = TimeoutMs });

        // Absent rather than disabled. A greyed-out leaderboard advertises a ranking to a department that decided
        // against having one.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Classement", Exact = true }))
            .ToHaveCountAsync(0, new() { Timeout = TimeoutMs });

        var response = await page.APIRequest.GetAsync(
            "/api/kudos/leaderboard?scope=unit",
            new APIRequestContextOptions { Headers = AntiForgery });

        // And the API says the same thing, which is what actually holds the promise.
        response.Status.ShouldBe(403);
    }

    [Fact]
    public async Task Switching_the_department_to_points_reveals_the_leaderboard_and_the_badges()
    {
        await ConfigureKudoRulesAsync(CounterMode);

        var page = await stack.SignInAsync("nadia.kessler");

        // Given while the department was still a bare counter.
        await GiveAsync(page, "above-and-beyond", "Est resté tard pour débloquer la mise en production.");

        await ConfigureKudoRulesAsync(PointsMode);

        var reader = await stack.SignInAsync("camille.villeneuve");

        await reader.GotoAsync("/kudos");

        await reader.GetByRole(AriaRole.Button, new() { Name = "Classement", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // The kudo given before the switch counts on the board it created. A ranking that started at zero on the
        // day of a settings change would be a ranking nobody believes.
        await Expect(reader.Locator(".data-table tbody tr").First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await Expect(reader.Locator(".data-table tbody .chip--badge").First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task My_year_lists_what_a_review_would_claim()
    {
        await ConfigureKudoRulesAsync(CounterMode);

        var giver = await stack.SignInAsync("olivier.marchand");

        await GiveAsync(giver, "mentoring", "A accompagné le nouvel arrivant sur l'astreinte.");

        var page = await stack.SignInAsync("mehdi.sadaoui");

        await page.GotoAsync("/kudos");

        await page.GetByRole(AriaRole.Button, new() { Name = "Mon année", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // The sentences, in full, grouped by category. That is the whole artefact: a count without them proves
        // nothing to whoever reads the review.
        await Expect(page.Locator(".annual__item").First)
            .ToContainTextAsync("accompagné", new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task Nobody_can_recognise_themselves()
    {
        await ConfigureKudoRulesAsync(CounterMode);

        var page = await stack.SignInAsync("camille.villeneuve");

        var response = await page.APIRequest.PostAsync("/api/kudos", new APIRequestContextOptions
        {
            Headers = AntiForgery,
            DataObject = new Dictionary<string, object?>
            {
                ["toPersonId"] = "c0000000-0000-0000-0000-000000000001",
                ["category"] = "initiative",
                ["message"] = "Je me félicite.",
            },
        });

        // 422: well-formed request, and the domain said no.
        response.Status.ShouldBe(422);
    }

    // --- Arrangement ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Sets the IS department's kudo rules, as its head.
    /// </summary>
    /// <remarks>
    /// Through the API on a real signed-in session, so the arrangement passes the same BFF, policies and RLS a
    /// head would. Setting it up behind the application's back could arrange states the application forbids.
    /// </remarks>
    private async Task ConfigureKudoRulesAsync(string kudoRulesJson)
    {
        var page = await stack.SignInAsync("olivier.marchand");

        var response = await page.APIRequest.PutAsync(
            $"/api/directory/departments/{InformationSystems}/config",
            new APIRequestContextOptions
            {
                Headers = AntiForgery,
                DataObject = new Dictionary<string, object?>
                {
                    ["activityTaxonomyJson"] = "{}",
                    ["roleLabelsJson"] = "{}",
                    ["kudoRulesJson"] = kudoRulesJson,
                    ["defaultBoardLayout"] = "week",
                    ["iterationPresetsJson"] = """["1w","2w","1m"]""",
                    ["weeklyTargetHours"] = 35,
                    ["enforceWeeklyTarget"] = false,
                },
            });

        response.Status.ShouldBe(200);
    }

    /// <summary>
    /// Gives one kudo to Mehdi through the API on the caller's own session.
    /// </summary>
    /// <remarks>
    /// The modal is exercised by the unit and integration suites; what this suite is for is the journey after it,
    /// and driving a five-field form six times would make every assertion below depend on the form's markup.
    /// </remarks>
    private static async Task GiveAsync(IPage page, string category, string message)
    {
        var response = await page.APIRequest.PostAsync("/api/kudos", new APIRequestContextOptions
        {
            Headers = AntiForgery,
            DataObject = new Dictionary<string, object?>
            {
                ["toPersonId"] = Mehdi,
                ["category"] = category,
                ["message"] = message,
            },
        });

        // The body on failure, because everything that can refuse here — eligibility, the category, the cap — says
        // exactly which, and a bare status code further down would send the reader looking in the wrong place.
        response.Status.ShouldBe(201, $"POST /api/kudos returned {response.Status}: {await response.TextAsync()}");
    }

    /// <summary>How many kudos this unit has had this month, straight from the counter every mode has.</summary>
    private static async Task<int> CountAsync(IPage page)
    {
        // The header on a GET as well: the BFF requires it on everything it proxies, and a request without it is
        // refused before the API is ever asked — which would make this count a test of the BFF's CSRF rule.
        var response = await page.APIRequest.GetAsync(
            "/api/kudos/summary?scope=unit&period=month",
            new APIRequestContextOptions { Headers = AntiForgery });

        response.Status.ShouldBe(200);

        var body = await response.JsonAsync();

        return body?.GetProperty("total").GetInt32() ?? 0;
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
