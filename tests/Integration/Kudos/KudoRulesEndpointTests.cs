using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Kudos.Contracts;
using Cracra.Tests.Integration.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Kudos;

/// <summary>
/// The one endpoint that answers about somebody else's department.
/// </summary>
/// <remarks>
/// Kudos resolves a recipient's placement through the reference reader, deliberately outside the caller's own
/// visibility — that is what lets a project teammate in a contributing department be recognised at all. This test
/// exists because that same lookup, exposed without a guard, would be an oracle for "which department is this
/// person in", answerable by anybody holding a person id.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class KudoRulesEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_rules_for_a_unit_peer_are_their_departments()
    {
        await using var factory = await SeededAsync();

        await ConfigureAsync(factory, """{"mode":"points"}""");

        var rules = await RulesAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Mehdi.UserId);

        rules.DepartmentId.ShouldBe(SeedOrganisation.Departments.InformationSystems);
        rules.ShowsPoints.ShouldBeTrue();
    }

    [Fact]
    public async Task Asking_about_a_stranger_answers_about_yourself()
    {
        await using var factory = await SeededAsync();

        // Sofia is in Finance, and nothing connects Camille to her. The answer is Camille's own department's
        // rules — the same thing she would have got by not naming anybody.
        var rules = await RulesAsync(factory, SeedOrganisation.Camille, SeedOrganisation.Sofia.UserId);

        rules.DepartmentId.ShouldBe(SeedOrganisation.Departments.InformationSystems);
    }

    [Fact]
    public async Task Naming_nobody_answers_about_your_own_department()
    {
        await using var factory = await SeededAsync();

        var rules = await RulesAsync(factory, SeedOrganisation.Sofia, personId: null);

        rules.DepartmentId.ShouldBe(SeedOrganisation.Departments.Finance);
        rules.Categories.Select(category => category.Code).ShouldContain("above-and-beyond");

        // A department that configured nothing gets the platform's five categories and no score.
        rules.Mode.ShouldBe(KudoModes.Counter);
        rules.ShowsPoints.ShouldBeFalse();
    }

    // --- Fixture ---------------------------------------------------------------------------------------------

    private static async Task<KudoRulesView> RulesAsync(
        CracraApplicationFactory factory,
        IUserContext caller,
        Guid? personId)
    {
        factory.AsUser(caller);

        var query = personId is { } id ? $"?personId={id}" : string.Empty;

        return (await factory.CreateClient().GetFromJsonAsync<KudoRulesView>(
            $"/api/kudos/rules{query}",
            TestContext.Current.CancellationToken))!;
    }

    private static async Task ConfigureAsync(CracraApplicationFactory factory, string kudoRulesJson)
    {
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/departments/{SeedOrganisation.Departments.InformationSystems}/config",
            new
            {
                activityTaxonomyJson = "{}",
                roleLabelsJson = "{}",
                kudoRulesJson,
                defaultBoardLayout = "week",
                iterationPresetsJson = """["1w","2w","1m"]""",
                weeklyTargetHours = 35m,
                enforceWeeklyTarget = false,
            },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

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

            var ct = TestContext.Current.CancellationToken;

            var kudos = scope.ServiceProvider
                .GetRequiredService<Cracra.Modules.Kudos.Infrastructure.KudosDbContext>();

            await kudos.Kudos.ExecuteDeleteAsync(ct);

            var directory = scope.ServiceProvider
                .GetRequiredService<Cracra.Modules.Directory.Data.DirectoryDbContext>();

            await directory.DepartmentConfigs.ExecuteUpdateAsync(
                setters => setters.SetProperty(config => config.KudoRulesJson, "{}"),
                ct);
        }

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        return factory;
    }
}
