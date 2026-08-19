using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// S5's acceptance journey: a developer logs an hour by hand, then pulls a sprint task and logs against it.
/// </summary>
/// <remarks>
/// Logging your week is the one thing every single person on the platform does, several times a day. This drives
/// it the way they will: open the board, press the button, fill the form, watch the meter move.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class ActivityJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    [Fact]
    public async Task The_board_shows_a_real_weekly_target()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/board");

        // The target comes from the department's own configuration now, not from a constant in the client — which
        // is exactly why the number is not asserted here: another journey in this shared stack may have changed
        // it, and a test that breaks when the data it does not own changes is testing the wrong thing. That the
        // configured value is honoured is pinned in the integration suite, where the config is controlled.
        await Expect(page.GetByText(new System.Text.RegularExpressions.Regex(@"objectif \d+ h")))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // What this level can prove is that the meter is live rather than the S0 placeholder that always read
        // zero against a hard-coded 35.
        await Expect(page.GetByText(new System.Text.RegularExpressions.Regex(@"\d+ / \d+ h")))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_developer_logs_an_hour_by_hand_and_the_week_moves()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/board");

        await page.GetByRole(AriaRole.Button, new() { Name = "Ajout rapide" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var dialog = page.GetByRole(AriaRole.Dialog);

        // Quality-of-life needs no project, which is the point of it: not all work belongs to one.
        await dialog.GetByLabel("Type d'activité").SelectOptionAsync(new SelectOptionValue { Value = "quality-of-life" });
        await dialog.GetByLabel("Heures").FillAsync("2");
        await dialog.GetByLabel("Note").FillAsync("Revue de la documentation d'exploitation");

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Enregistrer", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // The entry lands in the week's list. First, because the Aspire stack persists between runs and an
        // earlier run's identical note is still sitting there — which is fine: what matters is that this one
        // arrived, not that it is the only one.
        await Expect(page.GetByText("Revue de la documentation d'exploitation").First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task Project_work_asks_for_a_project_and_other_work_does_not()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/board");

        await page.GetByRole(AriaRole.Button, new() { Name = "Ajout rapide" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var dialog = page.GetByRole(AriaRole.Dialog);

        // Counted rather than matched by label: "Projet — BUILD" is an option inside the type picker, so both a
        // substring match and a whole-label regex find the wrong element. The form has exactly one select until
        // the project one appears, which is the thing being asserted anyway.
        var selects = dialog.Locator("select");

        await dialog.GetByLabel("Type d'activité").SelectOptionAsync(new SelectOptionValue { Value = "quality-of-life" });

        // Quality-of-life work belongs to nobody's project, so the field is not merely optional — it is absent.
        await Expect(selects).ToHaveCountAsync(1, new() { Timeout = TimeoutMs });

        // The field appears because the taxonomy says this type requires one — the client reads the rule rather
        // than hard-coding which types are project work.
        await dialog.GetByLabel("Type d'activité").SelectOptionAsync(new SelectOptionValue { Value = "project-build" });
        await Expect(selects).ToHaveCountAsync(2, new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_developer_pulls_a_sprint_task_and_logs_against_it()
    {
        // The dropdown only offers tasks on projects the person is actually on, so the journey has to put them on
        // one first. Arranged here rather than relied upon: another journey in this shared stack may or may not
        // have staffed Camille, and a test that passes only in a particular order is not evidence of anything.
        var projectId = await ProjectWithCamilleAsync();

        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/board");

        await page.GetByRole(AriaRole.Button, new() { Name = "Ajout rapide" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var dialog = page.GetByRole(AriaRole.Dialog);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Importer une tâche assignée" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var task = dialog.GetByRole(AriaRole.Button, new() { Name = "Sprint task", Exact = false }).First;

        await Expect(task).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await task.ClickAsync();

        // Picking pre-fills rather than submits: the project, the type and the reference come from the task, and
        // the one thing the external system cannot know — how long it took — is left for the person.
        await Expect(dialog.GetByText("Lié à", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // The project select appeared alongside the type one, pre-filled from the task.
        await Expect(dialog.Locator("select")).ToHaveCountAsync(2, new() { Timeout = TimeoutMs });

        await dialog.GetByLabel("Heures").FillAsync("3");
        await dialog.GetByLabel("Heure de début").FillAsync("14:00");

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Enregistrer", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // The entry keeps its reference back to the work item, which is what makes a pulled hour auditable rather
        // than merely convenient to type.
        await Expect(page.GetByText(new System.Text.RegularExpressions.Regex(@"AB-\d+")).First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    /// <summary>
    /// Creates a project with Camille on it, as the head who is allowed to.
    /// </summary>
    /// <remarks>
    /// Through the API on the head's own signed-in session rather than against the database, so the arrangement
    /// goes through the same BFF, the same policies and the same RLS a real head would. A precondition set up
    /// behind the application's back can arrange states the application would never allow.
    /// </remarks>
    private async Task<string> ProjectWithCamilleAsync()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        var code = $"PRJ-{Guid.CreateVersion7().ToString("N")[^8..]}";

        var created = await page.APIRequest.PostAsync("/api/projects", new APIRequestContextOptions
        {
            // The BFF refuses a proxied /api call without this. The session is a cookie, which a cross-site
            // request would send automatically but could not add a header to — so the header is what makes the
            // cookie safe to use, and arranging state has to play by the same rule.
            Headers = AntiForgery,
            DataObject = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["name"] = "Sprint host",
                ["classification"] = "build",
                ["costAmount"] = 0,
                ["costCurrency"] = "EUR",
                ["leadDepartmentId"] = InformationSystems,
                ["contributingDepartmentIds"] = Array.Empty<string>(),
            },
        });

        created.Status.ShouldBe(201);

        var projectId = (await created.JsonAsync())!.Value.GetProperty("id").GetString()!;

        var added = await page.APIRequest.PostAsync($"/api/projects/{projectId}/members", new APIRequestContextOptions
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

        return projectId;
    }

    private static readonly Dictionary<string, string> AntiForgery = new() { ["X-Cracra-Csrf"] = "1" };

    private const string Camille = "c0000000-0000-0000-0000-000000000001";
    private const string InformationSystems = "11111111-1111-1111-1111-111111111111";
    private const string DeveloperRole = "f0000000-0000-0000-0000-000000000001";

    [Fact]
    public async Task The_activity_type_picker_is_driven_by_the_department_taxonomy()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        await page.GotoAsync("/board");

        await page.GetByRole(AriaRole.Button, new() { Name = "Ajout rapide" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var picker = page.GetByRole(AriaRole.Dialog).GetByLabel("Type d'activité");

        // The four canonical buckets every department inherits, localized. A department that adds subtypes gets
        // them here too, without a deploy.
        foreach (var label in new[] { "Projet — BUILD", "Projet — RUN", "Qualité de vie", "Recrutement & administratif" })
        {
            await Expect(picker.GetByRole(AriaRole.Option, new() { Name = label }))
                .ToBeAttachedAsync(new() { Timeout = TimeoutMs });
        }
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
