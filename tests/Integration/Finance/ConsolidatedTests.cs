using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Finance.Data;
using Cracra.Modules.Finance.Services;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Finance;

/// <summary>
/// The consolidated view end to end (v2 §04), through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// The complaint §04 answers is that the v1 finance page was empty until you picked a project. The fix is not a
/// better empty state: the page lands on the highest node the caller heads and drills down, which is only testable
/// against a real tree and a real session.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ConsolidatedTests(PostgresFixture postgres)
{
    private static readonly int Year = 2026;

    [Fact]
    public async Task A_head_who_names_no_node_lands_on_the_one_they_run()
    {
        await using var factory = await SeededAsync();

        await AddLicenseAsync(factory, SeedOrganisation.Olivier, "SharePoint", seats: 100, unitCost: 40m);

        var view = await ConsolidatedAsync(factory, SeedOrganisation.Olivier);

        view.Landed.ShouldBeTrue();
        view.Node.NodeId.ShouldBe(SeedOrganisation.Departments.InformationSystems);
        view.Node.Subtree.Total.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public async Task A_licence_reaches_the_total_from_wherever_it_sits()
    {
        // A licence that appeared on the renewal calendar and in nobody's total is the discrepancy the module
        // exists to remove, so recording one lays down its cost line in the same act.
        await using var factory = await SeededAsync();

        await AddLicenseAsync(factory, SeedOrganisation.Olivier, "SharePoint", seats: 10, unitCost: 100m);

        var view = await ConsolidatedAsync(factory, SeedOrganisation.Olivier);

        view.Node.Subtree.Opex.ShouldBe(1000m);
    }

    [Fact]
    public async Task A_cost_at_a_unit_rolls_up_into_the_department_above_it()
    {
        await using var factory = await SeededAsync();

        await AddComponentAsync(factory, SeedOrganisation.Olivier, SeedOrganisation.Units.Development, 250m);

        var view = await ConsolidatedAsync(factory, SeedOrganisation.Olivier);

        view.Node.Own.Total.ShouldBe(0m);
        view.Node.Subtree.Total.ShouldBe(250m);

        var unit = view.Node.Children.Single(child => child.NodeId == SeedOrganisation.Units.Development);

        unit.Own.Total.ShouldBe(250m);
    }

    [Fact]
    public async Task The_rollup_invariant_holds_across_the_real_tree()
    {
        await using var factory = await SeededAsync();

        await AddComponentAsync(factory, SeedOrganisation.Olivier, SeedOrganisation.Units.Development, 100m);
        await AddComponentAsync(factory, SeedOrganisation.Olivier, SeedOrganisation.Units.Infrastructure, 60m);
        await AddComponentAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Departments.InformationSystems,
            15m);

        var view = await ConsolidatedAsync(factory, SeedOrganisation.Olivier);

        AssertRollup(view.Node);

        view.Node.Subtree.Total.ShouldBe(175m);
    }

    [Fact]
    public async Task An_envelope_at_the_department_is_measured_against_everything_beneath_it()
    {
        await using var factory = await SeededAsync();

        await AddComponentAsync(factory, SeedOrganisation.Olivier, SeedOrganisation.Units.Development, 400m);

        factory.AsUser(SeedOrganisation.Olivier);

        var saved = await factory.CreateClient().PostAsJsonAsync(
            "/api/finance/budgets",
            new
            {
                scopeType = "node",
                scopeId = SeedOrganisation.Departments.InformationSystems,
                fiscalYear = Year,
                plannedAmount = 1000m,
            },
            TestContext.Current.CancellationToken);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        var view = await ConsolidatedAsync(factory, SeedOrganisation.Olivier);

        view.Node.PlannedAmount.ShouldBe(1000m);
        view.Node.Variance.ShouldBe(600m);
    }

    [Fact]
    public async Task Capex_only_leaves_out_what_is_opex()
    {
        await using var factory = await SeededAsync();

        await AddComponentAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Units.Development,
            200m,
            treatment: "capex");

        await AddComponentAsync(
            factory,
            SeedOrganisation.Olivier,
            SeedOrganisation.Units.Development,
            50m,
            treatment: "opex");

        (await ConsolidatedAsync(factory, SeedOrganisation.Olivier, "capex")).Node.Subtree.Total.ShouldBe(200m);
        (await ConsolidatedAsync(factory, SeedOrganisation.Olivier, "opex")).Node.Subtree.Total.ShouldBe(50m);
        (await ConsolidatedAsync(factory, SeedOrganisation.Olivier)).Node.Subtree.Total.ShouldBe(250m);
    }

    [Fact]
    public async Task The_renewal_calendar_answers_with_what_falls_due()
    {
        await using var factory = await SeededAsync();

        var soon = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(30);
        var later = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(400);

        await AddLicenseAsync(factory, SeedOrganisation.Olivier, "Expiring soon", 1, 10m, soon);
        await AddLicenseAsync(factory, SeedOrganisation.Olivier, "Expiring later", 1, 10m, later);

        factory.AsUser(SeedOrganisation.Olivier);

        var renewing = await factory.CreateClient().GetFromJsonAsync<List<LicenseView>>(
            "/api/finance/licenses?within=90",
            TestContext.Current.CancellationToken);

        var names = renewing!.Select(license => license.ProductName).ToArray();

        names.ShouldContain("Expiring soon");
        names.ShouldNotContain("Expiring later");
    }

    [Fact]
    public async Task A_member_is_refused_the_module_rather_than_shown_an_empty_one()
    {
        // §04.4. Seeing the numbers is itself the privilege here, so the answer is a refusal and not a total of
        // zero — which would read as "your branch spent nothing".
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().GetAsync(
            "/api/finance/consolidated",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_head_of_another_branch_sees_none_of_it()
    {
        await using var factory = await SeededAsync();

        await AddComponentAsync(factory, SeedOrganisation.Olivier, SeedOrganisation.Units.Development, 500m);

        var view = await ConsolidatedAsync(
            factory,
            SeedOrganisation.Laurent,
            nodeId: SeedOrganisation.Departments.InformationSystems);

        // Laurent heads Finance. The node is nameable — ids are not secret — but the money is not his to read.
        view.Node.Subtree.Total.ShouldBe(0m);
    }

    private static void AssertRollup(ConsolidatedNodeView node)
    {
        var capex = node.Own.Capex + node.Children.Sum(child => child.Subtree.Capex);
        var opex = node.Own.Opex + node.Children.Sum(child => child.Subtree.Opex);

        node.Subtree.Capex.ShouldBe(capex, $"capex at {node.Code}");
        node.Subtree.Opex.ShouldBe(opex, $"opex at {node.Code}");

        foreach (var child in node.Children)
        {
            AssertRollup(child);
        }
    }

    private static async Task<ConsolidatedViewModel> ConsolidatedAsync(
        CracraApplicationFactory factory,
        UserContext viewer,
        string? mode = null,
        Guid? nodeId = null)
    {
        factory.AsUser(viewer);

        var query = new List<string> { $"fy={Year}" };

        if (mode is not null)
        {
            query.Add($"mode={mode}");
        }

        if (nodeId is { } node)
        {
            query.Add($"nodeId={node}");
        }

        return (await factory.CreateClient().GetFromJsonAsync<ConsolidatedViewModel>(
            $"/api/finance/consolidated?{string.Join('&', query)}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task AddLicenseAsync(
        CracraApplicationFactory factory,
        UserContext head,
        string product,
        int seats,
        decimal unitCost,
        DateOnly? renewal = null)
    {
        factory.AsUser(head);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/finance/licenses",
            new
            {
                nodeId = SeedOrganisation.Departments.InformationSystems,
                productName = product,
                seats,
                unitCost,
                billingCycle = "yearly",
                renewalDate = renewal ?? new DateOnly(Year, 6, 1),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task AddComponentAsync(
        CracraApplicationFactory factory,
        UserContext head,
        Guid nodeId,
        decimal amount,
        string? treatment = null)
    {
        factory.AsUser(head);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/finance/components",
            new
            {
                nodeId,
                ownerNodeId = nodeId,
                kind = "cloud",
                label = "Cloud spend",
                treatment,
                amount,
                periodStart = new DateOnly(Year, 1, 1),
                periodEnd = new DateOnly(Year, 12, 31),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed record ConsolidatedViewModel(
        int FiscalYear,
        string Mode,
        ConsolidatedNodeView Node,
        bool Landed);

    private sealed record SplitView(decimal Capex, decimal Opex, decimal Excluded, decimal Total);

    private sealed record ConsolidatedNodeView(
        Guid NodeId,
        Guid? ParentId,
        int LevelNo,
        string Code,
        string Name,
        SplitView Direct,
        SplitView Items,
        SplitView Own,
        SplitView Subtree,
        decimal? PlannedAmount,
        decimal? Variance,
        IReadOnlyList<ConsolidatedNodeView> Children);

    private async Task<CracraApplicationFactory> SeededAsync()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();

        var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IKeycloakDirectoryClient>();
                services.AddSingleton<IKeycloakDirectoryClient>(keycloak);
            },
        };

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

            var context = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var ct = TestContext.Current.CancellationToken;

            // The container is shared. Money another scenario left behind would land in this one's totals.
            await context.Components.ExecuteDeleteAsync(ct);
            await context.Licenses.ExecuteDeleteAsync(ct);
            await context.ExternalWorkers.ExecuteDeleteAsync(ct);
            await context.Budgets.ExecuteDeleteAsync(ct);
        }

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        return factory;
    }
}
