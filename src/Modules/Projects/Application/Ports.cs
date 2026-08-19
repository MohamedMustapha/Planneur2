namespace Cracra.Modules.Projects.Application;

/// <summary>
/// What Projects needs to know about the org, expressed as a port.
/// </summary>
/// <remarks>
/// An interface rather than a direct call into Directory so the domain rules can be unit-tested without a
/// database, and so the dependency is visible in one place instead of scattered through handlers. The adapter that
/// implements it lives in Infrastructure and speaks to Directory through its contracts assembly.
/// </remarks>
public interface IDirectoryPort
{
    /// <summary>True when the person exists, is active, and belongs to the given department.</summary>
    Task<bool> IsPersonInDepartmentAsync(Guid personId, Guid departmentId, CancellationToken ct);

    Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken ct);

    Task<bool> FunctionalRoleExistsAsync(Guid functionalRoleId, CancellationToken ct);

    /// <summary>Display names for a set of people, for the team projection. Empty for anyone RLS hid.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(IReadOnlyList<Guid> personIds, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetDepartmentNameKeysAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetFunctionalRoleCodesAsync(
        IReadOnlyList<Guid> functionalRoleIds,
        CancellationToken ct);
}

/// <summary>
/// Keeps the Access module's project-membership projection in step with the team.
/// </summary>
/// <remarks>
/// <para>
/// Called synchronously, inside the same transaction as the team change, rather than through the outbox. That is a
/// deliberate exception to the usual rule that cross-module effects go via integration events.
/// </para>
/// <para>
/// The projection is what <c>access.on_project</c> reads, so it decides who can see the project. Draining it
/// asynchronously would mean a newly added member cannot see the project they were just added to for a few
/// seconds, and — the direction that actually matters — a removed member still can. Eventual consistency is fine
/// for a read model; it is not fine for an access-control input.
/// </para>
/// </remarks>
public interface IProjectAccessProjection
{
    Task ReplaceProjectMembershipAsync(
        Guid projectId,
        IReadOnlyList<(Guid PersonId, Guid DepartmentId)> members,
        CancellationToken ct);

    Task RemoveProjectAsync(Guid projectId, CancellationToken ct);
}
