namespace Cracra.Modules.Access.Domain;

/// <summary>What a role assignment applies to.</summary>
public enum ScopeType
{
    /// <summary>Everywhere. Only PMO is realistically granted globally.</summary>
    Global = 0,
    Department = 1,
    Unit = 2,
    Project = 3,
}

/// <summary>Where an assignment came from. Determines who may remove it.</summary>
public enum RoleSource
{
    /// <summary>Materialized from LDAP groups during directory sync. Rewritten on every run.</summary>
    Ldap = 0,

    /// <summary>Granted in-app because LDAP was incomplete or wrong. Survives sync.</summary>
    RbacOverride = 1,
}

/// <summary>
/// A contextual role someone holds, and over what.
/// </summary>
/// <remarks>
/// Rows with <see cref="RoleSource.Ldap"/> are owned by sync: it deletes and rewrites them wholesale each run, so
/// editing one by hand is pointless. Overrides live in <see cref="RbacOverride"/> precisely so they survive that.
/// </remarks>
public sealed class ContextualRoleAssignment
{
    public required Guid Id { get; init; }

    public required Guid PersonId { get; set; }

    /// <summary>One of <c>ContextualRole</c>'s constants. Stored as the wire string the GUC and SQL compare.</summary>
    public required string Role { get; set; }

    public required ScopeType ScopeType { get; set; }

    /// <summary>The unit, department or project id. Null when <see cref="ScopeType"/> is Global.</summary>
    public Guid? ScopeId { get; set; }

    public required RoleSource Source { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A hand-granted or hand-revoked role. The fallback for when LDAP does not reflect reality.
/// </summary>
/// <remarks>
/// <para>
/// A deny beats a grant. Someone who should not have a role usually needs that to be true <em>now</em> — an
/// incident, a departure, a mistaken group — and having to find and delete a grant first would leave a window
/// where the answer depends on which row is read first.
/// </para>
/// <para>
/// <see cref="ExpiresAt"/> exists because most overrides are temporary — covering an absence, a handover — and an
/// override nobody remembers to remove is how an access model rots. Expiry is evaluated at resolution time, so a
/// lapsed row stops applying without anything having to run.
/// </para>
/// </remarks>
public sealed class RbacOverride
{
    public required Guid Id { get; init; }

    public required Guid PersonId { get; set; }

    public required string Role { get; set; }

    public required ScopeType ScopeType { get; set; }

    public Guid? ScopeId { get; set; }

    /// <summary>False revokes rather than grants. Always wins over a grant of the same role and scope.</summary>
    public bool IsGrant { get; set; } = true;

    /// <summary>Required. An override with no stated reason is one nobody can safely remove later.</summary>
    public required string Reason { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public required Guid CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? RevokedBy { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Active means granted, not revoked, and not expired at <paramref name="asOf"/>.</summary>
    public bool IsActiveAt(DateTimeOffset asOf) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > asOf);
}

/// <summary>
/// Append-only record of every override written or revoked.
/// </summary>
/// <remarks>
/// The matrix requires access changes to be auditable, and this is the one place a human can widen someone's
/// visibility by hand. "Who granted this, when, and why" has to survive the override being deleted.
/// </remarks>
public sealed class RbacOverrideAudit
{
    public required Guid Id { get; init; }

    public required Guid OverrideId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>granted | revoked</summary>
    public required string Action { get; init; }

    public required string SnapshotJson { get; init; }

    public required Guid ChangedBy { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }
}

/// <summary>
/// Who is on which project, projected into the access schema.
/// </summary>
/// <remarks>
/// <para>
/// visibility-matrix.md §3 writes <c>access.on_project</c> as a query against <c>projects.project_member</c>. This
/// is that data, projected here instead, for two reasons.
/// </para>
/// <para>
/// First, architecture.md §2 forbids a module reaching into another module's schema, and an RLS predicate that
/// selects from <c>projects.*</c> is exactly that coupling — expressed in SQL where no architecture test can see
/// it. Second, the predicates have to exist and be testable in this slice, and the Projects module does not land
/// until S3; a function referencing a table that does not exist yet cannot even be created.
/// </para>
/// <para>
/// S3 maintains this projection from its own integration events. Until then it is empty, which makes every
/// project-scoped predicate evaluate false — the correct answer when no projects exist.
/// </para>
/// </remarks>
public sealed class ProjectMembership
{
    public required Guid PersonId { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>The department contributing this person, for the cross-department knowledge-flow rule.</summary>
    public required Guid DepartmentId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
