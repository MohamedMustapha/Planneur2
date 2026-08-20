using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Scheduling.Domain;

public enum WorkOrderState
{
    /// <summary>In the pool, on the fixed top row, waiting for someone to take it.</summary>
    Unassigned = 0,
    Assigned = 1,
    Done = 2,
    Cancelled = 3,
}

/// <summary>
/// A RUN task waiting to be given to someone.
/// </summary>
/// <remarks>
/// <para>
/// The 6a archetype's unit of work: tickets arrive in the pool — typed in, or pulled read-only from ServiceNow —
/// and a lead drags them onto an agent's row. This is the thin domain the slice warrants, because assignment has
/// rules that a projection cannot enforce: a work order belongs to one person at a time, a closed one cannot be
/// reassigned, and assigning is what brings a planned activity into existence.
/// </para>
/// <para>
/// A pulled work order is a local shadow of the ticket, never the ticket itself. Nothing here is ever written back
/// to ServiceNow — the moment it were, two systems would own the same fact and they would disagree by lunchtime.
/// The state below is the platform's view of the assignment, not the ticket's lifecycle.
/// </para>
/// </remarks>
public sealed class WorkOrder
{
    private readonly List<object> _domainEvents = [];

    private WorkOrder()
    {
        // EF Core.
    }

    public Guid Id { get; private init; }

    /// <summary>Short human handle, shown on the pool card. Mirrors the external reference where there is one.</summary>
    public string Reference { get; private init; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>manual | servicenow | azure-devops. Where the item came from.</summary>
    public string Source { get; private init; } = "manual";

    public string? ExternalRef { get; private init; }

    /// <summary>
    /// The unit whose pool this sits in.
    /// </summary>
    /// <remarks>
    /// Work orders belong to a unit rather than to a project, because a helpdesk queue is a unit's queue. RLS
    /// scopes the pool by this, which is what stops one team's tickets appearing on another team's board.
    /// </remarks>
    public Guid UnitId { get; private init; }

    public Guid DepartmentId { get; private init; }

    public Guid? ProjectId { get; private set; }

    /// <summary>
    /// The activity type an assignment will book against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Carried on the order rather than assumed, because <c>project-run</c> — the obvious default, and what the
    /// spec names — requires a project under S5's taxonomy, and a helpdesk queue routinely has tickets attached to
    /// no project at all. Rather than weaken that rule or quietly file incidents under a bucket that means
    /// something else, the order says which type it is, and a department with unattached RUN work configures a
    /// top-level type for it. That is precisely what S5's extension mechanism is for.
    /// </para>
    /// <para>
    /// Where the chosen type does need a project and none is set, Activities refuses the assignment with its own
    /// message. That is the right failure: it tells the lead to link a project or pick another type, which are the
    /// only two things that would actually fix it.
    /// </para>
    /// </remarks>
    public string ActivityTypeCode { get; private init; } = "project-run";

    public Guid? AssignedToPersonId { get; private set; }

    /// <summary>The planned activity assignment created. Removing the assignment removes that entry too.</summary>
    public Guid? ActivityEntryId { get; private set; }

    public DateTimeOffset? ScheduledStart { get; private set; }

    public DateTimeOffset? ScheduledEnd { get; private set; }

    /// <summary>What the lead expects it to take. Defaults to an hour, which is the honest guess for a ticket.</summary>
    public decimal EstimatedHours { get; private set; }

    public WorkOrderState State { get; private set; }

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public Guid ModifiedBy { get; private set; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public IReadOnlyCollection<object> DomainEvents => _domainEvents;

    public bool IsOpen => State is WorkOrderState.Unassigned or WorkOrderState.Assigned;

    public static WorkOrder Create(
        string reference,
        string title,
        string? description,
        string source,
        string? externalRef,
        Guid unitId,
        Guid departmentId,
        Guid? projectId,
        string? activityTypeCode,
        decimal estimatedHours,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleViolationException("A work order needs a title.");
        }

        if (unitId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A work order needs the unit whose pool it belongs to.");
        }

        if (estimatedHours <= 0 || estimatedHours > 24)
        {
            throw new DomainRuleViolationException("An estimate must be between a quarter of an hour and a day.");
        }

        return new WorkOrder
        {
            Id = Guid.CreateVersion7(),
            Reference = string.IsNullOrWhiteSpace(reference) ? "WO" : reference.Trim(),
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Source = string.IsNullOrWhiteSpace(source) ? "manual" : source.Trim().ToLowerInvariant(),
            ExternalRef = string.IsNullOrWhiteSpace(externalRef) ? null : externalRef.Trim(),
            UnitId = unitId,
            DepartmentId = departmentId,
            ProjectId = projectId,
            ActivityTypeCode = string.IsNullOrWhiteSpace(activityTypeCode)
                ? "project-run"
                : activityTypeCode.Trim().ToLowerInvariant(),
            EstimatedHours = estimatedHours,
            State = WorkOrderState.Unassigned,
            CreatedBy = createdBy,
            ModifiedBy = createdBy,
            CreatedAt = now,
            ModifiedAt = now,
        };
    }

    /// <summary>
    /// Gives the work order to someone, over a slot.
    /// </summary>
    /// <remarks>
    /// The slot is what makes the assignment renderable: dragging a pool card onto a row lands it at a time, and
    /// that time is the planned activity's slot. The caller creates that activity and hands back its id, because
    /// only Activities can apply its own rules to it.
    /// </remarks>
    public void AssignTo(Guid personId, DateTimeOffset start, DateTimeOffset end, Guid assignedBy, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new DomainRuleViolationException($"A {State.ToString().ToLowerInvariant()} work order cannot be assigned.");
        }

        if (personId == Guid.Empty)
        {
            throw new DomainRuleViolationException("An assignment needs someone to assign to.");
        }

        if (end <= start)
        {
            throw new DomainRuleViolationException("An assignment must end after it starts.");
        }

        // Reassignment is a legitimate act — a ticket moves between agents all day — but it has to go through
        // unassign first so the previous person's planned activity is actually removed rather than orphaned.
        if (State is WorkOrderState.Assigned && AssignedToPersonId != personId)
        {
            throw new DomainRuleViolationException(
                "That work order is already assigned. Unassign it before giving it to someone else.");
        }

        AssignedToPersonId = personId;
        ScheduledStart = start;
        ScheduledEnd = end;
        State = WorkOrderState.Assigned;

        Touch(assignedBy, now);
    }

