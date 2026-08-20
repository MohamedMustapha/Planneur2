using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Meetings.Domain;

namespace Cracra.Tests.Unit.Meetings;

/// <summary>
/// RRULE parsing and expansion — the one genuinely non-trivial piece of a 2-layer module.
/// </summary>
/// <remarks>
/// Worth testing exhaustively precisely because it is pure and because everything downstream trusts it. A board
/// showing the copil on the wrong Tuesday is not a visible bug: it is a meeting somebody misses.
/// </remarks>
public sealed class RecurrenceRuleTests
{
    private static readonly DateOnly Monday = new(2026, 8, 17);
    private static readonly DateOnly YearEnd = new(2026, 12, 31);

    // --- Parsing -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_weekly_rule_parses()
    {
        var rule = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO");

        rule.Frequency.ShouldBe(RecurrenceFrequency.Weekly);
        rule.Interval.ShouldBe(1);
        rule.ByDay.ShouldBe([DayOfWeek.Monday]);
    }

    [Fact]
    public void The_iCal_line_prefix_is_accepted()
    {
        // Both forms mean the same thing, and whichever way somebody pasted it they meant the same thing.
        RecurrenceRule.Parse("RRULE:FREQ=WEEKLY;BYDAY=TU").ByDay.ShouldBe([DayOfWeek.Tuesday]);
    }

    [Fact]
    public void An_unsupported_part_is_refused_rather_than_ignored()
    {
        var refusal = Should.Throw<DomainRuleViolationException>(() =>
            RecurrenceRule.Parse("FREQ=WEEKLY;BYSETPOS=-1"));

        // The whole reason to store RRULE rather than three columns is that the string means what it says.
        // Dropping the part we do not understand would produce a schedule that is confidently wrong.
        refusal.Message.ShouldContain("BYSETPOS");
    }

    [Fact]
    public void A_yearly_rule_is_refused_with_the_supported_list()
    {
        Should.Throw<DomainRuleViolationException>(() => RecurrenceRule.Parse("FREQ=YEARLY"))
            .Message.ShouldContain("DAILY, WEEKLY or MONTHLY");
    }

    [Fact]
    public void A_rule_without_a_frequency_is_refused()
    {
        Should.Throw<DomainRuleViolationException>(() => RecurrenceRule.Parse("INTERVAL=2"))
            .Message.ShouldContain("FREQ");
    }

    [Fact]
    public void Count_and_until_together_are_refused()
    {
        // RFC 5545 says one or the other. Accepting both would mean picking a winner, and either choice
        // contradicts what somebody wrote.
        Should.Throw<DomainRuleViolationException>(() =>
            RecurrenceRule.Parse("FREQ=WEEKLY;COUNT=3;UNTIL=20261231"));
    }

