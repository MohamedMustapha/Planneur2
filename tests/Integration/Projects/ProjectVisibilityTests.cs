using System.Net;
using System.Net.Http.Json;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Directory.Sync;
using Cracra.Modules.Projects.Contracts;
using Cracra.Tests.Integration.Directory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Projects;

/// <summary>
/// Projects end to end, through HTTP, with real RLS.
/// </summary>
/// <remarks>
/// S3 is where the cross-department knowledge-flow rule stops being hypothetical: until now there were no projects
/// for it to apply to. The fixture builds the case the rule exists for — one project led by IS with a Finance
/// contributor — and asserts what each role sees of it.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ProjectVisibilityTests(PostgresFixture postgres)
{
    private static readonly Guid DevRole = Guid.Parse("f0000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task A_head_can_create_a_cross_department_project()
    {
        await using var factory = await SeededAsync();

        var projectId = await CreateCrossDepartmentProjectAsync(factory);

        projectId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task The_creator_can_read_their_own_project()
    {
        await using var factory = await SeededAsync();
        var projectId = await CreateCrossDepartmentProjectAsync(factory);

        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient()
            .GetAsync($"/api/projects/{projectId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_head_of_a_contributing_department_reads_the_project_and_its_foreign_members()
    {
        await using var factory = await SeededAsync();
        var projectId = await CreateCrossDepartmentProjectAsync(factory);

        // Laurent heads Finance. The project is led by IS; Finance merely contributes Sofia. The matrix says he
        // sees it — that is the cross-department knowledge flow, and it is the whole reason the rule exists.
        factory.AsUser(SeedOrganisation.Laurent);

        var team = await factory.CreateClient()
            .GetFromJsonAsync<ProjectTeam>($"/api/projects/{projectId}/team", TestContext.Current.CancellationToken);

        team.ShouldNotBeNull();
        team.TotalMembers.ShouldBe(2);

        // And he sees the IS members too, not just his own department's.
        team.Departments.Select(department => department.DepartmentId)
            .ShouldContain(SeedOrganisation.Departments.InformationSystems);
    }

    [Fact]
    public async Task A_member_on_the_project_reads_the_team()
    {
        await using var factory = await SeededAsync();
        var projectId = await CreateCrossDepartmentProjectAsync(factory);

        // Sofia is a plain member in Finance with no head role at all. She is on the project, so she sees it.
        factory.AsUser(SeedOrganisation.Sofia);

        var team = await factory.CreateClient()
            .GetFromJsonAsync<ProjectTeam>($"/api/projects/{projectId}/team", TestContext.Current.CancellationToken);

        team.ShouldNotBeNull();
        team.TotalMembers.ShouldBe(2);
    }

    [Fact]
    public async Task An_unrelated_member_sees_nothing_of_the_project()
    {
        await using var factory = await SeededAsync();
        var projectId = await CreateCrossDepartmentProjectAsync(factory);

        // Mehdi is in IS but not on the project. Being in the lead department is not enough — membership or a head
        // role is, and he has neither.
        factory.AsUser(SeedOrganisation.Mehdi);

        var response = await factory.CreateClient()
            .GetAsync($"/api/projects/{projectId}", TestContext.Current.CancellationToken);

        // 404, not 403: RLS removed the row, so the endpoint genuinely cannot distinguish "may not see" from
        // "does not exist" — and neither can a prober enumerating ids.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unrelated_member_does_not_see_the_project_in_the_list()
    {
        await using var factory = await SeededAsync();
        await CreateCrossDepartmentProjectAsync(factory);

        factory.AsUser(SeedOrganisation.Mehdi);

        var projects = await factory.CreateClient()
            .GetFromJsonAsync<List<ProjectSummary>>("/api/projects", TestContext.Current.CancellationToken);

        projects.ShouldNotBeNull();
        projects.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_PMO_sees_every_project()
    {
        await using var factory = await SeededAsync();
        await CreateCrossDepartmentProjectAsync(factory);

        factory.AsUser(SeedOrganisation.Nadia);

        var projects = await factory.CreateClient()
            .GetFromJsonAsync<List<ProjectSummary>>("/api/projects", TestContext.Current.CancellationToken);

        projects!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Removing_a_member_revokes_their_access_immediately()
    {
        await using var factory = await SeededAsync();
        var projectId = await CreateCrossDepartmentProjectAsync(factory);

        factory.AsUser(SeedOrganisation.Sofia);
        var client = factory.CreateClient();

        (await client.GetAsync($"/api/projects/{projectId}", TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        factory.AsUser(SeedOrganisation.Olivier);

        await client.DeleteAsync(
            $"/api/projects/{projectId}/members/{SeedOrganisation.Sofia.UserId}",
            TestContext.Current.CancellationToken);

        factory.AsUser(SeedOrganisation.Sofia);

        // The reason the access projection is written synchronously rather than through the outbox. If it drained
        // asynchronously, Sofia would keep reading a project she was just removed from for as long as the queue
        // took — which is not eventual consistency, it is a hole.
        (await client.GetAsync($"/api/projects/{projectId}", TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_cannot_be_added_from_a_department_the_project_does_not_have()
    {
        await using var factory = await SeededAsync();

        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();

        // Created with IS only — Finance is not a contributing department.
        var created = await client.PostAsJsonAsync(
            "/api/projects",
            new
            {
                code = "PRJ-SOLO",
                name = "IS only",
                classification = "build",
                costAmount = 0m,
                costCurrency = "EUR",
                leadDepartmentId = SeedOrganisation.Departments.InformationSystems,
                contributingDepartmentIds = Array.Empty<Guid>(),
            },
            TestContext.Current.CancellationToken);

        var projectId = (await created.Content.ReadFromJsonAsync<CreatedResponse>(
            TestContext.Current.CancellationToken))!.Id;

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/members",
            new
            {
                personId = SeedOrganisation.Sofia.UserId,
                departmentId = SeedOrganisation.Departments.Finance,
                functionalRoleId = DevRole,
            },
            TestContext.Current.CancellationToken);

        // 422: well-formed request, domain said no.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_malformed_command_is_rejected_with_field_errors()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Olivier);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/projects",
            new
            {
                code = "",
                name = "",
                classification = "nonsense",
                costAmount = -5m,
                costCurrency = "XYZ",
                leadDepartmentId = SeedOrganisation.Departments.InformationSystems,
                contributingDepartmentIds = Array.Empty<Guid>(),
            },
            TestContext.Current.CancellationToken);

        // 400 from the validation behavior, distinct from the 422 a domain rule produces — the client can tell
        // "you typed it wrong" from "the rules forbid this".
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_plain_member_cannot_create_a_project()
    {
        await using var factory = await SeededAsync();
        factory.AsUser(SeedOrganisation.Mehdi);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/projects",
            new
            {
                code = "PRJ-NOPE",
                name = "Should not exist",
                classification = "build",
                costAmount = 0m,
                costCurrency = "EUR",
                leadDepartmentId = SeedOrganisation.Departments.InformationSystems,
                contributingDepartmentIds = Array.Empty<Guid>(),
            },
            TestContext.Current.CancellationToken);

        // Refused at the door by the endpoint policy; it never reaches RLS.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_team_is_grouped_by_department_then_function()
    {
        await using var factory = await SeededAsync();
        var projectId = await CreateCrossDepartmentProjectAsync(factory);

        factory.AsUser(SeedOrganisation.Nadia);

        var team = await factory.CreateClient()
            .GetFromJsonAsync<ProjectTeam>($"/api/projects/{projectId}/team", TestContext.Current.CancellationToken);

        // The shape the project view renders directly: department groups, function sub-groups, members inside.
        team!.Departments.Count.ShouldBe(2);
        team.Departments.ShouldAllBe(department => department.Functions.Count > 0);
        team.Departments.SelectMany(d => d.Functions).ShouldAllBe(function => function.Members.Count > 0);
    }

    // --- Fixture -------------------------------------------------------------------------------------------------

    private sealed record CreatedResponse(Guid Id);

    /// <summary>
    /// Creates the canonical cross-department project: led by IS, with Camille (IS) and Sofia (Finance) on it.
    /// </summary>
    private static async Task<Guid> CreateCrossDepartmentProjectAsync(CracraApplicationFactory factory)
    {
        factory.AsUser(SeedOrganisation.Olivier);
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var created = await client.PostAsJsonAsync(
            "/api/projects",
            new
            {
                code = "PRJ-M365",
                name = "Migration M365",
                description = "Cross-department migration",
                classification = "build",
                costAmount = 62400m,
                costCurrency = "EUR",
                leadDepartmentId = SeedOrganisation.Departments.InformationSystems,
                contributingDepartmentIds = new[] { SeedOrganisation.Departments.Finance },
            },
            ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var projectId = (await created.Content.ReadFromJsonAsync<CreatedResponse>(ct))!.Id;

        foreach (var (person, department) in new[]
                 {
                     (SeedOrganisation.Camille, SeedOrganisation.Departments.InformationSystems),
                     (SeedOrganisation.Sofia, SeedOrganisation.Departments.Finance),
                 })
        {
            var added = await client.PostAsJsonAsync(
                $"/api/projects/{projectId}/members",
                new
                {
                    personId = person.UserId,
                    departmentId = department,
                    functionalRoleId = DevRole,
                    allocationPercent = 50,
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

        var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IKeycloakDirectoryClient>();
                services.AddSingleton<IKeycloakDirectoryClient>(keycloak);
            },
        };

        await ResetAsync(factory);

        await factory.Services.GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

        return factory;
    }

    /// <summary>
    /// Clears projects, the access projection and the directory so each scenario starts from nothing. The Postgres
    /// container is shared for speed, and a project left behind would be counted by the list assertions.
    /// </summary>
    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var ct = TestContext.Current.CancellationToken;

        var projects = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Projects.Infrastructure.ProjectsDbContext>();

        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ExecuteDeleteAsync(projects.ProjectMembers, ct);
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ExecuteDeleteAsync(projects.ProjectDepartments, ct);
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ExecuteDeleteAsync(projects.Projects, ct);

        var access = scope.ServiceProvider.GetRequiredService<Cracra.Modules.Access.Data.AccessDbContext>();

        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ExecuteDeleteAsync(access.ProjectMemberships, ct);

        var directory = scope.ServiceProvider
            .GetRequiredService<Cracra.Modules.Directory.Data.DirectoryDbContext>();

        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ExecuteDeleteAsync(directory.PersonFunctionalRoles, ct);
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ExecuteDeleteAsync(directory.PersonUnits, ct);
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ExecuteDeleteAsync(directory.People, ct);
    }
}
