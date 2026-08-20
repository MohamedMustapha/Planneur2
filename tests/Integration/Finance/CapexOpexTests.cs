using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Finance.Domain;
using Cracra.Modules.Finance.Services;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Finance;

/// <summary>
/// S11 through HTTP, with real RLS: the split, the rate card that values it, and who may see any of it.
/// </summary>
/// <remarks>
/// This is a 2-layer module, so per conventions.md §6 the arithmetic is unit-tested and everything else is
/// covered here end to end. The assertions worth having are the ones that would catch the slice's claims being
/// false: that the numbers reconcile to seeded activities and costs, that a rate card actually values hours, and
/// that a member or a project-lead never sees a figure at all.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class CapexOpexTests(PostgresFixture postgres)
{
    private static readonly DateOnly Month = new(2026, 8, 1);
    private static readonly DateTimeOffset Monday = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid DevRole = Guid.Parse("f0000000-0000-0000-0000-000000000001");
    private static readonly Guid InformationSystems = SeedOrganisation.Departments.InformationSystems;

    // --- The door ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("camille")]
    [InlineData("mehdi")]
    public async Task A_member_is_refused_outright(string who)
    {
        await using var factory = await SeededAsync();

        factory.AsUser(who == "camille" ? SeedOrganisation.Camille : SeedOrganisation.Mehdi);

        var response = await factory.CreateClient().GetAsync(
            "/api/finance/capex-opex",
            TestContext.Current.CancellationToken);

        // The policy, before a query runs. The spec asks for this in as many words: members and project-leads
        // without a head role get a 403, not an empty view they might mistake for "nothing happened this month".
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_project_lead_without_a_head_role_is_refused_too()
    {
        await using var factory = await SeededAsync();

        // A project lead governs a project's delivery. Capitalization is a department's business, and leading a
        // project is not a claim on the department's books.
        factory.AsUser(SeedOrganisation.Camille with { Roles = ["member", ContextualRole.ProjectLead] });

        var response = await factory.CreateClient().GetAsync(
            "/api/finance/capex-opex",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_unit_head_reaches_the_endpoint_and_sees_no_configuration()
    {
        await using var factory = await SeededAsync();

        await SaveRuleAsync(factory, SeedOrganisation.Olivier, runTreatment: Treatments.Capex);

        factory.AsUser(SeedOrganisation.Thomas);

        var rule = await factory.CreateClient().GetFromJsonAsync<CapexOpexRuleView>(
            $"/api/finance/rules?departmentId={InformationSystems}",
            TestContext.Current.CancellationToken);

        // The matrix says capex/opex is dept-head and PMO; a unit-head is a head, so the door opens, and RLS then
        // hands back nothing of the department's — so what they get is the platform default rather than the
        // configured rule. Shown nothing, rather than told no.
        rule!.Configured.ShouldBeFalse();
        rule.RunTreatment.ShouldBe(Treatments.Opex);
    }

    [Fact]
    public async Task A_head_of_another_department_sees_none_of_it()
    {
        await using var factory = await SeededAsync();

        await SaveRateCardAsync(factory, SeedOrganisation.Olivier, 100m);

        factory.AsUser(SeedOrganisation.Laurent);

        var cards = await factory.CreateClient().GetFromJsonAsync<List<RateCardView>>(
            "/api/finance/rate-cards",
            TestContext.Current.CancellationToken);

        // A rate card states what an organization pays a job title. There is no audience for that between
        // "administers this department" and "nobody".
        cards.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_pmo_sees_every_departments_rate_cards()
    {
        await using var factory = await SeededAsync();

        await SaveRateCardAsync(factory, SeedOrganisation.Olivier, 100m);
        await SaveRateCardAsync(factory, SeedOrganisation.Laurent, 120m, SeedOrganisation.Departments.Finance);

        factory.AsUser(SeedOrganisation.Nadia);

        var cards = await factory.CreateClient().GetFromJsonAsync<List<RateCardView>>(
            "/api/finance/rate-cards",
            TestContext.Current.CancellationToken);

        cards!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_head_cannot_write_another_departments_rate_card()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Laurent);

        var response = await factory.CreateClient().PutAsJsonAsync(
            "/api/finance/rate-cards",
            new
            {
                departmentId = InformationSystems,
                functionalRoleId = DevRole,
                hourlyRate = 1m,
                currency = "EUR",
                effectiveFrom = "2026-01-01",
            },
            TestContext.Current.CancellationToken);

        // The WITH CHECK refuses the insert, and the platform's handler maps Postgres' 42501 to a 403. That is
        // the right answer and a different one from the 422 a service-level rule would give: this is not a
        // malformed request, it is a well-formed request about somebody else's department.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- The rule ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_department_that_has_configured_nothing_still_has_a_rule()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var rule = await factory.CreateClient().GetFromJsonAsync<CapexOpexRuleView>(
            "/api/finance/rules",
            TestContext.Current.CancellationToken);

        // The defaults S11 names, and the flag that lets the editor say they are defaults. Requiring somebody to
        // press save before the view works would make the first visit look broken.
        rule!.Configured.ShouldBeFalse();
        rule.BuildTreatment.ShouldBe(Treatments.Capex);
        rule.RunTreatment.ShouldBe(Treatments.Opex);
        rule.QolTreatment.ShouldBe(Treatments.Opex);
        rule.AdminTreatment.ShouldBe(Treatments.Excluded);
    }

    [Fact]
    public async Task A_treatment_outside_the_three_is_refused()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PutAsJsonAsync(
            "/api/finance/rules",
            new
            {
                departmentId = InformationSystems,
                buildTreatment = "amortized",
                runTreatment = Treatments.Opex,
                qolTreatment = Treatments.Opex,
                adminTreatment = Treatments.Excluded,
            },
            TestContext.Current.CancellationToken);

        // A fourth treatment is the shape of an amortization rule, which the spec puts out of scope.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Saving_the_rule_twice_updates_it_rather_than_adding_one()
    {
        await using var factory = await SeededAsync();

        await SaveRuleAsync(factory, SeedOrganisation.Olivier, runTreatment: Treatments.Capex);
        var second = await SaveRuleAsync(factory, SeedOrganisation.Olivier, runTreatment: Treatments.Excluded);

        second.RunTreatment.ShouldBe(Treatments.Excluded);
        second.Configured.ShouldBeTrue();
    }

    // --- The numbers ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_split_reconciles_to_the_seeded_activities_and_cost()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, cost: 40_000m, classification: "build");

        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 8m, projectId, Monday);
        await LogAsync(factory, SeedOrganisation.Camille, "project-run", 4m, projectId, Monday.AddDays(1));
        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 2m, null, Monday.AddDays(2));
        await LogAsync(factory, SeedOrganisation.Camille, "recruitment-admin", 1m, null, Monday.AddDays(3));

        var view = await ViewAsync(factory, SeedOrganisation.Olivier);

        view.Totals.CapexHours.ShouldBe(8m);

        // Run and quality-of-life are both opex by default, so the opex hours are the two together.
        view.Totals.OpexHours.ShouldBe(6m);
        view.Totals.ExcludedHours.ShouldBe(1m);
        view.Totals.TotalHours.ShouldBe(15m);

        // No rate card yet, so the money is the project's manual cost and nothing else — which is the spec's
        // stated fallback: effort in hours only.
        view.Totals.CapexAmount.ShouldBe(40_000m);
        view.Totals.EffortValued.ShouldBeFalse();

        var line = view.Projects.ShouldHaveSingleItem();

        line.ProjectId.ShouldBe(projectId);
        line.BuildHours.ShouldBe(8m);
        line.RunHours.ShouldBe(4m);
    }

    [Fact]
    public async Task A_rate_card_values_the_hours_it_covers()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, cost: 0m, classification: "build");

        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 10m, projectId, Monday);

        await SaveRateCardAsync(factory, SeedOrganisation.Olivier, 75m);

        var view = await ViewAsync(factory, SeedOrganisation.Olivier);

        // Camille is an architecte in the seeded realm, so the card has to name that role to price her hours.
        view.Totals.CapexHours.ShouldBe(10m);
        view.Totals.EffortCapex.ShouldBe(750m);
        view.Totals.EffortValued.ShouldBeTrue();
        view.Currency.ShouldBe("EUR");
    }

    [Fact]
    public async Task A_rate_card_that_ended_before_the_period_values_nothing()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, cost: 0m, classification: "build");

        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 10m, projectId, Monday);

        await SaveRateCardAsync(
            factory,
            SeedOrganisation.Olivier,
            75m,
            from: new DateOnly(2025, 1, 1),
            to: new DateOnly(2026, 1, 1));

        var view = await ViewAsync(factory, SeedOrganisation.Olivier);

        // Last year's report must not move when this year's rates are entered, and this is the same property seen
        // from the other side: this year's hours are not priced by last year's card.
        view.Totals.CapexHours.ShouldBe(10m);
        view.Totals.EffortCapex.ShouldBe(0m);
        view.Totals.EffortValued.ShouldBeFalse();
    }

    [Fact]
    public async Task Two_cards_that_would_price_the_same_day_are_refused()
    {
        await using var factory = await SeededAsync();

        await SaveRateCardAsync(factory, SeedOrganisation.Olivier, 75m, from: new DateOnly(2026, 1, 1));

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PutAsJsonAsync(
            "/api/finance/rate-cards",
            new
            {
                departmentId = InformationSystems,
                functionalRoleId = ArchitectRole,
                hourlyRate = 90m,
                currency = "EUR",
                effectiveFrom = "2026-06-01",
            },
            TestContext.Current.CancellationToken);

        // An overlap has no defensible resolution at read time: whichever row came back first would decide, and
        // the same report would produce different money on different days.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Changing_the_rule_moves_the_money_between_the_columns()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, cost: 10_000m, classification: "run");

        await LogAsync(factory, SeedOrganisation.Camille, "project-run", 5m, projectId, Monday);

        var before = await ViewAsync(factory, SeedOrganisation.Olivier);

        before.Totals.OpexAmount.ShouldBe(10_000m);

        await SaveRuleAsync(factory, SeedOrganisation.Olivier, runTreatment: Treatments.Capex);

        var after = await ViewAsync(factory, SeedOrganisation.Olivier);

        // The whole point of the rule being a row: a department that treats its RUN work as capitalizable
        // maintenance says so, and nothing is deployed.
        after.Totals.CapexAmount.ShouldBe(10_000m);
        after.Totals.OpexAmount.ShouldBe(0m);
        after.Totals.CapexHours.ShouldBe(5m);
    }

    [Fact]
    public async Task A_project_scope_counts_that_project_and_no_other()
    {
        await using var factory = await SeededAsync();

        var first = await CreateProjectAsync(factory, cost: 1_000m, classification: "build", code: "PRJ-A");
        var second = await CreateProjectAsync(factory, cost: 2_000m, classification: "build", code: "PRJ-B");

        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 3m, first, Monday);
        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 7m, second, Monday.AddDays(1));

        var view = await ViewAsync(factory, SeedOrganisation.Olivier, scope: "project", scopeId: second);

        view.Projects.ShouldHaveSingleItem().ProjectId.ShouldBe(second);
        view.Totals.CapexHours.ShouldBe(7m);
        view.Totals.CapexAmount.ShouldBe(2_000m);
    }

    [Fact]
    public async Task A_period_outside_the_logged_hours_is_empty_rather_than_wrong()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, cost: 5_000m, classification: "build");

        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 8m, projectId, Monday);

        var view = await ViewAsync(factory, SeedOrganisation.Olivier, from: new DateOnly(2026, 6, 1));

        view.Totals.TotalHours.ShouldBe(0m);

        // The manual cost has no date on it, so it is the project's whole budget in whatever period the project
        // is looked at. Spreading it across months would be an amortization schedule, which is out of scope in as
        // many words — and the monthly breakdown carries effort only for exactly that reason.
        view.Totals.CapexAmount.ShouldBe(5_000m);
        view.Periods.ShouldAllBe(month => month.CapexHours == 0m);
    }

    [Fact]
    public async Task The_monthly_breakdown_puts_each_hour_in_its_own_month()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, cost: 0m, classification: "build");

        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 4m, projectId, new DateTimeOffset(2026, 7, 6, 9, 0, 0, TimeSpan.Zero));
        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 6m, projectId, Monday);

        var view = await ViewAsync(
            factory,
            SeedOrganisation.Olivier,
            period: FinancePeriods.Quarter,
            from: new DateOnly(2026, 7, 1));

        view.Periods.Count.ShouldBe(3);
        view.Periods.Single(month => month.Month == "2026-07").CapexHours.ShouldBe(4m);
        view.Periods.Single(month => month.Month == "2026-08").CapexHours.ShouldBe(6m);
        view.Periods.Single(month => month.Month == "2026-09").CapexHours.ShouldBe(0m);
    }

    [Fact]
    public async Task A_department_counts_its_own_peoples_hours_wherever_they_logged_them()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, cost: 0m, classification: "build");

        // Non-project work belongs to nobody's project and to somebody's department, which is why the department
        // scope reads people rather than projects.
        await LogAsync(factory, SeedOrganisation.Camille, "quality-of-life", 3m, null, Monday);
        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 2m, projectId, Monday.AddDays(1));

        var view = await ViewAsync(factory, SeedOrganisation.Olivier);

        view.Totals.OpexHours.ShouldBe(3m);
        view.Totals.CapexHours.ShouldBe(2m);
    }

    // --- The export ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_head_exports_the_split_and_gets_a_link()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateProjectAsync(factory, cost: 12_000m, classification: "build");

        await LogAsync(factory, SeedOrganisation.Camille, "project-build", 5m, projectId, Monday);

        factory.AsUser(SeedOrganisation.Olivier);

        var export = await factory.CreateClient().GetFromJsonAsync<CapexOpexExportView>(
            $"/api/finance/capex-opex/export?period={FinancePeriods.Month}&from={Month:yyyy-MM-dd}",
            TestContext.Current.CancellationToken);

        export!.Format.ShouldBe("xlsx");
        export.ContentType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        export.Key.ShouldContain("capex-opex-department");

        // Stored and served through a presigned URL rather than proxied, so a large workbook never occupies a
        // request thread. The in-memory storage the test substitutes still round-trips the object.
        var storage = factory.Services.GetRequiredService<BuildingBlocks.Storage.IObjectStorage>();

        (await storage.ExistsAsync(export.Key, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_format_the_platform_does_not_render_is_refused()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().GetAsync(
            "/api/finance/capex-opex/export?format=pdf",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_member_cannot_export_either()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().GetAsync(
            "/api/finance/capex-opex/export",
            TestContext.Current.CancellationToken);

        // The export recomposes the view under the caller's own session, so it is refused at the same door and for
        // the same reason. A link somebody shares cannot become a way around the policy.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Fixture -------------------------------------------------------------------------------------------------

    /// <summary>Camille's job identity in the seeded realm. The rate card has to name it to price her hours.</summary>
    private static readonly Guid ArchitectRole = Guid.Parse("f0000000-0000-0000-0000-000000000003");

    private static async Task<CapexOpexView> ViewAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string scope = "department",
        Guid? scopeId = null,
        string period = FinancePeriods.Month,
        DateOnly? from = null)
    {
        factory.AsUser(person);

        var query = $"?scope={scope}&period={period}&from={(from ?? Month):yyyy-MM-dd}"
                    + (scopeId is { } id ? $"&scopeId={id}" : string.Empty);

        return (await factory.CreateClient().GetFromJsonAsync<CapexOpexView>(
            "/api/finance/capex-opex" + query,
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<CapexOpexRuleView> SaveRuleAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string? runTreatment = null)
    {
        factory.AsUser(person);

        var response = await factory.CreateClient().PutAsJsonAsync(
            "/api/finance/rules",
            new
            {
                departmentId = person.DepartmentIds[0],
                buildTreatment = Treatments.Capex,
                runTreatment = runTreatment ?? Treatments.Opex,
                qolTreatment = Treatments.Opex,
                adminTreatment = Treatments.Excluded,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<CapexOpexRuleView>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task SaveRateCardAsync(
        CracraApplicationFactory factory,
        UserContext person,
        decimal rate,
        Guid? departmentId = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        factory.AsUser(person);

        var response = await factory.CreateClient().PutAsJsonAsync(
            "/api/finance/rate-cards",
            new
            {
                departmentId = departmentId ?? person.DepartmentIds[0],
                functionalRoleId = ArchitectRole,
                hourlyRate = rate,
                currency = "EUR",
                effectiveFrom = (from ?? new DateOnly(2026, 1, 1)).ToString("yyyy-MM-dd"),
                effectiveTo = to?.ToString("yyyy-MM-dd"),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task LogAsync(
        CracraApplicationFactory factory,
        UserContext person,
        string type,
        decimal hours,
        Guid? projectId,
        DateTimeOffset start)
    {
        factory.AsUser(person);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/activities",
            new
            {
                activityTypeCode = type,
                projectId,
                kind = "actual",
                source = "manual",
                slotStart = start,
                slotEnd = start.AddHours((double)hours),
                hours,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<Guid> CreateProjectAsync(
        CracraApplicationFactory factory,
        decimal cost,
        string classification,
        string code = "PRJ-FIN")
    {
        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var created = await client.PostAsJsonAsync(
            "/api/projects",
            new
            {
                code,
                name = "Socle applicatif",
                classification,
                costAmount = cost,
                costCurrency = "EUR",
                leadDepartmentId = InformationSystems,
                contributingDepartmentIds = Array.Empty<Guid>(),
            },
            ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var projectId = (await created.Content.ReadFromJsonAsync<CreatedResponse>(ct))!.Id;

        var added = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/members",
            new
            {
                personId = SeedOrganisation.Camille.UserId,
                departmentId = InformationSystems,
                functionalRoleId = DevRole,
            },
            ct);

        added.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        return projectId;
    }

    private sealed record CreatedResponse(Guid Id);

    private async Task<CracraApplicationFactory> SeededAsync()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();

        keycloak.Users.Add(FakeKeycloakDirectory.User(
            SeedOrganisation.Laurent, "Laurent", "Bouchard", "expert-comptable"));

        var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IKeycloakDirectoryClient>();
                services.AddSingleton<IKeycloakDirectoryClient>(keycloak);

                // The export writes to object storage, and nothing is listening on RustFS in a test run. The
                // in-memory substitute round-trips the object so the assertion is about what was stored.
                services.RemoveAll<BuildingBlocks.Storage.IObjectStorage>();
                services.AddSingleton<BuildingBlocks.Storage.IObjectStorage, InMemoryObjectStorage>();
            },
        };

        await ResetAsync(factory);

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        return factory;
    }

    /// <summary>
    /// Clears everything these scenarios count.
    /// </summary>
    /// <remarks>
    /// The Postgres container is shared for speed, and a capitalization view is a sum over everything visible —
    /// which makes it the suite most exposed to another class's leftover rows.
    /// </remarks>
    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;

        var finance = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Finance.Data.FinanceDbContext>();

        await finance.RateCards.ExecuteDeleteAsync(ct);
        await finance.Rules.ExecuteDeleteAsync(ct);

        var activities = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Activities.Infrastructure.ActivitiesDbContext>();

        await activities.Entries.ExecuteDeleteAsync(ct);

        var projects = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Projects.Infrastructure.ProjectsDbContext>();

        await projects.Projects.ExecuteDeleteAsync(ct);
    }
}
