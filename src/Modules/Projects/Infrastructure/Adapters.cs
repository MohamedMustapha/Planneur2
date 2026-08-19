using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Projects.Application;

namespace Cracra.Modules.Projects.Infrastructure;

/// <summary>
/// The Directory port, implemented against Directory's contracts.
/// </summary>
/// <remarks>
/// Both ports are Directory's, consumed through its contracts assembly — Projects never touches Directory's
/// DbContext or entities, and an architecture test asserts it cannot even reference that assembly.
///
/// Which of the two is used matters. Department name keys come from the caller-scoped reader, so a caller only
/// ever sees labels for departments they could already read. Validation and team names come from the reference
/// reader, which runs outside their visibility — see IDirectoryReferenceReader for why that is necessary and how
/// the authority is derived rather than assumed.
/// </remarks>
internal sealed class DirectoryAdapter(IDirectoryReader directory, IDirectoryReferenceReader reference)
    : IDirectoryPort
{
    // Validation goes through the reference reader, not the caller-scoped one. A head leading a cross-department
    // project cannot see the contributing department's people until they are on the team, and cannot put them on
    // the team without seeing them — the authority here comes from having loaded the project, which RLS already
    // proved the caller may write.
    public async Task<bool> IsPersonInDepartmentAsync(Guid personId, Guid departmentId, CancellationToken ct)
    {
        var person = await reference.GetPersonAsync(personId, ct);

        return person is { Active: true } && person.DepartmentId == departmentId;
    }

    public async Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken ct) =>
        await reference.DepartmentExistsAsync(departmentId, ct);

    public async Task<bool> FunctionalRoleExistsAsync(Guid functionalRoleId, CancellationToken ct) =>
        await reference.FunctionalRoleExistsAsync(functionalRoleId, ct);

    // Names for the team panel. Everyone here is already on a project the caller can read, so naming them adds no
    // visibility they did not have — and leaving them anonymous would make the panel unreadable.
    public async Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct) =>
        await reference.GetPersonNamesAsync(personIds, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDepartmentNameKeysAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct) =>
        await directory.GetDepartmentNameKeysAsync(departmentIds, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetFunctionalRoleCodesAsync(
        IReadOnlyList<Guid> functionalRoleIds,
        CancellationToken ct) =>
        await directory.GetFunctionalRoleCodesAsync(functionalRoleIds, ct);
}

/// <summary>
/// Keeps <c>access.project_membership</c> in step, through the Access module's contract.
/// </summary>
/// <remarks>
/// Synchronous and in-transaction, unlike every other cross-module effect in the system. The projection is what
/// <c>access.on_project</c> reads, so it decides visibility — and a removed member who can still read the project
/// until a queue drains is not an eventual-consistency trade-off, it is a hole.
/// </remarks>
internal sealed class ProjectAccessProjectionAdapter(IProjectMembershipProjection projection)
    : IProjectAccessProjection
{
    public async Task ReplaceProjectMembershipAsync(
        Guid projectId,
        IReadOnlyList<(Guid PersonId, Guid DepartmentId)> members,
        CancellationToken ct) =>
        await projection.ReplaceAsync(
            projectId,
            [.. members.Select(member => new ProjectMembershipEntry(member.PersonId, member.DepartmentId))],
            ct);

    public async Task RemoveProjectAsync(Guid projectId, CancellationToken ct) =>
        await projection.ReplaceAsync(projectId, [], ct);
}
