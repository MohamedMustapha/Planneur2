using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Meetings.Contracts;

namespace Cracra.Modules.Meetings.Domain;

// =================================================================================================================
// Minutes — the compte-rendu (v2 §07.1).
//
// Meetings remains a 2-layer module and these are still tables with a few local rules on them, but the rules here
// earn their place: a CR that can be published twice, or that carries an action linked to nothing, is the dead
// document this slice exists to replace. The guards live on the entity rather than in the service so no second
// caller can invent a shortcut past them.
// =================================================================================================================

/// <summary>
/// One occurrence's minutes.
/// </summary>
/// <remarks>
/// <para>
/// The scope columns are copied from the occurrence when the CR is opened, exactly as the occurrence copies them
/// from its series and for the same reason: the RLS policy reads them on every candidate row, and an EXISTS back
/// to the occurrence would be a subquery per row on the query every dashboard runs.
/// </para>
/// <para>
/// <see cref="Published"/> is the hinge of the whole thing. A draft belongs to its author and to whoever may run
/// the meeting; publishing is what distributes it to the scope. Without that split people write minutes somewhere
/// else and paste them in when they are ready, which is how the CR stops being where the decisions live.
/// </para>
/// </remarks>
public sealed class MeetingMinutes
{
    private readonly List<MeetingDecision> _decisions = [];
    private readonly List<ActionItem> _actions = [];

    private MeetingMinutes()
    {
    }

    public Guid Id { get; private init; }

    public Guid OccurrenceId { get; private init; }

    public Guid SeriesId { get; private init; }

    /// <summary>unit / node / cross-node / service / project — <see cref="MeetingLevels"/>.</summary>
    public string Level { get; private set; } = MeetingLevels.Node;

    public string ScopeType { get; private set; } = MeetingScopeTypes.Unit;

    public Guid? ScopeId { get; private set; }

    /// <summary>
    /// The nodes a cross-node CR reaches, beyond its own scope.
    /// </summary>
    /// <remarks>
    /// Empty for every other level. A cross-node meeting is the one case where "who is this for" is genuinely a
    /// list — three bureaux meeting about a shared platform — and forcing it into a single scope id would either
    /// hide the CR from two of them or publish it to the whole service.
    /// </remarks>
    public Guid[] ScopeIds { get; private set; } = [];

    public DateTimeOffset OccurredAt { get; private init; }

    public Guid AuthorPersonId { get; private init; }

    public string? Agenda { get; private set; }

    public Guid[] Attendees { get; private set; } = [];

    public Guid[] Absentees { get; private set; } = [];

    public string? Summary { get; private set; }

