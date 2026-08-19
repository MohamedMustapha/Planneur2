using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.Tests.Integration.Directory;

/// <summary>
/// Reconciliation against a real Postgres, real RLS and real policies.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DirectorySyncTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Sync_reproduces_the_fixture_organisation()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();
        await using var factory = Factory(keycloak);

        await ResetAsync(factory);
        await SyncAsync(factory);

        var (departments, units, people) = await CountsAsync(factory);

        // S1's acceptance criterion, literally: two departments, two units each, and every seeded person placed.
        departments.ShouldBe(2);
        units.ShouldBe(4);
        people.ShouldBe(keycloak.Users.Count);
    }

    [Fact]
    public async Task Departments_and_units_take_their_names_from_the_group_tree()
    {
        await using var factory = Factory(FakeKeycloakDirectory.SeededOrganisation());

        await ResetAsync(factory);
        await SyncAsync(factory);

        await using var scope = SystemScope(factory);
        var context = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

        var department = await context.Departments
            .SingleAsync(d => d.Id == SeedOrganisation.Departments.InformationSystems, TestContext.Current.CancellationToken);

        var unit = await context.Units
            .SingleAsync(u => u.Id == SeedOrganisation.Units.Infrastructure, TestContext.Current.CancellationToken);

        // Not an id-derived placeholder. A department called "11111111" is one nobody renames, because nobody
        // realises it was ever meant to be renamed.
        department.Code.ShouldBe("dsi");
        unit.Name.ShouldBe("Infrastructure & Réseaux");
    }

    [Fact]
    public async Task Running_sync_twice_changes_nothing_the_first_run_did_not()
    {
        await using var factory = Factory(FakeKeycloakDirectory.SeededOrganisation());

        await ResetAsync(factory);

        var first = await SyncAsync(factory);
        var second = await SyncAsync(factory);

        first.PeopleCreated.ShouldBeGreaterThan(0);

        // Idempotency is what makes it safe to run this on startup, on a timer and on demand at once. A second run
        // that reported changes would also republish an integration event per person, hourly, forever.
        second.PeopleCreated.ShouldBe(0);
        second.PeopleUpdated.ShouldBe(0);
        second.PeopleDeactivated.ShouldBe(0);
        second.DepartmentsCreated.ShouldBe(0);
        second.UnitsCreated.ShouldBe(0);
    }

    [Fact]
    public async Task Someone_who_leaves_the_directory_is_deactivated_not_deleted()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();
        await using var factory = Factory(keycloak);

        await ResetAsync(factory);
        await SyncAsync(factory);

        keycloak.Users.RemoveAll(user => user.Id == SeedOrganisation.Mehdi.UserId);

        var result = await SyncAsync(factory);

        result.PeopleDeactivated.ShouldBe(1);

        await using var scope = SystemScope(factory);
        var context = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

        var person = await context.People
            .SingleOrDefaultAsync(p => p.Id == SeedOrganisation.Mehdi.UserId, TestContext.Current.CancellationToken);

        // The row survives. Their logged activity, kudos and project history all point at it, and a hard delete
        // would orphan every one of them.
        person.ShouldNotBeNull();
        person.Active.ShouldBeFalse();
    }

    [Fact]
    public async Task A_reactivated_person_comes_back_rather_than_being_recreated()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();
        var mehdi = keycloak.Users.Single(user => user.Id == SeedOrganisation.Mehdi.UserId);

        await using var factory = Factory(keycloak);

        await ResetAsync(factory);
        await SyncAsync(factory);

        keycloak.Users.RemoveAll(user => user.Id == SeedOrganisation.Mehdi.UserId);
        await SyncAsync(factory);

        keycloak.Users.Add(mehdi);
        await SyncAsync(factory);

        await using var scope = SystemScope(factory);
        var context = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

        var matches = await context.People
            .Where(p => p.Id == SeedOrganisation.Mehdi.UserId)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Keyed on the Keycloak subject, so a returning colleague reoccupies their old row and everything that
        // ever referenced them still resolves.
        matches.Count.ShouldBe(1);
        matches[0].Active.ShouldBeTrue();
    }

    [Fact]
    public async Task A_move_between_units_is_applied()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();
        await using var factory = Factory(keycloak);

        await ResetAsync(factory);
        await SyncAsync(factory);

        var camille = keycloak.Users.Single(user => user.Id == SeedOrganisation.Camille.UserId);
        keycloak.Users.Remove(camille);
        keycloak.Users.Add(camille with
        {
            Attributes = new Dictionary<string, List<string>>(camille.Attributes!)
            {
                ["unit_id"] = [SeedOrganisation.Units.Development.ToString()],
            },
        });

        var result = await SyncAsync(factory);

        result.PeopleUpdated.ShouldBe(1);

        await using var scope = SystemScope(factory);
        var context = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

        var person = await context.People
            .SingleAsync(p => p.Id == SeedOrganisation.Camille.UserId, TestContext.Current.CancellationToken);

        person.PrimaryUnitId.ShouldBe(SeedOrganisation.Units.Development);
    }

    [Fact]
    public async Task A_user_pointing_at_an_undeclared_unit_is_skipped_rather_than_inventing_one()
    {
        var keycloak = FakeKeycloakDirectory.SeededOrganisation();

        keycloak.Users.Add(FakeKeycloakDirectory.User(
            SeedOrganisation.Camille with
            {
                UserId = Guid.Parse("c0000000-0000-0000-0000-0000000000ff"),
                UserName = "ghost.user",
                UnitId = Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
            },
            "Ghost",
            "User",
            "dev"));

        await using var factory = Factory(keycloak);

        await ResetAsync(factory);
        await SyncAsync(factory);

        var (_, units, people) = await CountsAsync(factory);

        // Creating the unit on the fly would produce one nobody manages and an RLS scope nobody intended. Four
        // units, and the ghost is not in the directory.
        units.ShouldBe(4);
        people.ShouldBe(keycloak.Users.Count - 1);
    }

    [Fact]
    public async Task Sync_creates_a_configuration_for_every_department_it_discovers()
    {
        await using var factory = Factory(FakeKeycloakDirectory.SeededOrganisation());

        await ResetAsync(factory);
        await SyncAsync(factory);

        await using var scope = SystemScope(factory);
        var context = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

        var configs = await context.DepartmentConfigs.CountAsync(TestContext.Current.CancellationToken);

        // A department with no config is a department whose boards cannot render — S5, S6 and S9 all read it.
        configs.ShouldBe(2);
    }

    // --- Helpers -------------------------------------------------------------------------------------------------

    private CracraApplicationFactory Factory(FakeKeycloakDirectory keycloak) =>
        new(postgres.AdminConnectionString)
        {
            ConfigureAdditionalServices = services =>
            {
                services.RemoveAll<IKeycloakDirectoryClient>();
                services.AddSingleton<IKeycloakDirectoryClient>(keycloak);
            },
        };

    private static async Task<DirectorySyncResult> SyncAsync(CracraApplicationFactory factory) =>
        await factory.Services
            .GetRequiredService<IDirectorySynchronizer>()
            .SynchronizeAsync(TestContext.Current.CancellationToken);

    private static AsyncServiceScope SystemScope(CracraApplicationFactory factory)
    {
        var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        return scope;
    }

    /// <summary>
    /// Empties the directory so each scenario starts from nothing. The Postgres container is shared for speed, and
    /// sync is defined by what it reconciles <em>against</em> — leftovers from another test would be reconciled too.
    /// </summary>
    private static async Task ResetAsync(CracraApplicationFactory factory)
    {
        await using var scope = SystemScope(factory);
        var context = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
        var ct = TestContext.Current.CancellationToken;

        await context.PersonFunctionalRoles.ExecuteDeleteAsync(ct);
        await context.PersonUnits.ExecuteDeleteAsync(ct);
        await context.People.ExecuteDeleteAsync(ct);
        await context.DepartmentConfigAudits.ExecuteDeleteAsync(ct);
        await context.DepartmentConfigs.ExecuteDeleteAsync(ct);
        await context.Units.ExecuteDeleteAsync(ct);
        await context.Departments.ExecuteDeleteAsync(ct);
    }

    private static async Task<(int Departments, int Units, int People)> CountsAsync(CracraApplicationFactory factory)
    {
        await using var scope = SystemScope(factory);
        var context = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
        var ct = TestContext.Current.CancellationToken;

        return (
            await context.Departments.CountAsync(ct),
            await context.Units.CountAsync(ct),
            await context.People.CountAsync(ct));
    }
}
