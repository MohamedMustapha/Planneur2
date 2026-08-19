using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// S4's acceptance journey: propose a candidate, commit to it, and watch it move across the board.
/// </summary>
/// <remarks>
/// The lifecycle guards are unit-tested and the RLS is integration-tested. What only this can show is that the
/// board a real person opens, after a real login, renders the lanes the server sends and that a transition moves
/// the card — through the BFF, the API, RLS and back into the DOM.
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class PortfolioJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    [Fact]
    public async Task A_head_sees_the_four_lanes()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/portfolio");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Portefeuille", new() { Timeout = TimeoutMs });

        // Every lane, including the empty ones — the considered lane is where candidates get dropped, so a board
        // that hides it because nothing is in it is a broken board.
        // Matched case-insensitively: the lane headings are uppercased in CSS, not in the DOM.
        foreach (var lane in new[] { "Envisagé", "Engagé", "Actif", "Déphasé" })
        {
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = lane, Level = 2 }))
                .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
        }
    }

    [Fact]
    public async Task A_head_proposes_a_candidate_and_it_appears_in_the_considered_lane()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/portfolio");

        await page.GetByRole(AriaRole.Button, new() { Name = "Proposer un candidat" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var name = $"Refonte du portail {Guid.CreateVersion7():N}"[..40];

        var dialog = page.GetByRole(AriaRole.Dialog);

        await dialog.GetByLabel("Nom du candidat").FillAsync(name);
        await dialog.GetByLabel("Notes").FillAsync("Remonté au comité de pilotage.");

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Proposer un candidat" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Button, new() { Name = name }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_plain_member_is_not_offered_the_propose_control()
    {
        var page = await stack.SignInAsync("mehdi.sadaoui");

        await page.GotoAsync("/portfolio");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Portefeuille", new() { Timeout = TimeoutMs });

        // Hidden as a courtesy; the endpoint policy is what actually refuses him, and the integration suite proves
        // that. Showing a button the server will reject is just a worse way to say no.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Proposer un candidat" }))
            .ToBeHiddenAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task The_portfolio_nav_entry_no_longer_shows_a_placeholder()
    {
        var page = await stack.SignInAsync("camille.villeneuve");

        var rail = page.GetByRole(AriaRole.Navigation);

        await rail.GetByRole(AriaRole.Link, new() { Name = "Portefeuille" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Portefeuille", new() { Timeout = TimeoutMs });

        // The placeholder said "S4"; the real screen says nothing of the sort.
        await Expect(page.GetByText("S4", new() { Exact = true }))
            .ToBeHiddenAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task The_full_lifecycle_runs_from_candidate_to_archived()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/portfolio");

        var name = $"Socle {Guid.CreateVersion7():N}"[..24];

        // --- Register the candidate ------------------------------------------------------------------------
        await page.GetByRole(AriaRole.Button, new() { Name = "Proposer un candidat" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var composer = page.GetByRole(AriaRole.Dialog);
        await composer.GetByLabel("Nom du candidat").FillAsync(name);
        await composer.GetByLabel("Notes").FillAsync("Remonté au comité de pilotage.");
        await composer.GetByRole(AriaRole.Button, new() { Name = "Proposer un candidat" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        ILocator card = page.Locator("article.item-card").Filter(new() { HasText = name });

        await Expect(card).ToBeVisibleAsync(new() { Timeout = TimeoutMs });
        await Expect(page.Locator(".lane--considered").Filter(new() { HasText = name }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // --- Commit: a project is provisioned ---------------------------------------------------------------
        await card.GetByRole(AriaRole.Button, new() { Name = "Engager" }).ClickAsync(new() { Timeout = TimeoutMs });

        var decision = page.GetByRole(AriaRole.Dialog);
        await decision.GetByLabel("Code du projet").FillAsync($"PRJ-{Guid.CreateVersion7():N}"[..12]);
        await decision.GetByLabel("Motif de la décision").FillAsync("Budget validé en comité.");
        await decision.GetByRole(AriaRole.Button, new() { Name = "Engager" }).ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.Locator(".lane--committed").Filter(new() { HasText = name }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // --- Staff the project ------------------------------------------------------------------------------
        // Activation refuses without a team, and only Projects can answer whether there is one. So the journey
        // goes through the project screen the commitment just created, which is what the S3/S4 seam actually is.
        await card.GetByRole(AriaRole.Link, new() { Name = "Ouvrir le projet" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var personSelect = page.GetByLabel("Personne");

        await Expect(personSelect).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await personSelect.SelectOptionAsync(new SelectOptionValue { Label = "Camille Villeneuve" });
        await page.GetByLabel("Fonction").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Ajouter", Exact = true })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // Confirmed here rather than inferred from the activation succeeding: if staffing quietly failed, the
        // activation refusal further down would look like a broken guard instead of a broken setup step.
        await Expect(page.Locator(".project__member-name").Filter(new() { HasText = "Camille Villeneuve" }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await page.GotoAsync("/portfolio");

        card = page.Locator("article.item-card").Filter(new() { HasText = name });

        await Expect(card).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // --- Plan a two-week iteration ----------------------------------------------------------------------
        await card.GetByRole(AriaRole.Button, new() { Name = "Ajouter une itération" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var planner = page.GetByRole(AriaRole.Dialog);
        await planner.GetByLabel("Nom de l'itération").FillAsync("Sprint 1");
        await planner.GetByRole(AriaRole.Button, new() { Name = "2 sem." }).ClickAsync();
        await planner.GetByLabel("Début").FillAsync("2026-08-31");

        // The preset pre-fills the end date before anything is sent — fourteen days inclusive, the same answer the
        // server will compute.
        await Expect(planner.GetByText("2026-09-13")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await planner.GetByRole(AriaRole.Button, new() { Name = "Ajouter une itération" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        // --- Activate ---------------------------------------------------------------------------------------
        await card.GetByRole(AriaRole.Button, new() { Name = "Démarrer" }).ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.Locator(".lane--active").Filter(new() { HasText = name }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // Activation started the first planned iteration, so the strip on the card is running.
        await Expect(card.Locator(".iteration.is-active")).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // --- Archive ----------------------------------------------------------------------------------------
        await card.GetByRole(AriaRole.Button, new() { Name = "Déphaser" }).ClickAsync(new() { Timeout = TimeoutMs });

        var archive = page.GetByRole(AriaRole.Dialog);
        await archive.GetByLabel("Motif de la décision").FillAsync("Remplacé par le socle groupe.");
        await archive.GetByRole(AriaRole.Button, new() { Name = "Déphaser" }).ClickAsync(new() { Timeout = TimeoutMs });

        await Expect(page.Locator(".lane--dephase").Filter(new() { HasText = name }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // Read-only from here: no transition, no planning, nothing to close. This is the same freeze S6 applies to
        // its boards, and the card must not offer what the server would refuse.
        await Expect(card.GetByRole(AriaRole.Button, new() { Name = "Ajouter une itération" }))
            .ToBeHiddenAsync(new() { Timeout = TimeoutMs });
        await Expect(card.GetByRole(AriaRole.Button, new() { Name = "Clôturer l'itération" }))
            .ToBeHiddenAsync(new() { Timeout = TimeoutMs });
        await Expect(card.GetByRole(AriaRole.Button, new() { Name = "Déphaser" }))
            .ToBeHiddenAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task The_decision_history_records_every_move()
    {
        var page = await stack.SignInAsync("olivier.marchand");

        await page.GotoAsync("/portfolio");

        var name = $"Trace {Guid.CreateVersion7():N}"[..24];

        await page.GetByRole(AriaRole.Button, new() { Name = "Proposer un candidat" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        var composer = page.GetByRole(AriaRole.Dialog);
        await composer.GetByLabel("Nom du candidat").FillAsync(name);
        await composer.GetByLabel("Notes").FillAsync("Demande de la direction financière.");
        await composer.GetByRole(AriaRole.Button, new() { Name = "Proposer un candidat" })
            .ClickAsync(new() { Timeout = TimeoutMs });

        await page.GetByRole(AriaRole.Button, new() { Name = name }).ClickAsync(new() { Timeout = TimeoutMs });

        // The reason travels with the transition, all the way to the panel someone opens a year later asking why.
        await Expect(page.GetByText("Demande de la direction financière."))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
