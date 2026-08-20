using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Finance.Domain;

/// <summary>
/// The window a capitalization view covers.
/// </summary>
/// <remarks>
/// <para>
/// Months, quarters and years — not weeks. That is the one place this differs from S8's report period, and the
/// difference is the audience: a capitalization split is read against a reporting calendar, and nobody
/// capitalizes a week.
/// </para>
/// <para>
/// The arithmetic is duplicated from S8 rather than shared, for the reason S8 itself records: a type in another
/// module's Domain is not something this may reference, and a shared kernel that grows every slice is a worse
/// outcome than forty lines of date maths in two places.
/// </para>
/// <para>
/// Pure and static, so every boundary case is testable without a database.
/// </para>
/// </remarks>
public readonly record struct FinancePeriod
{
    /// <summary>A capitalization view spanning more than two years is a request nobody meant to make.</summary>
    public const int MaximumDays = 731;

    private FinancePeriod(string kind, DateOnly from, DateOnly to)
    {
        Kind = kind;
        From = from;
        To = to;
    }

    public string Kind { get; }

    public DateOnly From { get; }

    /// <summary>Inclusive. Every consumer treats the last day as part of the period.</summary>
    public DateOnly To { get; }

    public int Days => To.DayNumber - From.DayNumber + 1;

    public static FinancePeriod Month(DateOnly within)
    {
        var first = new DateOnly(within.Year, within.Month, 1);

        return new FinancePeriod(FinancePeriods.Month, first, first.AddMonths(1).AddDays(-1));
    }

    public static FinancePeriod Quarter(DateOnly within)
    {
        // The quarter's first month: January, April, July, October.
        var first = new DateOnly(within.Year, (((within.Month - 1) / 3) * 3) + 1, 1);

        return new FinancePeriod(FinancePeriods.Quarter, first, first.AddMonths(3).AddDays(-1));
    }

    public static FinancePeriod Year(DateOnly within)
    {
        var first = new DateOnly(within.Year, 1, 1);

        return new FinancePeriod(FinancePeriods.Year, first, first.AddYears(1).AddDays(-1));
    }

    public static FinancePeriod Custom(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new DomainRuleViolationException("The end of the period cannot be before its start.");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaximumDays)
        {
            throw new DomainRuleViolationException(
                $"A capex/opex view covers at most {MaximumDays} days.");
        }

        return new FinancePeriod(FinancePeriods.Custom, from, to);
    }

    /// <summary>
    /// Resolves what the query string asked for.
    /// </summary>
    /// <remarks>
    /// An unrecognized kind falls back to the current month rather than being refused. The period is a lens on a
    /// read-only view, not an instruction — somebody who typed <c>period=fortnight</c> is better served by this
    /// month's figures with the kind stated on them than by a 422 they have to decode.
    /// </remarks>
    public static FinancePeriod Resolve(string? kind, DateOnly? from, DateOnly? to, DateOnly today) =>
        (kind ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            FinancePeriods.Custom when from is { } start && to is { } end => Custom(start, end),
            FinancePeriods.Quarter => Quarter(from ?? today),
            FinancePeriods.Year => Year(from ?? today),
            _ => Month(from ?? today),
        };

    /// <summary>The months this period spans, in order. What the "by period" breakdown is drawn from.</summary>
    /// <remarks>
    /// Always whole months, even for a custom range that starts mid-month: a column headed 2026-08 that silently
    /// contained eleven days of August would be read as a month and would not add up to one.
    /// </remarks>
    public IReadOnlyList<FinancePeriod> Months()
    {
        var months = new List<FinancePeriod>();
        var cursor = new DateOnly(From.Year, From.Month, 1);

        while (cursor <= To)
        {
            months.Add(Month(cursor));
            cursor = cursor.AddMonths(1);
        }

        return months;
    }

    public bool Contains(DateOnly day) => day >= From && day <= To;

    public override string ToString() => $"{Kind} {From:yyyy-MM-dd}..{To:yyyy-MM-dd}";
}

public static class FinancePeriods
{
    public const string Month = "month";
    public const string Quarter = "quarter";
    public const string Year = "year";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> All = [Month, Quarter, Year, Custom];
}
