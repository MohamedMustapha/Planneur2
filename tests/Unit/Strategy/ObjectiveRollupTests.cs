using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Strategy.Contracts;
using Cracra.Modules.Strategy.Domain;

namespace Cracra.Tests.Unit.Strategy;

/// <summary>
/// The rollup arithmetic and the status derivation (v2 §06.2).
/// </summary>
/// <remarks>
/// These are the numbers a COPIL argues about, so they are asserted rather than trusted. The two that matter most
/// are the ones a naive implementation gets wrong: progress measured from the baseline rather than from zero, so a
/// "reduce X" objective does not start at 125%; and "on track" measured against the calendar rather than against
/// zero, so 5% in week one and 5% in month ten are not the same answer.
/// </remarks>
public sealed class ObjectiveRollupTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Node = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Owner = Guid.Parse("c0000000-0000-0000-0000-000000000001");

    // --- Progress -------------------------------------------------------------------------------------------------

    [Fact]
    public void Progress_is_measured_from_the_baseline_not_from_zero()
    {
        // "Cut ticket handling from 40 minutes to 32." At 36 the work is half done; read as current/target it
        // would report 112% and fall as the objective succeeded.
        var objective = Measured(baseline: 40m, target: 32m, current: 36m);

        objective.Progress().ShouldBe(0.5m);
    }

    [Fact]
    public void An_untouched_objective_has_made_no_progress()
    {
        Measured(baseline: 40m, target: 32m, current: null).Progress().ShouldBe(0m);
    }

    [Fact]
    public void Progress_never_exceeds_one()
    {
        // Beating a target is good news, not 180% of an objective — a strategy whose weighted total could exceed
        // 100% would let one runaway objective hide three that never moved.
        Measured(baseline: 0m, target: 10m, current: 18m).Progress().ShouldBe(1m);
    }

    [Fact]
    public void Progress_is_never_negative()
    {
        Measured(baseline: 40m, target: 32m, current: 44m).Progress().ShouldBe(0m);
    }

    [Fact]
    public void A_milestone_is_done_or_it_is_not()
    {
        var objective = Milestone();

        objective.Progress().ShouldBe(0m);

        objective.OverrideStatus(ObjectiveStatuses.Done, Created);

        objective.Progress().ShouldBe(1m);
    }

    // --- Status derivation ----------------------------------------------------------------------------------------

    [Fact]
    public void Reaching_the_target_is_done_whatever_the_calendar_says()
    {
        var objective = Measured(baseline: 0m, target: 10m, current: 10m, due: new DateOnly(2026, 12, 31));

        ObjectiveMath.Derive(objective, new DateOnly(2026, 2, 1)).ShouldBe(ObjectiveStatuses.Done);
    }

    [Fact]
    public void Early_in_the_period_a_low_reading_is_still_on_track()
    {
        var objective = Measured(baseline: 0m, target: 100m, current: 5m, due: new DateOnly(2026, 12, 31));

        ObjectiveMath.Derive(objective, new DateOnly(2026, 1, 15)).ShouldBe(ObjectiveStatuses.OnTrack);
    }

    [Fact]
    public void The_same_reading_late_in_the_period_is_off_track()
    {
        var objective = Measured(baseline: 0m, target: 100m, current: 5m, due: new DateOnly(2026, 12, 31));

        ObjectiveMath.Derive(objective, new DateOnly(2026, 11, 1)).ShouldBe(ObjectiveStatuses.OffTrack);
    }

    [Fact]
    public void Slipping_a_little_behind_the_even_line_is_at_risk()
    {
        // Roughly a fifth of the way through the year, with a twentieth of the work done: behind, but not the
        // kind of behind that needs escalating.
        var objective = Measured(baseline: 0m, target: 100m, current: 5m, due: new DateOnly(2026, 12, 31));

        ObjectiveMath.Derive(objective, new DateOnly(2026, 4, 1)).ShouldBe(ObjectiveStatuses.AtRisk);
    }

    [Fact]
    public void An_objective_with_no_due_date_can_never_be_late()
    {
        // A commitment with no date is a commitment nobody agreed when to keep. Calling it at risk would be
        // inventing a signal out of a blank field.
        var objective = Measured(baseline: 0m, target: 100m, current: 0m);

        ObjectiveMath.Derive(objective, new DateOnly(2030, 1, 1)).ShouldBe(ObjectiveStatuses.OnTrack);
    }

    [Fact]
    public void An_unfinished_milestone_goes_off_track_once_its_date_passes()
    {
        var objective = Milestone(due: new DateOnly(2026, 6, 30));

        ObjectiveMath.Derive(objective, new DateOnly(2026, 6, 1)).ShouldBe(ObjectiveStatuses.OnTrack);
        ObjectiveMath.Derive(objective, new DateOnly(2026, 7, 1)).ShouldBe(ObjectiveStatuses.OffTrack);
    }

    [Fact]
    public void An_overridden_status_survives_the_derivation()
    {
        var objective = Measured(baseline: 0m, target: 100m, current: 5m, due: new DateOnly(2026, 12, 31));

        objective.OverrideStatus(ObjectiveStatuses.OnTrack, Created);

        ObjectiveMath.Derive(objective, new DateOnly(2026, 11, 1)).ShouldBe(ObjectiveStatuses.OnTrack);
    }

    [Fact]
    public void Clearing_the_override_hands_the_status_back_to_the_numbers()
    {
        var strategy = Plan();
        var objective = strategy.AddObjective(
            "Cut handling time",
            null,
            MetricKinds.Number,
            0m,
            100m,
            5m,
            "min",
            new DateOnly(2026, 12, 31),
            1m,
            Created);

        objective.OverrideStatus(ObjectiveStatuses.OnTrack, Created);
        objective.OverrideStatus(null, new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));

        objective.StatusOverridden.ShouldBeFalse();
        objective.Status.ShouldBe(ObjectiveStatuses.OffTrack);
    }

    // --- Weighted strategy rollup ---------------------------------------------------------------------------------

    [Fact]
    public void A_strategy_with_no_objectives_has_achieved_nothing()
    {
        // Zero rather than one: an empty strategy is not a finished one.
        Plan().Progress().ShouldBe(0m);
    }

    [Fact]
    public void Objectives_roll_up_weighted()
    {
        var strategy = Plan();

        Add(strategy, target: 100m, current: 100m, weight: 3m);
        Add(strategy, target: 100m, current: 0m, weight: 1m);

        strategy.Progress().ShouldBe(0.75m);
    }

    [Fact]
    public void Weights_that_do_not_add_to_one_are_normalised_rather_than_refused()
    {
        // Four objectives at weight 1 means "equally". Demanding 0.25 four times is arithmetic homework.
        var strategy = Plan();

        Add(strategy, target: 100m, current: 100m, weight: 1m);
        Add(strategy, target: 100m, current: 0m, weight: 1m);

        strategy.Progress().ShouldBe(0.5m);
    }

    // --- Guards ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_measured_objective_needs_a_target() =>
        Should.Throw<DomainRuleViolationException>(() => Plan().AddObjective(
            "Improve things",
            null,
            MetricKinds.Number,
            null,
            null,
            null,
            null,
            null,
            1m,
            Created));

    [Fact]
    public void A_weight_of_zero_is_an_objective_nobody_is_committing_to() =>
        Should.Throw<DomainRuleViolationException>(() => Add(Plan(), 100m, 0m, weight: 0m));

    [Fact]
    public void A_milestone_cannot_be_measured_with_a_number() =>
        Should.Throw<DomainRuleViolationException>(() => Milestone().Measure(5m, Created));

    [Fact]
    public void A_closed_strategy_refuses_new_objectives()
    {
        var strategy = Plan();

        strategy.Amend(null, null, null, null, StrategyStatuses.Closed, Created);

        Should.Throw<DomainRuleViolationException>(() => Add(strategy, 100m, 0m));
    }

    [Fact]
    public void A_strategy_cannot_end_before_it_starts() =>
        Should.Throw<DomainRuleViolationException>(() => StrategyPlan.Open(
            StrategyScopeTypes.Node,
            Node,
            new DateOnly(2026, 12, 31),
            new DateOnly(2026, 1, 1),
            "Backwards",
            null,
            Owner,
            Created));

    // --- Contributions --------------------------------------------------------------------------------------------

    [Fact]
    public void Linking_the_same_thing_twice_re_weighs_it_rather_than_duplicating_it()
    {
        var objective = Add(Plan(), 100m, 0m);
        var item = Guid.CreateVersion7();

        objective.Link(ContributionSources.Item, item, 1m, null, Created);
        objective.Link(ContributionSources.Item, item, 2m, "Bigger than we thought.", Created);

        objective.Contributions.Count.ShouldBe(1);
        objective.Contributions.Single().Weight.ShouldBe(2m);
    }

    [Fact]
    public void A_contribution_has_to_point_at_something() =>
        Should.Throw<DomainRuleViolationException>(
            () => Add(Plan(), 100m, 0m).Link(ContributionSources.Item, Guid.Empty, 1m, null, Created));

    [Fact]
    public void A_problem_is_a_contribution_source_too()
    {
        // §06.1: solving this pain advances the objective. The intake pipeline and the strategy spine meet here.
        var objective = Add(Plan(), 100m, 0m);

        objective.Link(ContributionSources.Problem, Guid.CreateVersion7(), 1m, null, Created);

        objective.Contributions.Single().SourceType.ShouldBe(ContributionSources.Problem);
    }

    // --- Fixture --------------------------------------------------------------------------------------------------

    private static StrategyPlan Plan() => StrategyPlan.Open(
        StrategyScopeTypes.Node,
        Node,
        new DateOnly(2026, 1, 1),
        new DateOnly(2026, 12, 31),
        "Do better things",
        null,
        Owner,
        Created);

    private static Objective Add(
        StrategyPlan strategy,
        decimal target,
        decimal current,
        decimal weight = 1m,
        DateOnly? due = null) =>
        strategy.AddObjective(
            "An objective",
            null,
            MetricKinds.Number,
            0m,
            target,
            current,
            null,
            due,
            weight,
            Created);

    private static Objective Measured(decimal baseline, decimal target, decimal? current, DateOnly? due = null) =>
        Plan().AddObjective(
            "A measured objective",
            null,
            MetricKinds.Number,
            baseline,
            target,
            current,
            "min",
            due,
            1m,
            Created);

    private static Objective Milestone(DateOnly? due = null) =>
        Plan().AddObjective(
            "A milestone",
            null,
            MetricKinds.Milestone,
            null,
            null,
            null,
            null,
            due,
            1m,
            Created);
}
