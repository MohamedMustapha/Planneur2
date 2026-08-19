using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Access.Domain;

namespace Cracra.Modules.Access.Services;

/// <summary>
/// The contract face of <see cref="IRoleAssignmentWriter"/>, so Directory's sync can hand over what it discovered
/// without referencing anything of Access's beyond the contracts assembly.
/// </summary>
internal sealed class RoleMaterializer(IRoleAssignmentWriter writer) : IRoleMaterializer
{
    public async Task<int> MaterializeAsync(
        IReadOnlyList<SyncedRoleAssignmentDto> assignments,
        CancellationToken ct)
    {
        var mapped = assignments
            .Select(assignment => new SyncedRoleAssignment(
                assignment.PersonId,
                assignment.Role,
                Enum.TryParse<ScopeType>(assignment.ScopeType, ignoreCase: true, out var scope)
                    ? scope
                    // An unrecognised scope becomes Global, which is the *widest* reading and therefore the wrong
                    // default — so refuse instead. A malformed mapping should stop sync, not silently promote
                    // somebody.
                    : throw new ArgumentOutOfRangeException(
                        nameof(assignments),
                        $"'{assignment.ScopeType}' is not a valid scope type."),
                assignment.ScopeId))
            .ToArray();

        return await writer.ReplaceLdapAssignmentsAsync(mapped, ct);
    }
}
