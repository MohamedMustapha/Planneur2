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

    /// <summary>
    /// Live, with a next version queued (v2 §03.1).
    /// </summary>
    /// <remarks>
    /// Ordered between Active and Dephase deliberately: it is a forward step from active, and reverting to active
    /// is what dropping the queued version means. It is not retirement — the thing is still in production and
    /// still costs money, which is exactly why v1's four states could not express it.
    /// </remarks>
    AwaitingVnext = 3,
    Dephase = 4,
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
    private readonly List<Epic> _epics = [];
    private readonly List<ItemMember> _members = [];
    private readonly List<ItemDependency> _dependencies = [];
    private readonly List<object> _domainEvents = [];

    private PortfolioItem()
    {
        // EF Core.
    }

    public Guid Id { get; private init; }

    /// <summary>Null while the item is only a candidate. Set on commitment, and never cleared afterwards.</summary>
    public Guid? ProjectId { get; private set; }

    /// <summary>Stable short code. What people say out loud and search by; never localized.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public ItemType Type { get; private set; }

    /// <summary>
    /// Free text, offered from the branch's profile rather than fixed here (v2 §03.1).
    /// </summary>
    /// <remarks>
    /// An enum would have to know that IT says "infra" and a bilateral service says "europe", which is precisely
    /// the deployment-specific vocabulary §01 keeps out of the code.
    /// </remarks>
    public string? Category { get; private set; }

    public ItemClassification Classification { get; private set; }

    /// <summary>The node that owns this. Replaces the sponsoring department, at whatever depth it sits.</summary>
    public Guid OwnerNodeId { get; private set; }

    /// <summary>
    /// The owning node's path, maintained by the node-tree trigger.
    /// </summary>
    /// <remarks>
    /// Mapped rather than merely present in the database so a caller can ask "every item under this node" with one
    /// array overlap instead of walking the tree itself. Written by <c>access.copy_node_path</c> on every insert
    /// and update — never by this aggregate, which is why it has no setter anybody can reach.
    /// </remarks>
    public Guid[] NodeAncestorIds { get; private set; } = [];

    public Guid? LeadPersonId { get; private set; }

    public Guid? PoPersonId { get; private set; }

    public decimal? EstimateAmount { get; private set; }

    public string Currency { get; private set; } = "EUR";

    /// <summary>The label of the queued version — "v2". Set with <see cref="PortfolioState.AwaitingVnext"/>.</summary>
    public string? AwaitingVersion { get; private set; }

    public string? Summary { get; private set; }

    /// <summary>Opt-out of the org-wide discovery projection (v2 §01 §3.1).</summary>
    public bool Confidential { get; private set; }

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

    public IReadOnlyCollection<Epic> Epics => _epics;

    public IReadOnlyCollection<ItemMember> Members => _members;

    public IReadOnlyCollection<ItemDependency> Dependencies => _dependencies;

    public IReadOnlyCollection<object> DomainEvents => _domainEvents;

    public Iteration? CurrentIteration =>
        _iterations.FirstOrDefault(iteration => iteration.State is IterationState.Active)
        ?? _iterations.Where(iteration => iteration.State is IterationState.Planned)
            .OrderBy(iteration => iteration.Sequence)
            .FirstOrDefault();

    /// <summary>Once archived, everything downstream is read-only — S6 freezes its boards off this.</summary>
    public bool IsArchived => State is PortfolioState.Dephase;

    public static PortfolioItem Consider(
        string code,
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

        // A candidate is an item from its first minute, and §03.1's code is unique across all of them. This
        // demanded nothing while candidates predated the catalog; once they shared the table it made every
        // proposal after the first collide on an empty string.
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainRuleViolationException("A candidate needs a code.");
        }

        if (departmentId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A candidate needs a sponsoring department.");
        }

        var item = new PortfolioItem
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
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

    // --- v2 §03: identity, epics, dependencies and team ---------------------------------------------------------

    /// <summary>
    /// Creates any kind of portfolio item.
    /// </summary>
    /// <remarks>
    /// Steps 1 and 2 of the wizard are enough: a type, a name and an owning node produce a real identity card
    /// rather than the nameless stub v1's "propose a candidate" left behind. Everything else is optional and
    /// fillable later, which is what makes the short path usable without making the long one a different entity.
    /// </remarks>
    public static PortfolioItem Create(
        string code,
        string name,
        ItemType type,
        string? category,
        ItemClassification classification,
        Guid ownerNodeId,
        Guid? leadPersonId,
        Guid? poPersonId,
        string? summary,
        decimal? estimateAmount,
        string? currency,
        int priority,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("An item needs a name.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainRuleViolationException("An item needs a code.");
        }

        if (ownerNodeId == Guid.Empty)
        {
            throw new DomainRuleViolationException("An item needs an owning node.");
        }

        if (estimateAmount is < 0)
        {
            throw new DomainRuleViolationException("An estimate cannot be negative.");
        }

        var item = new PortfolioItem
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Type = type,
            Category = Blank(category),
            Classification = classification,
            OwnerNodeId = ownerNodeId,
            DepartmentId = ownerNodeId,
            LeadPersonId = leadPersonId,
            PoPersonId = poPersonId,
            Summary = Blank(summary),
            EstimateAmount = estimateAmount,
            Currency = Blank(currency) ?? "EUR",
            State = PortfolioState.Considered,
            Priority = priority,
            ConsideredAt = now,
            CreatedBy = createdBy,
            ModifiedBy = createdBy,
            ModifiedAt = now,
        };

        item._domainEvents.Add(new ItemConsidered(item.Id, item.Name, ownerNodeId));

        return item;
    }

    public void UpdateIdentity(
        string? name,
        string? category,
        ItemClassification? classification,
        Guid? leadPersonId,
        Guid? poPersonId,
        string? summary,
        decimal? estimateAmount,
        bool? confidential,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        RefuseWhenArchived();

        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DomainRuleViolationException("An item needs a name.");
            }

            Name = name.Trim();
        }

        if (category is not null)
        {
            Category = Blank(category);
        }

        if (classification is { } axis)
        {
            Classification = axis;
        }

        if (leadPersonId is { } lead)
        {
            LeadPersonId = lead == Guid.Empty ? null : lead;
        }

        if (poPersonId is { } po)
        {
            PoPersonId = po == Guid.Empty ? null : po;
        }

        if (summary is not null)
        {
            Summary = Blank(summary);
        }

        if (estimateAmount is { } estimate)
        {
            if (estimate < 0)
            {
                throw new DomainRuleViolationException("An estimate cannot be negative.");
            }

            EstimateAmount = estimate;
        }

        if (confidential is { } hidden)
        {
            Confidential = hidden;
        }

        Touch(modifiedBy, now);
    }

    /// <summary>
    /// Declares a next version queued behind what is live.
    /// </summary>
    /// <remarks>
    /// Refused without at least one queued epic, and that is the whole point of the state. "Awaiting v2" with an
    /// empty backlog is a wish; with epics behind it, it is a plan somebody can read. Making the backlog the
    /// precondition means the pill on the card can never claim more than the list behind it.
    /// </remarks>
    public void AwaitNextVersion(string version, Guid decidedBy, DateTimeOffset now)
    {
        Require(PortfolioState.Active, PortfolioState.AwaitingVnext);

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new DomainRuleViolationException("Awaiting a next version needs the version label.");
        }

        if (!_epics.Any(epic => epic.IsQueued))
        {
            throw new DomainRuleViolationException(
                "Awaiting a next version needs at least one planned or deferred epic to put in it.");
        }

        State = PortfolioState.AwaitingVnext;
        AwaitingVersion = version.Trim();

        Touch(decidedBy, now);

        _domainEvents.Add(new ItemAwaitingNextVersion(Id, AwaitingVersion));
    }

    public Epic AddEpic(
        string name,
        string? description,
        EpicStatus status,
        string? targetVersion,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        RefuseWhenArchived();

        var epic = Epic.For(Id, name, description, status, targetVersion, _epics.Count + 1, now);

        _epics.Add(epic);

        Touch(modifiedBy, now);

        return epic;
    }

    public void UpdateEpic(
        Guid epicId,
        string? name,
        string? description,
        EpicStatus? status,
        string? targetVersion,
        Guid? iterationId,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        RefuseWhenArchived();

        var epic = _epics.SingleOrDefault(candidate => candidate.Id == epicId)
                   ?? throw new ResourceNotFoundException($"No epic {epicId} on this item.");

        if (iterationId is { } pinned && pinned != Guid.Empty && _iterations.All(it => it.Id != pinned))
        {
            throw new DomainRuleViolationException("That iteration is not on this item.");
        }

        epic.Update(name, description, status, targetVersion, iterationId, now);

        Touch(modifiedBy, now);
    }

    /// <summary>
    /// Records that this item leans on another.
    /// </summary>
    /// <param name="wouldCycle">
    /// Whether adding this edge closes a loop. Only the caller can answer it — the aggregate holds its own edges
    /// and a cycle is a property of the whole graph — so the answer is supplied rather than guessed at.
    /// </param>
    public ItemDependency DependOn(
        Guid dependsOnItemId,
        DependencyKind kind,
        string? note,
        bool wouldCycle,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        RefuseWhenArchived();

        if (wouldCycle)
        {
            throw new DomainRuleViolationException(
                "That dependency would close a loop: the other item already depends on this one.");
        }

        if (_dependencies.Any(edge => edge.DependsOnItemId == dependsOnItemId))
        {
            throw new DomainRuleViolationException("This item already depends on that one.");
        }

        var dependency = ItemDependency.Between(Id, dependsOnItemId, kind, note, now);

        _dependencies.Add(dependency);

        Touch(modifiedBy, now);

        return dependency;
    }

    public void RemoveDependency(Guid dependencyId, Guid modifiedBy, DateTimeOffset now)
    {
        var edge = _dependencies.SingleOrDefault(candidate => candidate.Id == dependencyId)
                   ?? throw new ResourceNotFoundException($"No dependency {dependencyId} on this item.");

        _dependencies.Remove(edge);

        Touch(modifiedBy, now);
    }

    public ItemMember AddMember(
        Guid personId,
        Guid nodeId,
        Guid? functionalRoleId,
        int? allocationPercent,
        DateOnly from,
        DateOnly? to,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        RefuseWhenArchived();

        if (_members.Any(member => member.PersonId == personId && member.To is null))
        {
            throw new DomainRuleViolationException("That person is already on this item.");
        }

        var joined = ItemMember.Join(Id, personId, nodeId, functionalRoleId, allocationPercent, from, to);

        _members.Add(joined);

        Touch(modifiedBy, now);

        return joined;
    }

    public void RemoveMember(Guid personId, DateOnly on, Guid modifiedBy, DateTimeOffset now)
    {
        var member = _members.SingleOrDefault(candidate => candidate.PersonId == personId && candidate.To is null)
                     ?? throw new ResourceNotFoundException("That person is not currently on this item.");

        member.Leave(on);

        Touch(modifiedBy, now);
    }

    private void RefuseWhenArchived()
    {
        if (IsArchived)
        {
            throw new DomainRuleViolationException("A déphasé item is read-only.");
        }
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

public sealed record ItemAwaitingNextVersion(Guid ItemId, string Version);

public sealed record ItemCommitted(Guid ItemId, Guid ProjectId, string DecisionNotes);

public sealed record ItemActivated(Guid ItemId, Guid? ProjectId);

public sealed record ItemArchived(Guid ItemId, Guid? ProjectId, string Reason);

public sealed record ItemReverted(Guid ItemId, PortfolioState Target, string Reason);

public sealed record IterationOpened(Guid ItemId, Guid IterationId, DateOnly StartsOn, DateOnly EndsOn);

public sealed record IterationClosed(Guid ItemId, Guid IterationId);
