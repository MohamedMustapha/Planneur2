using Cracra.Modules.Projects.Domain;

namespace Cracra.Host.DevSeed;

/// <summary>
/// Someone the seeder can put on a project.
/// </summary>
/// <param name="Id">Matches the Keycloak account, so a browser session and a seeded row are the same person.</param>
/// <param name="UnitId">Copied onto every activity row, because that is what the RLS predicates compare.</param>
/// <param name="DepartmentId">The department they contribute from; it must be one the project has.</param>
/// <param name="FunctionalRoleId">One of the fixed ids the Directory migration inserts.</param>
/// <param name="AllocationPercent">What the project screen shows against their name.</param>
/// <param name="HoursPerDay">What they log on a working day. Kept under 7 so no week trips the 35h guardrail.</param>
/// <param name="Note">The French note written onto their entries — the dev box runs in French.</param>
internal sealed record SeedTeamMember(
    Guid Id,
    Guid UnitId,
    Guid DepartmentId,
    Guid FunctionalRoleId,
    int AllocationPercent,
    decimal HoursPerDay,
    string Note);

/// <summary>A project to seed, with the team that works on it.</summary>
internal sealed record SeedProject(
    string Code,
    string Name,
    string Description,
    Classification Classification,
    Money Cost,
    Guid OwnerPersonId,
    Guid LeadDepartmentId,
    IReadOnlyList<Guid> ContributingDepartmentIds,
    IReadOnlyList<SeedTeamMember> Team);

/// <summary>
/// The two projects the dev box comes up with, and who is on them.
/// </summary>
/// <remarks>
/// <para>
/// The ids are not invented here. Departments, units and people match <c>deploy/keycloak/build-realm.py</c> and
/// <c>SeedOrganisation</c> in the test fixtures, and the functional role ids match the fixed rows the Directory
/// migration inserts. That is the point: signing in as Camille in a browser and asserting "as Camille" in an
/// integration test have to be talking about one person, or the two suites never corroborate each other.
/// </para>
/// <para>
/// Restated here rather than referenced from <c>Cracra.BuildingBlocks.Testing</c>, which the host has no business
/// depending on. Two projects deliberately: one single-department BUILD, and one MIXED that Finance also
/// contributes to, so the cross-department read rule has something real to be visible on.
/// </para>
/// </remarks>
internal static class DevSeedCatalogue
{
    private static class Departments
    {
        public static readonly Guid InformationSystems = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid Finance = Guid.Parse("22222222-2222-2222-2222-222222222222");
    }

    private static class Units
    {
        public static readonly Guid Infrastructure = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        public static readonly Guid Development = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        public static readonly Guid Accounting = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    }

    private static class FunctionalRoles
    {
        public static readonly Guid Developer = Guid.Parse("f0000000-0000-0000-0000-000000000001");
        public static readonly Guid TechLead = Guid.Parse("f0000000-0000-0000-0000-000000000002");
        public static readonly Guid Architect = Guid.Parse("f0000000-0000-0000-0000-000000000003");
        public static readonly Guid UnitHead = Guid.Parse("f0000000-0000-0000-0000-000000000004");
        public static readonly Guid Accountant = Guid.Parse("f0000000-0000-0000-0000-000000000005");
    }

    private static class People
    {
        public static readonly Guid Camille = Guid.Parse("c0000000-0000-0000-0000-000000000001");
        public static readonly Guid Mehdi = Guid.Parse("c0000000-0000-0000-0000-000000000002");
        public static readonly Guid Julie = Guid.Parse("c0000000-0000-0000-0000-000000000005");
        public static readonly Guid Sofia = Guid.Parse("c0000000-0000-0000-0000-000000000007");
        public static readonly Guid Nadia = Guid.Parse("c0000000-0000-0000-0000-000000000009");
        public static readonly Guid Pierre = Guid.Parse("c0000000-0000-0000-0000-00000000000a");
    }

    public static IReadOnlyList<SeedProject> Projects { get; } =
    [
        new SeedProject(
            "PRJ-2026-001",
            "Portail RH",
            "Refonte du portail RH : demandes de congés, notes de frais et entretiens annuels.",
            Classification.Build,
            new Money(120_000m, "EUR"),
            OwnerPersonId: People.Pierre,
            LeadDepartmentId: Departments.InformationSystems,
            ContributingDepartmentIds: [],
            Team:
            [
                // Pierre owns it and holds project-lead in the realm, so this is the project his own session leads.
                new SeedTeamMember(
                    People.Pierre,
                    Units.Development,
                    Departments.InformationSystems,
                    FunctionalRoles.TechLead,
                    AllocationPercent: 60,
                    HoursPerDay: 4m,
                    "Revue de conception et accompagnement de l'équipe"),
                new SeedTeamMember(
                    People.Julie,
                    Units.Development,
                    Departments.InformationSystems,
                    FunctionalRoles.Developer,
                    AllocationPercent: 80,
                    HoursPerDay: 6m,
                    "Développement du module de demande de congés"),

                // Mehdi sits in another unit of the same department: an ordinary member cannot see his activity,
                // his unit head can, and the project screen lists him either way.
                new SeedTeamMember(
                    People.Mehdi,
                    Units.Infrastructure,
                    Departments.InformationSystems,
                    FunctionalRoles.Developer,
                    AllocationPercent: 40,
                    HoursPerDay: 3m,
                    "Préparation des environnements et de la chaîne de déploiement"),
            ]),

        new SeedProject(
            "PRJ-2026-002",
            "Refonte Facturation",
            "Reprise de la chaîne de facturation et maintien en condition opérationnelle de l'existant.",
            Classification.Mixed,
            new Money(85_000m, "EUR"),
            OwnerPersonId: People.Nadia,
            LeadDepartmentId: Departments.InformationSystems,

            // Finance contributes, which is what makes this project visible to Laurent as a Finance head — the
            // cross-department knowledge-flow rule, with a row behind it rather than only a policy.
            ContributingDepartmentIds: [Departments.Finance],
            Team:
            [
                new SeedTeamMember(
                    People.Nadia,
                    Units.Development,
                    Departments.InformationSystems,
                    FunctionalRoles.UnitHead,
                    AllocationPercent: 30,
                    HoursPerDay: 2.5m,
                    "Pilotage et coordination inter-directions"),
                new SeedTeamMember(
                    People.Camille,
                    Units.Infrastructure,
                    Departments.InformationSystems,
                    FunctionalRoles.Architect,
                    AllocationPercent: 50,
                    HoursPerDay: 4.5m,
                    "Architecture de la reprise de données et exploitation courante"),
                new SeedTeamMember(
                    People.Sofia,
                    Units.Accounting,
                    Departments.Finance,
                    FunctionalRoles.Accountant,
                    AllocationPercent: 50,
                    HoursPerDay: 3.5m,
                    "Recette comptable et validation des écritures"),
            ]),
    ];
}
