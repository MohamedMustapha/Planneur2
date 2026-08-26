using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Problems.Domain;

/// <summary>
/// What kind of pain this is (v2 §05.1).
/// </summary>
/// <remarks>
/// The category is what decides who gets to pick the problem up: a node whose profile says it solves
/// <c>tooling</c> reads tooling problems from anywhere in the org, which is how other branches "fill in needs for
/// IT" without anybody building their own tool on the side.
/// </remarks>
public static class ProblemCategories
{
    public const string WorkProcess = "work-process";
    public const string Project = "project";
    public const string QualityOfLife = "quality-of-life";
    public const string Tooling = "tooling";
    public const string Data = "data";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All =
        [WorkProcess, Project, QualityOfLife, Tooling, Data, Other];
}

/// <summary>Where it hurts.</summary>
public static class OriginScopes
{
    public const string Person = "person";
    public const string Unit = "unit";
    public const string Node = "node";
    public const string Item = "item";

    public static readonly IReadOnlyList<string> All = [Person, Unit, Node, Item];
}

public enum ProblemStatus
{
    New = 0,
    Triaged = 1,
    Accepted = 2,
    Converted = 3,
    Resolved = 4,
    Declined = 5,
    Duplicate = 6,
}

public static class ProblemStatuses
{
    public const string New = "new";
    public const string Triaged = "triaged";
    public const string Accepted = "accepted";
    public const string Converted = "converted";
    public const string Resolved = "resolved";
    public const string Declined = "declined";
    public const string Duplicate = "duplicate";

    public static readonly IReadOnlyList<string> All =
        [New, Triaged, Accepted, Converted, Resolved, Declined, Duplicate];

    public static string Wire(ProblemStatus status) => status.ToString().ToLowerInvariant();
}

public enum ImpactFrequency
{
    Occasional = 0,
    Monthly = 1,
    Weekly = 2,
    Daily = 3,
}

public static class ImpactFrequencies
{
    public const string Occasional = "occasional";
    public const string Monthly = "monthly";
    public const string Weekly = "weekly";
    public const string Daily = "daily";

    public static readonly IReadOnlyList<string> All = [Occasional, Monthly, Weekly, Daily];

    public static ImpactFrequency Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Occasional => ImpactFrequency.Occasional,
        Monthly => ImpactFrequency.Monthly,
        Weekly => ImpactFrequency.Weekly,
        Daily => ImpactFrequency.Daily,
        _ => throw new DomainRuleViolationException($"'{code}' is not a frequency."),
    };

    /// <summary>How many times a year the pain recurs. The multiplier behind the ranking.</summary>
    public static decimal PerYear(ImpactFrequency frequency) => frequency switch
    {
        ImpactFrequency.Daily => 220m,
        ImpactFrequency.Weekly => 46m,
        ImpactFrequency.Monthly => 11m,
        _ => 2m,
    };
}

/// <summary>
/// An irritant somebody filed.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate owns the lifecycle, and the lifecycle is the whole point: a problem that can be converted after
/// being declined, or voted on twice by the same person, is a backlog nobody trusts. Every transition is guarded
/// here rather than in a handler, so no endpoint can invent a shortcut.
/// </para>
/// <para>
/// Proposals, votes and comments hang off it because they are meaningless without it and because the guards read
/// them: a duplicate has to name what it duplicates, and a conversion has to leave a link behind.
/// </para>
/// </remarks>
public sealed class Problem
{
    private readonly List<Proposal> _proposals = [];
    private readonly List<ProblemVote> _votes = [];
    private readonly List<ProblemComment> _comments = [];

    private Problem()
    {
    }

    public Guid Id { get; private init; }