    [Fact]
    public void An_ordinal_weekday_outside_a_monthly_rule_is_refused()
    {
        Should.Throw<DomainRuleViolationException>(() => RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=3TH"));
    }

    [Fact]
    public void A_rule_round_trips_through_its_canonical_form()
    {
        const string text = "FREQ=MONTHLY;INTERVAL=2;BYDAY=3TH;COUNT=6";

        var once = RecurrenceRule.Parse(text);
        var twice = RecurrenceRule.Parse(once.ToString());

        // Stored canonical rather than verbatim, so two people writing the same rule two ways produce one string.
        once.ToString().ShouldBe(twice.ToString());
        once.ToString().ShouldBe(text);
    }

    // --- Weekly expansion --------------------------------------------------------------------------------------

    [Fact]
    public void A_weekly_stand_up_lands_on_its_weekday()
    {
        var days = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO")
            .Expand(Monday, Monday, Monday.AddDays(27))
            .ToList();

        days.ShouldBe([Monday, Monday.AddDays(7), Monday.AddDays(14), Monday.AddDays(21)]);
    }

    [Fact]
    public void Several_weekdays_come_out_in_order()
    {
        var days = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=FR,MO,WE")
            .Expand(Monday, Monday, Monday.AddDays(6))
            .ToList();

        // Written out of order, because that is how people type it. The sequence still has to ascend, or every
        // consumer would have to re-sort what it was handed.
        days.ShouldBe([Monday, Monday.AddDays(2), Monday.AddDays(4)]);
    }

    [Fact]
    public void A_fortnightly_series_counts_weeks_from_its_own_start()
    {
        var days = RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO")
            .Expand(Monday, Monday, Monday.AddDays(28))
            .ToList();

        days.ShouldBe([Monday, Monday.AddDays(14), Monday.AddDays(28)]);
    }

    [Fact]
    public void A_window_that_starts_mid_series_still_lands_on_the_right_weeks()
    {
        // The failure this catches is the tempting optimization: jumping straight to the window instead of
        // enumerating from the anchor. It would put a fortnightly series on the wrong Monday half the time —
        // and on the right one the other half, which is what makes it hard to notice.
        var days = RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO")
            .Expand(Monday, Monday.AddDays(7), Monday.AddDays(35))
            .ToList();

        days.ShouldBe([Monday.AddDays(14), Monday.AddDays(28)]);
    }

    [Fact]
    public void Nothing_before_the_anchor_is_produced()
    {
        var days = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO,FR")
            // Anchored on the Wednesday, so that week's Monday is behind the start even though it is in range.
            .Expand(Monday.AddDays(2), Monday, Monday.AddDays(6))
            .ToList();

        days.ShouldBe([Monday.AddDays(4)]);
    }

    // --- Limits ------------------------------------------------------------------------------------------------

    [Fact]
    public void Count_is_counted_from_the_anchor_not_from_the_window()
    {
        var rule = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO;COUNT=3");

        // Three in total. Asking about a later window must not restart the count and grant three more.
        rule.Expand(Monday, Monday, YearEnd).Count().ShouldBe(3);
        rule.Expand(Monday, Monday.AddDays(14), YearEnd).ShouldBe([Monday.AddDays(14)]);
    }

    [Fact]
    public void Until_stops_the_series_even_when_the_window_reaches_further()
    {
        var days = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO;UNTIL=20260907")
            .Expand(Monday, Monday, YearEnd)
            .ToList();

        days.ShouldBe([Monday, Monday.AddDays(7), Monday.AddDays(14), Monday.AddDays(21)]);
    }

    [Fact]
    public void An_until_carrying_a_time_is_read_as_its_date()
    {
        RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20260819T235959Z")
            .Expand(Monday, Monday, YearEnd)
            .ShouldBe([Monday, Monday.AddDays(1), Monday.AddDays(2)]);
    }

    // --- Daily and monthly -------------------------------------------------------------------------------------

    [Fact]
    public void A_daily_rule_steps_by_its_interval()
    {
        RecurrenceRule.Parse("FREQ=DAILY;INTERVAL=3")
            .Expand(Monday, Monday, Monday.AddDays(9))
            .ShouldBe([Monday, Monday.AddDays(3), Monday.AddDays(6), Monday.AddDays(9)]);
    }

    [Fact]
    public void A_bare_monthly_rule_repeats_the_anchor_day()
    {
        RecurrenceRule.Parse("FREQ=MONTHLY")
            .Expand(new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30))
            .ShouldBe([new(2026, 1, 15), new(2026, 2, 15), new(2026, 3, 15), new(2026, 4, 15)]);
    }

    [Fact]
    public void A_month_too_short_for_the_anchor_day_is_skipped_rather_than_clamped()
    {
        var days = RecurrenceRule.Parse("FREQ=MONTHLY")
            .Expand(new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30))
            .ToList();

        // February has no 31st. Moving the meeting to the 28th would invent one nobody scheduled, and quietly
        // disagree with every calendar client that would also have skipped it.
        days.ShouldBe([new(2026, 1, 31), new(2026, 3, 31)]);
    }

    [Fact]
    public void The_third_thursday_copil_lands_on_the_third_thursday()
    {
        var days = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=3TH")
            .Expand(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31))
            .ToList();

        days.ShouldBe([new(2026, 1, 15), new(2026, 2, 19), new(2026, 3, 19)]);
    }

    [Fact]
    public void A_negative_ordinal_counts_back_from_the_end_of_the_month()
    {
        var days = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR")
            .Expand(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31))
            .ToList();

        days.ShouldBe([new(2026, 1, 30), new(2026, 2, 27), new(2026, 3, 27)]);
    }

    [Fact]
    public void A_negative_month_day_counts_back_from_the_end()
    {
        RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=-1")
            .Expand(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31))
            .ShouldBe([new(2026, 1, 31), new(2026, 2, 28), new(2026, 3, 31)]);
    }

    // --- Windows -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_window_entirely_before_the_anchor_produces_nothing()
    {
        RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO")
            .Expand(Monday, Monday.AddDays(-30), Monday.AddDays(-1))
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_window_is_inclusive_at_both_ends()
    {
        RecurrenceRule.Parse("FREQ=DAILY")
            .Expand(Monday, Monday, Monday.AddDays(2))
            .ShouldBe([Monday, Monday.AddDays(1), Monday.AddDays(2)]);
    }
}
