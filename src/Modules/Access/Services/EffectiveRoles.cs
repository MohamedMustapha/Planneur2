using Cracra.Modules.Access.Domain;

namespace Cracra.Modules.Access.Services;

/// <summary>One role a person effectively holds, and why.</summary>
public sealed record EffectiveRole(string Role, ScopeType ScopeType, Guid? ScopeId, RoleSource Source);

/// <summary>The merged answer for one person.</summary>
/// <param name="IsAuthoritative">
/// True when the Access module holds any row for this person — an assignment, an override, or both.
///
/// This distinguishes "everything they had was denied" from "we have nothing on file yet", which are the same
/// empty list and must not be treated the same. An empty-but-authoritative answer means no roles, full stop; an
/// empty non-authoritative one means Access cannot answer and the caller should fall back to the token. Collapsing
/// the two would make a deny that removes someone's last role silently restore it from the token.
/// </param>
public sealed record EffectiveRoleSet(
    Guid PersonId,
    IReadOnlyList<EffectiveRole> Roles,
    bool IsAuthoritative)
{
    public static EffectiveRoleSet Empty(Guid personId) => new(personId, [], IsAuthoritative: false);

    /// <summary>
    /// The flat role names that go into the <c>app.roles</c> GUC and therefore into every RLS predicate.
    /// </summary>
    /// <remarks>
    /// Scope is deliberately dropped here. The GUC carries which roles you hold; the predicates decide where they
    /// apply, by comparing the row's unit or department against <c>access.unit()</c> and <c>access.depts()</c>.
    /// Encoding scope into the role string instead would push that comparison into every policy and give two
    /// places to get it wrong.
    /// </remarks>
    public IReadOnlyList<string> RoleNames =>
        [.. Roles.Select(role => role.Role).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}

/// <summary>
/// Merges LDAP-derived assignments with in-app overrides.
/// </summary>
/// <remarks>
/// Pure and static so the merge rules — which decide what everyone can see — are testable without a database.
/// </remarks>
public static class EffectiveRoles
{
    /// <summary>
    /// Effective roles = LDAP assignments ∪ active grants, minus anything actively denied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A deny beats a grant, and beats LDAP. Revoking access is nearly always the urgent direction — someone left,
    /// someone was in the wrong group, an incident is in progress — and if a deny merely competed with a grant,
    /// the outcome would depend on which row happened to be read first.
    /// </para>
    /// <para>
    /// A deny is matched on role and scope. Denying <c>dept-head</c> over one department does not touch the same
    /// role over another, because those are genuinely different grants.
    /// </para>
    /// </remarks>
    public static EffectiveRoleSet Resolve(
        Guid personId,
        IReadOnlyList<ContextualRoleAssignment> assignments,
        IReadOnlyList<RbacOverride> overrides,
        DateTimeOffset asOf)
    {
        var active = overrides.Where(item => item.IsActiveAt(asOf)).ToArray();

        var denied = active
            .Where(item => !item.IsGrant)
            .Select(item => (item.Role, item.ScopeType, item.ScopeId))
            .ToHashSet();

        var resolved = new List<EffectiveRole>();

        foreach (var assignment in assignments)
        {
            if (!denied.Contains((assignment.Role, assignment.ScopeType, assignment.ScopeId)))
            {
                resolved.Add(new EffectiveRole(
                    assignment.Role,
                    assignment.ScopeType,
                    assignment.ScopeId,
                    assignment.Source));
            }
        }

        foreach (var grant in active.Where(item => item.IsGrant))
        {
            if (denied.Contains((grant.Role, grant.ScopeType, grant.ScopeId)))
            {
                continue;
            }

            var alreadyHeld = resolved.Any(role =>
                role.Role == grant.Role && role.ScopeType == grant.ScopeType && role.ScopeId == grant.ScopeId);

            if (!alreadyHeld)
            {
                resolved.Add(new EffectiveRole(
                    grant.Role,
                    grant.ScopeType,
                    grant.ScopeId,
                    RoleSource.RbacOverride));
            }
        }

        // Authoritative when Access has anything on file, even if the merge cancels out to nothing.
        return new EffectiveRoleSet(personId, resolved, assignments.Count > 0 || overrides.Count > 0);
    }
}
