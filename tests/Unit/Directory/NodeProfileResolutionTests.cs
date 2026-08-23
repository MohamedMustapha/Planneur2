using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Domain;

namespace Cracra.Tests.Unit.Directory;

/// <summary>
/// The inheritance walk from v2 §10.1.
/// </summary>
/// <remarks>
/// This is the rule the whole slice rests on, and its failures are quiet ones: a walk that stopped one level early
/// does not throw, it hands a branch somebody else's board and the wrong activity subtypes, and the first person
/// to notice is whoever tries to log their week. So the cases below are deliberately the boring ones — a chain of
/// one, a chain with a gap, a chain where the near end says almost nothing.
/// </remarks>
public sealed class NodeProfileResolutionTests
{
    [Fact]
    public void An_empty_chain_resolves_to_nothing()
    {
        // Not an empty profile. A deployment that has attached nothing must fall through to platform behaviour,
        // and handing back a profile with every field blank would instead assert that the branch has no boards,
        // no item types and no vocabulary.
        NodeProfileResolution.Resolve([]).ShouldBeNull();
    }

    [Fact]
    public void A_single_profile_supplies_every_field()
    {
        var resolved = NodeProfileResolution.Resolve([Full("DELIVERY")]);

        resolved.ShouldNotBeNull();
        resolved.SourceCode.ShouldBe("DELIVERY");
        resolved.BoardArchetypes.ShouldBe(["task-progress"]);
        resolved.ItemTypes.ShouldBe(["project"]);
        resolved.SolvesCategories.ShouldBe(["tooling"]);
        resolved.HeadlinePattern.ShouldBe("{people} pers");
        resolved.ActivityTaxonomyJson.ShouldContain("dev");
        resolved.BudgetDefaultsJson.ShouldContain("capex");
    }

    [Fact]
    public void The_nearest_profile_names_the_result_even_when_it_sets_almost_nothing()
    {
        // The distinction v2 §10.1 draws between effective_profile and effective_field. A unit that overrode only
        // the board is still "running the BOARD-ONLY profile" as far as an administrator is concerned, and
        // labelling it with the ancestor's name would make the attachment they just made invisible to them.
        var resolved = NodeProfileResolution.Resolve([BoardOnly("BOARD-ONLY"), Full("DELIVERY")]);

        resolved.ShouldNotBeNull();
        resolved.SourceCode.ShouldBe("BOARD-ONLY");
        resolved.LabelKey.ShouldBe("profile.board-only");
    }

    [Fact]
    public void An_override_replaces_one_field_and_inherits_the_rest()
    {
        var resolved = NodeProfileResolution.Resolve([BoardOnly("BOARD-ONLY"), Full("DELIVERY")]);

        resolved.ShouldNotBeNull();

        // Its own board...
        resolved.BoardArchetypes.ShouldBe(["work-orders"]);

        // ...and everything else from above. This is what makes a partial override worth having: a unit that
        // wants a different board should not have to restate its parent's taxonomy to get one, because a restated
        // copy is one that silently stops tracking the parent the next time the parent changes.
        resolved.ItemTypes.ShouldBe(["project"]);
        resolved.ActivityTaxonomyJson.ShouldContain("dev");
        resolved.HeadlinePattern.ShouldBe("{people} pers");
    }

    [Fact]
    public void A_level_that_says_nothing_is_simply_absent_from_the_chain()
    {
        // The "inheritance across a skipped level" case. A middle node with no profile attached contributes no
        // entry, so a grandchild inherits from its grandparent with no special handling anywhere — which is the
        // property that lets the same code serve a three-level org and a six-level one.
        var resolved = NodeProfileResolution.Resolve([BoardOnly("LEAF"), Full("ROOT")]);

        resolved.ShouldNotBeNull();
        resolved.ActivityTaxonomyJson.ShouldContain("dev");
    }

