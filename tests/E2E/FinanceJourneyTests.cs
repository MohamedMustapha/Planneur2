using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// S11's acceptance journey: a head sets a rate card, reads the split, exports it — and a member is refused.
/// </summary>
/// <remarks>
/// The slice's acceptance criteria are two sentences: effort and manual cost split into capex and opex by
/// configurable rules, optionally valued through rate cards; and the view is head-only and exportable. This runs
/// both, through the real BFF, the real policies and real RLS.
///
/// The refusal is a first-class case here rather than an afterthought. A capitalization view that renders empty
/// for somebody who may not see it looks exactly like a department that spent nothing, and telling those two
/// apart is the point of the matrix row this slice implements.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class FinanceJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    private static readonly Dictionary<string, string> AntiForgery = new() { ["X-Cracra-Csrf"] = "1" };

    private const string Camille = "c0000000-0000-0000-0000-000000000001";
    private const string InformationSystems = "11111111-1111-1111-1111-111111111111";
    private const string DeveloperRole = "f0000000-0000-0000-0000-000000000001";

    /// <summary>Camille's job identity in the seeded realm. A rate card has to name it to price her hours.</summary>
    private const string ArchitectRole = "f0000000-0000-0000-0000-000000000003";

    [Fact]
    public async Task A_department_head_reads_the_capex_opex_split()
    {
        await ProjectWithLoggedHoursAsync();

        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/finance");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Capex", new() { Timeout = TimeoutMs });

        // Both columns, side by side, with the hours beside the money. The figure a head is looking for is nearly
        // always one of the two, which is why they are the only two things this large on the page.
        await Expect(page.Locator(".finance__figure--capex")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
        await Expect(page.Locator(".finance__figure--opex")).ToBeVisibleAsync();

        await Expect(page.GetByText("Par projet")).ToBeVisibleAsync();
        await Expect(page.GetByText("Par mois")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Setting_a_rate_card_values_the_hours()
    {
        await ProjectWithLoggedHoursAsync();

        var page = await stack.SignInAsync("olivier.marchand");

        // This journey is a before-and-after, and the "before" is "nothing is priced yet" — which its own
        // "after" destroys. The Aspire stack persists between runs, so without this the test passes once on a
        // fresh database and fails on every run after it, for a reason that looks nothing like its cause.
        // Cleared through the public API, so the arrangement obeys the policies a head would.
        await ClearRateCardsAsync(page);

        await page.GotoAsync("/finance");

        // Before: hours and the entered cost, and the screen says so rather than showing a zero somebody would
        // read as "this was free".
        await Expect(page.GetByText("Aucun taux horaire", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await page.GetByRole(AriaRole.Button, new() { Name = "Règles et taux" }).ClickAsync();

        await page.GetByLabel("Rôle métier").SelectOptionAsync(ArchitectRole);
        await page.GetByLabel("Taux horaire").FillAsync("80");
        await page.GetByLabel("À partir du").FillAsync("2026-01-01");

        await page.GetByRole(AriaRole.Button, new() { Name = "Ajouter le taux" }).ClickAsync();

        // After: the notice is gone, because something is now priced.
        await Expect(page.GetByText("Aucun taux horaire", new() { Exact = false }))
            .ToBeHiddenAsync(new() { Timeout = TimeoutMs });

        await Expect(page.Locator(".finance__figure--capex")).ToContainTextAsync("EUR");
    }

    [Fact]
    public async Task A_head_moves_a_bucket_to_the_other_column_and_the_figures_follow()
    {
        await ProjectWithLoggedHoursAsync();

        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/finance");

        await page.GetByRole(AriaRole.Button, new() { Name = "Règles et taux" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // The whole reason the rule is a row rather than a constant: a department that treats its RUN work as
        // capitalizable maintenance says so here, and nothing is deployed.
        await page.GetByLabel("Projet — RUN").SelectOptionAsync("capex");

        var runRow = page.Locator(".data-table tr", new() { HasTextString = "Projet — RUN" }).First;

        await Expect(runRow.GetByText("Capex", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_head_exports_the_split_to_excel()
    {
        await ProjectWithLoggedHoursAsync();

        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/finance");

        await page.GetByRole(AriaRole.Button, new() { Name = "Exporter (Excel)" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // Shown rather than followed: the link is short-lived, and a download that starts on its own is
        // indistinguishable from one that failed.
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Télécharger" }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_is_told_no_rather_than_shown_an_empty_department()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        // The rail does not offer it, so this is somebody typing the URL — which the client is deliberately not
        // the thing that stops.
        await page.GotoAsync("/finance");

        await Expect(page.Locator(".finance__error"))
            .ToContainTextAsync("chefs de département", new() { Timeout = TimeoutMs });

        // And nothing of the numbers, because the API never sent any.
        await Expect(page.Locator(".finance__figure--capex")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task The_rail_offers_finance_to_a_head_and_not_to_a_member()
    {
        var head = await stack.SignInAsync("olivier.marchand");

        await Expect(head.GetByRole(AriaRole.Link, new() { Name = "Finance" }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        var member = await stack.SignInAsync("camille.villeneuve");

        // Tidiness rather than security — what protects the data is the policy and RLS, both asserted above.
        await Expect(member.GetByRole(AriaRole.Link, new() { Name = "Finance" }))
            .ToHaveCountAsync(0, new() { Timeout = TimeoutMs });
    }

    /// <summary>
    /// A project with cost, a member, and hours logged against it — the state the view has something to say about.
    /// </summary>
    /// <remarks>
    /// Through the API on each person's own signed-in session, so the arrangement passes the same policies and the
    /// same RLS a real head and a real developer would. A precondition set up behind the application's back can
    /// arrange states the application would never allow.
    /// </remarks>
    /// <summary>Removes every rate card this head can see, so the "nothing is priced" state is reachable again.</summary>
    private static async Task ClearRateCardsAsync(IPage page)
    {
        // The header goes on the GET too: the BFF requires it on every proxied /api call, not only on writes.
        var existing = await page.APIRequest.GetAsync(
            "/api/finance/rate-cards",
            new APIRequestContextOptions { Headers = AntiForgery });

        existing.Status.ShouldBe(200);

        foreach (var card in (await existing.JsonAsync())!.Value.EnumerateArray())
        {
            var id = card.GetProperty("id").GetString();

            await page.APIRequest.DeleteAsync(
                $"/api/finance/rate-cards/{id}",
                new APIRequestContextOptions { Headers = AntiForgery });
        }
    }

    private async Task ProjectWithLoggedHoursAsync()
    {
        var head = await stack.SignInAsync("olivier.marchand");

        var code = $"PRJ-{Guid.CreateVersion7().ToString("N")[^8..]}";

        var created = await head.APIRequest.PostAsync("/api/projects", new APIRequestContextOptions
        {
            Headers = AntiForgery,
            DataObject = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["name"] = "Socle applicatif",
                ["classification"] = "build",
                ["costAmount"] = 40_000,
                ["costCurrency"] = "EUR",
                ["leadDepartmentId"] = InformationSystems,
                ["contributingDepartmentIds"] = Array.Empty<string>(),
            },
        });

        created.Status.ShouldBe(201);

        var projectId = (await created.JsonAsync())!.Value.GetProperty("id").GetString()!;

        var added = await head.APIRequest.PostAsync($"/api/projects/{projectId}/members", new APIRequestContextOptions
        {
            Headers = AntiForgery,
            DataObject = new Dictionary<string, object?>
            {
                ["personId"] = Camille,
                ["departmentId"] = InformationSystems,
                ["functionalRoleId"] = DeveloperRole,
            },
        });

        added.Status.ShouldBe(204);

        var developer = await stack.SignInAsync("camille.villeneuve");

        // Dated inside the month the screen opens on, because the view's default period is the current one and a
        // journey that logged last month's hours would assert against an empty page for the right reason.
        var day = DateTime.UtcNow.Date.AddDays(1 - DateTime.UtcNow.Day).AddHours(9);

        foreach (var (type, hours) in new[] { ("project-build", 6), ("project-run", 2) })
        {
            var logged = await developer.APIRequest.PostAsync("/api/activities", new APIRequestContextOptions
            {
                Headers = AntiForgery,
                DataObject = new Dictionary<string, object?>
                {
                    ["activityTypeCode"] = type,
                    ["projectId"] = projectId,
                    ["kind"] = "actual",
                    ["source"] = "manual",
                    ["slotStart"] = day.ToString("O"),
                    ["slotEnd"] = day.AddHours(hours).ToString("O"),
                    ["hours"] = hours,
                },
            });

            logged.Status.ShouldBe(201);

            day = day.AddDays(1);
        }
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
