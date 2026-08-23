using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Portfolio.Domain;

public enum EpicStatus
{
    Idea = 0,
    Planned = 1,
    InProgress = 2,
    Done = 3,
    Deferred = 4,
}

public static class EpicStatuses
{
    public const string Idea = "idea";
    public const string Planned = "planned";
    public const string InProgress = "in-progress";
    public const string Done = "done";
    public const string Deferred = "deferred";

    public static readonly IReadOnlyList<string> All = [Idea, Planned, InProgress, Done, Deferred];

    public static EpicStatus Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Idea => EpicStatus.Idea,
        Planned => EpicStatus.Planned,
        InProgress => EpicStatus.InProgress,
        Done => EpicStatus.Done,
        Deferred => EpicStatus.Deferred,
        _ => throw new DomainRuleViolationException($"'{code}' is not an epic status."),
    };
}

/// <summary>
/// A feature of an item, optionally pinned to an iteration and to a target version.
/// </summary>
/// <remarks>
/// Epics are what makes "awaiting v2" mean something. An item in that state is live and has a next version queued;
/// the queue is the epics somebody deferred or planned against it. Without them the state would be a label with
/// nothing behind it, which is exactly the kind of status field that stops being maintained.
/// </remarks>
public sealed class Epic
{
    private Epic()
    {
    }

    public Guid Id { get; private init; }

    public Guid ItemId { get; private init; }

    public Guid? IterationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public EpicStatus Status { get; private set; }

    /// <summary>The version this belongs to — "v2". Free text; the organisation's own numbering.</summary>
    public string? TargetVersion { get; private set; }

    public int Sequence { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ModifiedAt { get; private set; }

    /// <summary>Counts towards a next version: not shipped, and not merely an idea nobody committed to.</summary>
    public bool IsQueued => Status is EpicStatus.Planned or EpicStatus.Deferred or EpicStatus.InProgress;

    public static Epic For(
        Guid itemId,
        string name,
        string? description,
        EpicStatus status,
        string? targetVersion,
        int sequence,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("An epic needs a name.");
        }

        return new Epic
        {
            Id = Guid.CreateVersion7(),
            ItemId = itemId,
            Name = name.Trim(),
            Description = description?.Trim(),
            Status = status,
            TargetVersion = targetVersion?.Trim(),
            Sequence = sequence,
            CreatedAt = now,
            ModifiedAt = now,
        };
    }

    public void Update(
        string? name,
        string? description,
        EpicStatus? status,
        string? targetVersion,
        Guid? iterationId,
        DateTimeOffset now)
    {
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DomainRuleViolationException("An epic needs a name.");
            }

            Name = name.Trim();
        }

        if (description is not null)
        {
            Description = description.Trim();
        }

        if (status is { } moved)
        {
            Status = moved;
        }

        if (targetVersion is not null)
        {
            TargetVersion = targetVersion.Trim() is { Length: > 0 } version ? version : null;
        }

        if (iterationId is not null)
        {
            IterationId = iterationId == Guid.Empty ? null : iterationId;
        }

        ModifiedAt = now;
    }
}
