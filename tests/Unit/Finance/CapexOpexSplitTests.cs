using Cracra.Modules.Finance.Domain;

namespace Cracra.Tests.Unit.Finance;

/// <summary>
/// The split arithmetic.
/// </summary>
/// <remarks>
/// This is where the slice's numbers are decided, so it is where they are pinned. The cases worth having are the
/// ones a reader of the finished view would be unable to check by eye: a mixed project apportioned by its own
/// hours, an hour nobody has priced, a bucket a department chose to exclude, and money that ends up in neither
/// column.
/// </remarks>
public sealed class CapexOpexSplitTests
{
    private static readonly Guid Department = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void The_default_rule_capitalizes_build_and_expenses_run()
    {
        var totals = Split()
            .AddEffort(Buckets.Build, 10m, 1000m)
            .AddEffort(Buckets.Run, 5m, 500m)
            .Build();

        totals.CapexAmount.ShouldBe(1000m);
        totals.OpexAmount.ShouldBe(500m);
        totals.CapexHours.ShouldBe(10m);
        totals.OpexHours.ShouldBe(5m);
    }

    [Fact]
    public void Quality_of_life_is_opex_and_administration_is_excluded()
    {
        var totals = Split()
            .AddEffort(Buckets.QualityOfLife, 4m, 400m)
            .AddEffort(Buckets.Admin, 3m, 300m)
            .Build();

        // The defaults S11 names. Both are real hours; only one of them is spend anybody reports.
        totals.OpexAmount.ShouldBe(400m);
        totals.ExcludedAmount.ShouldBe(300m);
        totals.ExcludedHours.ShouldBe(3m);

        // Excluded means excluded from the two columns, not from the hours.
        totals.TotalHours.ShouldBe(7m);
    }

    [Fact]
    public void A_department_can_move_a_bucket_to_the_other_column()
    {
        var rule = Rule();

        // A department that treats its RUN work as capitalizable maintenance. The platform has an opinion about
        // the default and none about this — which is the whole reason the rule is a row rather than a constant.
        rule.RunTreatment = Treatments.Capex;

        var totals = new CapexOpexSplit(rule)
            .AddEffort(Buckets.Run, 8m, 800m)
            .Build();

        totals.CapexAmount.ShouldBe(800m);
        totals.OpexAmount.ShouldBe(0m);
    }

    [Fact]
    public void An_hour_nobody_priced_counts_as_an_hour_and_not_as_zero()
    {
        var totals = Split()
            .AddEffort(Buckets.Build, 6m, cost: null)
            .Build();

        totals.CapexHours.ShouldBe(6m);
        totals.CapexAmount.ShouldBe(0m);

        // The flag is what stops a reader taking the zero for a fact. "We have not priced this" and "this was
        // free" are the same number and entirely different statements.
        totals.EffortValued.ShouldBeFalse();
        totals.ByBucket.Single(bucket => bucket.Bucket == Buckets.Build).UnvaluedHours.ShouldBe(6m);
    }

    [Fact]
    public void A_partly_priced_department_reports_both_the_money_and_the_gap()
    {
        var totals = Split()
            .AddEffort(Buckets.Build, 10m, 1000m)
            .AddEffort(Buckets.Build, 5m, cost: null)
            .Build();

        totals.CapexAmount.ShouldBe(1000m);
        totals.CapexHours.ShouldBe(15m);

        // Valued, because some of it is — and the unvalued hours are on the bucket so the screen can say which
        // part of the money is missing rather than hiding the whole column.
        totals.EffortValued.ShouldBeTrue();
        totals.ByBucket.Single(bucket => bucket.Bucket == Buckets.Build).UnvaluedHours.ShouldBe(5m);
    }

    [Fact]
    public void An_activity_code_this_module_cannot_place_is_reported_rather_than_filed()
    {
        var totals = Split()
            .AddEffort(bucket: null, 9m, 900m)
            .Build();

        // The case where S5 grew a fifth canonical bucket and nobody told Finance. Filing it under
        // quality-of-life by guesswork would put real work in the opex column with nothing to notice.
        totals.UnclassifiedHours.ShouldBe(9m);
        totals.CapexAmount.ShouldBe(0m);
        totals.OpexAmount.ShouldBe(0m);
        totals.TotalHours.ShouldBe(9m);
    }

