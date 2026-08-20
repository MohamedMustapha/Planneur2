using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Directory;

/// <summary>
/// The executable form of <c>visibility-matrix.md</c> for the Directory, as conventions.md §6 requires: seed the
/// fixed org, then assert for every contextual role the exact set of rows they can see.
/// </summary>
/// <remarks>
/// Every assertion here goes through the HTTP surface rather than the DbContext, because that is the path a real
/// caller takes — endpoint policy, then the RLS session, then the policies. Testing the service directly would
/// skip the very layer being asserted.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class DirectoryVisibilityMatrixTests(PostgresFixture postgres)
{
    private sealed record MeResponse(
        Guid PersonId,
        string DisplayName,
        Guid? PrimaryUnitId,
        Guid? PrimaryDepartmentId,
        string UiLanguage,
        IReadOnlyList<UnitSummary> Units,
        IReadOnlyList<DepartmentSummary> Departments,
        IReadOnlyList<string> FunctionalRoleCodes,
        IReadOnlyList<string> ContextualRoles);

    [Fact]
    public async Task Me_returns_the_callers_own_record()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var me = await factory.CreateClient()
            .GetFromJsonAsync<MeResponse>("/api/directory/me", TestContext.Current.CancellationToken);

        me.ShouldNotBeNull();
        me.PersonId.ShouldBe(SeedOrganisation.Camille.UserId);
        me.DisplayName.ShouldBe("Camille Villeneuve");
        me.PrimaryUnitId.ShouldBe(SeedOrganisation.Units.Infrastructure);
        me.PrimaryDepartmentId.ShouldBe(SeedOrganisation.Departments.InformationSystems);
        me.FunctionalRoleCodes.ShouldContain("architecte");
        me.ContextualRoles.ShouldContain(ContextualRole.Member);
    }

    [Fact]
    public async Task Me_honours_the_language_from_the_directory()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Sofia);

        var me = await factory.CreateClient()
            .GetFromJsonAsync<MeResponse>("/api/directory/me", TestContext.Current.CancellationToken);

        // S1's acceptance criteria: "me drives the client context; language and timezone honoured."
        me!.UiLanguage.ShouldBe("fr");
    }

    [Fact]
    public async Task A_member_sees_their_own_department_and_no_other()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var departments = await factory.CreateClient()
            .GetFromJsonAsync<List<DepartmentSummary>>("/api/directory/departments", TestContext.Current.CancellationToken);

        departments.ShouldNotBeNull();
        departments.Select(d => d.Id).ShouldBe([SeedOrganisation.Departments.InformationSystems]);
    }

    [Fact]
    public async Task A_member_sees_colleagues_across_their_department_but_not_another_departments_people()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var people = await factory.CreateClient()
            .GetFromJsonAsync<List<PersonSummary>>("/api/directory/people", TestContext.Current.CancellationToken);

        people.ShouldNotBeNull();

        var visible = people.Select(person => person.Id).ToList();

        // The deliberate widening S1 states: directory rows are department-scoped for members, because you cannot
        // collaborate with colleagues you cannot see. It widens the *directory* only — activity stays unit-scoped.
        visible.ShouldContain(SeedOrganisation.Olivier.UserId);

        // And it stops at the department boundary. Sofia is in Finance.
        visible.ShouldNotContain(SeedOrganisation.Sofia.UserId);
    }

    [Fact]
    public async Task A_member_of_another_department_sees_their_own_people_only()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Sofia);

        var people = await factory.CreateClient()
            .GetFromJsonAsync<List<PersonSummary>>("/api/directory/people", TestContext.Current.CancellationToken);

        people.ShouldNotBeNull();

        var visible = people.Select(person => person.Id).ToList();

        visible.ShouldNotContain(SeedOrganisation.Camille.UserId);
        visible.ShouldContain(SeedOrganisation.Sofia.UserId);
    }

    [Fact]
    public async Task The_PMO_sees_every_department_and_everyone()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Nadia);

        var client = factory.CreateClient();

        var departments = await client.GetFromJsonAsync<List<DepartmentSummary>>(
            "/api/directory/departments", TestContext.Current.CancellationToken);

        var people = await client.GetFromJsonAsync<List<PersonSummary>>(
            "/api/directory/people", TestContext.Current.CancellationToken);

        departments!.Count.ShouldBe(2);

        var visible = people!.Select(person => person.Id).ToList();

        visible.ShouldContain(SeedOrganisation.Sofia.UserId);
        visible.ShouldContain(SeedOrganisation.Camille.UserId);
    }

    [Fact]
    public async Task A_head_reads_their_own_departments_configuration()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().GetAsync(
            $"/api/directory/departments/{SeedOrganisation.Departments.InformationSystems}/config",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_head_cannot_read_another_departments_configuration()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().GetAsync(
            $"/api/directory/departments/{SeedOrganisation.Departments.Finance}/config",
            TestContext.Current.CancellationToken);

        // 404, not 403. RLS removed the row, so the endpoint genuinely cannot tell "you may not see it" from
        // "it does not exist" — and neither can a prober.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_head_can_retune_their_own_department()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/departments/{SeedOrganisation.Departments.InformationSystems}/config",
            ConfigPayload(weeklyTargetHours: 39m),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<DepartmentConfigSnapshot>(
            TestContext.Current.CancellationToken);

        updated!.WeeklyTargetHours.ShouldBe(39m);
        updated.Version.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task A_head_cannot_retune_another_department()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/departments/{SeedOrganisation.Departments.Finance}/config",
            ConfigPayload(weeklyTargetHours: 20m),
            TestContext.Current.CancellationToken);

        // The endpoint policy lets any head through the door — it cannot know which department is in the URL. The
        // RLS write policy is what stops a Finance head retuning IT, and this asserts that it does.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_cannot_retune_their_own_department()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Camille);

        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/departments/{SeedOrganisation.Departments.InformationSystems}/config",
            ConfigPayload(weeklyTargetHours: 20m),
            TestContext.Current.CancellationToken);

        // Refused at the door this time: the policy requires a head, so it never reaches RLS.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_configuration_change_is_audited()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        await factory.CreateClient().PutAsJsonAsync(
            $"/api/directory/departments/{SeedOrganisation.Departments.InformationSystems}/config",
            ConfigPayload(weeklyTargetHours: 37m),
            TestContext.Current.CancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var context = scope.ServiceProvider.GetRequiredService<Cracra.Modules.Directory.Data.DirectoryDbContext>();

        var audits = context.DepartmentConfigAudits
            .Where(audit => audit.DepartmentId == SeedOrganisation.Departments.InformationSystems)
            .ToList();

        // "Per-department config is editable and versioned (audit trail)" — someone asks why the weekly rule
        // changed three months later, and the answer has to be recoverable.
        audits.ShouldNotBeEmpty();
        audits[^1].ChangedBy.ShouldBe(SeedOrganisation.Olivier.UserId);
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        await using var factory = await SeededAsync();
        factory.AsAnonymous();

        var response = await factory.CreateClient()
            .GetAsync("/api/directory/people", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Helpers -------------------------------------------------------------------------------------------------

    private static object ConfigPayload(decimal weeklyTargetHours) => new
    {
        activityTaxonomyJson = """{"build":{"labelKey":"activity.build"}}""",
        roleLabelsJson = "{}",
        kudoRulesJson = """{"mode":"counter"}""",
        defaultBoardLayout = "week",
        iterationPresetsJson = """["1w","2w"]""",
        weeklyTargetHours,
        enforceWeeklyTarget = false,
    };

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

        await ResetAndSyncAsync(factory);

        return factory;
    }

    private static async Task ResetAndSyncAsync(CracraApplicationFactory factory)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

            var context = scope.ServiceProvider
                .GetRequiredService<Cracra.Modules.Directory.Data.DirectoryDbContext>();
            var ct = TestContext.Current.CancellationToken;

            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ExecuteDeleteAsync(context.PersonFunctionalRoles, ct);
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ExecuteDeleteAsync(context.PersonUnits, ct);
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ExecuteDeleteAsync(context.People, ct);
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ExecuteDeleteAsync(context.DepartmentConfigAudits, ct);
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ExecuteDeleteAsync(context.DepartmentConfigs, ct);
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ExecuteDeleteAsync(context.Units, ct);
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ExecuteDeleteAsync(context.Departments, ct);
        }

        await factory.Services
            .GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);
    }
}
