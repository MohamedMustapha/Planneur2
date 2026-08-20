using Cracra.BuildingBlocks.Testing;
using Cracra.Modules.Directory.Sync;

namespace Cracra.Tests.Integration.Directory;

/// <summary>
/// A Keycloak the tests can rewrite between sync runs.
/// </summary>
/// <remarks>
/// Faked rather than containerised because these tests are about the <em>reconciliation</em> — does a second run
/// change nothing, does a departure deactivate rather than delete — and driving those cases through a real
/// Keycloak would mean mutating a realm over the admin API to set up each one. The realm's own correctness is
/// covered where it belongs: the Playwright suite logs in through it for real.
/// </remarks>
public sealed class FakeKeycloakDirectory : IKeycloakDirectoryClient
{
    public List<KeycloakUser> Users { get; } = [];

    public List<KeycloakGroup> Groups { get; } = [];

    public Task<IReadOnlyList<KeycloakUser>> GetUsersAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<KeycloakUser>>([.. Users]);

    public Task<IReadOnlyList<KeycloakGroup>> GetGroupsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<KeycloakGroup>>([.. Groups]);

    /// <summary>
    /// The fixture org S1's acceptance criteria name: two departments, two units each, and the ten seeded people —
    /// the same ids the realm export and <see cref="SeedOrganisation"/> use.
    /// </summary>
    public static FakeKeycloakDirectory SeededOrganisation()
    {
        var keycloak = new FakeKeycloakDirectory();

        keycloak.Groups.Add(Department(
            "dsi",
            SeedOrganisation.Departments.InformationSystems,
            [
                ("infra", SeedOrganisation.Units.Infrastructure, "Infrastructure & Réseaux"),
                ("etudes", SeedOrganisation.Units.Development, "Études & Développement"),
            ]));

        keycloak.Groups.Add(Department(
            "daf",
            SeedOrganisation.Departments.Finance,
            [
                ("compta", SeedOrganisation.Units.Accounting, "Comptabilité"),
                ("controle", SeedOrganisation.Units.Controlling, "Contrôle de gestion"),
            ]));

        keycloak.Users.AddRange(
        [
            User(SeedOrganisation.Camille, "Camille", "Villeneuve", "architecte"),
            User(SeedOrganisation.Mehdi, "Mehdi", "Sadaoui", "dev"),
            User(SeedOrganisation.Thomas, "Thomas", "Berthier", "tech-lead"),
            User(SeedOrganisation.Olivier, "Olivier", "Marchand", "chef-de-pole"),
            User(SeedOrganisation.Sofia, "Sofia", "Navarro", "comptable"),
            User(SeedOrganisation.Nadia, "Nadia", "Kessler", "chef-de-pole"),
        ]);

        return keycloak;
    }

    public static KeycloakGroup Department(
        string code,
        Guid departmentId,
        (string Code, Guid Id, string Name)[] units) =>
        new(
            Guid.CreateVersion7(),
            code,
            $"/{code}",
            new Dictionary<string, List<string>> { ["department_id"] = [departmentId.ToString()] },
            [
                .. units.Select(unit => new KeycloakGroup(
                    Guid.CreateVersion7(),
                    unit.Code,
                    $"/{code}/{unit.Code}",
                    new Dictionary<string, List<string>>
                    {
                        ["unit_id"] = [unit.Id.ToString()],
                        ["unit_name"] = [unit.Name],
                    },
                    null)),
            ]);

    public static KeycloakUser User(
        Cracra.BuildingBlocks.Web.Users.UserContext person,
        string firstName,
        string lastName,
        string functionalRole,
        bool enabled = true) =>
        new(
            person.UserId,
            person.UserName,
            firstName,
            lastName,
            $"{person.UserName}@cracra.local",
            enabled,
            new Dictionary<string, List<string>>
            {
                ["unit_id"] = [person.UnitId!.Value.ToString()],
                ["dept_ids"] = [.. person.DepartmentIds.Select(id => id.ToString())],
                ["functional_role"] = [functionalRole],
                ["locale"] = [person.Language],
            });
}
