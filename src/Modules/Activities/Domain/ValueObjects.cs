using Cracra.BuildingBlocks.Abstractions;
using System.Globalization;

namespace Cracra.Modules.Activities.Domain;

/// <summary>
/// Planned time, or time actually spent.
/// </summary>
/// <remarks>
/// One aggregate carries both rather than two tables, because the interesting question is the gap between them and
/// a gap is easier to see when the two halves sit on the same row. A <c>planned</c> entry is a slot someone opened;
/// an <c>actual</c> is what happened, and it may legitimately differ.
/// </remarks>
public enum ActivityKind
{
    Planned = 0,
    Actual = 1,
}

/// <summary>
/// Where the entry came from.
/// </summary>
/// <remarks>
/// Recorded so a report can say "this hour was typed in" versus "this hour came from a sprint task". The two
/// external sources are read-only in both directions the platform cares about: S10 pulls from them and nothing
/// ever writes back.
/// </remarks>
public enum ActivitySource
{
    Manual = 0,
    AzureDevOps = 1,
    ServiceNow = 2,
}

/// <summary>
/// A span of working time.
/// </summary>
/// <remarks>
/// Stored as instants rather than as a date plus a duration: the boards draw slots against a clock, and a slot
/// that knows only "Tuesday, 3 hours" cannot be drawn without inventing where on Tuesday it sits.
/// </remarks>
public readonly record struct TimeSlot
{
    public TimeSlot(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new DomainRuleViolationException("An activity slot must end after it starts.");
        }

        if ((end - start).TotalHours > MaximumHours)
        {
            throw new DomainRuleViolationException(
                $"A single slot cannot exceed {MaximumHours} hours. Split it across days.");
        }

        Start = start;
        End = end;
    }

    /// <summary>
    /// The longest a single slot may be.
    /// </summary>
    /// <remarks>
    /// A slot spanning more than a day is almost always a date-picker mistake, and the weekly guardrail would
    /// otherwise be tripped by one bad entry rather than by a real pattern of overtime.
    /// </remarks>
    public const double MaximumHours = 24d;

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    public decimal Hours => Math.Round((decimal)(End - Start).TotalHours, 2, MidpointRounding.AwayFromZero);

    /// <summary>True when the two spans share any time at all. Touching endpoints do not count as overlapping.</summary>
    public bool Overlaps(TimeSlot other) => Start < other.End && other.Start < End;

    public override string ToString() =>
        $"{Start:yyyy-MM-dd HH:mm}–{End:HH:mm}";
}

/// <summary>
/// Hours worked, to the quarter.
/// </summary>
/// <remarks>
/// Quantised to quarter-hours because that is the finest granularity anyone genuinely reports, and because free
/// decimals turn a weekly total of 35 into 34.999999 often enough to matter when a guardrail compares against it.
/// </remarks>
public readonly record struct WorkHours
{
    public const decimal Increment = 0.25m;

    public static readonly WorkHours Zero = new(0m);

    public WorkHours(decimal hours)
    {
        if (hours < 0)
        {
            throw new DomainRuleViolationException("Hours cannot be negative.");
        }

        var quantised = Math.Round(hours / Increment, MidpointRounding.AwayFromZero) * Increment;

        if (quantised > (decimal)TimeSlot.MaximumHours)
        {
            throw new DomainRuleViolationException(
                $"A single entry cannot record more than {TimeSlot.MaximumHours} hours.");
        }

        Value = quantised;
    }

    public decimal Value { get; }

    public static WorkHours operator +(WorkHours left, WorkHours right) => new(left.Value + right.Value);

    public override string ToString() => Value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>
/// An ISO week, which is the unit the 35h guardrail is expressed in.
/// </summary>
/// <remarks>
/// <para>
/// Stored on the row as year plus week rather than derived on read. The guardrail sums a person's week on every
/// write, and a derived value would mean either a function-based index or a sequential scan of their whole history
/// each time.
/// </para>
/// <para>
/// ISO, not "the week containing 1 January": French and Spanish business calendars run on ISO weeks, the design
/// labels its pager "Sem. 34", and a January entry landing in week 53 of the previous year is correct rather than
/// a bug — that is precisely what ISO says.
/// </para>
/// </remarks>
public readonly record struct IsoWeek : IComparable<IsoWeek>
{
    public IsoWeek(int year, int week)
    {
        if (week is < 1 or > 53)
        {
            throw new DomainRuleViolationException($"{week} is not an ISO week number.");
        }

        Year = year;
        Week = week;
    }

    public int Year { get; }

    public int Week { get; }

    public static IsoWeek Of(DateTimeOffset moment) => Of(DateOnly.FromDateTime(moment.UtcDateTime));

    public static IsoWeek Of(DateOnly day)
    {
        var date = day.ToDateTime(TimeOnly.MinValue);

        // ISOWeek handles the year boundary properly: 1 January 2027 is week 53 of 2026, and treating it as week 1
        // of 2027 would put two Mondays' work in different weeks for guardrail purposes.
        return new IsoWeek(System.Globalization.ISOWeek.GetYear(date), System.Globalization.ISOWeek.GetWeekOfYear(date));
    }

    public DateOnly Monday => DateOnly.FromDateTime(System.Globalization.ISOWeek.ToDateTime(Year, Week, DayOfWeek.Monday));

    public DateOnly Sunday => Monday.AddDays(6);

    public int CompareTo(IsoWeek other) =>
        Year != other.Year ? Year.CompareTo(other.Year) : Week.CompareTo(other.Week);

    public override string ToString() => $"{Year}-W{Week:00}";
}