    [Fact]
    public void A_build_projects_manual_cost_follows_the_build_treatment()
    {
        var totals = Split()
            .AddManualCost("build", 50_000m, buildHours: 0m, runHours: 0m)
            .Build();

        totals.CapexAmount.ShouldBe(50_000m);
        totals.ManualCapex.ShouldBe(50_000m);
        totals.EffortCapex.ShouldBe(0m);
    }

    [Fact]
    public void A_mixed_project_is_apportioned_by_its_own_hours()
    {
        var totals = Split()
            .AddManualCost("mixed", 10_000m, buildHours: 30m, runHours: 10m)
            .Build();

        // Three quarters of the effort was BUILD, so three quarters of the budget is capitalized. Splitting it in
        // half would have invented a ratio the project itself already answers.
        totals.CapexAmount.ShouldBe(7_500m);
        totals.OpexAmount.ShouldBe(2_500m);
    }

    [Fact]
    public void An_apportioned_cost_still_adds_back_to_the_original()
    {
        // A ratio that does not divide cleanly: a third of a cent either way, repeated across a portfolio, is how
        // a total comes to disagree with the sum of its rows.
        var totals = Split()
            .AddManualCost("mixed", 100m, buildHours: 1m, runHours: 2m)
            .Build();

        (totals.CapexAmount + totals.OpexAmount).ShouldBe(100m);
        totals.CapexAmount.ShouldBe(33.33m);
    }

    [Fact]
    public void A_mixed_project_with_no_logged_hours_is_unallocated_rather_than_guessed()
    {
        var totals = Split()
            .AddManualCost("mixed", 20_000m, buildHours: 0m, runHours: 0m)
            .Build();

        // Visible, and in neither column. Defaulting it to opex would quietly understate capex for every mixed
        // project nobody logged against — which is a number somebody reports upward.
        totals.UnallocatedAmount.ShouldBe(20_000m);
        totals.CapexAmount.ShouldBe(0m);
        totals.OpexAmount.ShouldBe(0m);
    }

    [Fact]
    public void Money_in_an_excluded_bucket_is_unallocated_rather_than_lost()
    {
        var rule = Rule();

        rule.BuildTreatment = Treatments.Excluded;

        var totals = new CapexOpexSplit(rule)
            .AddManualCost("build", 5_000m, 0m, 0m)
            .Build();

        // Excluded from both columns, still on the page. A reconciliation that cannot find five thousand euros is
        // worse than one that finds them under a heading somebody has to explain.
        totals.UnallocatedAmount.ShouldBe(5_000m);
    }

    [Fact]
    public void Manual_cost_and_effort_cost_are_carried_separately_as_well_as_together()
    {
        var totals = Split()
            .AddEffort(Buckets.Build, 10m, 1_000m)
            .AddManualCost("build", 4_000m, 10m, 0m)
            .Build();

        totals.CapexAmount.ShouldBe(5_000m);

        // Both halves stay visible, because they answer different questions — what was budgeted, and what it cost
        // in people — and a reader who cannot separate them may double-count a project whose budget was itself
        // derived from effort.
        totals.ManualCapex.ShouldBe(4_000m);
        totals.EffortCapex.ShouldBe(1_000m);
    }

    [Fact]
    public void The_capex_ratio_is_null_when_there_is_nothing_to_take_a_share_of()
    {
        Split().Build().CapexRatio.ShouldBeNull();

        Split().AddEffort(Buckets.Build, 1m, 100m).Build().CapexRatio.ShouldBe(1m);
    }

    [Fact]
    public void Zero_hours_and_zero_amounts_change_nothing()
    {
        var totals = Split()
            .AddEffort(Buckets.Build, 0m, 0m)
            .AddManualCost("build", 0m, 0m, 0m)
            .Build();

        totals.TotalHours.ShouldBe(0m);
        totals.EffortValued.ShouldBeFalse();
    }

    private static CapexOpexRule Rule() => CapexOpexRule.Default(Department);

    private static CapexOpexSplit Split() => new(Rule());
}
