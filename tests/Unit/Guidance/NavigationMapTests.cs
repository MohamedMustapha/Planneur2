using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Guidance;
using Cracra.Modules.Guidance.Services;

namespace Cracra.Tests.Unit.Guidance;

/// <summary>
/// The role-to-intent map of v2 §02.1.
/// </summary>
/// <remarks>
/// Two rules, and both rot silently. The first is that a head is one role rendered by tree position, never three
/// roles — get that wrong and adding a fifth level means editing the nav. The second is that a hidden section is
/// *absent*: a member who can see a Budget entry they cannot open has been told the platform is denying them
/// something, which is not what is happening.
/// </remarks>
public sealed class NavigationMapTests
{
    [Fact]
    public void A_member_lands_on_their_own_week()
    {
        var shell = NavigationMap.For(Position([ContextualRole.Member]));

        shell.Position.ShouldBe(ShellPositions.Member);
        shell.LandingId.ShouldBe(ShellSections.Week);
        shell.Primary.ShouldBe([ShellSections.Week, ShellSections.Problems, ShellSections.Kudos]);
    }

    [Fact]
    public void A_member_is_shown_no_governance_at_all()
    {
        var shell = NavigationMap.For(Position([ContextualRole.Member]));

        // Hidden, not disabled, and not merely one click away: these are not part of their work.
        string[] hidden = [ShellSections.Finance, ShellSections.Admin, ShellSections.Reports];

        foreach (var section in hidden)
        {
            shell.Primary.ShouldNotContain(section);
            shell.Secondary.ShouldNotContain(section);
        }
    }

    [Fact]
    public void What_a_member_may_read_but_does_not_work_in_stays_one_click_away()
    {
        var shell = NavigationMap.For(Position([ContextualRole.Member]));

        // Hiding these outright would contradict the slices that came after 02: item discovery is org-wide so
        // duplicates surface, a CR is read by everybody the meeting reached, and a member reads the objectives
        // their work serves. Reachable, but not in their face.
        foreach (var section in new[] { ShellSections.Portfolio, ShellSections.Meetings, ShellSections.Strategy })
        {
            shell.Primary.ShouldNotContain(section);
            shell.Secondary.ShouldContain(section);
        }
    }

    [Theory]
    [InlineData(false, false, ShellPositions.HeadLeaf)]
    [InlineData(true, false, ShellPositions.HeadBranch)]
    [InlineData(true, true, ShellPositions.HeadTop)]
    [InlineData(false, true, ShellPositions.HeadTop)]
    public void One_head_role_is_rendered_by_tree_position(bool hasChildren, bool isRoot, string expected)
    {
        // The app asks "does my node have children", never "am I a bureau head". Adding a fifth level changes
        // nothing here, which is the whole claim of §01.2.
        NavigationMap
            .PositionFor([ContextualRole.Member, ContextualRole.NodeHead], true, hasChildren, isRoot)
            .ShouldBe(expected);
    }

    [Fact]
    public void A_head_lands_on_the_screen_that_compares_what_is_beneath_them()
    {
        foreach (var position in new[] { ShellPositions.HeadLeaf, ShellPositions.HeadBranch, ShellPositions.HeadTop })
        {
            NavigationMap.For(position).LandingId.ShouldBe(ShellSections.Node);
        }
    }

    [Fact]
    public void A_head_over_children_is_offered_the_things_a_head_over_people_is_not()
    {
        var branch = NavigationMap.For(ShellPositions.HeadBranch);
        var leaf = NavigationMap.For(ShellPositions.HeadLeaf);

        branch.Primary.ShouldContain(ShellSections.Finance);
        branch.Primary.ShouldContain(ShellSections.Admin);

        leaf.Primary.ShouldNotContain(ShellSections.Admin);
    }

    [Fact]
    public void The_widest_responsibility_wins_where_somebody_wears_two_hats()
    {
        // Being a PMO is the job; being a member is how the payroll describes you. Opening on the personal week
        // would make the first click of every session "navigate away from here".
        NavigationMap
            .PositionFor([ContextualRole.Member, ContextualRole.NodeHead, ContextualRole.Pmo], true, true, false)
            .ShouldBe(ShellPositions.Pmo);

        NavigationMap
            .PositionFor([ContextualRole.Member, ContextualRole.Pmo, ContextualRole.Admin], false, false, false)
            .ShouldBe(ShellPositions.Admin);
    }

    [Fact]
    public void A_head_role_without_a_node_is_not_a_head()
    {
        // The role arrives from a group; the node is what makes it mean anything. Landing them on a node board
        // they head nothing in would be a screen with no rows and no explanation.
        NavigationMap
            .PositionFor([ContextualRole.Member, ContextualRole.NodeHead], false, false, false)
            .ShouldBe(ShellPositions.Member);
    }

    [Fact]
    public void A_capability_removes_a_section_the_position_would_otherwise_offer()
    {
        var shell = NavigationMap.ApplyCapabilities(
            NavigationMap.For(ShellPositions.HeadBranch),
            Profile(new Dictionary<string, bool> { [NodeCapabilities.Budget] = false }));

        // §10.3: being entitled to see budgets does not conjure one for a branch that has none.
        shell.Primary.ShouldNotContain(ShellSections.Finance);
        shell.Primary.ShouldContain(ShellSections.Node);
    }

    [Fact]
    public void A_capability_that_removes_the_landing_moves_it_somewhere_real()
    {
        var shell = NavigationMap.ApplyCapabilities(
            NavigationMap.For(ShellPositions.Pmo),
            Profile(new Dictionary<string, bool>
            {
                [NodeCapabilities.Budget] = false,
                [NodeCapabilities.Strategy] = false,
            }));

        // The portfolio survives, so the landing does. What is being pinned is that the landing is never left
        // pointing at a section that was just removed.
        shell.LandingId.ShouldBe(ShellSections.Portfolio);
        shell.Primary.ShouldNotContain(ShellSections.Strategy);
    }

    [Fact]
    public void No_profile_takes_nothing_away()
    {
        var shell = NavigationMap.For(ShellPositions.HeadBranch);

        NavigationMap.ApplyCapabilities(shell, null).ShouldBe(shell);
    }

    private static string Position(string[] roles) => NavigationMap.PositionFor(roles, false, false, false);

    private static NodeProfileSnapshot Profile(Dictionary<string, bool> capabilities)
    {
        var merged = new Dictionary<string, bool>(NodeCapabilities.Defaults, StringComparer.OrdinalIgnoreCase);

        foreach (var (code, enabled) in capabilities)
        {
            merged[code] = enabled;
        }

        return new NodeProfileSnapshot("TEST", "profile.test", "{}", [], [], merged, [], "{}", null);
    }
}
