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
