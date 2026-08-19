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

    private static UpdateDepartmentConfigRequest Valid() => new(
        ActivityTaxonomyJson: """{"build":{"labelKey":"activity.build"},"run":{"labelKey":"activity.run"}}""",
        RoleLabelsJson: """{"dev":{"fr":"Développeur","en":"Developer","es":"Desarrollador"}}""",
        KudoRulesJson: """{"mode":"counter","monthlyCap":5}""",
        DefaultBoardLayout: "week",
        IterationPresetsJson: """["1w","2w","1m"]""",
        WeeklyTargetHours: 35m,
        EnforceWeeklyTarget: false);
}
