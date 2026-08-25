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
    string ShiftTemplatesJson = "{}",
    /// <summary>
    /// When the department's day starts and ends, and where its morning and afternoon sit inside that. Empty means
    /// the defaults. Drives the board's axis and the quick-add presets, so a department that works 07:00-15:00 gets
    /// a canvas of its own hours rather than one built around somebody else's.
    /// </summary>
    string WorkingDayJson = "{}");

/// <summary>
/// A node's behaviour, after the inheritance walk — what the branch actually gets (v2 §10).
/// </summary>
/// <remarks>
/// <para>
/// Every field is resolved, so a consumer never re-implements the walk and never has to decide what a null means.
/// <paramref name="SourceCode"/> is present for display and support ("this unit is running the DELIVERY profile"),
/// not for branching: an architecture test asserts no module switches on it.
/// </para>
/// <para>
/// <paramref name="HeadlinePattern"/> stays nullable because "no pattern configured anywhere in my ancestry" is a
/// real answer — the report then falls back to its own sentence rather than rendering an empty template.
/// </para>
/// </remarks>
public sealed record NodeProfileSnapshot(
    string SourceCode,
    string LabelKey,
    string ActivityTaxonomyJson,
    IReadOnlyList<string> BoardArchetypes,
    IReadOnlyList<string> ItemTypes,
    IReadOnlyDictionary<string, bool> Capabilities,
    IReadOnlyList<string> SolvesCategories,
    string BudgetDefaultsJson,
    string? HeadlinePattern)
{
    /// <summary>Whether a capability is on for this node. Unknown codes are off — a typo must not grant a feature.</summary>
    public bool Allows(string capability) =>
        Capabilities.TryGetValue(capability, out var enabled) && enabled;
}

/// <summary>
/// Resolves the profile in force at a node, for modules outside Directory.
/// </summary>
/// <remarks>
/// Caller-scoped like the rest of Directory's read surface, and it degrades rather than throws: a node with no
/// profile anywhere in its ancestry returns null, and every consumer is expected to fall back to platform
/// behaviour. Profiles refine the platform; not being able to read one must not stop someone working.
/// </remarks>
public interface INodeProfileReader
{
    /// <summary>The effective profile for a unit, walking unit to department to the department's ancestors.</summary>
    Task<NodeProfileSnapshot?> ResolveForUnitAsync(Guid unitId, CancellationToken ct);

    /// <summary>The effective profile for a department, walking it and its ancestors.</summary>
    Task<NodeProfileSnapshot?> ResolveForDepartmentAsync(Guid departmentId, CancellationToken ct);

    /// <summary>The effective profile at a node, walking its ancestor path nearest-first.</summary>
    Task<NodeProfileSnapshot?> ResolveForNodeAsync(Guid nodeId, CancellationToken ct);
}

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
public sealed record HomeNodeScope(Guid NodeId, IReadOnlyList<Guid> NodePath);

public interface IOrgNodeReader
{
    Task<HomeNodeScope?> GetHomeScopeAsync(Guid personId, CancellationToken ct);

    /// <summary>A node and everything beneath it, in no particular order. RLS decides what the caller sees.</summary>
    Task<IReadOnlyList<OrgNodeSummary>> GetSubtreeAsync(Guid nodeId, CancellationToken ct);

    /// <summary>People attached anywhere under a node, each carrying the path that says where.</summary>
    Task<IReadOnlyList<NodeMember>> GetPeopleInSubtreeAsync(Guid nodeId, CancellationToken ct);

    /// <summary>
    /// A node's ancestry, root first and itself last. Empty where the node is unreadable or unknown.
    /// </summary>
    /// <remarks>
    /// The upward walk, where <see cref="GetSubtreeAsync"/> is the downward one. Anything inherited down the tree
    /// — a profile, a connection — is answered by asking which of these carries it.
    /// </remarks>
    Task<IReadOnlyList<Guid>> GetPathAsync(Guid nodeId, CancellationToken ct);
}

/// <summary>A person and where they hang. The path is what lets a caller fold them under any ancestor.</summary>
public sealed record NodeMember(
    Guid PersonId,
    string DisplayName,
    Guid NodeId,
    IReadOnlyList<Guid> NodeAncestorIds,
    Guid? UnitId,
    Guid? DepartmentId,
    bool Active);

/// <summary>One node, as anything outside Directory sees it. Level is a number, never a name.</summary>
public sealed record OrgNodeSummary(
    Guid Id,
    Guid? ParentId,
    int LevelNo,
    string Code,
    string Name,
    bool Active);

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
