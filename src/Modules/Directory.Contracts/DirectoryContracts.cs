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
    int Version);

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
