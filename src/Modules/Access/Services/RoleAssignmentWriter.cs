using Cracra.Modules.Access.Data;
using Cracra.Modules.Access.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Access.Services;

/// <summary>One person's LDAP-derived roles, as sync discovered them.</summary>
public sealed record SyncedRoleAssignment(Guid PersonId, string Role, ScopeType ScopeType, Guid? ScopeId);

/// <summary>
/// Replaces the LDAP-sourced assignments. Called by the Directory module's sync through the Access contracts, so
/// Directory never touches this schema itself.
/// </summary>
public interface IRoleAssignmentWriter
{
    /// <summary>
    /// Rewrites every <see cref="RoleSource.Ldap"/> assignment to match what sync just read.
    /// </summary>
    /// <remarks>
    /// Replace rather than merge. LDAP is the source of truth for these rows, and a merge would leave a role
    /// behind when someone is removed from a group — which is the one direction that matters, because it means
    /// somebody keeps seeing what they should no longer see. Overrides are a different source and are untouched.
    /// </remarks>
    Task<int> ReplaceLdapAssignmentsAsync(IReadOnlyList<SyncedRoleAssignment> assignments, CancellationToken ct);
}

internal sealed class RoleAssignmentWriter(AccessDbContext context, IEffectiveRoleResolver resolver)
    : IRoleAssignmentWriter
{
    public async Task<int> ReplaceLdapAssignmentsAsync(
        IReadOnlyList<SyncedRoleAssignment> assignments,
        CancellationToken ct)
    {
        var existing = await context.RoleAssignments
            .Where(assignment => assignment.Source == RoleSource.Ldap)
            .AsTracking()
            .ToListAsync(ct);

        var desired = assignments
            .Select(assignment => (assignment.PersonId, assignment.Role, assignment.ScopeType, assignment.ScopeId))
            .ToHashSet();

        var current = existing
            .Select(assignment => (assignment.PersonId, assignment.Role, assignment.ScopeType, assignment.ScopeId))
            .ToHashSet();

        var touched = new HashSet<Guid>();
        var changes = 0;

        foreach (var stale in existing.Where(assignment =>
                     !desired.Contains((assignment.PersonId, assignment.Role, assignment.ScopeType, assignment.ScopeId))))
        {
            context.RoleAssignments.Remove(stale);
            touched.Add(stale.PersonId);
            changes++;
        }

        foreach (var assignment in assignments.Where(assignment =>
                     !current.Contains((assignment.PersonId, assignment.Role, assignment.ScopeType, assignment.ScopeId))))
        {
            context.RoleAssignments.Add(new ContextualRoleAssignment
            {
                Id = Guid.CreateVersion7(),
                PersonId = assignment.PersonId,
                Role = assignment.Role,
                ScopeType = assignment.ScopeType,
                ScopeId = assignment.ScopeId,
                Source = RoleSource.Ldap,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            touched.Add(assignment.PersonId);
            changes++;
        }

        await context.SaveChangesAsync(ct);

        // Anyone whose roles moved must stop being served a cached answer immediately. A stale grant outliving the
        // sync that removed it is the failure this whole slice exists to prevent.
        foreach (var personId in touched)
        {
            resolver.Invalidate(personId);
        }

        return changes;
    }
}
