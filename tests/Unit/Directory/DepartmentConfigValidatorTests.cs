using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Directory.Services;

namespace Cracra.Tests.Unit.Directory;

/// <summary>
/// Config validation. This is the one write path a department head drives directly, so the failure modes are the
/// ones a real person will meet — and a rejected save with a clear reason beats a stored value that breaks a board.
/// </summary>
public sealed class DepartmentConfigValidatorTests
{
    [Fact]
    public void Accepts_a_well_formed_configuration()
    {
        Should.NotThrow(() => DepartmentConfigValidator.Validate(Valid()));
    }

    [Fact]
    public void Rejects_a_taxonomy_that_is_not_an_object()
    {
        // Valid JSON, wrong shape. S5 indexes into the taxonomy by key; an array would break it at render time,
        // far away from the person who typed it.
        var exception = Should.Throw<DomainRuleViolationException>(
            () => DepartmentConfigValidator.Validate(Valid() with { ActivityTaxonomyJson = "[]" }));

        exception.Message.ShouldContain("ActivityTaxonomyJson");
    }

    [Fact]
    public void Rejects_malformed_json()
    {
        var exception = Should.Throw<DomainRuleViolationException>(
            () => DepartmentConfigValidator.Validate(Valid() with { KudoRulesJson = "{ not json" }));

        exception.Message.ShouldContain("KudoRulesJson");
    }

    [Fact]
    public void Rejects_iteration_presets_that_are_not_an_array()
    {
        Should.Throw<DomainRuleViolationException>(
            () => DepartmentConfigValidator.Validate(Valid() with { IterationPresetsJson = """{"1w":true}""" }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(61)]
    public void Rejects_an_implausible_weekly_target(decimal hours)
    {
        Should.Throw<DomainRuleViolationException>(
            () => DepartmentConfigValidator.Validate(Valid() with { WeeklyTargetHours = hours }));
    }

    [Theory]
    [InlineData(35)]
    [InlineData(39)]
    [InlineData(60)]
    public void Accepts_a_plausible_weekly_target(decimal hours)
    {
        // 35 is the default, but the platform is department-agnostic: a department on a 39-hour week is a
        // configuration, not a special case in code.
        Should.NotThrow(() => DepartmentConfigValidator.Validate(Valid() with { WeeklyTargetHours = hours }));
    }

    [Fact]
    public void Rejects_a_blank_board_layout()
    {
        Should.Throw<DomainRuleViolationException>(
            () => DepartmentConfigValidator.Validate(Valid() with { DefaultBoardLayout = "  " }));
    }

    [Fact]
    public void Accepts_an_empty_working_day_as_meaning_the_defaults()
    {
        // The value every row holds before a department configures one, and the value the migration backfilled.
        // If this threw, no existing configuration could be saved again without editing a field nobody set.
        Should.NotThrow(() => DepartmentConfigValidator.Validate(Valid() with { WorkingDayJson = "{}" }));
    }

    [Fact]
    public void Accepts_a_working_day_that_is_configured_end_to_end()
    {
        Should.NotThrow(() => DepartmentConfigValidator.Validate(Valid() with { WorkingDayJson = WorkingDay() }));
    }

    [Fact]
    public void Rejects_a_session_that_ends_before_it_starts()
    {
        var exception = Should.Throw<DomainRuleViolationException>(
            () => DepartmentConfigValidator.Validate(Valid() with
            {
                WorkingDayJson = WorkingDay(afternoonStart: "18:00", afternoonEnd: "14:00"),
            }));

        exception.Message.ShouldContain("afternoon");
    }

    [Fact]
    public void Rejects_a_session_that_falls_outside_the_working_day()
    {
        // The rule that matters most: S5 ignores an incoherent day and falls back to the platform default, so a
        // configuration accepted here would save cleanly and then quietly do nothing.
        Should.Throw<DomainRuleViolationException>(
            () => DepartmentConfigValidator.Validate(Valid() with
            {
                WorkingDayJson = WorkingDay(dayEnd: "17:00"),
            }));
    }

    [Fact]
    public void Rejects_a_morning_that_overlaps_the_afternoon()
    {
        Should.Throw<DomainRuleViolationException>(
            () => DepartmentConfigValidator.Validate(Valid() with
            {
                WorkingDayJson = WorkingDay(morningEnd: "15:00"),
            }));
    }

    [Fact]
    public void Rejects_a_working_day_missing_a_session()
    {
        Should.Throw<DomainRuleViolationException>(
            () => DepartmentConfigValidator.Validate(Valid() with
            {
                WorkingDayJson = """{"dayStart":"06:00","dayEnd":"20:00"}""",
            }));
    }

    [Fact]
    public void Rejects_a_time_that_is_not_a_time()
    {
        var exception = Should.Throw<DomainRuleViolationException>(
            () => DepartmentConfigValidator.Validate(Valid() with
            {
                WorkingDayJson = WorkingDay(dayStart: "morning"),
            }));

        exception.Message.ShouldContain("dayStart");
    }

    private static string WorkingDay(
        string dayStart = "06:00",
        string dayEnd = "20:00",
        string morningStart = "09:00",
        string morningEnd = "13:00",
        string afternoonStart = "14:00",
        string afternoonEnd = "18:00") =>
        $$"""
        {
          "dayStart": "{{dayStart}}",
          "dayEnd": "{{dayEnd}}",
          "morning": { "start": "{{morningStart}}", "end": "{{morningEnd}}" },
          "afternoon": { "start": "{{afternoonStart}}", "end": "{{afternoonEnd}}" }
        }
        """;

    private static UpdateDepartmentConfigRequest Valid() => new(
        ActivityTaxonomyJson: """{"build":{"labelKey":"activity.build"},"run":{"labelKey":"activity.run"}}""",
        RoleLabelsJson: """{"dev":{"fr":"Développeur","en":"Developer","es":"Desarrollador"}}""",
        KudoRulesJson: """{"mode":"counter","monthlyCap":5}""",
        DefaultBoardLayout: "week",
        IterationPresetsJson: """["1w","2w","1m"]""",
        WeeklyTargetHours: 35m,
        EnforceWeeklyTarget: false);
}
