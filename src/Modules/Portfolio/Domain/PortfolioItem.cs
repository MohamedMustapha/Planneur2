using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Portfolio.Domain;

/// <summary>
/// The portfolio lifecycle, using the glossary's vocabulary verbatim.
/// </summary>
/// <remarks>
/// <c>Dephase</c> is French shorthand the organisation already uses for "retired from production". It is kept as
/// the stored code rather than translated to "archived" because the glossary fixes it, the UI keys off it, and a
/// term the business already says out loud is worth more than a tidier English one.
/// </remarks>
public enum PortfolioState
{
    Considered = 0,
    Committed = 1,
    Active = 2,
    Dephase = 3,
}

/// <summary>
/// A project's place in the portfolio, and its lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// Exists independently of a Project. A <see cref="PortfolioState.Considered"/> item is a candidate nobody has
/// committed to yet — there is no team, no cost, and no reason to create a full project for something that may
/// never happen. The Project is created or linked at the moment of commitment, which is exactly when it stops
/// being hypothetical.
/// </para>
/// <para>
/// Every transition is guarded and stamped. "Why is this déphasé and who decided" is the question this aggregate
/// exists to keep answerable.
/// </para>
/// </remarks>
public sealed class PortfolioItem
{
    private readonly List<Iteration> _iterations = [];
    private readonly List<object> _domainEvents = [];

    private PortfolioItem()
    {
        // EF Core.
    }

    public Guid Id { get; private init; }

    /// <summary>Null while the item is only a candidate. Set on commitment, and never cleared afterwards.</summary>
    public Guid? ProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public PortfolioState State { get; private set; }

    /// <summary>1 is highest. Free-form ordering within a lane on the board.</summary>
    public int Priority { get; private set; }

    /// <summary>
    /// The department sponsoring the candidate. Needed before there is a project, because RLS has to scope a
    /// considered stub somehow and there is no team to scope it by.
    /// </summary>
    public Guid DepartmentId { get; private set; }

    public string? DecisionNotes { get; private set; }

    public DateTimeOffset ConsideredAt { get; private set; }

    public DateTimeOffset? CommittedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public Guid CreatedBy { get; private init; }

    public Guid ModifiedBy { get; private set; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public IReadOnlyCollection<Iteration> Iterations => _iterations;

    public IReadOnlyCollection<object> DomainEvents => _domainEvents;

    public Iteration? CurrentIteration =>
        _iterations.FirstOrDefault(iteration => iteration.State is IterationState.Active)
        ?? _iterations.Where(iteration => iteration.State is IterationState.Planned)
            .OrderBy(iteration => iteration.Sequence)
            .FirstOrDefault();

    /// <summary>Once archived, everything downstream is read-only — S6 freezes its boards off this.</summary>
    public bool IsArchived => State is PortfolioState.Dephase;

    public static PortfolioItem Consider(
        string name,
        int priority,
        Guid departmentId,
        string? notes,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A candidate needs a name.");
        }

        if (departmentId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A candidate needs a sponsoring department.");
        }

        var item = new PortfolioItem
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            State = PortfolioState.Considered,
            Priority = priority,
            DepartmentId = departmentId,
            DecisionNotes = notes?.Trim(),
            ConsideredAt = now,
            CreatedBy = createdBy,
            ModifiedBy = createdBy,
            ModifiedAt = now,
        };

        item._domainEvents.Add(new ItemConsidered(item.Id, item.Name, departmentId));

