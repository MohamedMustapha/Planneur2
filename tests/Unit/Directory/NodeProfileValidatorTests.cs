using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Directory.Services;

namespace Cracra.Tests.Unit.Directory;

/// <summary>
/// Validation of an authored profile (v2 §10.6).
/// </summary>
/// <remarks>
/// The rule these tests are really protecting is a negative one: the validator must <em>not</em> know what a
/// sensible taxonomy looks like. A deployment invents its own subtypes, so a validator with opinions about them
/// would be the hardcoded semantics this slice exists to delete. What it may check is shape, and the two things
/// that genuinely cannot work — an archetype no component renders, and a capability nothing reads.
/// </remarks>
public sealed class NodeProfileValidatorTests
{
    [Fact]
    public void Accepts_a_profile_that_sets_nothing_but_its_identity()
    {
        // The most important acceptance case. Every behavioural field null means "inherit everything", which is a
        // legitimate and useful profile — it names a branch without changing how it works.
        Should.NotThrow(() => NodeProfileValidator.Validate(new SaveNodeProfileRequest("CASEWORK", "profile.casework")));
    }

    [Fact]
    public void Accepts_invented_subtypes_the_platform_has_never_heard_of()
    {
        var request = new SaveNodeProfileRequest(
            "CASEWORK",
            "profile.casework",
            ActivityTaxonomyJson: """
                {"types":[
                  {"code":"eligibility-check","parent":"project-run"},
                  {"code":"payment-run","parent":"project-run"},
                  {"code":"appeals","parent":"quality-of-life"}
                ]}
                """);

        Should.NotThrow(() => NodeProfileValidator.Validate(request));
    }

    [Fact]
    public void Rejects_a_taxonomy_that_is_not_an_object()
    {
        var exception = Should.Throw<DomainRuleViolationException>(() => NodeProfileValidator.Validate(
            new SaveNodeProfileRequest("X", "profile.x", ActivityTaxonomyJson: "[]")));

        exception.Message.ShouldContain("ActivityTaxonomyJson");
    }

    [Fact]
    public void Rejects_an_archetype_no_component_renders()
    {
        // Caught at save time on purpose. The alternative is a blank board discovered by whoever opens it on
        // Monday, a long way from the person who typed the name.
        var exception = Should.Throw<DomainRuleViolationException>(() => NodeProfileValidator.Validate(
            new SaveNodeProfileRequest("X", "profile.x", BoardArchetypes: ["gantt"])));

        exception.Message.ShouldContain("gantt");
    }

    [Fact]
    public void Rejects_an_empty_archetype_list_because_that_is_not_how_you_inherit()
    {
        // An empty array and a null mean different things, and only one of them is reachable by accident. Saying
        // so is kinder than storing an override that hides the parent's boards and shows none of its own.
        Should.Throw<DomainRuleViolationException>(() => NodeProfileValidator.Validate(
            new SaveNodeProfileRequest("X", "profile.x", BoardArchetypes: [])));
    }

    [Fact]
    public void Rejects_a_capability_nothing_reads()
    {
        // Strict on write, lenient on read. The same typo at read time would silently do nothing, and an
        // administrator would spend an afternoon wondering why switching it off changed no screen.
        var exception = Should.Throw<DomainRuleViolationException>(() => NodeProfileValidator.Validate(
            new SaveNodeProfileRequest("X", "profile.x", CapabilitiesJson: """{"integrationz":false}""")));

        exception.Message.ShouldContain("integrationz");
        exception.Message.ShouldContain("integrations");
    }

    [Fact]
    public void Rejects_a_capability_that_is_not_a_boolean()
    {
        Should.Throw<DomainRuleViolationException>(() => NodeProfileValidator.Validate(
            new SaveNodeProfileRequest("X", "profile.x", CapabilitiesJson: """{"kudos":"yes"}""")));
    }

    [Fact]
    public void Requires_a_code_and_a_label()
    {
        Should.Throw<DomainRuleViolationException>(() => NodeProfileValidator.Validate(
            new SaveNodeProfileRequest("  ", "profile.x")));

        Should.Throw<DomainRuleViolationException>(() => NodeProfileValidator.Validate(
            new SaveNodeProfileRequest("X", "  ")));
    }
}
