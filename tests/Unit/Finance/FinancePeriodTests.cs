using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Finance.Domain;

namespace Cracra.Tests.Unit.Finance;

/// <summary>Period arithmetic, and the effective-dating a rate card depends on.</summary>
public sealed class FinancePeriodTests
{
    private static readonly DateOnly August = new(2026, 8, 20);

    [Fact]
    public void A_month_runs_from_the_first_to_the_last_day()
    {
        var period = FinancePeriod.Month(August);

        period.From.ShouldBe(new DateOnly(2026, 8, 1));
        period.To.ShouldBe(new DateOnly(2026, 8, 31));
        period.Days.ShouldBe(31);
    }

    [Fact]
    public void February_in_a_leap_year_has_twenty_nine_days()
    {
        FinancePeriod.Month(new DateOnly(2028, 2, 14)).Days.ShouldBe(29);
    }

    [Theory]
    [InlineData(1, 1, 3)]
    [InlineData(5, 4, 6)]
    [InlineData(8, 7, 9)]
    [InlineData(12, 10, 12)]
    public void A_quarter_starts_on_its_own_first_month(int month, int firstMonth, int lastMonth)
    {
        var period = FinancePeriod.Quarter(new DateOnly(2026, month, 15));

        period.From.ShouldBe(new DateOnly(2026, firstMonth, 1));
        period.To.Month.ShouldBe(lastMonth);
        period.Months().Count.ShouldBe(3);
    }

    [Fact]
    public void A_year_is_the_calendar_year()
    {
        var period = FinancePeriod.Year(August);

        period.From.ShouldBe(new DateOnly(2026, 1, 1));
        period.To.ShouldBe(new DateOnly(2026, 12, 31));
        period.Months().Count.ShouldBe(12);
    }

    [Fact]
    public void A_custom_range_that_ends_before_it_starts_is_refused()
    {
        Should.Throw<DomainRuleViolationException>(() =>
            FinancePeriod.Custom(new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public void A_custom_range_longer_than_two_years_is_refused()
    {
        // Not a policy about accounting, a guard against a typo: a request for a decade would load every activity
        // row in the platform to answer a question nobody asked.
        Should.Throw<DomainRuleViolationException>(() =>
            FinancePeriod.Custom(new DateOnly(2026, 1, 1), new DateOnly(2029, 1, 1)));
    }

    [Fact]
    public void A_partial_month_still_breaks_down_into_whole_months()
    {
        var period = FinancePeriod.Custom(new DateOnly(2026, 8, 20), new DateOnly(2026, 9, 5));

        var months = period.Months();

        // A column headed 2026-08 that silently held eleven days of August would be read as a month and would not
        // add up to one.
        months.Count.ShouldBe(2);
        months[0].From.ShouldBe(new DateOnly(2026, 8, 1));
        months[0].To.ShouldBe(new DateOnly(2026, 8, 31));
        months[1].To.ShouldBe(new DateOnly(2026, 9, 30));
    }

    [Fact]
    public void An_unrecognized_period_falls_back_to_the_month_rather_than_being_refused()
    {
        var period = FinancePeriod.Resolve("fortnight", null, null, August);

        // The period is a lens on a read-only view. Somebody who mistyped it is better served by this month's
        // figures, with the kind stated on them, than by a 422 they have to decode.
        period.Kind.ShouldBe(FinancePeriods.Month);
        period.From.ShouldBe(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public void A_custom_period_without_both_ends_falls_back_too()
    {
        FinancePeriod.Resolve("custom", new DateOnly(2026, 3, 4), null, August).Kind.ShouldBe(FinancePeriods.Month);
    }

    // --- Rate cards --------------------------------------------------------------------------------------------

    [Fact]
    public void A_rate_card_covers_from_its_start_up_to_but_not_including_its_end()
    {
        var card = Card(new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1));

        card.CoversDay(new DateOnly(2026, 1, 1)).ShouldBeTrue();
        card.CoversDay(new DateOnly(2026, 6, 30)).ShouldBeTrue();

        // Exclusive, so a card ending on the day the next begins is a clean handover rather than a day priced
        // twice or not at all.
        card.CoversDay(new DateOnly(2026, 7, 1)).ShouldBeFalse();
        card.CoversDay(new DateOnly(2025, 12, 31)).ShouldBeFalse();
    }

    [Fact]
    public void An_open_ended_card_covers_everything_after_its_start()
    {
        var card = Card(new DateOnly(2026, 1, 1), null);

        card.CoversDay(new DateOnly(2030, 1, 1)).ShouldBeTrue();
        card.CoversDay(new DateOnly(2025, 12, 31)).ShouldBeFalse();
    }

    [Fact]
    public void Consecutive_cards_do_not_overlap()
    {
        var first = Card(new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1));
        var second = Card(new DateOnly(2026, 7, 1), null);

        first.Overlaps(second).ShouldBeFalse();
        second.Overlaps(first).ShouldBeFalse();
    }

    [Fact]
    public void Two_cards_that_would_both_price_a_day_are_detected_either_way_round()
    {
        var first = Card(new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1));
        var overlapping = Card(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1));

        // Symmetry matters: which card is being saved and which is already stored is an accident of ordering, and
        // an overlap that only one direction catches is an overlap that gets in half the time.
        first.Overlaps(overlapping).ShouldBeTrue();
        overlapping.Overlaps(first).ShouldBeTrue();
    }

    [Fact]
    public void Two_open_ended_cards_always_overlap()
    {
        Card(new DateOnly(2026, 1, 1), null).Overlaps(Card(new DateOnly(2030, 1, 1), null)).ShouldBeTrue();
    }

    private static RateCard Card(DateOnly from, DateOnly? to) => new()
    {
        Id = Guid.CreateVersion7(),
        DepartmentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        FunctionalRoleId = Guid.Parse("f0000000-0000-0000-0000-000000000001"),
        HourlyRate = 75m,
        EffectiveFrom = from,
        EffectiveTo = to,
    };
}
