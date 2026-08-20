using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Kudos.Domain;

namespace Cracra.Tests.Unit.Kudos;

/// <summary>
/// The month the cap is counted in, and the window everything is drawn over.
/// </summary>
public sealed class KudoPeriodTests
{
    private static readonly DateTimeOffset Midday = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_month_knows_its_own_ends()
    {
        var month = new KudoMonth(2026, 2);

        month.First.ShouldBe(new DateOnly(2026, 2, 1));
        month.Last.ShouldBe(new DateOnly(2026, 2, 28));
    }

    [Fact]
    public void A_month_is_taken_in_utc()
    {
        // Half past midnight on the first, seen from Paris in summer, is still the previous month in UTC — and a
        // cap that could be spent twice by moving timezone would not be a cap.
        var lateJuly = new DateTimeOffset(2026, 8, 1, 1, 30, 0, TimeSpan.FromHours(2));

        KudoMonth.Of(lateJuly).ShouldBe(new KudoMonth(2026, 7));
    }

    [Fact]
    public void Months_order()
    {
        new KudoMonth(2025, 12).CompareTo(new KudoMonth(2026, 1)).ShouldBeLessThan(0);
    }

    [Fact]
    public void An_impossible_month_is_refused()
    {
        Should.Throw<DomainRuleViolationException>(() => new KudoMonth(2026, 13));
    }

    [Fact]
    public void The_default_period_is_this_month()
    {
        var period = KudoPeriod.Resolve(null, null, null, Midday);

        period.Kind.ShouldBe(KudoPeriod.MonthKind);
        period.From.ShouldBe(new DateOnly(2026, 8, 1));
        period.To.ShouldBe(new DateOnly(2026, 8, 31));
    }

    [Fact]
    public void A_year_spans_the_whole_of_it()
    {
        var period = KudoPeriod.Resolve("year", 2025, null, Midday);

        period.From.ShouldBe(new DateOnly(2025, 1, 1));
        period.To.ShouldBe(new DateOnly(2025, 12, 31));
    }

    [Theory]
    [InlineData("fortnight", null, null)]
    [InlineData("month", 12, 99)]
    [InlineData("month", 1, 8)]
    public void Nonsense_falls_back_rather_than_failing(string kind, int? year, int? month)
    {
        // These are read endpoints backing a widget. A board that renders an error because somebody hand-edited a
        // query parameter is worse than one that shows this month.
        var period = KudoPeriod.Resolve(kind, year, month, Midday);

        period.From.Year.ShouldBe(2026);
    }
}
