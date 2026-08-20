namespace Cracra.Modules.Access.Contracts;

// =================================================================================================================
// The Access module's public surface. Other modules consume role resolution through this and nothing else.
// =================================================================================================================

/// <summary>A contextual role and the scope it applies over, as another module sees it.</summary>
public sealed record EffectiveRoleDto(string Role, string ScopeType, Guid? ScopeId, string Source);

public sealed record EffectiveRolesResponse(
    Guid PersonId,
    IReadOnlyList<EffectiveRoleDto> Roles,
    IReadOnlyList<string> RoleNames);

/// <summary>
/// The caller's own effective roles and scopes. The client gates navigation on this — and only navigation: every
/// data call is still decided by RLS server-side, so a tampered response changes what is drawn, never what is
/// returned.
/// </summary>
public sealed record WhoAmIResponse(
    Guid PersonId,
    string UserName,
    Guid? UnitId,
    IReadOnlyList<Guid> DepartmentIds,
    IReadOnlyList<string> Roles,
    IReadOnlyList<EffectiveRoleDto> ScopedRoles,
    string Language);

public sealed record RbacOverrideDto(
    Guid Id,
    Guid PersonId,
    string Role,
    string ScopeType,
    Guid? ScopeId,
    bool IsGrant,
    string Reason,
    DateTimeOffset? ExpiresAt,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    bool IsActive);

/// <summary>
/// Roles the Directory module hands over during sync, so Access can materialize them.
/// </summary>
/// <remarks>
/// Directory discovers group membership; Access decides what it means. Passing the raw mapping across this
/// contract keeps that split intact — Directory never writes to the access schema, and Access never talks to LDAP.
/// </remarks>
public sealed record SyncedRoleAssignmentDto(Guid PersonId, string Role, string ScopeType, Guid? ScopeId);

/// <summary>Implemented by Access, called by Directory's sync.</summary>
public interface IRoleMaterializer
{
    Task<int> MaterializeAsync(IReadOnlyList<SyncedRoleAssignmentDto> assignments, CancellationToken ct);
}

/// <summary>One person on one project, as the access projection records it.</summary>
public sealed record ProjectMembershipEntry(Guid PersonId, Guid DepartmentId);

/// <summary>
/// Maintains the projection <c>access.on_project</c> and <c>access.project_in_my_depts</c> read.
/// </summary>
/// <remarks>
/// Implemented by Access, called by Projects inside the transaction that changes the team. Deliberately not an
/// integration event: this projection decides who can read a project, and a lag between removing someone and them
/// losing access is a hole rather than a latency.
/// </remarks>
public interface IProjectMembershipProjection
{
    /// <summary>Replaces the whole membership for a project. An empty list clears it.</summary>
    Task ReplaceAsync(Guid projectId, IReadOnlyList<ProjectMembershipEntry> members, CancellationToken ct);
}
