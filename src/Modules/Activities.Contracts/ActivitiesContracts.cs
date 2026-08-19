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

/// <summary>
/// Writing planned activity on someone's behalf, for the scheduling boards.
/// </summary>
/// <remarks>
/// <para>
/// S6 assigns work orders and plans shifts, and both of those <em>are</em> planned activity — the whole point of
/// the boards is that what a lead schedules shows up in the person's own week. Rather than let Scheduling write
/// rows into this schema, it asks through here, so every entry still passes the taxonomy check, the slot rules and
/// the department's guardrail.
/// </para>
/// <para>
/// Planned only, deliberately. Nothing outside this module may assert that someone actually did something: an
/// actual is a claim about the past and only its owner, or their lead acting for them, gets to make it.
/// </para>
/// </remarks>
public interface IActivityScheduler
{
    /// <summary>Creates a planned slot for someone. Returns the new entry's id.</summary>
    Task<Guid> PlanAsync(
        Guid personId,
        string activityTypeCode,
        Guid? projectId,
        DateTimeOffset start,
        DateTimeOffset end,
        string? note,
        string source,
        string? externalRef,
        CancellationToken ct);

    /// <summary>Moves a planned slot. Used when a task or an assignment is dragged on the timeline.</summary>
    Task RescheduleAsync(Guid entryId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);

    /// <summary>Removes a planned slot this module created. Silently tolerates one already gone.</summary>
    Task CancelAsync(Guid entryId, CancellationToken ct);

    /// <summary>Entries for a set of people over a window — the rows every board is drawn from.</summary>
    Task<IReadOnlyList<ActivityEntryView>> GetForPeopleAsync(
        IReadOnlyList<Guid> personIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);

    /// <summary>Entries against a project over a window, whoever logged them.</summary>
    Task<IReadOnlyList<ActivityEntryView>> GetForProjectAsync(
        Guid projectId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
