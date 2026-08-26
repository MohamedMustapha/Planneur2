using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// v2 §10's acceptance journey: two sibling branches, one department, different tools.
/// </summary>
/// <remarks>
/// <para>
/// What this suite proves that the integration tests cannot is that the resolved profile reaches the screen. The
/// server can answer "this branch has no integrations" perfectly and still render the tab, because the hiding
/// happens in a component the API never sees. §10.3 is explicit that a capability which is off means the control
/// is <em>absent</em> — that is a statement about the DOM, so it is asserted against the DOM.
/// </para>
/// <para>
/// Études &amp; Développement and Support &amp; Assistance are the pair. Both sit directly under DSI, so nothing
/// that distinguishes them below can be coming from their department — which is the entire claim of the slice and
/// the reason the dev seeder attaches those two units in particular.
/// </para>
/// <para>
/// The attachments come from the dev seeder rather than from a fixture here, deliberately. A test that arranged
/// its own profiles would prove the API works and prove nothing about whether a person who logs into the dev box
/// meets a coherent product.
/// </para>
/// </remarks>
[Collection(AspireStackCollection.Name)]
public sealed class NodeProfileJourneyTests(AspireStackFixture stack)
{
    private const int TimeoutMs = 60_000;

    // Études & Développement — the seeder attaches DELIVERY. Julie is a plain member of it.
    private const string DeliveryMember = "julie.ondracek";

    // Support & Assistance — DISPATCH. Fatou is a plain member of it.
    private const string DispatchMember = "fatou.diallo";

    // Transformation Digitale — ADVISORY, which switches integrations and budget off. Nadia is the PMO, so she is
    // the one person who can reach every administration screen and therefore the one who can show a tab missing
    // for a reason other than her role.
    private const string AdvisoryPmo = "nadia.kessler";

    // Études & Développement's head. Same administration rights as any head, DELIVERY's capabilities.
    private const string DeliveryHead = "karim.benali";

    [Fact]
    public async Task Each_branch_is_offered_its_own_activity_subtypes()
    {
        // The heart of §10.2. Both people open the same dialog on the same screen of the same deployment, and the
        // list of things they may say they did is different — because their branches do different work, not
        // because anyone wrote code about either of them.
        var delivery = await OpenLogDialogAsync(DeliveryMember);

        var deliveryTypes = await TypeOptionsAsync(delivery);

        deliveryTypes.ShouldContain("Développement");
        deliveryTypes.ShouldContain("Architecture");
        deliveryTypes.ShouldNotContain("Tri");
        deliveryTypes.ShouldNotContain("Intervention");

        var dispatch = await OpenLogDialogAsync(DispatchMember);

        var dispatchTypes = await TypeOptionsAsync(dispatch);

        dispatchTypes.ShouldContain("Tri");
        dispatchTypes.ShouldContain("Intervention");
        dispatchTypes.ShouldNotContain("Architecture");
    }

    [Fact]
    public async Task Both_branches_still_share_the_four_universal_buckets()
    {
        // The other half of §10.2, and the reason the top layer is fixed platform-wide: an L1 head's rollup
        // stacks a delivery branch's work and a dispatch branch's under the same four headings. Take these away
        // and the two lists above become incomparable, which is a worse problem than them being identical.
        foreach (var person in new[] { DeliveryMember, DispatchMember })
        {
            var page = await OpenLogDialogAsync(person);

            var types = await TypeOptionsAsync(page);

            types.ShouldContain("Projet — BUILD", $"{person} lost a universal bucket.");
            types.ShouldContain("Projet — RUN", $"{person} lost a universal bucket.");
            types.ShouldContain("Qualité de vie", $"{person} lost a universal bucket.");
            types.ShouldContain("Recrutement & administratif", $"{person} lost a universal bucket.");
        }
    }

