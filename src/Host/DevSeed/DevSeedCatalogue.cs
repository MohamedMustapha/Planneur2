using Cracra.Modules.Projects.Domain;

namespace Cracra.Host.DevSeed;

/// <summary>
/// Whether someone's work on a project is BUILD, RUN, or both.
/// </summary>
/// <remarks>
/// Per member rather than per project, because that is how delivery actually looks: on the same project a
/// developer ships features while the helpdesk absorbs the bugs those features produce. A project-level flag would
/// force one answer on both of them and make S8's BUILD-versus-RUN split a fiction.
/// </remarks>
internal enum SeedWorkNature
{
    /// <summary>Everything logged is <c>project-build</c>.</summary>
    Build,

    /// <summary>Everything logged is <c>project-run</c>.</summary>
    Run,

    /// <summary>Alternates by day — an architect designing the new chain while keeping the old one alive.</summary>
    Alternating,
}

/// <summary>
/// Someone the seeder can put on a project.
/// </summary>
/// <param name="Id">Matches the Keycloak account, so a browser session and a seeded row are the same person.</param>
/// <param name="UnitId">Copied onto every activity row, because that is what the RLS predicates compare.</param>
/// <param name="DepartmentId">The department they contribute from; it must be one the project has.</param>
/// <param name="FunctionalRoleId">One of the fixed ids the Directory migrations insert.</param>
/// <param name="AllocationPercent">What the project screen shows against their name.</param>
/// <param name="HoursPerDay">What they log on a working day. Kept under 7 so no week trips the 35h guardrail.</param>
/// <param name="Nature">BUILD, RUN or both — see <see cref="SeedWorkNature"/>.</param>
/// <param name="Tasks">
/// The French task titles their entries rotate through — the dev box runs in French. Several rather than one, so a
/// week on the timeline reads as a week of work rather than as the same block repeated five times.
/// </param>
internal sealed record SeedTeamMember(
    Guid Id,
    Guid UnitId,
    Guid DepartmentId,
    Guid FunctionalRoleId,
    int AllocationPercent,
    decimal HoursPerDay,
    SeedWorkNature Nature,
    IReadOnlyList<string> Tasks);

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
/// migrations insert. That is the point: signing in as Camille in a browser and asserting "as Camille" in an
/// integration test have to be talking about one person, or the two suites never corroborate each other.
/// </para>
/// <para>
/// Restated here rather than referenced from <c>Cracra.BuildingBlocks.Testing</c>, which the host has no business
/// depending on. Two projects deliberately, and between them every seeded account is on a team: a dev box where
/// half the logins land on an empty board teaches nothing about the product.
/// </para>
/// <para>
/// The trades are drawn as the org would draw them. Security does homologation and audit; Ops builds the
/// production environment and deploys; Design designs; Development ships sprint features; the helpdesk absorbs
/// RUN; the PO runs workshops and the COPIL. Julie is on both projects on purpose — BUILD on one and RUN on the
/// other — because a developer split between a new build and the maintenance of what they shipped last year is
/// the ordinary case, and it is exactly the case a per-project BUILD/RUN flag could never express.
/// </para>
/// </remarks>
internal static class DevSeedCatalogue
{
    private static class Departments
    {
        public static readonly Guid InformationSystems = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid Finance = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public static readonly Guid Communication = Guid.Parse("33333333-3333-3333-3333-333333333333");
    }

    private static class Units
    {
        public static readonly Guid Ops = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        public static readonly Guid Development = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        public static readonly Guid Security = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
        public static readonly Guid Helpdesk = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");
        public static readonly Guid Transformation = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005");
        public static readonly Guid Accounting = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        public static readonly Guid Controlling = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        public static readonly Guid Design = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    }

