using Cracra.BuildingBlocks.Web.Users;

namespace Cracra.BuildingBlocks.Testing;

/// <summary>
/// The fixed org the RLS matrix test asserts against: two departments, two units each, people in every contextual
/// role, exactly as <c>conventions.md §6</c> requires.
/// </summary>
/// <remarks>
/// These ids are the same ones <c>deploy/keycloak/build-realm.py</c> seeds into the realm. Keeping one set means a
/// Playwright test logging in as Camille and an integration test running "as Camille" are talking about the same
/// person — which is the only way the two suites can corroborate each other rather than merely coexist.
/// </remarks>
public static class SeedOrganisation
{
    public static class Departments
    {
        public static readonly Guid InformationSystems = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid Finance = Guid.Parse("22222222-2222-2222-2222-222222222222");
    }

    public static class Units
    {
        public static readonly Guid Infrastructure = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        public static readonly Guid Development = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        public static readonly Guid Accounting = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        public static readonly Guid Controlling = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    }

    /// <summary>Member of the Infrastructure unit. The baseline "ordinary employee" case.</summary>
    public static readonly UserContext Camille = Person(
        "c0000000-0000-0000-0000-000000000001",
        "camille.villeneuve",
        Units.Infrastructure,
        Departments.InformationSystems,
        ContextualRole.Member);

    /// <summary>Another member of the same unit — proves "I can see my unit peers" without proving "I can see everyone".</summary>
    public static readonly UserContext Mehdi = Person(
        "c0000000-0000-0000-0000-000000000002",
        "mehdi.sadaoui",
        Units.Infrastructure,
        Departments.InformationSystems,
        ContextualRole.Member);

    /// <summary>Head of the Infrastructure unit.</summary>
    public static readonly UserContext Thomas = Person(
        "c0000000-0000-0000-0000-000000000004",
        "thomas.berthier",
        Units.Infrastructure,
        Departments.InformationSystems,
        ContextualRole.Member,
        ContextualRole.UnitHead);

    /// <summary>Head of the IS department.</summary>
    public static readonly UserContext Olivier = Person(
        "c0000000-0000-0000-0000-000000000006",
        "olivier.marchand",
        Units.Development,
        Departments.InformationSystems,
        ContextualRole.Member,
        ContextualRole.DepartmentHead);

    /// <summary>Member of a different department entirely — the negative case for every cross-department rule.</summary>
    public static readonly UserContext Sofia = Person(
        "c0000000-0000-0000-0000-000000000007",
        "sofia.navarro",
        Units.Accounting,
        Departments.Finance,
        ContextualRole.Member);

    /// <summary>Head of the Finance department. The far side of every cross-department assertion.</summary>
    public static readonly UserContext Laurent = Person(
        "c0000000-0000-0000-0000-000000000008",
        "laurent.bouchard",
        Units.Controlling,
        Departments.Finance,
        ContextualRole.Member,
        ContextualRole.DepartmentHead);

    /// <summary>PMO: sees everything, portfolio-wide.</summary>
    public static readonly UserContext Nadia = Person(
        "c0000000-0000-0000-0000-000000000009",
        "nadia.kessler",
        Units.Development,
        Departments.InformationSystems,
        ContextualRole.Member,
        ContextualRole.Pmo);

    private static UserContext Person(
        string id,
        string userName,
        Guid unitId,
        Guid departmentId,
        params string[] roles) => new()
        {
            IsAuthenticated = true,
            UserId = Guid.Parse(id),
            UserName = userName,
            UnitId = unitId,
            DepartmentIds = [departmentId],
            Roles = roles,
            Language = SupportedLanguages.French,
        };
}