        return item;
    }

    /// <summary>
    /// Commits to the candidate, linking the project that will deliver it.
    /// </summary>
    /// <remarks>
    /// Notes are required. This is the transition where money and people start being spent, and "who decided to
    /// commit to this, and why" is the first question anyone asks six months later — a blank field there makes the
    /// audit trail technically complete and practically useless.
    /// </remarks>
    public void Commit(Guid projectId, string decisionNotes, Guid decidedBy, DateTimeOffset now)
    {
        Require(PortfolioState.Considered, PortfolioState.Committed);

        if (string.IsNullOrWhiteSpace(decisionNotes))
        {
            throw new DomainRuleViolationException(
                "Committing needs a decision note: someone will ask why this was committed.");
        }

        if (projectId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Committing needs a project to deliver the work.");
        }

        ProjectId = projectId;
        State = PortfolioState.Committed;
        DecisionNotes = decisionNotes.Trim();
        CommittedAt = now;

        Touch(decidedBy, now);

        _domainEvents.Add(new ItemCommitted(Id, projectId, decisionNotes.Trim()));
    }

    /// <summary>
    /// Starts delivery.
    /// </summary>
    /// <remarks>
    /// Requires at least one planned iteration, and <paramref name="hasTeam"/> asserts the project has members —
    /// which only the Projects module can answer, so the caller supplies it. An active project with nobody on it
    /// and no plan is a status, not a state.
    /// </remarks>
    public void Activate(bool hasTeam, Guid decidedBy, DateTimeOffset now)
    {
        Require(PortfolioState.Committed, PortfolioState.Active);

        if (!_iterations.Any(iteration => iteration.State is IterationState.Planned))
        {
            throw new DomainRuleViolationException(
                "Activating needs at least one planned iteration. Add one first.");
        }

        if (!hasTeam)
        {
            throw new DomainRuleViolationException("Activating needs a team on the project.");
        }

        State = PortfolioState.Active;
        ActivatedAt = now;

        // The first planned iteration starts with the item; leaving it planned would mean an active project whose
        // iteration strip shows nothing running.
        _iterations
            .Where(iteration => iteration.State is IterationState.Planned)
            .OrderBy(iteration => iteration.Sequence)
            .FirstOrDefault()
            ?.Start(now);

        Touch(decidedBy, now);

        _domainEvents.Add(new ItemActivated(Id, ProjectId));
    }

    /// <summary>
    /// Retires the item from production.
    /// </summary>
    /// <remarks>
    /// Cancels every open iteration in the same act. An archived item with an iteration still marked active would
    /// leave S6's boards showing live work on something nobody is delivering.
    /// </remarks>
    public void Archive(string reason, Guid decidedBy, DateTimeOffset now)
    {
        if (State is PortfolioState.Dephase)
        {
            throw new DomainRuleViolationException("This item is already déphasé.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainRuleViolationException("Archiving needs a reason.");
        }

        foreach (var iteration in _iterations)
        {
            iteration.CancelIfOpen(now);
        }

        State = PortfolioState.Dephase;
        ArchivedAt = now;
        DecisionNotes = reason.Trim();

        Touch(decidedBy, now);

        _domainEvents.Add(new ItemArchived(Id, ProjectId, reason.Trim()));
    }

    /// <summary>
    /// Moves the item back to an earlier state. PMO only, and the caller enforces that.
    /// </summary>
    /// <remarks>
    /// Exists because reality does: a project gets archived by mistake, or is committed and then paused. Refusing
    /// to model it would only mean somebody editing the database by hand. The reason is mandatory and the whole
    /// thing is audited.
    /// </remarks>
    public void Revert(PortfolioState target, string reason, Guid decidedBy, DateTimeOffset now)
    {
        if (target >= State)
        {
            throw new DomainRuleViolationException(
                $"{target} is not earlier than {State}; use the forward transitions.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainRuleViolationException("Reverting needs a reason.");
        }

        State = target;
        DecisionNotes = reason.Trim();

        ArchivedAt = null;

        if (target < PortfolioState.Active)
        {
            ActivatedAt = null;
        }

        if (target < PortfolioState.Committed)
        {
            CommittedAt = null;
        }

        Touch(decidedBy, now);

        _domainEvents.Add(new ItemReverted(Id, target, reason.Trim()));
    }

    public Iteration AddIteration(
        string name,
        IterationLength length,
        DateOnly startsOn,
        DateOnly? endsOn,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        if (IsArchived)
        {
            throw new DomainRuleViolationException("A déphasé item is read-only.");
        }

        var iteration = Iteration.Create(Id, _iterations.Count + 1, name, length, startsOn, endsOn, now);

        _iterations.Add(iteration);

        Touch(modifiedBy, now);

        _domainEvents.Add(new IterationOpened(Id, iteration.Id, iteration.StartsOn, iteration.EndsOn));

        return iteration;
    }

    public void RescheduleIteration(
        Guid iterationId,
        string name,
        IterationLength length,
        DateOnly startsOn,
        DateOnly? endsOn,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        if (IsArchived)
        {
            throw new DomainRuleViolationException("A déphasé item is read-only.");
        }

        Find(iterationId).Reschedule(name, length, startsOn, endsOn, now);

        Touch(modifiedBy, now);
    }

    /// <summary>
    /// Cancels an iteration that will not be run.
    /// </summary>
    /// <remarks>
    /// Cancelled, not deleted. A plan that changed is history worth keeping — "we had three sprints scheduled and
    /// dropped one" is a different story from "we always planned two", and only one of them is true. The sequence
    /// numbers stay as they were for the same reason.
    /// </remarks>
    public void CancelIteration(Guid iterationId, Guid modifiedBy, DateTimeOffset now)
    {
        if (IsArchived)
        {
            throw new DomainRuleViolationException("A déphasé item is read-only.");
        }

        var iteration = Find(iterationId);

        if (!iteration.IsOpen)
        {
            throw new DomainRuleViolationException($"This iteration is already {iteration.State}.");
        }

        iteration.CancelIfOpen(now);

        Touch(modifiedBy, now);

        _domainEvents.Add(new IterationClosed(Id, iterationId));
    }

    public void CloseIteration(Guid iterationId, Guid modifiedBy, DateTimeOffset now)
    {
        if (IsArchived)
        {
            throw new DomainRuleViolationException("A déphasé item is read-only.");
        }

        var iteration = Find(iterationId);

        iteration.Close(now);

        // The next planned one takes over, so an active item always has something running. Without this, closing
        // the last iteration would leave the strip empty and the board misleading.
        _iterations
            .Where(candidate => candidate.State is IterationState.Planned)
            .OrderBy(candidate => candidate.Sequence)
            .FirstOrDefault()
            ?.Start(now);

        Touch(modifiedBy, now);

        _domainEvents.Add(new IterationClosed(Id, iterationId));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private Iteration Find(Guid iterationId) =>
        _iterations.SingleOrDefault(iteration => iteration.Id == iterationId)
        ?? throw new ResourceNotFoundException($"No iteration {iterationId} on this item.");

    /// <summary>
    /// The state machine itself: forward transitions happen one step at a time and only from the state before.
    /// </summary>
    /// <remarks>
    /// Skipping is refused rather than silently allowed. Jumping considered → active would leave no committed_at
    /// stamp, and the portfolio's whole value is being able to say when each decision was taken.
    /// </remarks>
    private void Require(PortfolioState from, PortfolioState to)
    {
        if (State != from)
        {
            throw new DomainRuleViolationException(
                $"An item must be {from.ToString().ToLowerInvariant()} to become "
                + $"{to.ToString().ToLowerInvariant()}; this one is {State.ToString().ToLowerInvariant()}.");
        }
    }

    private void Touch(Guid modifiedBy, DateTimeOffset now)
    {
        ModifiedBy = modifiedBy;
        ModifiedAt = now;
    }
}

// --- Domain events -------------------------------------------------------------------------------------------

public sealed record ItemConsidered(Guid ItemId, string Name, Guid DepartmentId);

public sealed record ItemCommitted(Guid ItemId, Guid ProjectId, string DecisionNotes);

public sealed record ItemActivated(Guid ItemId, Guid? ProjectId);

public sealed record ItemArchived(Guid ItemId, Guid? ProjectId, string Reason);

public sealed record ItemReverted(Guid ItemId, PortfolioState Target, string Reason);

public sealed record IterationOpened(Guid ItemId, Guid IterationId, DateOnly StartsOn, DateOnly EndsOn);

public sealed record IterationClosed(Guid ItemId, Guid IterationId);