    public string Code { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string Category { get; private set; } = ProblemCategories.Other;

    public string OriginScopeType { get; private set; } = OriginScopes.Person;

    public Guid OriginScopeId { get; private set; }

    /// <summary>The node the problem is scoped to for visibility. Always set, whatever the origin scope is.</summary>
    public Guid NodeId { get; private set; }

    public Guid ReporterPersonId { get; private init; }

    /// <summary>Hours lost each time it bites. Optional — plenty of pains are real and unquantified.</summary>
    public decimal? ImpactTimeLoss { get; private set; }

    public ImpactFrequency ImpactFrequency { get; private set; }

    public int? AffectedPeopleEstimate { get; private set; }

    public ProblemStatus Status { get; private set; }

    public Guid? ConvertedItemId { get; private set; }

    public Guid? DuplicateOfProblemId { get; private set; }

    public string? DecisionReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public IReadOnlyCollection<Proposal> Proposals => _proposals;

    public IReadOnlyCollection<ProblemVote> Votes => _votes;

    public IReadOnlyCollection<ProblemComment> Comments => _comments;

    /// <summary>
    /// Hours a year this costs the organisation, as far as anybody has said.
    /// </summary>
    /// <remarks>
    /// Time lost, times how often, times how many people it hits. Deliberately not blended with votes: a pain
    /// twenty people feel weekly and a pain one person feels daily are different problems, and a single score
    /// that hid which was which would rank them by whichever the formula happened to favour. Votes are their own
    /// column, and the board sorts by either.
    /// </remarks>
    public decimal AnnualHoursLost =>
        (ImpactTimeLoss ?? 0m)
        * ImpactFrequencies.PerYear(ImpactFrequency)
        * (AffectedPeopleEstimate ?? 1);

    public int VoteCount => _votes.Count;

    public static Problem File(
        string code,
        string title,
        string? description,
        string category,
        string originScopeType,
        Guid originScopeId,
        Guid nodeId,
        Guid reporterPersonId,
        decimal? impactTimeLoss,
        ImpactFrequency frequency,
        int? affectedPeopleEstimate,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleViolationException("A problem needs a title somebody else would recognise.");
        }

        if (!ProblemCategories.All.Contains(category, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{category}' is not a problem category.");
        }

        if (!OriginScopes.All.Contains(originScopeType, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{originScopeType}' is not an origin scope.");
        }

        if (nodeId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A problem needs the node it belongs to.");
        }

        if (impactTimeLoss is < 0)
        {
            throw new DomainRuleViolationException("Time lost cannot be negative.");
        }

        if (affectedPeopleEstimate is < 0)
        {
            throw new DomainRuleViolationException("An estimate of affected people cannot be negative.");
        }

        return new Problem
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
            Title = title.Trim(),
            Description = Blank(description),
            Category = category,
            OriginScopeType = originScopeType,
            OriginScopeId = originScopeId,
            NodeId = nodeId,
            ReporterPersonId = reporterPersonId,
            ImpactTimeLoss = impactTimeLoss,
            ImpactFrequency = frequency,
            AffectedPeopleEstimate = affectedPeopleEstimate,
            Status = ProblemStatus.New,
            CreatedAt = now,
            ModifiedAt = now,
        };
    }

    /// <summary>
    /// A head or the PMO looks at it and decides what happens next.
    /// </summary>
    /// <remarks>
    /// One method for all four outcomes because they are one act — somebody read it and said what it is — and
    /// splitting them would let an endpoint accept a decline with no reason or a duplicate with nothing to point
    /// at. Whether the caller may triage is the endpoint's and RLS's question; whether the decision is coherent is
    /// this one's.
    /// </remarks>
    public void Triage(string decision, string? reason, Guid? duplicateOf, DateTimeOffset now)
    {
        if (Status is not (ProblemStatus.New or ProblemStatus.Triaged))
        {
            throw new DomainRuleViolationException(
                $"This problem is already {ProblemStatuses.Wire(Status)}; triage happens once.");
        }

        switch (decision?.Trim().ToLowerInvariant())
        {
            case ProblemStatuses.Accepted:
                Status = ProblemStatus.Accepted;
                DecisionReason = Blank(reason);
                break;

            case ProblemStatuses.Declined:
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new DomainRuleViolationException(
                        "Declining needs a reason: somebody took the trouble to file this.");
                }

                Status = ProblemStatus.Declined;
                DecisionReason = reason.Trim();
                break;

            case ProblemStatuses.Duplicate:
                if (duplicateOf is not { } original || original == Guid.Empty)
                {
                    throw new DomainRuleViolationException("Marking a duplicate needs the problem it duplicates.");
                }

                if (original == Id)
                {
                    throw new DomainRuleViolationException("A problem cannot duplicate itself.");
                }

                Status = ProblemStatus.Duplicate;
                DuplicateOfProblemId = original;
                DecisionReason = Blank(reason);
                break;

            case ProblemStatuses.Triaged:
                Status = ProblemStatus.Triaged;
                DecisionReason = Blank(reason);
                break;

            default:
                throw new DomainRuleViolationException(
                    $"'{decision}' is not a triage decision. Use triaged, accepted, declined or duplicate.");
        }

        ModifiedAt = now;
    }

    /// <summary>
    /// Turns an accepted problem into work somebody owns.
    /// </summary>
    /// <remarks>
    /// Only from accepted, and that is the no-shadow-IT rule in one line: converting is how a pain becomes a
    /// project run inside the department that will carry it, and letting a declined or duplicate problem be
    /// converted would route around the decision somebody already took.
    /// </remarks>
    public void Convert(Guid itemId, DateTimeOffset now)
    {
        if (Status is not ProblemStatus.Accepted)
        {
            throw new DomainRuleViolationException(
                $"Only an accepted problem becomes a portfolio item; this one is {ProblemStatuses.Wire(Status)}.");
        }

        if (itemId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Converting needs the item that will carry the work.");
        }

        Status = ProblemStatus.Converted;
        ConvertedItemId = itemId;
        ModifiedAt = now;
    }

