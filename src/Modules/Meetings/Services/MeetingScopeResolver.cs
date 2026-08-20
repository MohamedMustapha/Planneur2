using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Projects.Contracts;

namespace Cracra.Modules.Meetings.Services;

/// <summary>A validated target, with the department the RLS predicate needs carried alongside it.</summary>
public sealed record ResolvedScope(string ScopeType, Guid? ScopeId, Guid? DepartmentId);

/// <summary>
/// Turns a (scope type, scope id) pair from a request into something safe to store.
/// </summary>
/// <remarks>
/// <para>
/// Two jobs, and both matter. It rejects a target that does not exist — a meeting aimed at a deleted unit is
/// invisible to everyone and impossible to diagnose — and it resolves the department, which the row carries so
/// that the access predicate never has to reach into <c>directory.unit</c>.
/// </para>
/// <para>
/// Every lookup runs through the caller's own RLS session, so "does this unit exist" is really "does this unit
/// exist for you". That is the right question: a head who cannot see a unit has no business scheduling into it,
/// and the check inherits the visibility rules instead of restating them.
/// </para>
/// </remarks>
public interface IMeetingScopeResolver
{
    Task<ResolvedScope> ResolveAsync(string? scopeType, Guid? scopeId, CancellationToken ct);
}

internal sealed class MeetingScopeResolver(IDirectoryReader directory, IProjectProvisioner projects)
    : IMeetingScopeResolver
{
    public async Task<ResolvedScope> ResolveAsync(string? scopeType, Guid? scopeId, CancellationToken ct)
    {
        var normalized = (scopeType ?? string.Empty).Trim().ToLowerInvariant();

        if (!MeetingScopeTypes.All.Contains(normalized, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException(
                $"'{scopeType}' is not a scope. Use one of: {string.Join(", ", MeetingScopeTypes.All)}.");
        }

        if (MeetingScopeTypes.RequiresScopeId(normalized) && scopeId is null)
        {
            throw new DomainRuleViolationException($"A {normalized}-scoped entry needs the id of the {normalized} it targets.");
        }

        switch (normalized)
        {
            case MeetingScopeTypes.Org:
                // An org-wide entry has no target and belongs to no department. Who may create one is settled by
                // the endpoint policy and the write predicate, not here.
                return new ResolvedScope(MeetingScopeTypes.Org, null, null);

            case MeetingScopeTypes.Unit:
            {
                var units = await directory.GetUnitsAsync(null, ct);
                var unit = units.FirstOrDefault(candidate => candidate.Id == scopeId);

                if (unit is null)
                {
                    // Indistinguishable from "you may not see it", deliberately — the same reason
                    // ResourceNotFoundException exists at all.
                    throw new ResourceNotFoundException("That unit does not exist.");
                }

                return new ResolvedScope(MeetingScopeTypes.Unit, unit.Id, unit.DepartmentId);
            }

            case MeetingScopeTypes.Department:
            {
                if (!await directory.DepartmentExistsAsync(scopeId!.Value, ct))
                {
                    throw new ResourceNotFoundException("That department does not exist.");
                }

                // The department is both the target and the department: the predicate reads one column either
                // way, so writing it twice keeps the SQL from having to special-case the scope type.
                return new ResolvedScope(MeetingScopeTypes.Department, scopeId, scopeId);
            }

            default:
            {
                if (!await projects.ExistsAsync(scopeId!.Value, ct))
                {
                    throw new ResourceNotFoundException("That project does not exist.");
                }

                // No department. A project is not one department's — that is the whole point of it — and picking
                // its lead department here would quietly hand the copil to whoever happens to lead the project.
                return new ResolvedScope(MeetingScopeTypes.Project, scopeId, null);
            }
        }
    }
}
