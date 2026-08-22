using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Scheduling.Contracts;
using Cracra.Modules.Scheduling.Domain;

namespace Cracra.Modules.Scheduling.Application;

public interface IWorkOrderRepository
{
    Task<WorkOrder> GetAsync(Guid workOrderId, CancellationToken ct);

    Task AddAsync(WorkOrder workOrder, CancellationToken ct);

    /// <summary>The 6a pool: open work orders in a unit, assigned or not.</summary>
    Task<IReadOnlyList<WorkOrder>> GetForUnitAsync(Guid unitId, CancellationToken ct);

    /// <summary>True where this external item is already shadowed locally, so a pull does not duplicate it.</summary>
    Task<bool> ExistsForExternalRefAsync(string source, string externalRef, CancellationToken ct);
}

public interface IShiftRepository
{
    Task<Shift> GetAsync(Guid shiftId, CancellationToken ct);

    Task AddAsync(Shift shift, CancellationToken ct);

    Task DeleteAsync(Shift shift, CancellationToken ct);

    Task<IReadOnlyList<Shift>> GetForUnitAsync(Guid unitId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<Shift>> GetForPersonAsync(Guid personId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>What Scheduling needs from the org to build rows.</summary>
public interface IDirectoryPort
{
    Task<PersonSummary?> GetPersonAsync(Guid personId, CancellationToken ct);

    Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(Guid? unitId, Guid? departmentId, CancellationToken ct);

    Task<IReadOnlyList<UnitSummary>> GetUnitsAsync(Guid? departmentId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetDepartmentNameKeysAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct);

    /// <summary>The department's shift slots, falling back to the platform defaults.</summary>
    Task<IReadOnlyList<Domain.ShiftTemplate>> GetShiftTemplatesAsync(Guid departmentId, CancellationToken ct);

    /// <summary>Which archetype a department's boards default to, from <c>default_board_layout</c>.</summary>
    Task<string> GetDefaultBoardLayoutAsync(Guid departmentId, CancellationToken ct);
}

public interface IProjectsPort
{
    Task<ProjectSummaryView?> GetProjectAsync(Guid projectId, CancellationToken ct);

    /// <summary>The team, already grouped by department then function — S3's own projection, reused as rows.</summary>
    Task<IReadOnlyList<ProjectMemberView>> GetTeamAsync(Guid projectId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetProjectCodesAsync(
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct);
}

public sealed record ProjectSummaryView(Guid Id, string Code, string Name, string Classification);

public sealed record ProjectMemberView(
    Guid PersonId,
    string? DisplayName,
    Guid DepartmentId,
    string DepartmentNameKey,
    string FunctionCode);

/// <summary>The activity rows every board is drawn from, and the write-back for assignments.</summary>
public interface IActivitiesPort
{
    Task<IReadOnlyList<ActivityEntryView>> GetForPeopleAsync(
        IReadOnlyList<Guid> personIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);

    Task<IReadOnlyList<ActivityEntryView>> GetForProjectAsync(
        Guid projectId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);

    Task<Guid> PlanAsync(
        Guid personId,
        string activityTypeCode,
        Guid? projectId,
        DateTimeOffset start,
        DateTimeOffset end,
        string? note,
        string source,
        string? externalRef,
        int? percentComplete,
        CancellationToken ct);

    Task SetProgressAsync(Guid entryId, int? percentComplete, CancellationToken ct);

    Task RescheduleAsync(Guid entryId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);

    Task CancelAsync(Guid entryId, CancellationToken ct);
}

/// <summary>Iteration ranges from S4, drawn as overlays on the project board.</summary>
public interface IPortfolioPort
{
    Task<IReadOnlyList<IterationRange>> GetIterationsAsync(Guid projectId, CancellationToken ct);
}

public sealed record IterationRange(Guid Id, string Name, DateOnly StartsOn, DateOnly EndsOn, string State);

/// <summary>
/// Special days, deadlines and meeting bands — S7's overlays.
/// </summary>
/// <remarks>
/// <para>
/// Declared here and answered with nothing until S7 exists. The spec is explicit that the boards can ship first
/// and the overlay follow, and this is what makes that true: the department board already asks the question and
/// already renders whatever comes back, so S7 is a registration change rather than a board change.
/// </para>
/// <para>
/// The alternative — leaving the concept out entirely and adding it later — would mean revisiting every board's
/// payload shape and its client template at exactly the point where the schedule is tightest.
/// </para>
/// </remarks>
public interface ICalendarOverlaySource
{
    Task<IReadOnlyList<BoardOverlay>> GetOverlaysAsync(
        Guid? unitId,
        Guid? departmentId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}

/// <summary>
/// The read-only pull of unassigned work from an external system.
/// </summary>
/// <remarks>
/// The 6a pool's other source. S5 already declares the per-person equivalent for its dropdown; this one is
/// per-unit and returns what nobody has picked up, which is a different question and a different query.
/// </remarks>
public interface IWorkOrderPoolSource
{
    string Source { get; }

    Task<IReadOnlyList<PooledWorkOrder>> GetUnassignedAsync(Guid unitId, CancellationToken ct);
}

public sealed record PooledWorkOrder(
    string ExternalRef,
    string Title,
    string? Description,
    decimal EstimatedHours);