    public bool Published { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public IReadOnlyCollection<MeetingDecision> Decisions => _decisions;

    public IReadOnlyCollection<ActionItem> Actions => _actions;

    public static MeetingMinutes Open(
        Guid occurrenceId,
        Guid seriesId,
        string level,
        string scopeType,
        Guid? scopeId,
        Guid[] scopeIds,
        DateTimeOffset occurredAt,
        Guid authorPersonId,
        DateTimeOffset now)
    {
        if (occurrenceId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Minutes belong to an occurrence.");
        }

        if (!MeetingLevels.All.Contains(level, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{level}' is not a meeting level.");
        }

        return new MeetingMinutes
        {
            Id = Guid.CreateVersion7(),
            OccurrenceId = occurrenceId,
            SeriesId = seriesId,
            Level = level,
            ScopeType = scopeType,
            ScopeId = scopeId,
            ScopeIds = MeetingLevels.IsMultiScope(level) ? scopeIds : [],
            OccurredAt = occurredAt,
            AuthorPersonId = authorPersonId,
            CreatedAt = now,
            ModifiedAt = now,
        };
    }

    public void Amend(
        string? agenda,
        IReadOnlyList<Guid>? attendees,
        IReadOnlyList<Guid>? absentees,
        string? summary,
        DateTimeOffset now)
    {
        if (agenda is not null)
        {
            Agenda = Blank(agenda);
        }

        if (attendees is not null)
        {
            Attendees = [.. attendees.Distinct()];
        }

        if (absentees is not null)
        {
            // Somebody in both lists was present; the attendance list wins, because that is the one somebody
            // ticked while looking at the room.
            Absentees = [.. absentees.Distinct().Where(person => !Attendees.Contains(person))];
        }

        if (summary is not null)
        {
            Summary = Blank(summary);
        }

        ModifiedAt = now;
    }

    /// <summary>
    /// Distributes the CR to its scope.
    /// </summary>
    /// <remarks>
    /// Refuses an empty one, and that is not pedantry: a published CR appears on other people's dashboards and in
    /// their reports, and one with neither a summary nor a decision nor an action is a notification that wastes
    /// everybody's attention on nothing. Publishing twice is refused rather than ignored — the second press means
    /// somebody expected something to happen.
    /// </remarks>
    public void Publish(DateTimeOffset now)
    {
        if (Published)
        {
            throw new DomainRuleViolationException("These minutes are already published.");
        }

        if (string.IsNullOrWhiteSpace(Summary) && _decisions.Count == 0 && _actions.Count == 0)
        {
            throw new DomainRuleViolationException(
                "There is nothing in these minutes to publish: write a summary, a decision or an action.");
        }

        Published = true;
        PublishedAt = now;
        ModifiedAt = now;
    }

    public MeetingDecision Decide(string text, string? rationale, string? decidedBy, DateTimeOffset now)
    {
        RefuseWhenPublished();

        var decision = MeetingDecision.For(Id, text, rationale, decidedBy, now);

        _decisions.Add(decision);
        ModifiedAt = now;

        return decision;
    }

    public ActionItem Assign(
        string title,
        Guid ownerPersonId,
        DateOnly? due,
        string linkType,
        Guid? linkId,
        DateTimeOffset now)
    {
        // Deliberately allowed after publication, unlike a decision. A decision is what the room agreed and
        // rewriting it later is falsifying a record; an action is work, and work gets added, reassigned and
        // closed long after the meeting ended.
        var action = ActionItem.For(Id, title, ownerPersonId, due, linkType, linkId, now);

        _actions.Add(action);
        ModifiedAt = now;

        return action;
    }

    public ActionItem Action(Guid actionId) =>
        _actions.SingleOrDefault(action => action.Id == actionId)
        ?? throw new ResourceNotFoundException($"No action {actionId} on these minutes.");

    private void RefuseWhenPublished()
    {
        if (Published)
        {
            throw new DomainRuleViolationException(
                "These minutes are published; a decision recorded afterwards is not what the room agreed.");
        }
    }

    internal static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class MeetingDecision
{
    private MeetingDecision()
    {
    }

    public Guid Id { get; private init; }

    public Guid MinutesId { get; private init; }

    public string Text { get; private set; } = string.Empty;

    public string? Rationale { get; private set; }

    /// <summary>Free text: "le COPIL", "Olivier". Not a person id — plenty of decisions are a room's.</summary>
    public string? DecidedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    internal static MeetingDecision For(
        Guid minutesId,
        string text,
        string? rationale,
        string? decidedBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainRuleViolationException("A decision has to say what was decided.");
        }

        return new MeetingDecision
        {
            Id = Guid.CreateVersion7(),
            MinutesId = minutesId,
            Text = text.Trim(),
            Rationale = MeetingMinutes.Blank(rationale),
            DecidedBy = MeetingMinutes.Blank(decidedBy),
            CreatedAt = now,
        };
    }
}

/// <summary>
/// Something somebody owes, optionally pointing at the thing it is about.
/// </summary>
/// <remarks>
/// The link is what keeps a CR connected to real work rather than a dead document (v2 §07.2). Closing the linked
/// problem, item or objective can close the action — which is the difference between an action tracker people
/// maintain and one they abandon after three weeks of duplicate bookkeeping.
/// </remarks>
public sealed class ActionItem
{
    private ActionItem()
    {
    }

    public Guid Id { get; private init; }

    public Guid MinutesId { get; private init; }

    public string Title { get; private set; } = string.Empty;

    public Guid OwnerPersonId { get; private set; }

    public DateOnly? Due { get; private set; }

    public string Status { get; private set; } = ActionStatuses.Open;

    public string LinkType { get; private set; } = ActionLinkTypes.None;

    public Guid? LinkId { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public bool IsOpen => Status == ActionStatuses.Open;

    public bool IsOverdueAt(DateOnly today) => IsOpen && Due is { } due && due < today;

    internal static ActionItem For(
        Guid minutesId,
        string title,
        Guid ownerPersonId,
        DateOnly? due,
        string linkType,
        Guid? linkId,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleViolationException("An action has to say what is to be done.");
        }

        if (ownerPersonId == Guid.Empty)
        {
            throw new DomainRuleViolationException("An action nobody owns is a wish.");
        }

        var kind = linkType.Trim().ToLowerInvariant();

        if (!ActionLinkTypes.All.Contains(kind, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{linkType}' is not something an action can point at.");
        }

        if (kind != ActionLinkTypes.None && (linkId ?? Guid.Empty) == Guid.Empty)
        {
            throw new DomainRuleViolationException($"An action linked to a {kind} needs the {kind}'s id.");
        }

        return new ActionItem
        {
            Id = Guid.CreateVersion7(),
            MinutesId = minutesId,
            Title = title.Trim(),
            OwnerPersonId = ownerPersonId,
            Due = due,
            Status = ActionStatuses.Open,
            LinkType = kind,
            LinkId = kind == ActionLinkTypes.None ? null : linkId,
            CreatedAt = now,
            ModifiedAt = now,
        };
    }

    public void Retitle(string? title, Guid? owner, DateOnly? due, DateTimeOffset now)
    {
        if (title is { } newTitle && !string.IsNullOrWhiteSpace(newTitle))
        {
            Title = newTitle.Trim();
        }

        if (owner is { } newOwner && newOwner != Guid.Empty)
        {
            OwnerPersonId = newOwner;
        }

        if (due is not null)
        {
            Due = due;
        }

        ModifiedAt = now;
    }

    public void Settle(string status, DateTimeOffset now)
    {
        var wanted = status.Trim().ToLowerInvariant();

        if (!ActionStatuses.All.Contains(wanted, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{status}' is not an action status.");
        }

        Status = wanted;
        ModifiedAt = now;
    }

    /// <summary>
    /// Closes the action because the thing it pointed at resolved.
    /// </summary>
    /// <remarks>
    /// Idempotent and silent about an action already settled: the caller is an event handler reacting to a
    /// problem being resolved, and a duplicate delivery must not raise. Returns whether anything changed so the
    /// handler can say so.
    /// </remarks>
    public bool CloseBecauseLinkResolved(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return false;
        }

        Status = ActionStatuses.Done;
        ModifiedAt = now;

        return true;
    }
}
