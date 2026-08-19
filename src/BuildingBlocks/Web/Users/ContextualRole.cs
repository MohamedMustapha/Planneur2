namespace Cracra.BuildingBlocks.Web.Users;

/// <summary>
/// The contextual roles from <c>visibility-matrix.md §1</c>. These strings are the wire format: they travel in the
/// Keycloak token, land in the <c>app.roles</c> Postgres GUC verbatim, and are compared by
/// <c>access.has(role)</c> in SQL. Never localize them and never rename one without a data migration.
/// </summary>
public static class ContextualRole
{
    public const string Member = "member";
    public const string UnitHead = "unit-head";
    public const string DepartmentHead = "dept-head";
    public const string ProjectLead = "project-lead";
    public const string ProductOwner = "po";
    public const string Pmo = "pmo";

    /// <summary>Background jobs only — sync, AI and the outbox drainer. Never granted to a human session.</summary>
    public const string System = "system";

    public static readonly IReadOnlyList<string> All =
    [
        Member,
        UnitHead,
        DepartmentHead,
        ProjectLead,
        ProductOwner,
        Pmo,
        System,
    ];

    /// <summary>The roles that get cross-department read on shared projects (knowledge flow).</summary>
    public static readonly IReadOnlyList<string> Heads = [UnitHead, DepartmentHead, Pmo];
}
