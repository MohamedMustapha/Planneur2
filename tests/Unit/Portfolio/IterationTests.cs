using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Portfolio.Domain;

namespace Cracra.Tests.Unit.Portfolio;

/// <summary>
/// The quick selectors' date arithmetic.
/// </summary>
/// <remarks>
/// Worth its own class because an off-by-one here is invisible in the UI and wrong everywhere else: S6 draws
/// iteration boundaries as shaded ranges that must abut rather than overlap, and S8 counts effort per iteration.
/// </remarks>
public sealed class IterationTests
{
    private static readonly Guid Head = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Dept = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void One_week_is_seven_days_inclusive()
    {
        // Monday to the following Sunday. Eight days would make consecutive weekly iterations overlap by a day.
        Iteration.EndDateFor(IterationLength.OneWeek, new DateOnly(2026, 8, 17))
            .ShouldBe(new DateOnly(2026, 8, 23));
    }

    [Fact]
    public void Two_weeks_is_fourteen_days_inclusive()
    {
        Iteration.EndDateFor(IterationLength.TwoWeeks, new DateOnly(2026, 8, 17))
            .ShouldBe(new DateOnly(2026, 8, 30));
    }

    [Fact]
    public void One_month_is_a_calendar_month()
    {
        Iteration.EndDateFor(IterationLength.OneMonth, new DateOnly(2026, 1, 1))
            .ShouldBe(new DateOnly(2026, 1, 31));

        // Clamped, not rolled over: 31 January plus a month is the end of February, not 3 March.
        Iteration.EndDateFor(IterationLength.OneMonth, new DateOnly(2026, 1, 31))
            .ShouldBe(new DateOnly(2026, 2, 27));
    }

    [Fact]
    public void A_custom_length_needs_an_explicit_end_date()
    {
        var item = Candidate();

        Should.Throw<DomainRuleViolationException>(() =>
            item.AddIteration("Spike", IterationLength.Custom, new DateOnly(2026, 8, 17), null, Head, Now));
    }

    [Fact]
    public void An_explicit_end_date_overrides_the_preset()
    {
        var item = Candidate();

        // The presets pre-fill; they never constrain. An iteration is a free-length phase by definition.
        var iteration = item.AddIteration(
            "Sprint 1",
            IterationLength.OneWeek,
            new DateOnly(2026, 8, 17),
            new DateOnly(2026, 9, 4),
            Head,
            Now);

        iteration.EndsOn.ShouldBe(new DateOnly(2026, 9, 4));
    }

    [Fact]
    public void An_iteration_cannot_end_before_it_starts()
    {
        var item = Candidate();

        Should.Throw<DomainRuleViolationException>(() => item.AddIteration(
            "Backwards",
            IterationLength.Custom,
            new DateOnly(2026, 8, 17),
            new DateOnly(2026, 8, 10),
            Head,
            Now));
    }

    [Fact]
    public void Iterations_are_numbered_in_the_order_they_are_opened()
    {
        var item = Candidate();

        item.AddIteration("Sprint 1", IterationLength.OneWeek, new DateOnly(2026, 8, 17), null, Head, Now);
        item.AddIteration("Sprint 2", IterationLength.OneWeek, new DateOnly(2026, 8, 24), null, Head, Now);

        item.Iterations.Select(iteration => iteration.Sequence).ShouldBe([1, 2]);
    }

    [Fact]
    public void An_unnamed_iteration_gets_its_sequence_as_a_name()
    {
        var item = Candidate();

        var iteration = item.AddIteration("  ", IterationLength.OneWeek, new DateOnly(2026, 8, 17), null, Head, Now);

        iteration.Name.ShouldBe("Iteration 1");
    }

    [Fact]
    public void A_closed_iteration_cannot_be_rescheduled()
    {
        var item = Candidate();
        item.Commit(Guid.CreateVersion7(), "Approved.", Head, Now);
        var iteration = item.AddIteration("Sprint 1", IterationLength.OneWeek, new DateOnly(2026, 8, 17), null, Head, Now);
        item.Activate(hasTeam: true, Head, Now);
        item.CloseIteration(iteration.Id, Head, Now);

        Should.Throw<DomainRuleViolationException>(() => item.RescheduleIteration(
            iteration.Id,
            "Sprint 1 (again)",
            IterationLength.OneWeek,
            new DateOnly(2026, 9, 1),
            null,
            Head,
            Now));
    }

    private static PortfolioItem Candidate() =>
        PortfolioItem.Consider("REFONTEPORTA", "Refonte du portail", 10, Dept, null, Head, Now);
}
