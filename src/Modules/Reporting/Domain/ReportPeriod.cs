using System.Globalization;
using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Reporting.Contracts;

namespace Cracra.Modules.Reporting.Domain;

/// <summary>
/// The window a report covers.
/// </summary>
/// <remarks>
/// <para>
/// Weeks are ISO weeks, as everywhere else in the platform: French and Spanish business calendars run on them,
/// the pager says "Sem. 34", and a January Monday landing in week 53 of the previous year is what ISO says rather
/// than a bug. Activities has its own <c>IsoWeek</c> and this deliberately does not reuse it — that type lives in
/// another module's Domain, which nothing here may reference. Ten lines of arithmetic duplicated is the price of
/// the isolation rule, and it is the right price: the alternative is a shared kernel that grows every slice.
/// </para>
/// <para>
/// Pure and static, so period arithmetic is unit-testable without a database.
/// </para>
/// </remarks>
public readonly record struct ReportPeriod
{
    /// <summary>Nobody reports on more than a year at a time, and a custom range asking to is a typo.</summary>
    public const int MaximumDays = 366;

    private ReportPeriod(string kind, DateOnly from, DateOnly to, int? isoYear, int? isoWeek)
    {
        Kind = kind;
        From = from;
        To = to;
        IsoYear = isoYear;
        IsoWeek = isoWeek;
    }

    public string Kind { get; }

    public DateOnly From { get; }

    /// <summary>Inclusive. Every consumer of this treats the last day as part of the period.</summary>
    public DateOnly To { get; }

    public int? IsoYear { get; }

    public int? IsoWeek { get; }

    public int Days => To.DayNumber - From.DayNumber + 1;

    /// <summary>The ISO week containing a day.</summary>
    public static ReportPeriod Week(DateOnly within)
    {
        var date = within.ToDateTime(TimeOnly.MinValue);
        var year = ISOWeek.GetYear(date);
        var week = ISOWeek.GetWeekOfYear(date);
        var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday));

        return new ReportPeriod(ReportPeriods.Week, monday, monday.AddDays(6), year, week);
    }

    /// <summary>The calendar month containing a day.</summary>
    public static ReportPeriod Month(DateOnly within)
    {
        var first = new DateOnly(within.Year, within.Month, 1);

        return new ReportPeriod(
            ReportPeriods.Month,
            first,
            first.AddMonths(1).AddDays(-1),
            isoYear: null,
            isoWeek: null);
    }

    public static ReportPeriod Custom(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new DomainRuleViolationException("The end of the period cannot be before its start.");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaximumDays)
        {
            throw new DomainRuleViolationException($"A report covers at most {MaximumDays} days.");
        }

        return new ReportPeriod(ReportPeriods.Custom, from, to, isoYear: null, isoWeek: null);
    }

    /// <summary>
    /// Resolves what the caller asked for, defaulting to the current week.
    /// </summary>
    /// <remarks>
    /// The week rather than the month, because the weekly report is the one a lead runs on a Friday and the
    /// monthly one is a deliberate act. A default nobody chose should be the one people want most often.
    /// </remarks>
    public static ReportPeriod Resolve(string? kind, DateOnly? from, DateOnly? to, DateOnly today)
    {
        var requested = (kind ?? ReportPeriods.Week).Trim().ToLowerInvariant();

        return requested switch
        {
            ReportPeriods.Week => Week(from ?? today),
            ReportPeriods.Month => Month(from ?? today),
            ReportPeriods.Custom => Custom(
                from ?? throw new DomainRuleViolationException("A custom period needs a start date."),
                to ?? throw new DomainRuleViolationException("A custom period needs an end date.")),
            _ => throw new DomainRuleViolationException(
                $"'{kind}' is not a period. Use one of: {string.Join(", ", ReportPeriods.All)}."),
        };
    }

    /// <summary>
    /// The keyed label, with the numbers the client will interpolate.
    /// </summary>
    /// <remarks>
    /// A key rather than a rendered string: the server has no idea whether this browser reads "Semaine 34" or
    /// "Week 34" (conventions.md §5), and a month name it formatted itself would be in the server's culture.
    /// </remarks>
    public string LabelKey => Kind switch
    {
        ReportPeriods.Week => "reports.period.week",
        ReportPeriods.Month => "reports.period.month",
        _ => "reports.period.custom",
    };

    public ReportPeriodView ToView() => new(Kind, From, To, IsoYear, IsoWeek, LabelKey);

    /// <summary>
    /// A stable token for the cache key and the report id.
    /// </summary>
    /// <remarks>
    /// Built from the resolved dates rather than from what was requested, so "week" asked on a Tuesday and the
    /// same week asked on the Thursday produce one cache entry rather than two identical ones.
    /// </remarks>
    public override string ToString() => $"{Kind}:{From:yyyy-MM-dd}:{To:yyyy-MM-dd}";
}
