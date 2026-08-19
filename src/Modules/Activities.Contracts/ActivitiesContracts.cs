using Cracra.BuildingBlocks.Messaging;

namespace Cracra.Modules.Activities.Contracts;

// =================================================================================================================
// The Activities module's public surface. S6 draws these rows on its timelines and S8 summarises them; both read
// through these DTOs rather than through the schema.
// =================================================================================================================

public sealed record ActivityTypeOption(
    string Code,
    string? ParentCode,
    string LabelKey,
    bool RequiresProject);

public sealed record ActivityEntryView(
    Guid Id,
    Guid PersonId,
    string? PersonName,
    Guid UnitId,
    Guid DepartmentId,
    string ActivityTypeCode,
    string ActivityTypeLabelKey,
    Guid? ProjectId,
    string? ProjectCode,
    Guid? IterationId,
    string Kind,
    string Source,
    string? ExternalRef,
    DateTimeOffset SlotStart,
    DateTimeOffset SlotEnd,
    decimal Hours,
    Guid? SupersedesEntryId,
    bool Reconciled,
    string? Note);

/// <summary>Hours by type for one person's week, and where that leaves them against the target.</summary>
public sealed record WeeklySummary(
    int IsoYear,
    int IsoWeek,
    DateOnly Monday,
    DateOnly Sunday,
    decimal TargetHours,
    bool Enforced,
    decimal ActualHours,
    decimal PlannedHours,
    decimal Overtime,
    string Status,
    IReadOnlyList<WeeklyTypeTotal> ByType);

public sealed record WeeklyTypeTotal(string ActivityTypeCode, string LabelKey, decimal PlannedHours, decimal ActualHours);

/// <summary>
/// A task pulled read-only from an external system, offered as a pre-fill.
/// </summary>
/// <remarks>
/// Shaped by what the dropdown needs, not by what Azure DevOps or ServiceNow happen to return: whichever system it
/// came from, the user is choosing a thing with a title and picking hours against it. S10 owns the adapters that
/// produce these.
/// </remarks>
public sealed record AssignableTask(
    string Source,
    string ExternalRef,
    string Title,
    string? State,
    Guid? ProjectId,
    string? SuggestedActivityTypeCode);

// --- Integration events ------------------------------------------------------------------------------------------

/// <summary>Reporting aggregates on this; S6 refreshes the affected row.</summary>
public sealed record ActivityLogged(
    Guid EntryId,
    Guid PersonId,
    string ActivityTypeCode,
    Guid? ProjectId,
    string Kind,
    decimal Hours,
    int IsoYear,
    int IsoWeek) : IntegrationEvent;

/// <summary>
/// A plan met its actual. Carries both sides, because the gap is the point.
/// </summary>
public sealed record ActivityReconciled(
    Guid ActualEntryId,
    Guid PlannedEntryId,
    Guid PersonId,
    decimal PlannedHours,
    decimal ActualHours,
    string PlannedTypeCode,
    string ActualTypeCode) : IntegrationEvent;
