using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Kudos.Domain;

/// <summary>
/// A calendar month, which is the unit the giving cap is expressed in.
/// </summary>
/// <remarks>
/// <para>
/// Stored on the row as year plus month rather than derived on read, for the same reason S5 stores the ISO week:
/// the cap is checked on every single write, and a derived value would mean either a function-based index or a
/// scan of the giver's whole history each time.
/// </para>
/// <para>
/// Calendar months rather than ISO weeks here — the cap is monthly, the board widget is monthly and the review
/// claim is annual, and none of those three line up with a week. UTC, so that a kudo given at half past midnight
/// on the first cannot belong to two different months depending on who is looking.
/// </para>
/// </remarks>
public readonly record struct KudoMonth : IComparable<KudoMonth>
{
    public KudoMonth(int year, int month)
    {
        if (month is < 1 or > 12)
        {
            throw new DomainRuleViolationException($"{month} is not a month.");
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    public static KudoMonth Of(DateTimeOffset moment) => new(moment.UtcDateTime.Year, moment.UtcDateTime.Month);

    public static KudoMonth Of(DateOnly day) => new(day.Year, day.Month);

    public DateOnly First => new(Year, Month, 1);

    public DateOnly Last => First.AddMonths(1).AddDays(-1);

    public int CompareTo(KudoMonth other) =>
        Year != other.Year ? Year.CompareTo(other.Year) : Month.CompareTo(other.Month);

    public override string ToString() => $"{Year}-{Month:00}";
}
