using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Ai;
using Cracra.BuildingBlocks.Storage;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Reporting.Contracts;
using Cracra.Tests.Integration.Directory;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Reporting;

/// <summary>
/// S8 through HTTP, with real RLS and the real on-prem client pointed at the stub.
/// </summary>
/// <remarks>
/// The claims worth testing are the two the slice rests on: that a report never exceeds the visibility its viewer
/// already had, and that its numbers are computed in code rather than by the model. Everything else — sections,
/// labels, export plumbing — follows from those two being true.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ReportTests(PostgresFixture postgres) : IAsyncDisposable
{
    private static readonly DateOnly Monday = new(2026, 8, 17);
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The stub, hosted in-process and shared by every test in the class.</summary>
    private readonly WebApplicationFactory<Cracra.Tools.LlmStub.LlmStubEntryPoint> llm = new();

    public async ValueTask DisposeAsync()
    {
        await llm.DisposeAsync();

        GC.SuppressFinalize(this);
    }

    // --- Scope resolution ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_member_lands_on_the_widest_scope_their_role_grants()
    {
        await using var factory = await SeededAsync();

        var report = await ReportAsync(factory, SeedOrganisation.Camille);

        // Their own branch, which is where "what is my team doing" is answered.
        report.Scope.ShouldBe(ReportScopes.Node);
        report.AvailableScopes.ShouldBe([ReportScopes.Node, ReportScopes.Me]);
    }

    [Fact]
    public async Task A_head_lands_on_their_branch()
    {
        await using var factory = await SeededAsync();

        var report = await ReportAsync(factory, SeedOrganisation.Olivier);

        report.Scope.ShouldBe(ReportScopes.Node);
        report.AvailableScopes[0].ShouldBe(ReportScopes.Node);
    }

    [Fact]
    public async Task The_PMO_lands_on_the_portfolio()
    {
        await using var factory = await SeededAsync();

        (await ReportAsync(factory, SeedOrganisation.Nadia)).Scope.ShouldBe(ReportScopes.Portfolio);
    }

    [Fact]
    public async Task A_member_asking_for_the_portfolio_scope_is_refused()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient()
            .GetAsync("/api/reports?scope=portfolio", TestContext.Current.CancellationToken);

        // 403 rather than an empty portfolio. An empty one would tell Camille the organisation runs nothing,
        // which is a lie; a silent narrowing would answer a different question than the one she asked.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_bookmarked_level_named_scope_says_it_is_gone()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient()
            .GetAsync("/api/reports?scope=department", TestContext.Current.CancellationToken);

        // Not a 403: a head is not being refused a scope they lack, they are asking for one that no longer
        // exists. Sending them to look for a permission to fix would be the wrong signpost.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_scope_that_does_not_exist_is_a_bad_request_rather_than_a_refusal()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Nadia);

        var response = await factory.CreateClient()
            .GetAsync("/api/reports?scope=everything", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // --- The numbers -----------------------------------------------------------------------------------------

    [Fact]
    public async Task My_report_counts_the_hours_I_logged_and_nobody_elses()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 4);
        await LogAsync(factory, SeedOrganisation.Mehdi, "quality-of-life", 7);

        var report = await ReportAsync(factory, SeedOrganisation.Camille, ReportScopes.Me);

        var hours = report.Sections.Single(section => section.Key == "hours");

        hours.Metrics.Single(metric => metric.Key == "actualHours").Value.ShouldBe(4m);
        hours.Metrics.Single(metric => metric.Key == "targetHours").Value.ShouldBe(35m);
    }

    [Fact]
    public async Task Planned_hours_are_reported_apart_from_actual_ones()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 4);
        await LogAsync(factory, SeedOrganisation.Camille, "recruitment-admin", 3, kind: "planned",
            start: MondayMorning.AddDays(1));

        var hours = (await ReportAsync(factory, SeedOrganisation.Camille, ReportScopes.Me))
            .Sections.Single(section => section.Key == "hours");

        // A plan is an intention. Counting it as time worked would make every forward-looking week look complete.
        hours.Metrics.Single(metric => metric.Key == "actualHours").Value.ShouldBe(4m);
        hours.Metrics.Single(metric => metric.Key == "plannedHours").Value.ShouldBe(3m);
    }

    [Fact]
    public async Task Going_over_the_weekly_target_is_stated_as_a_note()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 8);
        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 8, start: MondayMorning.AddDays(1));
        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 8, start: MondayMorning.AddDays(2));
        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 8, start: MondayMorning.AddDays(3));
        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 6, start: MondayMorning.AddDays(4));

        var hours = (await ReportAsync(factory, SeedOrganisation.Camille, ReportScopes.Me))
            .Sections.Single(section => section.Key == "hours");

        hours.Metrics.Single(metric => metric.Key == "overtimeHours").Value.ShouldBe(3m);
        hours.Notes.ShouldContain(note => note.Key == "reports.note.overTarget");
    }

    [Fact]
    public async Task A_monthly_report_scales_the_weekly_target_rather_than_comparing_against_one_week()
    {
        await using var factory = await SeededAsync();

        var report = await ReportAsync(
            factory, SeedOrganisation.Camille, ReportScopes.Me, period: "month", from: Monday);

        var target = report.Sections.Single(section => section.Key == "hours")
            .Metrics.Single(metric => metric.Key == "targetHours").Value;

        // August has 31 days. Reporting 150 hours as "115 over target" would be arithmetic nobody trusts again.
        target.ShouldBeGreaterThan(140m);
        report.Period.Kind.ShouldBe(ReportPeriods.Month);
    }

    [Fact]
    public async Task A_leaf_branch_reports_a_row_per_member_and_its_RUN_load()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 4);

        var report = await ReportAsync(
            factory, SeedOrganisation.Thomas, ReportScopes.Node, SeedOrganisation.Units.Infrastructure);

        var members = report.Sections.Single(section => section.Key == "members");

        members.Tables[0].IdentifiesPeople.ShouldBeTrue();
        members.Tables[0].Rows.ShouldContain(row => row.Label == "Camille Villeneuve");

        report.Sections.ShouldContain(section => section.Key == "runLoad");
    }

    [Fact]
    public async Task A_branch_with_children_reports_a_row_per_child_rather_than_per_person()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 4);

        var report = await ReportAsync(
            factory,
            SeedOrganisation.Olivier,
            ReportScopes.Node,
            SeedOrganisation.Departments.InformationSystems);

        var members = report.Sections.Single(section => section.Key == "members");

        // The same section key and the same shape at both depths, which is what lets a head hand their report
        // upward and have it slot into their parent's as one block (v2 §01.4).
        members.Tables[0].IdentifiesPeople.ShouldBeFalse();
        members.Tables[0].Rows.ShouldContain(row => row.Label == "Infrastructure & Réseaux");
    }

    [Fact]
    public async Task A_branch_report_nobody_scoped_is_the_callers_own()
    {
        await using var factory = await SeededAsync();

        var report = await ReportAsync(factory, SeedOrganisation.Camille, ReportScopes.Node);

        // No scopeId at all: the server resolves where the caller hangs off the tree, which is the only answer
        // the client could have supplied and one it would have had to ask for first.
        report.ScopeLabel.ShouldBe("Infrastructure & Réseaux");
    }

    [Fact]
    public async Task A_branch_report_does_not_reach_into_a_sibling_branch()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Sofia, "quality-of-life", 6);

        var members = (await ReportAsync(
                factory, SeedOrganisation.Thomas, ReportScopes.Node, SeedOrganisation.Units.Infrastructure))
            .Sections.Single(section => section.Key == "members");

        // Sofia hangs off another branch entirely. Nothing about asking for a branch report widens RLS.
        members.Tables[0].Rows.ShouldNotContain(row => row.Label == "Sofia Navarro");
    }

    [Fact]
    public async Task Kudos_read_zero_until_S9_fills_the_seam()
    {
        await using var factory = await SeededAsync();

        var qol = (await ReportAsync(factory, SeedOrganisation.Thomas, ReportScopes.Node))
            .Sections.Single(section => section.Key == "qol");

        // The section renders now so that S9 is a registration change rather than a change to the contract, the
        // PDF renderer and the Angular view.
        qol.Metrics.Single(metric => metric.Key == "kudos").Value.ShouldBe(0m);
    }

    [Fact]
    public async Task A_branch_rolls_up_the_branches_beneath_it()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 5);

        var members = (await ReportAsync(
                factory,
                SeedOrganisation.Olivier,
                ReportScopes.Node,
                SeedOrganisation.Departments.InformationSystems))
            .Sections.Single(section => section.Key == "members");

        var infrastructure = members.Tables[0].Rows.Single(row => row.Label == "Infrastructure & Réseaux");

        // Headcount, hours, quality-of-life hours — in that column order.
        infrastructure.Values[1].ShouldBe(5m);
    }

    [Fact]
    public async Task An_item_report_needs_an_item()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient()
            .GetAsync("/api/reports?scope=item", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_item_report_splits_contribution_by_branch()
    {
        await using var factory = await SeededAsync();
        var (itemId, projectId) = await CrossBranchItemAsync(factory);

        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 6, projectId);
        await LogAsync(factory, SeedOrganisation.Sofia, "project-build", 2, projectId);

        var report = await ReportAsync(
            factory, SeedOrganisation.Olivier, ReportScopes.Item, scopeId: itemId);

        var contribution = report.Sections.Single(section => section.Key == "contribution");

        // Two branches, because an item is cross-branch by construction and the branch is what a head
        // recognises as theirs (v2 §00 §3).
        contribution.Tables[0].Rows.Count.ShouldBe(2);

        // The share is computed here rather than left to the reader: a report that makes you divide two of its
        // own numbers has not finished its job.
        contribution.Tables[0].Rows[0].Values[1].ShouldBe(75m);
    }

    [Fact]
    public async Task An_item_nobody_has_started_reports_its_card_rather_than_a_404()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Olivier);

        var created = await factory.CreateClient().PostAsJsonAsync(
            "/api/portfolio/items",
            new { name = "Bilateral summit 2027", type = "business-initiative" },
            TestContext.Current.CancellationToken);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var itemId = (await created.Content.ReadFromJsonAsync<CreatedResponse>(
            TestContext.Current.CancellationToken))!.Id;

        var report = await ReportAsync(
            factory, SeedOrganisation.Olivier, ReportScopes.Item, scopeId: itemId);

        // A catalogued thing nobody has started running is a real state, not a missing one — and 404ing on
        // something the caller can plainly see in the catalog would read as a bug.
        report.Sections.ShouldContain(section => section.Key == "identity");
        report.ScopeLabel.ShouldContain("Bilateral summit 2027");
    }

    [Fact]
    public async Task A_head_reaches_an_item_their_branch_merely_contributes_to()
    {
        await using var factory = await SeededAsync();
        var (itemId, projectId) = await CrossBranchItemAsync(factory);

        await LogAsync(factory, SeedOrganisation.Sofia, "project-build", 3, projectId);

        var report = await ReportAsync(
            factory, SeedOrganisation.Laurent, ReportScopes.Item, scopeId: itemId);

        // The one deliberate widening in the matrix. Laurent heads Finance, which contributes to a project IS
        // leads, and knowledge flow says he reads it — including the other department's contribution.
        report.Sections.Single(section => section.Key == "contribution")
            .Tables[0].Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_item_report_for_an_item_the_viewer_cannot_see_is_a_404()
    {
        await using var factory = await SeededAsync();

        // Led by one branch with nobody else contributing, so Finance has no path to it at all — unlike the
        // cross-branch case above, where the knowledge-flow rule would legitimately let Laurent in.
        var (itemId, _) = await CrossBranchItemAsync(factory, contributing: false);

        factory.AsUser(SeedOrganisation.Laurent);

        var response = await factory.CreateClient().GetAsync(
            $"/api/reports?scope=item&scopeId={itemId}",
            TestContext.Current.CancellationToken);

        // RLS removed the row, so the report genuinely cannot tell "may not see" from "does not exist" — and
        // neither can a prober.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- The narrative ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_summary_is_generated_in_the_viewers_language()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 4);

        var summary = await SummaryAsync(factory, SeedOrganisation.Camille, ReportScopes.Me);

        // The stub echoes the language it was told to answer in, so this asserts the whole chain: the report's
        // language, the prompt, and the client's system message.
        summary.Text.ShouldContain("[stub:fr]");
        summary.Language.ShouldBe("fr");
        summary.Stale.ShouldBeFalse();
    }

    [Fact]
    public async Task A_summary_states_the_language_it_was_asked_for()
    {
        await using var factory = await SeededAsync();

        var summary = await SummaryAsync(factory, SeedOrganisation.Camille, ReportScopes.Me, language: "es");

        summary.Text.ShouldContain("[stub:es]");
    }

    [Fact]
    public async Task Asking_twice_for_unchanged_figures_reuses_the_narrative()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 4);

        var first = await SummaryAsync(factory, SeedOrganisation.Camille, ReportScopes.Me);
        var second = await SummaryAsync(factory, SeedOrganisation.Camille, ReportScopes.Me);

        // Same row, not merely the same text. On an on-prem box sized for the building, a Monday morning where a
        // whole unit opens last week's report is exactly when this matters.
        second.Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Logging_another_hour_makes_the_stored_narrative_stale()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 4);
        await SummaryAsync(factory, SeedOrganisation.Camille, ReportScopes.Me);

        await LogAsync(factory, SeedOrganisation.Camille, "recruitment-admin", 2,
            start: MondayMorning.AddDays(1));

        var report = await ReportAsync(factory, SeedOrganisation.Camille, ReportScopes.Me);

        // Shown, and flagged. Hiding it would empty the panel for no visible reason; showing it unflagged would
        // let somebody quote sentences about numbers no longer on the screen beside them.
        report.Summary.ShouldNotBeNull();
        report.Summary.Stale.ShouldBeTrue();
    }

    [Fact]
    public async Task Regenerating_writes_a_new_narrative_even_when_nothing_moved()
    {
        await using var factory = await SeededAsync();

        var first = await SummaryAsync(factory, SeedOrganisation.Camille, ReportScopes.Me);
        var forced = await SummaryAsync(factory, SeedOrganisation.Camille, ReportScopes.Me, force: true);

        forced.Id.ShouldNotBe(first.Id);
    }

    [Fact]
    public async Task A_narrative_belongs_to_whoever_asked_for_it()
    {
        await using var factory = await SeededAsync();

        await SummaryAsync(factory, SeedOrganisation.Camille, ReportScopes.Me);

        var mehdi = await ReportAsync(factory, SeedOrganisation.Mehdi, ReportScopes.Me);

        // Two people in one unit both hold a "my" report for the same week. Serving Mehdi Camille's narrative
        // would be a disclosure dressed up as a cache hit.
        mehdi.Summary.ShouldBeNull();
    }

    [Fact]
    public async Task The_prompt_never_carries_a_persons_name()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 4);

        // The stub echoes the first 120 characters of the prompt back, which is what lets this assert on what
        // actually left the process rather than on what the builder claims it sent.
        var summary = await SummaryAsync(factory, SeedOrganisation.Thomas, ReportScopes.Node);

        summary.Text.ShouldNotContain("Camille");
    }

    [Fact]
    public async Task A_member_cannot_generate_a_summary_for_a_scope_they_do_not_hold()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/reports/summary",
            new { scope = ReportScopes.Portfolio },
            TestContext.Current.CancellationToken);

        // The model must never be handed a scope its reader could not have asked for directly.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Export ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_report_exports_to_a_stored_PDF()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 4);

        var report = await ReportAsync(factory, SeedOrganisation.Camille, ReportScopes.Me);
        var export = await ExportAsync(factory, SeedOrganisation.Camille, report.Id);

        export.Format.ShouldBe("pdf");
        export.Bytes.ShouldBeGreaterThan(0);
        export.Url.ToString().ShouldContain("reports/");

        var storage = (InMemoryObjectStorage)factory.Services.GetRequiredService<IObjectStorage>();

        var key = storage.Keys.ShouldHaveSingleItem();

        await using var stored = await storage.GetAsync(key, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stored, System.Text.Encoding.Latin1);

        // A real PDF, not an empty object with a plausible name.
        (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldStartWith("%PDF-1.7");
    }

    [Fact]
    public async Task Exporting_somebody_elses_report_id_produces_your_own_numbers()
    {
        await using var factory = await SeededAsync();

        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 9);

        var camille = await ReportAsync(factory, SeedOrganisation.Camille, ReportScopes.Me);

        // Mehdi holds Camille's id. The id says only what was asked for, and reopening it runs his own
        // authorization and his own RLS session — so he gets his own empty week, never hers.
        var export = await ExportAsync(factory, SeedOrganisation.Mehdi, camille.Id);

        export.ReportId.ShouldBe(camille.Id);

        var storage = (InMemoryObjectStorage)factory.Services.GetRequiredService<IObjectStorage>();

        await using var stored = await storage.GetAsync(storage.Keys.Single(), TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stored, System.Text.Encoding.Latin1);

        var text = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        text.ShouldNotContain("Camille Villeneuve");
    }

    [Fact]
    public async Task Exporting_an_id_for_a_scope_the_caller_does_not_hold_is_refused()
    {
        await using var factory = await SeededAsync();

        var head = await ReportAsync(factory, SeedOrganisation.Nadia, ReportScopes.Portfolio);

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient()
            .GetAsync($"/api/reports/{head.Id}/export", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_malformed_report_id_is_a_404()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient()
            .GetAsync("/api/reports/not-a-report/export", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unsupported_export_format_is_refused_by_name()
    {
        await using var factory = await SeededAsync();

        var report = await ReportAsync(factory, SeedOrganisation.Camille, ReportScopes.Me);

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient()
            .GetAsync($"/api/reports/{report.Id}/export?format=docx", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // --- Fixture ---------------------------------------------------------------------------------------------

    private sealed record LogResponse(Guid Id);

    private sealed record CreatedResponse(Guid Id);

    private static async Task<ReportView> ReportAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string? scope = null,
        Guid? scopeId = null,
        string? period = null,
        DateOnly? from = null)
    {
        factory.AsUser(person);

        var query = new List<string> { $"from={(from ?? Monday):yyyy-MM-dd}" };

        if (scope is not null)
        {
            query.Add($"scope={scope}");
        }

        if (scopeId is { } id)
        {
            query.Add($"scopeId={id}");
        }

        query.Add($"period={period ?? ReportPeriods.Week}");

        return (await factory.CreateClient().GetFromJsonAsync<ReportView>(
            $"/api/reports?{string.Join('&', query)}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<ReportSummaryView> SummaryAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string scope,
        string? language = null,
        bool force = false)
    {
        factory.AsUser(person);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/reports/summary",
            new { scope, period = ReportPeriods.Week, from = Monday, lang = language, force },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<ReportSummaryView>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<ReportExportView> ExportAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string reportId)
    {
        factory.AsUser(person);

        var response = await factory.CreateClient()
            .GetAsync($"/api/reports/{reportId}/export", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<ReportExportView>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<Guid> LogAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string type,
        decimal hours,
        Guid? projectId = null,
        string kind = "actual",
        DateTimeOffset? start = null)
    {
        factory.AsUser(person);

        var from = start ?? MondayMorning;
        var ct = TestContext.Current.CancellationToken;

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            new
            {
                activityTypeCode = type,
                projectId,
                kind,
                source = "manual",
                slotStart = from,
                slotEnd = from.AddHours((double)hours),
                hours,
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<LogResponse>(ct))!.Id;
    }

    /// <summary>Led by IS, with Camille from IS and Sofia from Finance on it — the cross-department case.</summary>
    /// <summary>
    /// A catalogued item, committed so it has a delivery row, staffed from two branches.
    /// </summary>
    /// <remarks>
    /// Arranged through the catalog rather than by creating a project directly, because the item is what the
    /// report is scoped by now — and an item conjured beside a project rather than in front of it would prove
    /// the report works on a shape production never produces.
    /// </remarks>
    private static async Task<(Guid ItemId, Guid ProjectId)> CrossBranchItemAsync(
        CracraApplicationFactory factory,
        bool contributing = true)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var considered = await client.PostAsJsonAsync(
            "/api/portfolio/considered",
            new
            {
                name = "Migration M365",
                departmentId = SeedOrganisation.Departments.InformationSystems,
                priority = 10,
                notes = "Raised at the steering committee.",
            },
            ct);

        considered.StatusCode.ShouldBe(HttpStatusCode.Created);

        var itemId = (await considered.Content.ReadFromJsonAsync<CreatedResponse>(ct))!.Id;

        var committed = await client.PostAsJsonAsync(
            $"/api/portfolio/{itemId}/commit",
            new { projectCode = "PRJ-REPORT", decisionNotes = "Budget approved." },
            ct);

        committed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var projectId = (await committed.Content.ReadFromJsonAsync<CommittedResponse>(ct))!.ProjectId;

        if (contributing)
        {
            // Committing gives the item a delivery row led by its owner and nothing else. A second branch has to
            // be declared before somebody from it can be staffed, which is the order a lead would do it in.
            var declared = await client.PostAsJsonAsync(
                $"/api/projects/{projectId}/departments",
                new { departmentId = SeedOrganisation.Departments.Finance },
                ct);

            declared.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var members = contributing
            ?
            [
                (SeedOrganisation.Camille, SeedOrganisation.Departments.InformationSystems),
                (SeedOrganisation.Sofia, SeedOrganisation.Departments.Finance),
            ]
            : new[] { (SeedOrganisation.Camille, SeedOrganisation.Departments.InformationSystems) };

        foreach (var (person, department) in members)
        {
            var added = await client.PostAsJsonAsync(
                $"/api/projects/{projectId}/members",
                new
                {
                    personId = person.UserId,
                    departmentId = department,
                    functionalRoleId = Guid.Parse("f0000000-0000-0000-0000-000000000001"),
                    allocationPercent = 50,
                },
                ct);

            added.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        return (itemId, projectId);
    }

    private sealed record CommittedResponse(Guid ProjectId);

    private static async Task<Guid> CrossDepartmentProjectAsync(
        CracraApplicationFactory factory,
        bool contributing = true)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var created = await client.PostAsJsonAsync(
            "/api/projects",
            new
            {
                code = "PRJ-REPORT",
                name = "Migration M365",
                classification = "build",
                costAmount = 12000m,
                costCurrency = "EUR",
                leadDepartmentId = SeedOrganisation.Departments.InformationSystems,
                contributingDepartmentIds = contributing
                    ? new[] { SeedOrganisation.Departments.Finance }
                    : [],
            },
            ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var projectId = (await created.Content.ReadFromJsonAsync<CreatedResponse>(ct))!.Id;

        var members = contributing
            ?
            [
                (SeedOrganisation.Camille, SeedOrganisation.Departments.InformationSystems),
                (SeedOrganisation.Sofia, SeedOrganisation.Departments.Finance),
            ]
            : new[] { (SeedOrganisation.Camille, SeedOrganisation.Departments.InformationSystems) };

        foreach (var (person, department) in members)
        {
            var added = await client.PostAsJsonAsync(
                $"/api/projects/{projectId}/members",
                new
                {
                    personId = person.UserId,
                    departmentId = department,
                    functionalRoleId = Guid.Parse("f0000000-0000-0000-0000-000000000001"),
                },
                ct);

            added.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        return projectId;
    }

    private async Task<CracraApplicationFactory> SeededAsync()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();

        keycloak.Users.Add(FakeKeycloakDirectory.User(
            SeedOrganisation.Laurent, "Laurent", "Bouchard", "expert-comptable"));

        // The real client, the real streaming parser and the real prompt, pointed at the stub hosted in-process.
        // A second fake would be a second thing to keep in step with the stub the dev box runs.
        var handler = llm.Server.CreateHandler();

        var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IKeycloakDirectoryClient>();
                services.AddSingleton<IKeycloakDirectoryClient>(keycloak);

                services.AddHttpClient(AiExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler);

                // RustFS is out of the loop for the same reason Keycloak is: what varies here is our own
                // behaviour, and standing up an S3 server to assert it would be testing the AWS SDK.
                services.RemoveAll<IObjectStorage>();
                services.AddSingleton<IObjectStorage>(new InMemoryObjectStorage());
            },
        };

        await ResetAsync(factory);

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        // The seeded shape, not whatever the administration tests left in the shared database.
        await OrgTreeReset.ApplyAsync(factory, TestContext.Current.CancellationToken);

        return factory;
    }

    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;

        var reporting = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Reporting.Infrastructure.ReportingDbContext>();

        await reporting.Summaries.ExecuteDeleteAsync(ct);

        var activities = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Activities.Infrastructure.ActivitiesDbContext>();

        await activities.Entries.ExecuteDeleteAsync(ct);

        var scheduling = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Scheduling.Infrastructure.SchedulingDbContext>();

        await scheduling.Shifts.ExecuteDeleteAsync(ct);
        await scheduling.WorkOrders.ExecuteDeleteAsync(ct);

        var meetings = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Meetings.Data.MeetingsDbContext>();

        await meetings.Attendance.ExecuteDeleteAsync(ct);
        await meetings.Occurrences.ExecuteDeleteAsync(ct);
        await meetings.Series.ExecuteDeleteAsync(ct);
        await meetings.SpecialDays.ExecuteDeleteAsync(ct);

        var portfolio = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Portfolio.Infrastructure.PortfolioDbContext>();

        await portfolio.Transitions.ExecuteDeleteAsync(ct);
        await portfolio.Iterations.ExecuteDeleteAsync(ct);
        await portfolio.Items.ExecuteDeleteAsync(ct);

        var projects = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Projects.Infrastructure.ProjectsDbContext>();

        await projects.ProjectMembers.ExecuteDeleteAsync(ct);
        await projects.ProjectDepartments.ExecuteDeleteAsync(ct);
        await projects.Projects.ExecuteDeleteAsync(ct);

        var access = scope.ServiceProvider.GetRequiredService<Cracra.Modules.Access.Data.AccessDbContext>();

        await access.ProjectMemberships.ExecuteDeleteAsync(ct);
    }
}