    /// <summary>Records the planned activity the assignment created, so unassigning knows what to remove.</summary>
    public void LinkActivity(Guid activityEntryId)
    {
        ActivityEntryId = activityEntryId;

        _domainEvents.Add(new WorkOrderAssigned(Id, AssignedToPersonId!.Value, activityEntryId));
    }

    /// <summary>
    /// Returns the work order to the pool.
    /// </summary>
    /// <remarks>
    /// Returns the activity entry to remove, rather than removing it: entries are Activities' to delete, and this
    /// aggregate knowing how would be it reaching across a module boundary to do so.
    /// </remarks>
    public Guid? Unassign(Guid unassignedBy, DateTimeOffset now)
    {
        if (State is not WorkOrderState.Assigned)
        {
            throw new DomainRuleViolationException("That work order is not assigned to anyone.");
        }

        var previous = AssignedToPersonId!.Value;
        var entryId = ActivityEntryId;

        AssignedToPersonId = null;
        ActivityEntryId = null;
        ScheduledStart = null;
        ScheduledEnd = null;
        State = WorkOrderState.Unassigned;

        Touch(unassignedBy, now);

        _domainEvents.Add(new WorkOrderUnassigned(Id, previous));

        return entryId;
    }

    /// <summary>Moves an already-assigned order in time, without changing who owns it.</summary>
    public void Reschedule(DateTimeOffset start, DateTimeOffset end, Guid modifiedBy, DateTimeOffset now)
    {
        if (State is not WorkOrderState.Assigned)
        {
            throw new DomainRuleViolationException("Only an assigned work order has a schedule to change.");
        }

        if (end <= start)
        {
            throw new DomainRuleViolationException("An assignment must end after it starts.");
        }

        ScheduledStart = start;
        ScheduledEnd = end;

        Touch(modifiedBy, now);
    }

    public void Close(Guid closedBy, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new DomainRuleViolationException($"That work order is already {State.ToString().ToLowerInvariant()}.");
        }

        State = WorkOrderState.Done;

        Touch(closedBy, now);
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void Touch(Guid modifiedBy, DateTimeOffset now)
    {
        ModifiedBy = modifiedBy;
        ModifiedAt = now;
    }
}

// --- Domain events -----------------------------------------------------------------------------------------------

public sealed record WorkOrderAssigned(Guid WorkOrderId, Guid PersonId, Guid? ActivityEntryId);

public sealed record WorkOrderUnassigned(Guid WorkOrderId, Guid PreviousPersonId);