    [Fact]
    public void The_first_non_null_wins_field_by_field_across_three_levels()
    {
        var middle = new NodeProfile
        {
            Id = Guid.CreateVersion7(),
            Code = "MIDDLE",
            LabelKey = "profile.middle",
            ItemTypes = ["run-service"],
            HeadlinePattern = "{hours} h",
        };

        var resolved = NodeProfileResolution.Resolve([BoardOnly("LEAF"), middle, Full("ROOT")]);

        resolved.ShouldNotBeNull();
        resolved.BoardArchetypes.ShouldBe(["work-orders"]);      // leaf
        resolved.ItemTypes.ShouldBe(["run-service"]);            // middle
        resolved.HeadlinePattern.ShouldBe("{hours} h");          // middle
        resolved.ActivityTaxonomyJson.ShouldContain("dev");      // root
    }

    [Fact]
    public void Capabilities_default_to_on_where_nothing_in_the_chain_mentions_them()
    {
        // Permissive by omission, deliberately. A profile authored before a capability existed must not switch it
        // off for everyone pointing at it the moment the capability ships.
        var resolved = NodeProfileResolution.Resolve([BoardOnly("BOARD-ONLY")]);

        resolved.ShouldNotBeNull();
        resolved.Allows(NodeCapabilities.Kudos).ShouldBeTrue();
        resolved.Allows(NodeCapabilities.Integrations).ShouldBeTrue();
    }

    [Fact]
    public void A_capability_switched_off_nearest_wins()
    {
        var leaf = new NodeProfile
        {
            Id = Guid.CreateVersion7(),
            Code = "QUIET",
            LabelKey = "profile.quiet",
            CapabilitiesJson = """{"integrations":false}""",
        };

        var resolved = NodeProfileResolution.Resolve([leaf, Full("ROOT")]);

        resolved.ShouldNotBeNull();
        resolved.Allows(NodeCapabilities.Integrations).ShouldBeFalse();

        // The whole blob is one field, so switching one capability off does not inherit the ancestor's answers for
        // the others — they fall back to the platform defaults instead. Worth pinning: the alternative, merging
        // capability maps key by key up the chain, is a defensible design but not the one §10.1 specifies, and
        // the difference is invisible until someone's ancestor switches something off.
        resolved.Allows(NodeCapabilities.Budget).ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_capability_key_is_ignored_rather_than_honoured()
    {
        var leaf = new NodeProfile
        {
            Id = Guid.CreateVersion7(),
            Code = "TYPO",
            LabelKey = "profile.typo",
            CapabilitiesJson = """{"integrationz":false,"kudos":false}""",
        };

        var resolved = NodeProfileResolution.Resolve([leaf]);

        resolved.ShouldNotBeNull();
        resolved.Allows("integrationz").ShouldBeFalse();          // never granted by a typo
        resolved.Allows(NodeCapabilities.Integrations).ShouldBeTrue();  // and the real one is untouched
        resolved.Allows(NodeCapabilities.Kudos).ShouldBeFalse();       // the correctly-spelled one still applies
    }

    [Fact]
    public void Malformed_capabilities_fall_back_to_the_defaults()
    {
        // Degrade, do not fail. One bad edit in an admin form should cost a branch its refinement, not its UI.
        var leaf = new NodeProfile
        {
            Id = Guid.CreateVersion7(),
            Code = "BROKEN",
            LabelKey = "profile.broken",
            CapabilitiesJson = "{ not json",
        };

        var resolved = NodeProfileResolution.Resolve([leaf]);

        resolved.ShouldNotBeNull();
        resolved.Allows(NodeCapabilities.Kudos).ShouldBeTrue();
    }

    private static NodeProfile Full(string code) => new()
    {
        Id = Guid.CreateVersion7(),
        Code = code,
        LabelKey = $"profile.{code.ToLowerInvariant()}",
        ActivityTaxonomyJson = """{"types":[{"code":"dev","parent":"project-build"}]}""",
        BoardArchetypes = ["task-progress"],
        ItemTypes = ["project"],
        CapabilitiesJson = """{"integrations":true,"budget":true}""",
        SolvesCategories = ["tooling"],
        BudgetDefaultsJson = """{"project-build":"capex"}""",
        HeadlinePattern = "{people} pers",
    };

    /// <summary>A profile that sets exactly one field. Everything else is a hole for an ancestor to fill.</summary>
    private static NodeProfile BoardOnly(string code) => new()
    {
        Id = Guid.CreateVersion7(),
        Code = code,
        LabelKey = $"profile.{code.ToLowerInvariant()}",
        BoardArchetypes = ["work-orders"],
    };
}