    public void Resolve(string? note, DateTimeOffset now)
    {
        if (Status is not (ProblemStatus.Accepted or ProblemStatus.Converted))
        {
            throw new DomainRuleViolationException(
                $"A {ProblemStatuses.Wire(Status)} problem has nothing to resolve.");
        }

        Status = ProblemStatus.Resolved;
        DecisionReason = Blank(note) ?? DecisionReason;
        ModifiedAt = now;
    }

    /// <summary>
    /// Somebody suggests a fix.
    /// </summary>
    /// <remarks>
    /// Deliberately does not touch <see cref="ModifiedAt"/>, and that is not tidiness. Anybody in scope may
    /// propose, vote and comment, but only the reporter or a head may write the problem row — so stamping it here
    /// would make every proposal an update the proposer is not allowed to make, and the whole discussion would be
    /// refused by RLS. The problem did not change; the conversation around it did.
    /// </remarks>
    public Proposal Propose(Guid authorPersonId, string description, decimal? effortGuess, DateTimeOffset now)
    {
        RefuseWhenClosed("propose a fix for");

        var proposal = Proposal.For(Id, authorPersonId, description, effortGuess, now);

        _proposals.Add(proposal);

        return proposal;
    }

    /// <summary>
    /// One vote per person.
    /// </summary>
    /// <remarks>
    /// Idempotent rather than an error: the button is a "me too", and somebody pressing it twice means they still
    /// mean it. Returning whether it counted lets the caller answer honestly without the second press failing.
    /// </remarks>
    public bool Vote(Guid personId, DateTimeOffset now)
    {
        RefuseWhenClosed("vote on");

        if (_votes.Any(vote => vote.PersonId == personId))
        {
            return false;
        }

        _votes.Add(ProblemVote.By(Id, personId, now));

        return true;
    }

    public ProblemComment Comment(Guid authorPersonId, string body, DateTimeOffset now)
    {
        RefuseWhenClosed("comment on");

        var comment = ProblemComment.For(Id, authorPersonId, body, now);

        _comments.Add(comment);

        return comment;
    }

    private void RefuseWhenClosed(string what)
    {
        if (Status is ProblemStatus.Declined or ProblemStatus.Duplicate or ProblemStatus.Resolved)
        {
            throw new DomainRuleViolationException(
                $"You cannot {what} a problem that is {ProblemStatuses.Wire(Status)}.");
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class Proposal
{
    private Proposal()
    {
    }

    public Guid Id { get; private init; }

    public Guid ProblemId { get; private init; }

    public Guid AuthorPersonId { get; private init; }

    public string Description { get; private set; } = string.Empty;

    /// <summary>Days, roughly. A guess offered by whoever suggested the fix, not an estimate anybody committed to.</summary>
    public decimal? EffortGuess { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public static Proposal For(
        Guid problemId,
        Guid authorPersonId,
        string description,
        decimal? effortGuess,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainRuleViolationException("A proposal needs to say what to do.");
        }

        if (effortGuess is < 0)
        {
            throw new DomainRuleViolationException("An effort guess cannot be negative.");
        }

        return new Proposal
        {
            Id = Guid.CreateVersion7(),
            ProblemId = problemId,
            AuthorPersonId = authorPersonId,
            Description = description.Trim(),
            EffortGuess = effortGuess,
            CreatedAt = now,
        };
    }
}

public sealed class ProblemVote
{
    private ProblemVote()
    {
    }

    public Guid ProblemId { get; private init; }

    public Guid PersonId { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public static ProblemVote By(Guid problemId, Guid personId, DateTimeOffset now) => new()
    {
        ProblemId = problemId,
        PersonId = personId,
        CreatedAt = now,
    };
}

public sealed class ProblemComment
{
    private ProblemComment()
    {
    }

    public Guid Id { get; private init; }

    public Guid ProblemId { get; private init; }

    public Guid AuthorPersonId { get; private init; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private init; }

    public static ProblemComment For(Guid problemId, Guid authorPersonId, string body, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new DomainRuleViolationException("An empty comment says nothing.");
        }

        return new ProblemComment
        {
            Id = Guid.CreateVersion7(),
            ProblemId = problemId,
            AuthorPersonId = authorPersonId,
            Body = body.Trim(),
            CreatedAt = now,
        };
    }
}
