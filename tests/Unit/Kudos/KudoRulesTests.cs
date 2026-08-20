using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Kudos.Domain;

namespace Cracra.Tests.Unit.Kudos;

/// <summary>
/// Department rules, merged over the platform's.
/// </summary>
/// <remarks>
/// The same shape as S5's taxonomy tests, and for the same reason: this is configuration written by hand in an
/// admin screen, so the interesting cases are all the ways it can be wrong.
/// </remarks>
public sealed class KudoRulesTests
{
    [Fact]
    public void A_department_that_configured_nothing_gets_the_canonical_categories()
    {
        var rules = KudoRules.Resolve(null);

        rules.Categories.Select(category => category.Code)
            .ShouldBe(["cleanup", "improvement", "initiative", "mentoring", "above-and-beyond"], ignoreOrder: true);
    }

    [Fact]
    public void A_department_that_configured_nothing_counts_rather_than_scores()
    {
        var rules = KudoRules.Default;

        rules.Mode.ShouldBe(KudoMode.Counter);
        rules.ShowsPoints.ShouldBeFalse();
        rules.ShowsLeaderboard.ShouldBeFalse();
    }

    [Fact]
    public void Points_mode_shows_points_but_ranks_nobody()
    {
        var rules = KudoRules.Resolve("""{"mode":"points"}""");

        rules.ShowsPoints.ShouldBeTrue();
        rules.ShowsLeaderboard.ShouldBeFalse();
    }

    [Fact]
    public void The_full_mode_enables_the_leaderboard()
    {
        var rules = KudoRules.Resolve("""{"mode":"points-badges-leaderboard"}""");

        rules.ShowsPoints.ShouldBeTrue();
        rules.ShowsLeaderboard.ShouldBeTrue();
    }

    [Fact]
    public void An_added_category_sits_alongside_the_canonical_ones()
    {
        var rules = KudoRules.Resolve(
            """{"categories":[{"code":"on-call-rescue","labelKey":"is.oncall","points":4}]}""");

        // Merged, not replaced: adding one must not cost a department the vocabulary S8 reports in.
        rules.Contains("on-call-rescue").ShouldBeTrue();
        rules.Contains("cleanup").ShouldBeTrue();
        rules.Get("on-call-rescue").LabelKey.ShouldBe("is.oncall");
    }

    [Fact]
    public void A_relabelled_canonical_category_keeps_its_code_and_its_price()
    {
        var rules = KudoRules.Resolve("""{"categories":[{"code":"cleanup","labelKey":"is.tidying"}]}""");

        rules.Get("cleanup").LabelKey.ShouldBe("is.tidying");
        rules.Get("cleanup").Points.ShouldBe(1);
    }

    [Fact]
    public void A_negative_price_is_clamped_to_zero()
    {
        var rules = KudoRules.Resolve("""{"categories":[{"code":"cleanup","points":-5}]}""");

        // Recognition software must not be usable to take points away from somebody.
        rules.Get("cleanup").Points.ShouldBe(0);
    }

    [Fact]
    public void An_unknown_category_is_refused_by_name()
    {
        var exception = Should.Throw<DomainRuleViolationException>(() => KudoRules.Default.Get("nonsense"));

        exception.Message.ShouldContain("nonsense");
    }

    [Fact]
    public void A_department_may_write_its_own_ladder()
    {
        var rules = KudoRules.Resolve(
            """{"mode":"points","badges":[{"code":"pillar","labelKey":"is.pillar","minPoints":50}]}""");

        // Replaced wholesale, unlike categories: a department raising a threshold must not be left with the old
        // rung sitting next to the new one.
        rules.Badges.ShouldHaveSingleItem().Code.ShouldBe("pillar");
    }

    [Fact]
    public void A_badge_with_no_bar_is_dropped()
    {
        var rules = KudoRules.Resolve("""{"mode":"points","badges":[{"code":"everyone"}]}""");

        // Otherwise a typo in department settings hands the whole unit a badge on their first kudo.
        rules.Badges.ShouldBeEmpty();
    }

    [Fact]
    public void A_configured_cap_replaces_the_default()
    {
        KudoRules.Resolve("""{"monthlyCapPerGiver":3}""").MonthlyCapPerGiver.ShouldBe(3);
    }

    [Fact]
    public void An_absurd_cap_is_clamped_rather_than_refused()
    {
        KudoRules.Resolve("""{"monthlyCapPerGiver":100000}""").MonthlyCapPerGiver
            .ShouldBe(KudoRules.MaximumMonthlyCapPerGiver);
    }

    [Fact]
    public void A_zero_cap_falls_back_to_the_default()
    {
        // Zero would mean a department that had switched recognition off by accident rather than by decision.
        KudoRules.Resolve("""{"monthlyCapPerGiver":0}""").MonthlyCapPerGiver
            .ShouldBe(KudoRules.DefaultMonthlyCapPerGiver);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{"mode":"anarchy"}""")]
    [InlineData("{}")]
    public void Nonsense_configuration_degrades_to_the_platform_default(string configJson)
    {
        var rules = KudoRules.Resolve(configJson);

        // One bad edit in department settings should cost a department its customisation, not its ability to
        // thank anybody.
        rules.Mode.ShouldBe(KudoMode.Counter);
        rules.Contains("initiative").ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_category_falls_back_to_a_generated_label()
    {
        // A department can drop a category it once offered, and the kudos given under it remain — on somebody's
        // review claim, most likely.
        KudoRules.Default.LabelFor("retired-thing").ShouldBe("kudos.category.retired-thing");
    }
}
