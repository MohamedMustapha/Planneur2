namespace Cracra.Modules.Kudos.Domain;

/// <summary>
/// The window a wall, a widget or a leaderboard is drawn over.
/// </summary>
/// <remarks>
/// <para>
/// Two lengths only, because the spec asks for exactly two questions: "what happened this month", which is what
/// the team board shows, and "what happened this year", which is what somebody takes into a review. A free date
/// range would be a third thing to test and a third thing for a leaderboard to be gamed with — pick your best
/// fortnight and you top it.
/// </para>
/// <para>
/// Inclusive of both ends, and expressed in dates rather than instants: the caller says "August", not "from the
/// first at midnight in some timezone", and every query here compares against a day.
/// </para>
/// </remarks>
public readonly record struct KudoPeriod
{
    private KudoPeriod(string kind, DateOnly from, DateOnly to)
    {
        Kind = kind;
        From = from;
        To = to;
    }

    public const string MonthKind = "month";
    public const string YearKind = "year";

    public string Kind { get; }

    public DateOnly From { get; }

    public DateOnly To { get; }

    public static KudoPeriod Month(KudoMonth month) => new(MonthKind, month.First, month.Last);

    public static KudoPeriod Year(int year) => new(YearKind, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    /// <summary>
    /// Resolves what the query string asked for.
    /// </summary>
    /// <remarks>
    /// Anything unrecognised is the current month rather than a 400. These are read endpoints backing a widget,
    /// and a board that renders an error page because somebody hand-edited a query parameter is worse than one
    /// that shows this month.
    /// </remarks>
    public static KudoPeriod Resolve(string? kind, int? year, int? month, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        if (string.Equals(kind, YearKind, StringComparison.OrdinalIgnoreCase))
        {
            return Year(Sane(year) ?? today.Year);
        }

        return Month(month is >= 1 and <= 12
            ? new KudoMonth(Sane(year) ?? today.Year, month.Value)
            : KudoMonth.Of(today));
    }

    /// <summary>
    /// Rejects a year nobody meant.
    /// </summary>
    /// <remarks>
    /// Not for safety — a query for the year 12 returns nothing either way — but because the label the client
    /// renders comes back with the answer, and "Kudos, year 12" is a screen that looks broken.
    /// </remarks>
    private static int? Sane(int? year) => year is >= 2000 and <= 2999 ? year : null;

    public override string ToString() => $"{From:yyyy-MM-dd}..{To:yyyy-MM-dd}";
}