    private static class FunctionalRoles
    {
        public static readonly Guid Developer = Guid.Parse("f0000000-0000-0000-0000-000000000001");
        public static readonly Guid TechLead = Guid.Parse("f0000000-0000-0000-0000-000000000002");
        public static readonly Guid Architect = Guid.Parse("f0000000-0000-0000-0000-000000000003");
        public static readonly Guid UnitHead = Guid.Parse("f0000000-0000-0000-0000-000000000004");
        public static readonly Guid Accountant = Guid.Parse("f0000000-0000-0000-0000-000000000005");
        public static readonly Guid SeniorAccountant = Guid.Parse("f0000000-0000-0000-0000-000000000006");
        public static readonly Guid Ops = Guid.Parse("f0000000-0000-0000-0000-000000000007");
        public static readonly Guid SecurityOfficer = Guid.Parse("f0000000-0000-0000-0000-000000000008");
        public static readonly Guid Support = Guid.Parse("f0000000-0000-0000-0000-000000000009");
        public static readonly Guid ProductOwner = Guid.Parse("f0000000-0000-0000-0000-00000000000a");
        public static readonly Guid Pmo = Guid.Parse("f0000000-0000-0000-0000-00000000000b");
        public static readonly Guid Designer = Guid.Parse("f0000000-0000-0000-0000-00000000000c");
        public static readonly Guid Director = Guid.Parse("f0000000-0000-0000-0000-00000000000d");
        public static readonly Guid DeputyDirector = Guid.Parse("f0000000-0000-0000-0000-00000000000e");
    }

    private static class People
    {
        // DSI — direction
        public static readonly Guid Olivier = Guid.Parse("c0000000-0000-0000-0000-000000000006");
        public static readonly Guid Helene = Guid.Parse("c0000000-0000-0000-0000-00000000000b");

        // DSI — exploitation & production
        public static readonly Guid Thomas = Guid.Parse("c0000000-0000-0000-0000-000000000004");
        public static readonly Guid Camille = Guid.Parse("c0000000-0000-0000-0000-000000000001");
        public static readonly Guid Mehdi = Guid.Parse("c0000000-0000-0000-0000-000000000002");
        public static readonly Guid Anais = Guid.Parse("c0000000-0000-0000-0000-000000000003");
        public static readonly Guid Tarek = Guid.Parse("c0000000-0000-0000-0000-000000000014");

        // DSI — études & développement
        public static readonly Guid Karim = Guid.Parse("c0000000-0000-0000-0000-00000000000c");
        public static readonly Guid Pierre = Guid.Parse("c0000000-0000-0000-0000-00000000000a");
        public static readonly Guid Julie = Guid.Parse("c0000000-0000-0000-0000-000000000005");
        public static readonly Guid Lea = Guid.Parse("c0000000-0000-0000-0000-00000000000d");

        // DSI — sécurité & conformité
        public static readonly Guid Marc = Guid.Parse("c0000000-0000-0000-0000-00000000000e");
        public static readonly Guid Ines = Guid.Parse("c0000000-0000-0000-0000-00000000000f");

        // DSI — support & assistance
        public static readonly Guid Yann = Guid.Parse("c0000000-0000-0000-0000-000000000010");
        public static readonly Guid Fatou = Guid.Parse("c0000000-0000-0000-0000-000000000011");

        // DSI — transformation digitale
        public static readonly Guid Sebastien = Guid.Parse("c0000000-0000-0000-0000-000000000012");
        public static readonly Guid Nadia = Guid.Parse("c0000000-0000-0000-0000-000000000009");
        public static readonly Guid Claire = Guid.Parse("c0000000-0000-0000-0000-000000000013");

        // Communication
        public static readonly Guid Valerie = Guid.Parse("c0000000-0000-0000-0000-000000000015");
        public static readonly Guid Hugo = Guid.Parse("c0000000-0000-0000-0000-000000000016");
        public static readonly Guid Amina = Guid.Parse("c0000000-0000-0000-0000-000000000017");

        // DAF
        public static readonly Guid Laurent = Guid.Parse("c0000000-0000-0000-0000-000000000008");
        public static readonly Guid Sofia = Guid.Parse("c0000000-0000-0000-0000-000000000007");
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

