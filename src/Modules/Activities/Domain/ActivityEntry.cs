using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Activities.Domain;

/// <summary>
/// One logged slot of someone's time.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate root of the module, and the row every board in S6 and every report in S8 is ultimately drawn
/// from. Deliberately small: an entry knows who, when, what type, and how long, and nothing about how it will be
/// displayed or summed.
/// </para>
/// <para>
/// The owner's unit and department are copied onto the row rather than joined. RLS evaluates
/// <c>access.can_read_activity(owner, unit, project, dept)</c> against every candidate row, and a policy that had
/// to join into the Directory schema to find the unit would both cross a module boundary and turn every read into
/// a nested loop. The copy is a snapshot of where the person was when the work happened, which is also the honest
/// answer: work done last year in a unit someone has since left was still done in that unit.
/// </para>
/// </remarks>
public sealed class ActivityEntry
{
    private readonly List<object> _domainEvents = [];

    private ActivityEntry()
    {
        // EF Core.
    }

    public Guid Id { get; private init; }

    public Guid PersonId { get; private init; }

    public Guid UnitId { get; private init; }

    public Guid DepartmentId { get; private init; }

    /// <summary>The taxonomy code, resolved against the department's configuration when the entry was written.</summary>
    public string ActivityTypeCode { get; private set; } = string.Empty;

    public Guid? ProjectId { get; private set; }

    /// <summary>Optional link to the S4 iteration the work fell in. Never required — most work is not iterated.</summary>
    public Guid? IterationId { get; private set; }

    public ActivityKind Kind { get; private set; }

    public ActivitySource Source { get; private init; }

    /// <summary>The pulled work item or ticket this came from. Null for anything typed in by hand.</summary>
    public string? ExternalRef { get; private init; }

    public DateTimeOffset SlotStart { get; private set; }

    public DateTimeOffset SlotEnd { get; private set; }

    public decimal Hours { get; private set; }

    /// <summary>Denormalised from the slot so the weekly guardrail can index rather than compute. See <see cref="IsoWeek"/>.</summary>
    public int IsoYear { get; private set; }

    public int IsoWeekNumber { get; private set; }

    /// <summary>
    /// The planned entry this actual supersedes, when there was one.
    /// </summary>
    /// <remarks>
    /// Nullable because the spec is explicit that a bare actual is allowed. Most work is not planned in advance,
    /// and refusing to record an hour because nobody opened a slot for it would just mean the hour goes unrecorded.
    /// </remarks>
    public Guid? SupersedesEntryId { get; private set; }

    /// <summary>Set on the planned entry once an actual has reconciled against it.</summary>
    public bool Reconciled { get; private set; }

