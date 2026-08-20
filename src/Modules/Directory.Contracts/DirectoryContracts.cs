using Cracra.BuildingBlocks.Messaging;

namespace Cracra.Modules.Directory.Contracts;

// =================================================================================================================
// The Directory module's public surface. Other modules may reference this assembly and nothing else of Directory's
// (architecture.md §2). Everything here is additive-only once released: another module is deserializing these from
// outbox rows written before the current deploy.
// =================================================================================================================

/// <summary>A person, as the rest of the system sees them. Never carries email or anything else not needed to render.</summary>
public sealed record PersonSummary(
    Guid Id,
    string DisplayName,
    Guid? UnitId,
    Guid? DepartmentId,
    IReadOnlyList<string> FunctionalRoleCodes,
    bool Active);

public sealed record UnitSummary(
    Guid Id,
    Guid DepartmentId,
    string Code,
    string Name,
    string Kind);

public sealed record DepartmentSummary(
    Guid Id,
    string Code,
    string NameKey,
    Guid? ParentDepartmentId);

/// <summary>
/// The department knobs S5, S6 and S9 read. Exposed as a contract rather than as the entity so those modules never
/// take a dependency on Directory's persistence.
/// </summary>
public sealed record DepartmentConfigSnapshot(
    Guid DepartmentId,
    string ActivityTaxonomyJson,
    string RoleLabelsJson,
    string KudoRulesJson,
    string DefaultBoardLayout,
    string IterationPresetsJson,
    decimal WeeklyTargetHours,
    bool EnforceWeeklyTarget,
    int Version,
    /// <summary>Shift slots the department offers, and their minimum staffing (S6). Empty means the defaults.</summary>
    string ShiftTemplatesJson = "{}");

// --- Integration events ------------------------------------------------------------------------------------------

/// <summary>
/// Raised when sync creates a person. Consumers key off <c>EventId</c> for idempotency — delivery is at-least-once.
/// </summary>
public sealed record PersonJoined(Guid PersonId, string DisplayName, Guid? UnitId, Guid? DepartmentId)
    : IntegrationEvent;

/// <summary>
/// Raised when someone moves unit or department. S5 and S6 care: a member's board scope changes underneath them,
/// and anything cached against their old unit is now wrong.
/// </summary>
public sealed record PersonMoved(
    Guid PersonId,
    Guid? PreviousUnitId,
    Guid? UnitId,
    Guid? PreviousDepartmentId,
    Guid? DepartmentId) : IntegrationEvent;

/// <summary>
/// Raised when a person leaves the directory. Deliberately "deactivated", not "deleted" — their activity, kudos and
/// project history remain, and consumers must not cascade a delete off this.
/// </summary>
public sealed record PersonDeactivated(Guid PersonId) : IntegrationEvent;

/// <summary>Raised on every config write so boards can invalidate what they cached against the old version.</summary>
public sealed record DepartmentConfigChanged(Guid DepartmentId, int Version) : IntegrationEvent;

// --- Read port for other modules -------------------------------------------------------------------------------

/// <summary>
/// The reads other modules need from the directory.
/// </summary>
/// <remarks>
/// Implemented by Directory, consumed through this assembly. Every call runs under the caller's RLS session, so a
/// consumer can only resolve people it was already allowed to see — validation inherits the visibility rules
/// instead of restating them.
/// </remarks>
public interface IDirectoryReader
{
    Task<PersonSummary?> GetPersonAsync(Guid personId, CancellationToken ct);

    Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken ct);

    Task<bool> FunctionalRoleExistsAsync(Guid functionalRoleId, CancellationToken ct);

    /// <summary>Display names by person id. Anyone RLS hid is simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetDepartmentNameKeysAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetFunctionalRoleCodesAsync(
        IReadOnlyList<Guid> functionalRoleIds,
        CancellationToken ct);

    /// <summary>Units of a department, or all the caller can see. The department board's rows.</summary>
    Task<IReadOnlyList<UnitSummary>> GetUnitsAsync(Guid? departmentId, CancellationToken ct);

    /// <summary>People in a unit or department. The team and unit boards' rows.</summary>
    Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(Guid? unitId, Guid? departmentId, CancellationToken ct);
}

/// <summary>
/// Referential lookups that run outside the caller's own visibility.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IDirectoryReader"/> so the distinction is impossible to miss at the call site: that
/// one answers "what can this caller see", this one answers "does this exist, and who is in these departments".
/// </para>
/// <para>
/// It resolves a genuine deadlock in the matrix. A head leading a cross-department project must add a colleague
/// from the contributing department, but the directory is department-scoped — so they cannot see that person until
/// the person is on the project, and cannot add them without seeing them.
/// </para>
/// <para>
/// The authority comes from elsewhere: callers reach this only after loading a project, and loading it proves
/// through that project's own RLS policy that they may write it. Deliberately narrow — no free-form query, only
/// existence by id and people filtered to departments the caller has already been authorized for.
/// </para>
/// </remarks>
public interface IDirectoryReferenceReader
{
    Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken ct);

    Task<bool> FunctionalRoleExistsAsync(Guid functionalRoleId, CancellationToken ct);

    Task<PersonSummary?> GetPersonAsync(Guid personId, CancellationToken ct);

    /// <summary>People in an explicit set of departments — the team picker for a cross-department project.</summary>
    Task<IReadOnlyList<PersonSummary>> GetPeopleInDepartmentsAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct);

    /// <summary>
    /// LDAP uids to person ids, for a background job reconciling an external system's idea of who somebody is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added by S10, whose sync has to turn "assigned to camille.villeneuve in DevOps" into a person id it can
    /// stamp on a mirror row. It belongs on this interface rather than on <see cref="IDirectoryReader"/> for the
    /// same reason the rest of it does: the caller is a system-context job with no unit and no department, so a
    /// caller-scoped lookup would resolve nobody and every pulled item would arrive unassigned.
    /// </para>
    /// <para>
    /// Narrow in the same way as its neighbours — an explicit list of uids in, ids out. No name, no department,
    /// no email: enough to say "this is that person", and not enough to enumerate the organization.
    /// </para>
    /// </remarks>
    Task<IReadOnlyDictionary<string, Guid>> ResolvePeopleByLdapUidAsync(
        IReadOnlyList<string> ldapUids,
        CancellationToken ct);
}

/// <summary>
/// Reads a department's configuration from outside Directory.
/// </summary>
/// <remarks>
/// <para>
/// S5 needs the activity taxonomy and weekly target on every write, and S6 and S9 need their own knobs. All of
/// them are in another module, so the read goes through a contract rather than through Directory's service, which
/// is internal to it.
/// </para>
/// <para>
/// Caller-scoped, so a config a caller may not read simply is not returned. Consumers are expected to fall back to
/// their own defaults rather than fail: a department's knobs are a refinement of platform behaviour, and not being
/// able to read them should degrade the refinement, not stop someone logging their week.
/// </para>
/// </remarks>
public interface IDepartmentConfigReader
{
    Task<DepartmentConfigSnapshot?> TryGetAsync(Guid departmentId, CancellationToken ct);
}