            // Communication contributes the design studio. Without the department on the project, AddMember
            // refuses its people — which is the rule working, not something to route around.
            ContributingDepartmentIds: [Departments.Communication],
            Team:
            [
                // --- Direction ---------------------------------------------------------------------------------
                new SeedTeamMember(
                    People.Olivier,
                    Units.Development,
                    Departments.InformationSystems,
                    FunctionalRoles.Director,
                    AllocationPercent: 15,
                    HoursPerDay: 1m,
                    SeedWorkNature.Build,
                    ["Arbitrage budgétaire portail RH", "Comité de direction SI", "Revue de trajectoire"]),

                // --- Études & développement --------------------------------------------------------------------
                new SeedTeamMember(
                    People.Karim,
                    Units.Development,
                    Departments.InformationSystems,
                    FunctionalRoles.UnitHead,
                    AllocationPercent: 30,
                    HoursPerDay: 2m,
                    SeedWorkNature.Build,
                    ["Capacité équipe et affectations", "Revue de code transverse", "Point hebdomadaire du pôle"]),

                // Pierre owns it and holds project-lead in the realm, so this is the project his own session leads.
                new SeedTeamMember(
                    People.Pierre,
                    Units.Development,
                    Departments.InformationSystems,
                    FunctionalRoles.TechLead,
                    AllocationPercent: 60,
                    HoursPerDay: 4m,
                    SeedWorkNature.Build,
                    [
                        "Conception technique du module congés",
                        "Revue d'architecture applicative",
                        "Accompagnement de l'équipe — sprint 14",
                        "Préparation de la démo de fin de sprint",
                    ]),
                new SeedTeamMember(
                    People.Julie,
                    Units.Development,
                    Departments.InformationSystems,
                    FunctionalRoles.Developer,
                    AllocationPercent: 60,
                    HoursPerDay: 4.5m,
                    SeedWorkNature.Build,
                    [
                        "Sprint 14 — saisie d'une demande de congés",
                        "Sprint 14 — validation par le manager",
                        "Sprint 15 — soldes et compteurs",
                        "Tests d'intégration du parcours congés",
                    ]),
                new SeedTeamMember(
                    People.Lea,
                    Units.Development,
                    Departments.InformationSystems,
                    FunctionalRoles.Developer,
                    AllocationPercent: 80,
                    HoursPerDay: 6m,
                    SeedWorkNature.Build,
                    [
                        "Sprint 14 — écran de note de frais",
                        "Sprint 14 — pièces jointes et justificatifs",
                        "Sprint 15 — export comptable",
                        "Correction des retours de recette",
                    ]),

                // --- Exploitation & production -----------------------------------------------------------------
                new SeedTeamMember(
                    People.Mehdi,
                    Units.Ops,
                    Departments.InformationSystems,
                    FunctionalRoles.Ops,
                    AllocationPercent: 40,
                    HoursPerDay: 3m,
                    SeedWorkNature.Build,
                    [
                        "Montage de l'environnement de production",
                        "Chaîne de déploiement automatisée",
                        "Mise en production — sprint 14",
                        "Supervision et alerting du portail",
                    ]),

                // --- Sécurité & conformité ---------------------------------------------------------------------
                new SeedTeamMember(
                    People.Marc,
                    Units.Security,
                    Departments.InformationSystems,
                    FunctionalRoles.UnitHead,
                    AllocationPercent: 30,
                    HoursPerDay: 2m,
                    SeedWorkNature.Build,
                    [
                        "Dossier d'homologation du portail RH",
                        "Analyse de risque RGPD",
                        "Revue des habilitations applicatives",
                    ]),

                // --- Transformation digitale (PO / PMO) --------------------------------------------------------
                new SeedTeamMember(
                    People.Claire,
                    Units.Transformation,
                    Departments.InformationSystems,
                    FunctionalRoles.ProductOwner,
                    AllocationPercent: 55,
                    HoursPerDay: 4m,
                    SeedWorkNature.Build,
                    [
                        "Atelier métier — parcours congés",
                        "Affinage et priorisation du backlog",
                        "COPIL portail RH",
                        "Rédaction des critères d'acceptation",
                    ]),
                new SeedTeamMember(
                    People.Nadia,
                    Units.Transformation,
                    Departments.InformationSystems,
                    FunctionalRoles.Pmo,
                    AllocationPercent: 25,
                    HoursPerDay: 2m,
                    SeedWorkNature.Build,
                    ["Suivi de portefeuille", "Consolidation des jalons", "Reporting mensuel à la direction"]),

                // --- Support & assistance ----------------------------------------------------------------------
                // RUN on a BUILD project, and that is the point: the pilot users are already raising tickets.
                new SeedTeamMember(
                    People.Fatou,
                    Units.Helpdesk,
                    Departments.InformationSystems,
                    FunctionalRoles.Support,
                    AllocationPercent: 35,
                    HoursPerDay: 2.5m,
                    SeedWorkNature.Run,
                    [
                        "Traitement des tickets pilote",
                        "Anomalie — échec de connexion SSO",
                        "Anomalie — solde de congés erroné",
                        "Assistance aux utilisateurs pilotes",
                    ]),

                // --- Communication -----------------------------------------------------------------------------
                new SeedTeamMember(
                    People.Hugo,
                    Units.Design,
                    Departments.Communication,
                    FunctionalRoles.Designer,
                    AllocationPercent: 70,
                    HoursPerDay: 5m,
                    SeedWorkNature.Build,
                    [
                        "Maquettes du parcours congés",
                        "Design system — composants de formulaire",
                        "Prototype cliquable — note de frais",
                        "Tests d'utilisabilité",
                    ]),
                new SeedTeamMember(
                    People.Valerie,
                    Units.Design,
                    Departments.Communication,
                    FunctionalRoles.Director,
                    AllocationPercent: 15,
                    HoursPerDay: 1m,
                    SeedWorkNature.Build,
                    ["Direction artistique", "Validation de la charte", "Plan de communication interne"]),
            ]),