    public string? Note { get; private set; }

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public Guid ModifiedBy { get; private set; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public TimeSlot Slot => new(SlotStart, SlotEnd);

    public IsoWeek Week => new(IsoYear, IsoWeekNumber);

    public IReadOnlyCollection<object> DomainEvents => _domainEvents;

    /// <summary>
    /// Logs a slot, planned or actual.
    /// </summary>
    /// <remarks>
    /// The taxonomy is passed in rather than looked up, because it belongs to the department and only the
    /// application layer knows how to fetch one. What the aggregate owns is the consequence: whether this type
    /// demands a project, and whether one was given.
    /// </remarks>
    public static ActivityEntry Log(
        Guid personId,
        Guid unitId,
        Guid departmentId,
        ActivityTaxonomy taxonomy,
        string activityTypeCode,
        Guid? projectId,
        Guid? iterationId,
        ActivityKind kind,
        ActivitySource source,
        string? externalRef,
        TimeSlot slot,
        WorkHours? hours,
        string? note,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (personId == Guid.Empty)
        {
            throw new DomainRuleViolationException("An activity entry needs an owner.");
        }

        var type = taxonomy.Get(activityTypeCode);

        if (type.RequiresProject && projectId is null)
        {
            throw new DomainRuleViolationException(
                $"'{type.Code}' must be logged against a project.");
        }

        if (!type.RequiresProject && projectId is not null)
        {
            // Not refused, but not silently kept either — an hour of recruitment attributed to a project would
            // show up in that project's cost in S11 and nobody would know why.
            throw new DomainRuleViolationException(
                $"'{type.Code}' is not project work; remove the project or pick a project activity type.");
        }

        if (iterationId is not null && projectId is null)
        {
            throw new DomainRuleViolationException("An iteration link needs the project it belongs to.");
        }

        // Hours default to the slot's own length. Supplying them separately is for the case the spec cares about:
        // a two-hour meeting slot in which someone actually spent 30 minutes on this activity.
        var recorded = hours ?? new WorkHours(slot.Hours);

        if (recorded.Value <= 0)
        {
            throw new DomainRuleViolationException("An entry must record some time.");
        }

        if (recorded.Value > slot.Hours)
        {
            throw new DomainRuleViolationException(
                "An entry cannot record more hours than its slot is long.");
        }

        var week = IsoWeek.Of(slot.Start);

        var entry = new ActivityEntry
        {
            Id = Guid.CreateVersion7(),
            PersonId = personId,
            UnitId = unitId,
            DepartmentId = departmentId,
            ActivityTypeCode = type.Code,
            ProjectId = projectId,
            IterationId = iterationId,
            Kind = kind,
            Source = source,
            ExternalRef = string.IsNullOrWhiteSpace(externalRef) ? null : externalRef.Trim(),
            SlotStart = slot.Start,
            SlotEnd = slot.End,
            Hours = recorded.Value,
            IsoYear = week.Year,
            IsoWeekNumber = week.Week,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedBy = createdBy,
            CreatedAt = now,
            ModifiedBy = createdBy,
            ModifiedAt = now,
        };

        entry._domainEvents.Add(new ActivityLogged(
            entry.Id, personId, entry.ActivityTypeCode, projectId, kind, entry.Hours, week));

        return entry;
    }

    /// <summary>
    /// Amends an entry in place.
    /// </summary>
    /// <remarks>
    /// The owner, source and external reference are fixed for the life of the row. Re-pointing an entry at a
    /// different person would rewrite two people's weeks at once, and re-pointing a pulled entry at a different
    /// ticket would make the audit trail back to Azure DevOps a lie.
    /// </remarks>
    public void Amend(
        ActivityTaxonomy taxonomy,
        string activityTypeCode,
        Guid? projectId,
        Guid? iterationId,
        TimeSlot slot,
        WorkHours? hours,
        string? note,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        var type = taxonomy.Get(activityTypeCode);

        if (type.RequiresProject && projectId is null)
        {
            throw new DomainRuleViolationException($"'{type.Code}' must be logged against a project.");
        }

        if (!type.RequiresProject && projectId is not null)
        {
            throw new DomainRuleViolationException(
                $"'{type.Code}' is not project work; remove the project or pick a project activity type.");
        }

        var recorded = hours ?? new WorkHours(slot.Hours);

        if (recorded.Value <= 0 || recorded.Value > slot.Hours)
        {
            throw new DomainRuleViolationException(
                "An entry must record some time, and no more than its slot is long.");
        }

        var week = IsoWeek.Of(slot.Start);

        ActivityTypeCode = type.Code;
        ProjectId = projectId;
        IterationId = iterationId;
        SlotStart = slot.Start;
        SlotEnd = slot.End;
        Hours = recorded.Value;
        IsoYear = week.Year;
        IsoWeekNumber = week.Week;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        Touch(modifiedBy, now);

        _domainEvents.Add(new ActivityLogged(
            Id, PersonId, ActivityTypeCode, ProjectId, Kind, Hours, week));
    }

    /// <summary>
    /// Records that this actual entry supersedes a planned one.
    /// </summary>
    /// <remarks>
    /// The plan is kept, not overwritten. "We planned four hours of BUILD and spent six on RUN" is the single most
    /// useful thing this module can tell a lead, and it is only answerable while both numbers still exist.
    /// </remarks>
    public void Reconcile(ActivityEntry planned, Guid modifiedBy, DateTimeOffset now)
    {
        if (Kind is not ActivityKind.Actual)
        {
            throw new DomainRuleViolationException("Only an actual entry can reconcile a plan.");
        }

        if (planned.Kind is not ActivityKind.Planned)
        {
            throw new DomainRuleViolationException("An actual can only supersede a planned entry.");
        }

        if (planned.PersonId != PersonId)
        {
            throw new DomainRuleViolationException("An entry can only supersede its own owner's plan.");
        }

        if (planned.Reconciled)
        {
            throw new DomainRuleViolationException("That planned slot has already been reconciled.");
        }

        SupersedesEntryId = planned.Id;
        planned.Reconciled = true;
        planned.Touch(modifiedBy, now);

        Touch(modifiedBy, now);

        _domainEvents.Add(new ActivityReconciled(
            Id, planned.Id, PersonId, planned.Hours, Hours, planned.ActivityTypeCode, ActivityTypeCode));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void Touch(Guid modifiedBy, DateTimeOffset now)
    {
        ModifiedBy = modifiedBy;
        ModifiedAt = now;
    }
}

// --- Domain events -----------------------------------------------------------------------------------------------

public sealed record ActivityLogged(
    Guid EntryId,
    Guid PersonId,
    string ActivityTypeCode,
    Guid? ProjectId,
    ActivityKind Kind,
    decimal Hours,
    IsoWeek Week);

public sealed record ActivityReconciled(
    Guid ActualEntryId,
    Guid PlannedEntryId,
    Guid PersonId,
    decimal PlannedHours,
    decimal ActualHours,
    string PlannedTypeCode,
    string ActualTypeCode);
