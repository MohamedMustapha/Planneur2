using Cracra.BuildingBlocks.Messaging;

namespace Cracra.Modules.Scheduling.Contracts;

// =================================================================================================================
// The board payload, shaped for the Mobiscroll timeline.
//
// Shaped server-side on purpose. Five boards over four modules' data, each with its own idea of what a row is, is
// exactly the assembly work that goes wrong when it lives in a client: the department board joins units, projects
// and activities, and doing that in TypeScript would mean the client issuing four requests and re-deriving rules
// that RLS already settled. What arrives here is already rows and events.
// =================================================================================================================

/// <summary>Which board is being asked for. The rows and the archetype follow from this.</summary>
public static class BoardTypes
{
    public const string My = "my";
    public const string Team = "team";
    public const string Unit = "unit";
    public const string Project = "project";
    public const string Department = "department";

    public static readonly IReadOnlyList<string> All = [My, Team, Unit, Project, Department];
}

/// <summary>Which of the three Mobiscroll archetypes the client should render with.</summary>
public static class BoardArchetypes
{
    /// <summary>6a — work-order assignment, with the unassigned pool as a fixed top row.</summary>
    public const string WorkOrders = "work-orders";

    /// <summary>6b — the shift scheduler.</summary>
    public const string Shifts = "shifts";

    /// <summary>6c — task progress, with a progress bar on each event.</summary>
    public const string TaskProgress = "task-progress";
}

/// <summary>
/// A timeline row. Maps onto <c>MbscResource</c>.
/// </summary>
/// <param name="Id">Row identity. A person id, a project id, a unit id, or an activity-type code.</param>
/// <param name="Kind">What this row represents, so the client can template it: person, project, unit, lane, pool.</param>
/// <param name="ParentId">Set where rows nest — project members grouped by department, units within a department.</param>
/// <param name="Color">The row's default event colour, from the design's activity tokens.</param>
public sealed record BoardResource(
    string Id,
    string Name,
    string Kind,
    string? ParentId,
    string? Color,
    string? SubtitleKey);

/// <summary>
/// A timeline event. Maps onto <c>MbscCalendarEvent</c>.
/// </summary>
/// <param name="Progress">0–100 for 6c, null elsewhere. Actuals against plan, not a typed-in percentage.</param>
/// <param name="Editable">False where the caller may look but not move it — RLS decided, not the client.</param>
public sealed record BoardEvent(
    string Id,
    string ResourceId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Kind,
    string? Color,
    string? CssClass,
    int? Progress,
    bool Editable,
    string? ActivityTypeCode,
    Guid? ProjectId,
    string? ExternalRef);

/// <summary>
/// Something drawn across the whole board rather than on one row: a special day, a deadline, an iteration range.
/// </summary>
/// <remarks>
/// Kept separate from events because Mobiscroll renders them differently — as marked ranges and coloured columns
/// rather than as things you can drag — and because an overlay belongs to no resource.
/// </remarks>
public sealed record BoardOverlay(
    string Id,
    string Kind,
    string Title,
    DateOnly From,
    DateOnly To,
    string? Color);

/// <summary>A staffing gap the shift board should show. Never blocks; a lead decides what to do about it.</summary>
public sealed record CoverageWarning(DateOnly Day, string SlotCode, int Required, int Scheduled);

/// <summary>
/// The whole board.
/// </summary>
/// <param name="Archetype">Which of the three timelines to render — see <see cref="BoardArchetypes"/>.</param>
/// <param name="Pool">6a's fixed top row: work orders nobody has picked up yet. Empty for other archetypes.</param>
/// <param name="CanAssign">Whether the caller may drag. Answered here so the client does not have to guess.</param>
public sealed record BoardPayload(
    string BoardType,
    string Archetype,
    string Title,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<BoardResource> Resources,
    IReadOnlyList<BoardEvent> Events,
    IReadOnlyList<BoardOverlay> Overlays,
    IReadOnlyList<WorkOrderView> Pool,
    IReadOnlyList<CoverageWarning> Coverage,
    bool CanAssign);

public sealed record WorkOrderView(
    Guid Id,
    string Reference,
    string Title,
    string? Description,
    string Source,
    string? ExternalRef,
    Guid? ProjectId,
    string? ProjectCode,
    string ActivityTypeCode,
    Guid UnitId,
    Guid? AssignedToPersonId,
    string? AssignedToName,
    DateTimeOffset? ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    decimal EstimatedHours,
    string State);

public sealed record ShiftView(
    Guid Id,
    Guid PersonId,
    string? PersonName,
    Guid UnitId,
    string TemplateCode,
    string LabelKey,
    DateOnly Day,
    DateTimeOffset Start,
    DateTimeOffset End,
    decimal Hours);

/// <summary>One configurable shift slot: morning, afternoon, on-call, or whatever a department names.</summary>
public sealed record ShiftTemplate(
    string Code,
    string LabelKey,
    TimeOnly Start,
    TimeOnly End,
    int MinimumStaff,
    string? Color);

/// <summary>The RUN side of a unit's week, in the two numbers a lead actually asks about.</summary>
/// <param name="CoverageGaps">Slot-days that fell short of their minimum staffing. Warnings, never refusals.</param>
public sealed record ScheduleLoad(
    Guid UnitId,
    int OpenWorkOrders,
    int AssignedWorkOrders,
    decimal EstimatedHours,
    int ShiftsPlanned,
    decimal ShiftHours,
    int CoverageGaps);

/// <summary>
/// The RUN load of a unit over a window.
/// </summary>
/// <remarks>
/// Added for S8: the unit report needs "RUN load (work-orders / shift coverage)" and had no way to ask for it.
/// Deliberately six numbers rather than the rows behind them — a report summarizes, and handing it the whole
/// board would invite it to re-derive on the client what the board already computes correctly.
///
/// Caller-scoped like every other cross-module reader, so a report can only count what its reader may see.
/// </remarks>
public interface IScheduleLoadReader
{
    Task<ScheduleLoad> GetLoadAsync(Guid unitId, DateOnly from, DateOnly to, CancellationToken ct);
}

// --- Integration events ------------------------------------------------------------------------------------------

public sealed record WorkOrderAssigned(Guid WorkOrderId, Guid PersonId, Guid? ActivityEntryId) : IntegrationEvent;

public sealed record WorkOrderUnassigned(Guid WorkOrderId, Guid PreviousPersonId) : IntegrationEvent;

public sealed record ShiftPlanned(Guid ShiftId, Guid PersonId, DateOnly Day, string TemplateCode) : IntegrationEvent;
