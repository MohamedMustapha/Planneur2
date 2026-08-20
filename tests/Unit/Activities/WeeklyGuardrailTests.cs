using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Activities.Domain;

namespace Cracra.Tests.Unit.Activities;

/// <summary>
/// The 35-hour week.
/// </summary>
/// <remarks>
/// The asymmetry between soft and hard is the whole design, so it gets tested from both sides. A platform that
/// refuses to record a 41-hour week does not enforce the limit — it stops knowing the limit was broken, which is
/// strictly worse than a warning nobody can miss.
/// </remarks>
public sealed class WeeklyGuardrailTests
{
    private const decimal Target = 35m;

    [Fact]
    public void A_week_within_target_says_nothing()
    {
        var verdict = WeeklyGuardrail.Check(alreadyRecordedHours: 28m, incomingHours: 7m, Target, enforce: false);

        verdict.Outcome.ShouldBe(GuardrailOutcome.Within);
        verdict.Overtime.ShouldBe(0m);
        verdict.IsOvertime.ShouldBeFalse();
    }

    [Fact]
    public void Landing_exactly_on_target_is_within_it()
    {
        // 35 is the target, not the first hour over it. An off-by-one here would warn every full-time week.
        WeeklyGuardrail.Check(34m, 1m, Target, enforce: false).Outcome.ShouldBe(GuardrailOutcome.Within);
    }

    [Fact]
    public void Going_over_warns_by_default_and_records_the_overtime()
    {
        var verdict = WeeklyGuardrail.Check(34m, 7m, Target, enforce: false);

        verdict.Outcome.ShouldBe(GuardrailOutcome.Warned);
        verdict.RecordedHours.ShouldBe(41m);
        verdict.Overtime.ShouldBe(6m);
    }

    [Fact]
    public void A_soft_warning_does_not_throw()
    {
        var verdict = WeeklyGuardrail.Check(34m, 7m, Target, enforce: false);

        // The hour is recorded. That is the point: overtime is flagged, never silently dropped.
        Should.NotThrow(() => WeeklyGuardrail.Enforce(verdict));
    }

    [Fact]
    public void A_department_that_enforces_blocks_instead()
    {
        var verdict = WeeklyGuardrail.Check(34m, 7m, Target, enforce: true);

        verdict.Outcome.ShouldBe(GuardrailOutcome.Blocked);

        Should.Throw<DomainRuleViolationException>(() => WeeklyGuardrail.Enforce(verdict));
    }

    [Fact]
    public void An_enforcing_department_still_allows_a_week_within_target()
    {
        var verdict = WeeklyGuardrail.Check(30m, 5m, Target, enforce: true);

        verdict.Outcome.ShouldBe(GuardrailOutcome.Within);
        Should.NotThrow(() => WeeklyGuardrail.Enforce(verdict));
    }

    [Fact]
    public void A_department_can_set_a_different_target()
    {
        // The 35 is French statutory, not a platform constant. A department on a 39-hour week configures one.
        WeeklyGuardrail.Check(36m, 2m, targetHours: 39m, enforce: false).Outcome.ShouldBe(GuardrailOutcome.Within);
    }

    [Fact]
    public void A_target_of_zero_opts_out_rather_than_blocking_everything()
    {
        // Reading a zero as "no hours allowed" would make a misconfigured department unable to log anything at all.
        var verdict = WeeklyGuardrail.Check(0m, 8m, targetHours: 0m, enforce: true);

        verdict.Outcome.ShouldBe(GuardrailOutcome.Within);
        Should.NotThrow(() => WeeklyGuardrail.Enforce(verdict));
    }
}
