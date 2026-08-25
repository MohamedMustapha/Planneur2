using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// S10's acceptance journey: configure a connection, pull it, and find the other system's work where people use it.
/// </summary>
/// <remarks>
/// The slice's acceptance criteria come down to one sentence: items mirror read-only, mapped to local projects and
/// units, and the resulting feeds power S5's dropdown and S6a's pool. That is what these run — through the real
/// BFF, the real policies, real RLS, and a stub standing in for Azure DevOps and ServiceNow at the far end.
///
/// The stub is the only thing here that is not production code. Everything between the browser and it — the
/// connection form, the 202, the pull, the mapping, the mirror, both feeds — is exactly what a deployment runs.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class IntegrationJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    private static readonly Dictionary<string, string> AntiForgery = new() { ["X-Cracra-Csrf"] = "1" };

    private const string Camille = "c0000000-0000-0000-0000-000000000001";
    private const string InformationSystems = "11111111-1111-1111-1111-111111111111";
    private const string DeveloperRole = "f0000000-0000-0000-0000-000000000001";

    [Fact]
    public async Task A_department_head_configures_a_devops_connection_from_the_settings_screen()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/settings/integrations");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Intégrations", new() { Timeout = TimeoutMs });

        // Said once, at the top of the screen, because it is the thing to understand about it.
        await Expect(page.Locator(".integrations__note")).ToContainTextAsync("ne leur écrit jamais");

        var name = $"SI — DevOps {Guid.CreateVersion7().ToString("N")[^6..]}";

        await page.GetByLabel("Système").First.SelectOptionAsync("azure-devops");
        await page.GetByLabel("Libellé").FillAsync(name);
        await page.GetByLabel("URL de base").FillAsync(stack.ProvidersBaseUrl);
        await page.GetByLabel("Projet d'équipe").FillAsync("CRACRA");
        await page.GetByLabel("Sprint courant").FillAsync(ExternalConnections.CurrentSprint);
        await page.GetByLabel("Référence du secret").FillAsync("dev-devops");
        await page.GetByLabel("Intervalle (minutes)").FillAsync("0");

        await page.GetByRole(AriaRole.Button, new() { Name = "Créer la connexion" }).ClickAsync();

        var card = page.Locator(".integrations__connection", new() { HasTextString = name });

        await Expect(card).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // A connection nobody has pulled yet reads "never run" rather than "failed". They are different facts, and
        // showing the second for the first would have every new connection looking broken.
        await Expect(card).ToContainTextAsync("Jamais exécutée");

        // The secret reference is on the card; the secret is not, and could not be — it is not in the database.
        await Expect(card).ToContainTextAsync("dev-devops");
    }

    [Fact]
    public async Task Pulling_mirrors_the_sprint_and_shows_it_read_only()
    {
        var projectId = await ProjectWithCamilleAsync();
        var head = await stack.SignInAsync("olivier.marchand");

        var connection = await ExternalConnections.DevOpsAsync(stack, head, projectId);

        await head.GotoAsync("/settings/integrations");

        var card = head.Locator(".integrations__connection", new() { HasTextString = connection.Name });

        await Expect(card).ToContainTextAsync("Réussie", new() { Timeout = TimeoutMs });

        await card.GetByRole(AriaRole.Button, new() { Name = "Voir les éléments recopiés" }).ClickAsync();

        // What came back is the other system's work, in the source's own words, with a link back to the record —
        // which is the whole of the write path, deliberately.
        await Expect(card.GetByText("Migrer le socle", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await Expect(card.GetByRole(AriaRole.Link, new() { Name = "AB#4301" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_pulled_sprint_task_reaches_a_developers_dropdown()
    {
        var projectId = await ProjectWithCamilleAsync();
        var head = await stack.SignInAsync("olivier.marchand");

        await ExternalConnections.DevOpsAsync(stack, head, projectId);

        // Camille is a plain member. She never sees the connection — the config is the head's — and she does see
        // what it mirrored, because the mirror answers to the visibility matrix rather than to who configured it.
        var developer = await stack.SignInAsync("camille.villeneuve");

        await developer.GotoAsync("/board");

        await developer.GetByRole(AriaRole.Button, new() { Name = "Ajout rapide" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var dialog = developer.GetByRole(AriaRole.Dialog);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Importer une tâche assignée" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Migrer le socle", Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_member_sees_no_connection_on_the_screen_at_all()
    {
        var projectId = await ProjectWithCamilleAsync();
        var head = await stack.SignInAsync("olivier.marchand");

        await ExternalConnections.DevOpsAsync(stack, head, projectId);

        var member = await stack.SignInAsync("camille.villeneuve");

        await member.GotoAsync("/settings/integrations");

        // The route renders — the client never gates on a role for anything that matters — and the list behind it
        // is refused by the policy. What she gets is a screen with nothing in it rather than her department's
        // supplier, its queue and the name of the secret it authenticates with.
        await Expect(member.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Intégrations", new() { Timeout = TimeoutMs });

        await Expect(member.Locator(".integrations__connection")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Pulled_tickets_fill_the_run_work_order_pool()
    {
        var head = await stack.SignInAsync("olivier.marchand");

        await ExternalConnections.ServiceNowAsync(stack, head);

        // The department has to be on the work-order layout for its node board to be 6a — that is what
        // default_board_layout is for, and why a helpdesk and a dev team can share one platform.
        //
        // Arranged last, immediately before the board is opened: the row is shared with every other journey that
        // sets a layout, so the further this sits from the assertion the more chance another has to put it back.
        await ConfigureWorkOrderBoardAsync();

        var lead = await stack.SignInAsync("thomas.berthier");

        await lead.GotoAsync("/node");

        // Asserted before it is clicked, so a board that rendered the wrong archetype reports what it did render
        // rather than a bare click timeout.
        await Expect(lead.GetByRole(AriaRole.Button, new() { Name = "Importer depuis ServiceNow" }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await lead.GetByRole(AriaRole.Button, new() { Name = "Importer depuis ServiceNow" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // The queue's unassigned tickets, in the pool a lead drags from.
        await Expect(lead.Locator(".pool__card", new() { HasTextString = "Poste bloqué au démarrage" }).First)
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // And not the one the stub reports as already assigned: work somebody has picked up is not free work, and
        // a pool that offered it would have two people on one incident.
        await Expect(lead.Locator(".pool__card", new() { HasTextString = "Accès VPN pour un prestataire" }))
            .ToHaveCountAsync(0);
    }

    /// <summary>Puts the IS department on the work-order board layout, as its head.</summary>
    private async Task ConfigureWorkOrderBoardAsync()
    {
        var head = await stack.SignInAsync("olivier.marchand");

        var response = await head.APIRequest.PutAsync(
            $"/api/directory/departments/{InformationSystems}/config",
            new APIRequestContextOptions
            {
                Headers = AntiForgery,
                DataObject = new Dictionary<string, object?>
                {
                    ["activityTaxonomyJson"] = "{}",
                    ["roleLabelsJson"] = "{}",
                    ["kudoRulesJson"] = "{}",
                    ["defaultBoardLayout"] = "work-orders",
                    ["iterationPresetsJson"] = """["1w","2w","1m"]""",
                    ["weeklyTargetHours"] = 35,
                    ["enforceWeeklyTarget"] = false,
                    ["shiftTemplatesJson"] = "{}",
                },
            });

        response.Status.ShouldBe(200);
    }

    /// <summary>Creates a project with Camille on it, as the head who is allowed to.</summary>
    private async Task<string> ProjectWithCamilleAsync()
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
                ["costAmount"] = 0,
                ["costCurrency"] = "EUR",
                ["leadDepartmentId"] = InformationSystems,
                ["contributingDepartmentIds"] = Array.Empty<string>(),
            },
        });

        created.Status.ShouldBe(201);

        var projectId = (await created.JsonAsync())!.Value.GetProperty("id").GetString()!;

        var added = await head.APIRequest.PostAsync(
            $"/api/projects/{projectId}/members",
            new APIRequestContextOptions
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

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
