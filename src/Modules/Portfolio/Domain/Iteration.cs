using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Portfolio.Domain;

/// <summary>
/// The quick selectors from the design's iteration strip.
/// </summary>
/// <remarks>
/// A preset only pre-fills the end date; the dates themselves are always free. The glossary calls an iteration a
/// "free-length phase with quick selectors", and the presets exist because most iterations are one of three
/// lengths — not because those are the only lengths allowed.
/// </remarks>
public enum IterationLength
{
    OneWeek = 0,
    TwoWeeks = 1,
    OneMonth = 2,
    Custom = 3,
}

public enum IterationState
{
    Planned = 0,
    Active = 1,
    Done = 2,
    Cancelled = 3,
}

/// <summary>
/// One iteration of a portfolio item.
/// </summary>
/// <remarks>
/// Part of the PortfolioItem aggregate, not an aggregate of its own: an iteration only means anything relative to
/// its item's lifecycle, and archiving the item has to cancel its open iterations atomically.
/// </remarks>
public sealed class Iteration
{
    private Iteration()
    {
        // EF Core.
    }

    public Guid Id { get; private init; }

    public Guid PortfolioItemId { get; private init; }

    /// <summary>1-based, in the order they were opened. What the strip labels each block with.</summary>
    public int Sequence { get; private init; }

    public string Name { get; private set; } = string.Empty;

    public IterationLength Length { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly EndsOn { get; private set; }

    public IterationState State { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public bool IsOpen => State is IterationState.Planned or IterationState.Active;

    internal static Iteration Create(
        Guid portfolioItemId,
        int sequence,
        string name,
        IterationLength length,
        DateOnly startsOn,
        DateOnly? endsOn,
        DateTimeOffset now)
    {
        var end = endsOn ?? EndDateFor(length, startsOn);

        if (end < startsOn)
        {
            throw new DomainRuleViolationException("An iteration cannot end before it starts.");
        }

        if (length is IterationLength.Custom && endsOn is null)
        {
            throw new DomainRuleViolationException("A custom-length iteration needs an explicit end date.");
        }

        return new Iteration
        {
            Id = Guid.CreateVersion7(),
            PortfolioItemId = portfolioItemId,
            Sequence = sequence,
            Name = string.IsNullOrWhiteSpace(name) ? $"Iteration {sequence}" : name.Trim(),
            Length = length,
            StartsOn = startsOn,
            EndsOn = end,
            State = IterationState.Planned,
            CreatedAt = now,
            ModifiedAt = now,
        };
    }

    /// <summary>
    /// The end date a preset implies, inclusive of both endpoints.
    /// </summary>
    /// <remarks>
    /// A one-week iteration starting Monday ends the following Sunday — seven days, not eight. Getting this off by
    /// one would misalign every iteration boundary drawn on the S6 timelines, where the shaded ranges have to abut
    /// rather than overlap.
    /// </remarks>
    public static DateOnly EndDateFor(IterationLength length, DateOnly startsOn) => length switch
    {
        IterationLength.OneWeek => startsOn.AddDays(6),
        IterationLength.TwoWeeks => startsOn.AddDays(13),
        // Calendar month, so a 31 January start ends 28 February — AddMonths already clamps, and the -1 keeps the
        // period inclusive like the others.
        IterationLength.OneMonth => startsOn.AddMonths(1).AddDays(-1),
        _ => startsOn,
    };

    internal void Reschedule(string name, IterationLength length, DateOnly startsOn, DateOnly? endsOn, DateTimeOffset now)
    {
        if (State is IterationState.Done or IterationState.Cancelled)
        {
            throw new DomainRuleViolationException("A closed iteration cannot be rescheduled.");
        }

        var end = endsOn ?? EndDateFor(length, startsOn);

        if (end < startsOn)
        {
            throw new DomainRuleViolationException("An iteration cannot end before it starts.");
        }

        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim();
        Length = length;
        StartsOn = startsOn;
        EndsOn = end;
        ModifiedAt = now;
    }

    internal void Start(DateTimeOffset now)
    {
        if (State is not IterationState.Planned)
        {
            throw new DomainRuleViolationException($"Only a planned iteration can be started; this one is {State}.");
        }

        State = IterationState.Active;
        ModifiedAt = now;
    }

    internal void Close(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new DomainRuleViolationException($"This iteration is already {State}.");
        }

        State = IterationState.Done;
        ModifiedAt = now;
    }

    /// <summary>Used by the archive cascade. Silent when already closed, because archiving is not a per-iteration act.</summary>
    internal void CancelIfOpen(DateTimeOffset now)
    {
        if (IsOpen)
        {
            State = IterationState.Cancelled;
            ModifiedAt = now;
        }
    }
}
