using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// S6's acceptance journeys, one per archetype.
/// </summary>
/// <remarks>
/// The boards are the visual heart of the product, so what these prove is that the shared canvas actually renders
/// from real data through the real stack — the three archetypes over one timeline, the pool assigning to a row,
/// and each board scoped to what its viewer may see.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class BoardJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    private static readonly Dictionary<string, string> AntiForgery = new() { ["X-Cracra-Csrf"] = "1" };

    private const string Camille = "c0000000-0000-0000-0000-000000000001";
    private const string Infrastructure = "aaaaaaaa-0000-0000-0000-000000000001";

    [Fact]
    public async Task The_boards_screen_offers_all_five()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/team");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Plannings", new() { Timeout = TimeoutMs });

        // All five to everybody. Which rows land inside is RLS's answer, and hiding a tab because someone probably
        // has nothing in it would be the client guessing at a decision the server already makes correctly.
        foreach (var board in new[] { "Mon tableau", "Mon équipe", "Mon unité", "Projet", "Département" })
        {
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = board, Exact = true }))
                .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
        }
    }

    [Fact]
    public async Task The_shared_timeline_renders_on_the_board()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/team");

        // The licensed Mobiscroll canvas, not a placeholder. One wrapper serves all three archetypes, so proving
        // it mounts here proves it for the other two.
        await Expect(page.Locator(".mbsc-timeline").First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_is_told_the_board_is_read_only_by_the_absence_of_controls()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/team");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Plannings", new() { Timeout = TimeoutMs });

        // Camille is a plain member. The roster editor belongs to whoever may actually roster, and offering it to
        // someone the server will refuse is a worse way to say no.
        await Expect(page.GetByText("Planifier un créneau")).ToBeHiddenAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task Six_a_a_lead_assigns_a_queued_work_order_onto_an_agents_row()
    {
        // The department has to be on the work-order layout for its team board to be 6a — that is what
        // default_board_layout is for, and why a helpdesk and a dev team can share one platform.
        await ConfigureDepartmentAsync("work-orders");

        var reference = await QueueWorkOrderAsync();

        var page = await stack.SignInAsync("thomas.berthier");

        await page.GotoAsync("/team");

        var card = page.GetByRole(AriaRole.Button, new() { Name = reference, Exact = false });

        await Expect(card).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await card.ClickAsync();

        // Picking the card up reveals the rows it can go to. A two-step gesture rather than a drag, because a drag
        // that silently does nothing when the server refuses is worse than a click that says why.
        await Expect(page.GetByText($"Affecter {reference}", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await page.Locator(".pool__drop").GetByRole(AriaRole.Button, new() { Name = "Camille Villeneuve" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // It left the queue, because it is now on somebody's week.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = reference, Exact = false }))
            .ToBeHiddenAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task Six_a_the_assignment_appears_on_the_agents_own_board()
    {
        await ConfigureDepartmentAsync("work-orders");

        var reference = await QueueWorkOrderAsync();

        var lead = await stack.SignInAsync("thomas.berthier");

        await lead.GotoAsync("/team");
        await lead.GetByRole(AriaRole.Button, new() { Name = reference, Exact = false })
            .ClickAsync(new() { Timeout = TimeoutMs });
        await lead.Locator(".pool__drop").GetByRole(AriaRole.Button, new() { Name = "Camille Villeneuve" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // The whole point of the archetype: what the lead scheduled shows up in the person's own week, as planned
        // activity that Activities' own rules accepted.
        var agent = await stack.SignInAsync("camille.villeneuve");

        await agent.GotoAsync("/board");

        await Expect(agent.GetByText(reference, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task Six_b_a_lead_builds_a_week_and_a_coverage_warning_clears_once_filled()
    {
        // Two people needed on mornings, so one roster entry leaves a gap and the second closes it.
        await ConfigureDepartmentAsync(
            "shifts",
            """{"shifts":[{"code":"morning","labelKey":"shift.morning","start":"08:00","end":"12:30","minimumStaff":2}]}""");

        var page = await stack.SignInAsync("thomas.berthier");

        // The Aspire stack persists between runs, so an earlier run's roster would already cover the week and
        // there would be no gap left to demonstrate. Cleared through the public API rather than the database, so
        // the arrangement obeys the same policies a lead would.
        await ClearShiftsAsync(page);

        await page.GotoAsync("/team");

        await Expect(page.GetByText("Planifier un créneau")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        var monday = MondayOfThisWeek();

        await RosterAsync(page, "Camille Villeneuve", monday);

        // One of two on the day just rostered. The warning is amber and informational — a thin morning is
        // sometimes genuinely fine, and a scheduler that refused to record it would be one people stopped using.
        var mondayWarning = page.Locator(".coverage__item").Filter(new() { HasText = monday });

        await Expect(mondayWarning).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
        await Expect(mondayWarning).ToContainTextAsync("1/2");

        await RosterAsync(page, "Mehdi Sadaoui", monday);

        await Expect(page.Locator(".coverage__item").Filter(new() { HasText = monday }))
            .ToHaveCountAsync(0, new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task Six_c_a_project_board_groups_its_team_by_department()
    {
        var projectId = await CrossDepartmentProjectAsync();

        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/team");

        await page.GetByRole(AriaRole.Button, new() { Name = "Projet", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // The project board is the one board that has to be told which project: the others derive their scope
        // from who the caller is. Selected by value rather than label, because the option reads "CODE — Name".
        var selector = page.Locator(".scope select");

        await Expect(selector).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await selector.SelectOptionAsync(new SelectOptionValue { Value = projectId });

        // Rows are department groups with people nested beneath them — S3's grouping, reused rather than
        // recomputed, which is what keeps "who from which department is doing what" a single answer.
        await Expect(page.Locator(".mbsc-timeline").First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByText("Camille Villeneuve").First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_project_board_with_no_project_chosen_says_so()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/team");

        await page.GetByRole(AriaRole.Button, new() { Name = "Projet", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // Says what to do rather than showing an empty canvas — which is indistinguishable from a quiet week —
        // and rather than asking the server a question the client can already answer.
        await Expect(page.GetByText("Choisissez un projet pour afficher son planning."))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task The_department_board_shows_a_row_per_unit()
    {
        await ConfigureDepartmentAsync("week");

        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/department");

        await page.GetByRole(AriaRole.Button, new() { Name = "Département", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // Rows are units here, not people: a head reading this wants to know where the department's effort is
        // going. Who individually is doing what is the unit board, one level down.
        await Expect(page.Locator(".mbsc-timeline").First).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
        await Expect(page.GetByText("Infrastructure", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    // --- Arrangement ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Sets the IS department's board layout, as its head.
    /// </summary>
    /// <remarks>
    /// Through the API on a real signed-in session, so the arrangement passes the same BFF, policies and RLS a
    /// head would. Setting it up behind the application's back could arrange states the application forbids.
    /// </remarks>
    private async Task ConfigureDepartmentAsync(string layout, string shiftTemplates = "{}")
    {
        var page = await stack.SignInAsync("olivier.marchand");

        var response = await page.APIRequest.PutAsync(
            "/api/directory/departments/11111111-1111-1111-1111-111111111111/config",
            new APIRequestContextOptions
            {
                Headers = AntiForgery,
                DataObject = new Dictionary<string, object?>
                {
                    ["activityTaxonomyJson"] = "{}",
                    ["roleLabelsJson"] = "{}",
                    ["kudoRulesJson"] = "{}",
                    ["defaultBoardLayout"] = layout,
                    ["iterationPresetsJson"] = """["1w","2w","1m"]""",
                    ["weeklyTargetHours"] = 35,
                    ["enforceWeeklyTarget"] = false,
                    ["shiftTemplatesJson"] = shiftTemplates,
                },
            });

        response.Status.ShouldBe(200);
    }

    /// <summary>Puts one work order in the Infrastructure queue and returns its reference.</summary>
    private async Task<string> QueueWorkOrderAsync()
    {
        var page = await stack.SignInAsync("thomas.berthier");

        var reference = $"INC-{Guid.CreateVersion7().ToString("N")[^8..]}";

        var response = await page.APIRequest.PostAsync("/api/scheduling/work-orders", new APIRequestContextOptions
        {
            Headers = AntiForgery,
            DataObject = new Dictionary<string, object?>
            {
                ["reference"] = reference,
                ["title"] = "Imprimante hors service",
                ["unitId"] = Infrastructure,
                // A ticket against no project, which is the helpdesk case: project-run would demand one.
                ["activityTypeCode"] = "quality-of-life",
                ["estimatedHours"] = 1,
            },
        });

        response.Status.ShouldBe(201);

        return reference;
    }

    /// <summary>Creates a project with Camille on it and returns its id.</summary>
    private async Task<string> CrossDepartmentProjectAsync()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        var code = $"PRJ-{Guid.CreateVersion7().ToString("N")[^8..]}";

        var created = await page.APIRequest.PostAsync("/api/projects", new APIRequestContextOptions
        {
            Headers = AntiForgery,
            DataObject = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["name"] = "Board host",
                ["classification"] = "build",
                ["costAmount"] = 0,
                ["costCurrency"] = "EUR",
                ["leadDepartmentId"] = "11111111-1111-1111-1111-111111111111",
                ["contributingDepartmentIds"] = Array.Empty<string>(),
            },
        });

        created.Status.ShouldBe(201);

        var projectId = (await created.JsonAsync())!.Value.GetProperty("id").GetString()!;

        var added = await page.APIRequest.PostAsync(
            $"/api/projects/{projectId}/members",
            new APIRequestContextOptions
            {
                Headers = AntiForgery,
                DataObject = new Dictionary<string, object?>
                {
                    ["personId"] = Camille,
                    ["departmentId"] = "11111111-1111-1111-1111-111111111111",
                    ["functionalRoleId"] = "f0000000-0000-0000-0000-000000000001",
                },
            });

        added.Status.ShouldBe(204);

        return projectId;
    }

    /// <summary>Removes every shift the board is currently showing, so the week starts empty.</summary>
    private static async Task ClearShiftsAsync(IPage page)
    {
        var board = await page.APIRequest.GetAsync(
            "/api/scheduling/board?type=team",
            new APIRequestContextOptions { Headers = AntiForgery });

        board.Status.ShouldBe(200);

        var payload = (await board.JsonAsync())!.Value;

        foreach (var element in payload.GetProperty("events").EnumerateArray())
        {
            if (element.GetProperty("kind").GetString() != "shift")
            {
                continue;
            }

            var removed = await page.APIRequest.DeleteAsync(
                $"/api/scheduling/shifts/{element.GetProperty("id").GetString()}",
                new APIRequestContextOptions { Headers = AntiForgery });

            removed.Status.ShouldBe(204);
        }
    }

    private static async Task RosterAsync(IPage page, string person, string day)
    {
        await page.GetByLabel("Personne").SelectOptionAsync(new SelectOptionValue { Label = person });
        await page.GetByLabel("Créneau").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.GetByLabel("Jour").FillAsync(day);

        await page.GetByRole(AriaRole.Button, new() { Name = "Ajouter", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });
    }

    /// <summary>The Monday the board is showing, which is the week the roster form defaults into.</summary>
    private static string MondayOfThisWeek()
    {
        var today = DateTime.UtcNow.Date;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        return monday.ToString("yyyy-MM-dd");
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
