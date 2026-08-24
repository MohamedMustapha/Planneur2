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
    string? Note,
    /// <summary>0-100 where somebody set one, null where the board should fall back to plan-versus-actual.</summary>
    int? PercentComplete = null);

/// <summary>
/// The shape of a working day, as the department defines it.
/// </summary>
/// <remarks>
/// Travels with the weekly summary rather than through an endpoint of its own, because it answers the same
/// question that payload already answers — what this department expects of this person's week — and the two would
/// otherwise be fetched together on every screen that draws a board.
/// </remarks>
public sealed record WorkingDay(
    TimeOnly DayStart,
    TimeOnly DayEnd,
    TimeOnly MorningStart,
    TimeOnly MorningEnd,
    TimeOnly AfternoonStart,
    TimeOnly AfternoonEnd);

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
    IReadOnlyList<WeeklyTypeTotal> ByType,
    /// <summary>The department's working day. Drives the board's axis and the quick-add presets.</summary>
    WorkingDay? WorkingDay = null);

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
        int? percentComplete,
        CancellationToken ct);

    /// <summary>
    /// Sets how far along a planned slot is, or clears it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RescheduleAsync"/> because the board offers the two as separate gestures — drag
    /// the block, or drag its progress handle — and collapsing them would make each one re-validate the other.
    /// </remarks>
    Task SetProgressAsync(Guid entryId, int? percentComplete, CancellationToken ct);

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

    /// <summary>
    /// Hours over a window, grouped by the node each entry is attached to, for every node in a subtree.
    /// </summary>
    /// <remarks>
    /// Grouped rather than rolled up: the caller owns the tree and can fold these into any shape it needs, and a
    /// per-node total that the caller sums itself is what makes the rollup invariant hold by construction instead
    /// of by two aggregations agreeing. RLS still applies, so this returns the viewer's own view of the subtree.
    /// </remarks>
    Task<IReadOnlyList<NodeHoursSlice>> GetHoursByNodeAsync(
        Guid rootNodeId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);

    /// <summary>Where each node's hours actually went, one row per node and activity type (v2 §07.3).</summary>
    Task<IReadOnlyList<NodeActivitySlice>> GetHoursByNodeAndTypeAsync(
        Guid rootNodeId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}

/// <summary>One node's hours against one activity type. The brief's "what did they actually do" line.</summary>
public sealed record NodeActivitySlice(Guid NodeId, string ActivityTypeCode, decimal ActualHours);

/// <summary>What one node's directly-attached people logged over a window.</summary>
public sealed record NodeHoursSlice(
    Guid NodeId,
    decimal ActualHours,
    decimal PlannedHours,
    int EntryCount,
    int PeopleCount);

/// <summary>
/// A department's activity types, as this module resolved them.
/// </summary>
/// <remarks>
/// <para>
/// Added by S11, which has to answer "is this hour BUILD or RUN" for an entry whose type may be a department's
/// own subtype — <c>project-build-poc</c> under <c>project-build</c>. Only this module can say: the merge of a
/// department's configured taxonomy over the canonical buckets lives in its Domain, and the entry view carries a
/// code without its lineage.
/// </para>
/// <para>
/// The alternative was for Finance to parse <c>activity_taxonomy_json</c> itself, which would put a second
/// implementation of the merge rules in the system — and the first department to add a subtype would discover the
/// two disagreeing about what its hours cost.
/// </para>
/// <para>
/// Caller-scoped like everything else here: it reads the department's configuration through the same reader S5
/// uses, so a consumer resolves only taxonomies it was already allowed to read, and falls back to the canonical
/// buckets otherwise.
/// </para>
/// </remarks>
public interface IActivityTaxonomyReader
{
    /// <summary>
    /// The department's types, each carrying its parent where it has one.
    /// </summary>
    /// <remarks>
    /// Returns the merged set rather than a code-to-bucket map, because the caller may want the labels too — and
    /// because a map would bake in the assumption that the hierarchy is only ever one level deep.
    /// </remarks>
    Task<IReadOnlyList<ActivityTypeOption>> GetTypesAsync(Guid? departmentId, CancellationToken ct);
}
