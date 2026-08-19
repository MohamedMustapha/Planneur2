using Cracra.BuildingBlocks.Messaging;

namespace Cracra.Modules.Projects.Contracts;

// =================================================================================================================
// The Projects module's public surface. Portfolio (S4), Activities (S5), Scheduling (S6), Reporting (S8) and
// Finance (S11) all hang off these — additive-only once released, because a consumer may be deserializing an
// outbox row written before the current deploy.
// =================================================================================================================

public sealed record ProjectSummary(
    Guid Id,
    string Code,
    string Name,
    string Classification,
    decimal CostAmount,
    string CostCurrency,
    Guid LeadDepartmentId,
    Guid OwnerPersonId,
    int ActiveMemberCount);

public sealed record ProjectDepartmentSummary(Guid DepartmentId, string NameKey, bool IsLead);

public sealed record ProjectDetail(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Classification,
    decimal CostAmount,
    string CostCurrency,
    string? CostNotes,
    Guid LeadDepartmentId,
    Guid OwnerPersonId,
    IReadOnlyList<ProjectDepartmentSummary> Departments);

/// <summary>
/// One member of the team. <see cref="DisplayName"/> is null when row-level security hid the person from this
/// caller — they still count toward the headcount, because omitting them would misreport the team's size.
/// </summary>
public sealed record ProjectTeamMember(Guid PersonId, string? DisplayName, int? AllocationPercent, DateOnly From);

public sealed record ProjectTeamFunction(Guid FunctionalRoleId, string Code, IReadOnlyList<ProjectTeamMember> Members);

public sealed record ProjectTeamDepartment(
    Guid DepartmentId,
    string NameKey,
    IReadOnlyList<ProjectTeamFunction> Functions);

/// <summary>The team grouped by department, then by function — the shape the project view renders directly.</summary>
public sealed record ProjectTeam(Guid ProjectId, IReadOnlyList<ProjectTeamDepartment> Departments, int TotalMembers);

// --- Integration events --------------------------------------------------------------------------------------

public sealed record ProjectCreated(Guid ProjectId, string Code, string Name, Guid LeadDepartmentId)
    : IntegrationEvent;

public sealed record ProjectUpdated(Guid ProjectId, string Classification, decimal CostAmount, string CostCurrency)
    : IntegrationEvent;

/// <summary>
/// Raised when the team changes.
/// </summary>
/// <remarks>
/// Carries the department because consumers scope by it — Reporting splits contribution per department, and
/// Scheduling needs to know which unit's board a member's work belongs on.
///
/// Note this is <em>not</em> what maintains the Access membership projection. That happens synchronously inside
/// the same transaction, because it decides who can see the project and cannot be allowed to lag.
/// </remarks>
public sealed record ProjectMemberChanged(Guid ProjectId, Guid PersonId, Guid DepartmentId, bool Added)
    : IntegrationEvent;

public sealed record ProjectDepartmentsChanged(Guid ProjectId, IReadOnlyList<Guid> DepartmentIds) : IntegrationEvent;

/// <summary>Someone who could be added to a project team, and the department they would contribute from.</summary>
public sealed record TeamCandidate(Guid PersonId, string DisplayName, Guid? DepartmentId);

/// <summary>
/// What Portfolio needs from Projects at the moment of commitment.
/// </summary>
/// <remarks>
/// Committing to a candidate is where it stops being hypothetical and acquires a team and a cost — which means it
/// acquires a Project. Portfolio can link one that already exists, or ask for one to be provisioned; either way it
/// goes through this rather than reaching into the Projects schema.
///
/// Synchronous and in the caller's transaction: a commitment that recorded the decision but lost the project would
/// leave an item committed to nothing, and the repair is manual.
/// </remarks>
public interface IProjectProvisioner
{
    /// <summary>Creates a project for a newly committed portfolio item and returns its id.</summary>
    Task<Guid> ProvisionAsync(
        string code,
        string name,
        string? description,
        Guid leadDepartmentId,
        CancellationToken ct);

    /// <summary>True when the project exists and the caller can see it — used to validate an explicit link.</summary>
    Task<bool> ExistsAsync(Guid projectId, CancellationToken ct);

    /// <summary>True when the project has at least one active member. The activation guard needs it.</summary>
    Task<bool> HasTeamAsync(Guid projectId, CancellationToken ct);

    /// <summary>Cost and classification for the board cards, which show them from S3.</summary>
    Task<IReadOnlyDictionary<Guid, ProjectSummary>> GetSummariesAsync(
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct);

    /// <summary>Every project the caller can read. RLS decides what that means; this does not filter further.</summary>
    Task<IReadOnlyList<Guid>> GetVisibleProjectIdsAsync(CancellationToken ct);
}

/// <summary>
/// Membership questions other modules ask about a project.
/// </summary>
/// <remarks>
/// Separate from <see cref="IProjectProvisioner"/> because the callers are different: provisioning is Portfolio's
/// one act at commitment, whereas S5 asks this on every hour anyone books. One interface holding both would give
/// each caller a surface mostly made of methods it must not use.
/// </remarks>
public interface IProjectMembershipReader
{
    /// <summary>True when the person is currently on the project — a past membership does not count.</summary>
    Task<bool> IsActiveMemberAsync(Guid projectId, Guid personId, CancellationToken ct);
}

/// <summary>One member of a project team, flattened for a consumer that groups them itself.</summary>
public sealed record ProjectTeamMemberView(
    Guid PersonId,
    string? DisplayName,
    Guid DepartmentId,
    string DepartmentNameKey,
    string FunctionCode);

/// <summary>
/// The team, for modules that draw rows from it.
/// </summary>
/// <remarks>
/// Flat rather than the nested <see cref="ProjectTeam"/> the API returns: S6 nests it differently again (a
/// department row, then a person row beneath it) and re-flattening a tree only to rebuild another one is work that
/// exists purely because the shape was chosen for a different consumer.
/// </remarks>
public interface IProjectTeamReader
{
    Task<IReadOnlyList<ProjectTeamMemberView>> GetTeamAsync(Guid projectId, CancellationToken ct);
}
