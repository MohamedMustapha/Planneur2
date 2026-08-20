using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Activities.Domain;

namespace Cracra.Tests.Unit.Activities;

/// <summary>
/// Taxonomy resolution from department configuration.
/// </summary>
/// <remarks>
/// This is the entire mechanism behind "department-agnostic": HR and Finance adopt the platform by editing a JSON
/// blob, not by anyone writing an <c>if (department == …)</c>. It is also the code most exposed to bad input,
/// since a human types that blob into a settings screen.
/// </remarks>
public sealed class ActivityTaxonomyTests
{
    [Fact]
    public void A_department_that_has_configured_nothing_still_gets_the_canonical_buckets()
    {
        var taxonomy = ActivityTaxonomy.Resolve(null);

        taxonomy.Types.Select(type => type.Code).ShouldBe(
            ["project-build", "project-run", "quality-of-life", "recruitment-admin"],
            ignoreOrder: true);
    }

    [Fact]
    public void The_two_project_buckets_require_a_project_and_the_others_do_not()
    {
        var taxonomy = ActivityTaxonomy.Resolve("{}");

        taxonomy.Get("project-build").RequiresProject.ShouldBeTrue();
        taxonomy.Get("project-run").RequiresProject.ShouldBeTrue();

        // Recruitment and quality-of-life work is real and belongs to nobody's project.
        taxonomy.Get("quality-of-life").RequiresProject.ShouldBeFalse();
        taxonomy.Get("recruitment-admin").RequiresProject.ShouldBeFalse();
    }

    [Fact]
    public void A_department_can_add_subtypes()
    {
        var taxonomy = ActivityTaxonomy.Resolve("""
            {"types":[
              {"code":"payroll-run","parent":"recruitment-admin","labelKey":"hr.payroll"},
              {"code":"hiring-campaign","parent":"recruitment-admin","labelKey":"hr.hiring"}
            ]}
            """);

        taxonomy.Get("payroll-run").ParentCode.ShouldBe("recruitment-admin");
        taxonomy.Get("hiring-campaign").LabelKey.ShouldBe("hr.hiring");

        // And the canonical buckets survive the addition.
        taxonomy.Contains("project-build").ShouldBeTrue();
    }

    [Fact]
    public void A_department_can_relabel_a_canonical_bucket_without_renaming_it()
    {
        var taxonomy = ActivityTaxonomy.Resolve("""
            {"types":[{"code":"project-run","labelKey":"finance.maintenance"}]}
            """);

        // The label moves, the code does not. S8 compares BUILD against RUN across departments and can only do
        // that while the codes mean the same thing everywhere.
        taxonomy.Get("project-run").LabelKey.ShouldBe("finance.maintenance");
        taxonomy.Get("project-run").Code.ShouldBe("project-run");
    }

    [Fact]
    public void A_subtype_inherits_its_parents_project_requirement()
    {
        var taxonomy = ActivityTaxonomy.Resolve("""
            {"types":[{"code":"sprint-work","parent":"project-build","requiresProject":false}]}
            """);

        // The department asked for false and does not get it: what project-build means is the platform's, not
        // theirs, and a subtype that escapes the requirement would let untracked hours into project costs.
        taxonomy.Get("sprint-work").RequiresProject.ShouldBeTrue();
    }

    [Fact]
    public void A_configured_type_gets_a_generated_label_key_when_none_is_given()
    {
        var taxonomy = ActivityTaxonomy.Resolve("""{"types":[{"code":"on-call"}]}""");

        taxonomy.Get("on-call").LabelKey.ShouldBe("activity.type.on-call");
    }

    [Fact]
    public void Codes_are_matched_without_regard_to_case()
    {
        ActivityTaxonomy.Resolve(null).Get("PROJECT-BUILD").Code.ShouldBe("project-build");
    }

    [Fact]
    public void Malformed_configuration_falls_back_to_canonical_rather_than_failing()
    {
        // One bad edit in department settings must not stop a whole department logging their week.
        foreach (var broken in new[] { "not json at all", "[]", """{"types":"nonsense"}""", "" })
        {
            ActivityTaxonomy.Resolve(broken).Contains("project-build").ShouldBeTrue();
        }
    }

    [Fact]
    public void An_unknown_code_is_refused_rather_than_invented()
    {
        Should.Throw<DomainRuleViolationException>(() => ActivityTaxonomy.Resolve(null).Get("holiday-planning"));
    }
}