    [Fact]
    public async Task A_branch_without_integrations_does_not_render_the_tab_at_all()
    {
        // Absent, not disabled (§10.3). Nadia is the PMO — there is no administration screen she may not open —
        // so a missing tab here can only be her branch's profile saying her work does not involve integrations.
        var page = await stack.SignInAsync(AdvisoryPmo);

        // /settings/profiles rather than /settings: the department-settings screen does not carry the tab strip,
        // so asserting a missing tab there would assert nothing at all.
        await page.GotoAsync("/settings/profiles");

        // Wait for the tab strip itself, so "no integrations tab" is a real absence rather than a screen that has
        // not painted yet. Without this the test would pass against a settings page that failed to render.
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Profils", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Intégrations", Exact = true }))
            .ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_branch_with_integrations_still_renders_the_tab()
    {
        // The positive half. Without it the test above passes just as well against a tab somebody deleted.
        var page = await stack.SignInAsync(DeliveryHead);

        await page.GotoAsync("/settings/profiles");

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Profils", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Intégrations", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task A_branch_that_carries_no_budget_loses_the_finance_entry_despite_the_role()
    {
        // Role and capability are ANDed, and this is the case that shows why. Nadia holds PMO, which is exactly
        // the role the visibility matrix says may see capex/opex — and she still does not get the entry, because
        // her branch has no budget for her to look at. Entitlement does not conjure one.
        var page = await stack.SignInAsync(AdvisoryPmo);

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // The rail is present and populated — otherwise this asserts nothing. A PMO lands on the portfolio, so
        // that is the entry that proves the rail drew.
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Portefeuille" }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Finance" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task An_administrator_can_author_a_profile_with_invented_words_and_attach_it()
    {
        // §10's configurability claim, driven the way an administrator would drive it: through the screen, with
        // vocabulary no line of code has heard of, with no deployment and no migration in between.
        var page = await stack.SignInAsync(AdvisoryPmo);

        await page.GotoAsync("/settings/profiles");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Profils", new() { Timeout = TimeoutMs });

        await page.GetByRole(AriaRole.Button, new() { Name = "Nouveau", Exact = true }).ClickAsync();

        // A code no seed ships and subtypes drawn from a trade the platform has never modelled.
        var code = $"E2E-CASEWORK-{DateTime.UtcNow:HHmmssfff}";

        await page.GetByLabel("Code").FillAsync(code);
        await page.GetByLabel("Clé de libellé").FillAsync("profile.e2e-casework");
        await page.GetByLabel("Plannings (séparés par des virgules)").FillAsync("week-grid");
        await page.GetByLabel("Taxonomie d'activités (JSON)").FillAsync(
            """
            {"types":[
              {"code":"eligibility-check","parent":"project-run","labelKey":"Contrôle d'éligibilité"},
              {"code":"payment-run","parent":"project-run","labelKey":"Ordonnancement"}
            ]}
            """);

        await page.GetByRole(AriaRole.Button, new() { Name = "Enregistrer", Exact = true }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync(
            "Profil enregistré",
            new() { Timeout = TimeoutMs });

        // And it is immediately attachable — the picker is fed from the same list, so a profile that saved but
        // could not be attached would show up here rather than in production.
        await Expect(page.GetByRole(AriaRole.Option, new() { Name = code, Exact = true }))
            .ToHaveCountAsync(1, new() { Timeout = TimeoutMs });
    }

    [Fact]
    public async Task Attaching_a_profile_changes_what_the_branch_sees_without_a_deploy()
    {
        // End to end in the strongest sense available: an administrator attaches a profile through the UI, and a
        // different person's screen changes. Nothing is restarted in between.
        var page = await stack.SignInAsync(AdvisoryPmo);

        await page.GotoAsync("/settings/profiles");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Profils", new() { Timeout = TimeoutMs });

        // Nadia's own unit runs ADVISORY, which hides integrations. Attaching DELIVERY to it should give the tab
        // back on her next load — the same control, appearing for the same reason it disappeared.
        await page.Locator("select[name=attachUnit]").SelectOptionAsync(
            new SelectOptionValue { Label = "Transformation Digitale" });
        await page.Locator("select[name=attachProfile]").SelectOptionAsync(
            new SelectOptionValue { Label = "DELIVERY" });

        await page.GetByRole(AriaRole.Button, new() { Name = "Rattacher", Exact = true }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync(
            "Profil enregistré",
            new() { Timeout = TimeoutMs });

        await page.ReloadAsync();

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Intégrations", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // Put it back, so the ordering of this class's tests cannot decide whether the others pass. The stack is
        // shared and the attachment is durable, which makes this cleanup part of the test rather than politeness.
        await page.GotoAsync("/settings/profiles");
        await page.Locator("select[name=attachUnit]").SelectOptionAsync(
            new SelectOptionValue { Label = "Transformation Digitale" });
        await page.Locator("select[name=attachProfile]").SelectOptionAsync(
            new SelectOptionValue { Label = "ADVISORY" });
        await page.GetByRole(AriaRole.Button, new() { Name = "Rattacher", Exact = true }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync(
            "Profil enregistré",
            new() { Timeout = TimeoutMs });
    }

    // --- Helpers -----------------------------------------------------------------------------------------------

    /// <summary>Signs in and opens the board's log dialog, which is where the branch's vocabulary is offered.</summary>
    private async Task<IPage> OpenLogDialogAsync(string username)
    {
        var page = await stack.SignInAsync(username);

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        await page.GetByRole(AriaRole.Button, new() { Name = "Ajout rapide" }).First.ClickAsync();

        await Expect(page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        return page;
    }

    // Each journey class declares these locally, as the rest of this suite does: Playwright's assertions are
    // static, and a local alias keeps the call sites reading as `Expect(locator)` rather than as `Assertions.`.
    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    /// <summary>The labels the type picker actually offers, which is what a person chooses from.</summary>
    private static async Task<IReadOnlyList<string>> TypeOptionsAsync(IPage page)
    {
        var select = page.GetByRole(AriaRole.Dialog).GetByLabel("Type d'activité");

        await Expect(select).ToBeVisibleAsync(new() { Timeout = TimeoutMs });

        // The taxonomy arrives on its own request, so a select holding nothing but its placeholder is a dialog
        // that opened first rather than a branch with no subtypes.
        await Expect(select.Locator("option")).Not.ToHaveCountAsync(1, new() { Timeout = TimeoutMs });

        var options = await select.Locator("option").AllTextContentsAsync();

        return [.. options.Select(option => option.Trim())];
    }
}