        new SeedProject(
            "PRJ-2026-002",
            "Refonte Facturation",
            "Reprise de la chaîne de facturation et maintien en condition opérationnelle de l'existant.",
            Classification.Mixed,
            new Money(85_000m, "EUR"),
            OwnerPersonId: People.Sebastien,
            LeadDepartmentId: Departments.InformationSystems,

            // Finance contributes, which is what makes this project visible to Laurent as a Finance head — the
            // cross-department knowledge-flow rule, with a row behind it rather than only a policy. Communication
            // is here too, for the designer reworking the invoice templates.
            ContributingDepartmentIds: [Departments.Finance, Departments.Communication],
            Team:
            [
                // --- Direction ---------------------------------------------------------------------------------
                new SeedTeamMember(
                    People.Helene,
                    Units.Transformation,
                    Departments.InformationSystems,
                    FunctionalRoles.DeputyDirector,
                    AllocationPercent: 25,
                    HoursPerDay: 2m,
                    SeedWorkNature.Alternating,
                    [
                        "Pilotage de la refonte facturation",
                        "Arbitrage inter-directions",
                        "Revue de l'exploitation courante",
                    ]),
                new SeedTeamMember(
                    People.Sebastien,
                    Units.Transformation,
                    Departments.InformationSystems,
                    FunctionalRoles.UnitHead,
                    AllocationPercent: 35,
                    HoursPerDay: 2.5m,
                    SeedWorkNature.Build,
                    ["Cadrage de la reprise de données", "Planification des jalons", "COPIL facturation"]),

                // --- Exploitation & production -----------------------------------------------------------------
                new SeedTeamMember(
                    People.Camille,
                    Units.Ops,
                    Departments.InformationSystems,
                    FunctionalRoles.Architect,
                    AllocationPercent: 50,
                    HoursPerDay: 4.5m,
                    SeedWorkNature.Alternating,
                    [
                        "Architecture de la reprise de données",
                        "Schéma d'intégration avec le SI comptable",
                        "Exploitation courante de la chaîne",
                        "Analyse des incidents récurrents",
                    ]),
                new SeedTeamMember(
                    People.Thomas,
                    Units.Ops,
                    Departments.InformationSystems,
                    FunctionalRoles.UnitHead,
                    AllocationPercent: 40,
                    HoursPerDay: 3m,
                    SeedWorkNature.Run,
                    ["Astreinte et suivi de production", "Revue des changements", "Capacité serveurs"]),
                new SeedTeamMember(
                    People.Anais,
                    Units.Ops,
                    Departments.InformationSystems,
                    FunctionalRoles.Ops,
                    AllocationPercent: 55,
                    HoursPerDay: 4m,
                    SeedWorkNature.Run,
                    [
                        "Déploiement du lot de correctifs",
                        "Rejeu des flux de facturation",
                        "Sauvegarde et restauration",
                        "Mise à jour des ordonnancements",
                    ]),
                new SeedTeamMember(
                    People.Tarek,
                    Units.Ops,
                    Departments.InformationSystems,
                    FunctionalRoles.Ops,
                    AllocationPercent: 55,
                    HoursPerDay: 4m,
                    SeedWorkNature.Run,
                    [
                        "Montage de l'environnement de production",
                        "Durcissement des serveurs applicatifs",
                        "Bascule et plan de retour arrière",
                        "Supervision des traitements nocturnes",
                    ]),

                // --- Études & développement --------------------------------------------------------------------
                // Julie's second project, and her RUN one. The same person, BUILD on Portail RH and RUN here.
                new SeedTeamMember(
                    People.Julie,
                    Units.Development,
                    Departments.InformationSystems,
                    FunctionalRoles.Developer,
                    AllocationPercent: 30,
                    HoursPerDay: 2m,
                    SeedWorkNature.Run,
                    [
                        "Maintenance corrective des éditions",
                        "Anomalie — arrondi de TVA",
                        "Reprise des factures en erreur",
                    ]),

                // --- Sécurité & conformité ---------------------------------------------------------------------
                new SeedTeamMember(
                    People.Ines,
                    Units.Security,
                    Departments.InformationSystems,
                    FunctionalRoles.SecurityOfficer,
                    AllocationPercent: 45,
                    HoursPerDay: 3.5m,
                    SeedWorkNature.Build,
                    [
                        "Audit de sécurité de la chaîne de facturation",
                        "Test d'intrusion applicatif",
                        "Dossier d'homologation facturation",
                        "Plan de remédiation des vulnérabilités",
                    ]),

                // --- Support & assistance ----------------------------------------------------------------------
                new SeedTeamMember(
                    People.Yann,
                    Units.Helpdesk,
                    Departments.InformationSystems,
                    FunctionalRoles.UnitHead,
                    AllocationPercent: 35,
                    HoursPerDay: 2.5m,
                    SeedWorkNature.Run,
                    ["Pilotage des tickets facturation", "Revue des incidents majeurs", "Suivi des engagements"]),

                // --- Communication -----------------------------------------------------------------------------
                new SeedTeamMember(
                    People.Amina,
                    Units.Design,
                    Departments.Communication,
                    FunctionalRoles.Designer,
                    AllocationPercent: 55,
                    HoursPerDay: 4m,
                    SeedWorkNature.Build,
                    [
                        "Refonte du modèle de facture",
                        "Maquettes du portail client",
                        "Déclinaison de la charte sur les éditions",
                    ]),

                // --- Finance -----------------------------------------------------------------------------------
                new SeedTeamMember(
                    People.Sofia,
                    Units.Accounting,
                    Departments.Finance,
                    FunctionalRoles.Accountant,
                    AllocationPercent: 50,
                    HoursPerDay: 3.5m,
                    SeedWorkNature.Run,
                    [
                        "Recette comptable des éditions",
                        "Validation des écritures reprises",
                        "Rapprochement des à-nouveaux",
                    ]),
                new SeedTeamMember(
                    People.Laurent,
                    Units.Controlling,
                    Departments.Finance,
                    FunctionalRoles.SeniorAccountant,
                    AllocationPercent: 25,
                    HoursPerDay: 2m,
                    SeedWorkNature.Run,
                    ["Contrôle des écarts de facturation", "Clôture mensuelle", "Revue budgétaire du projet"]),
            ]),
    ];
}
