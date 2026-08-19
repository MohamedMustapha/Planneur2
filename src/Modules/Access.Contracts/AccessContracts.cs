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
