using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;

namespace Cracra.Modules.Guidance.Services;

/// <summary>The role-to-intent map of v2 02.1, as a pure function of position.</summary>
public static class NavigationMap
{
    public static string PositionFor(
        IReadOnlyList<string> roles,
        bool headsANode,
        bool headedNodeHasChildren,
        bool headedNodeIsRoot)
    {
        ArgumentNullException.ThrowIfNull(roles);

        if (roles.Contains(ContextualRole.Admin))
        {
            return ShellPositions.Admin;
        }

        if (roles.Contains(ContextualRole.Pmo))
        {
            return ShellPositions.Pmo;
        }

        if (roles.Contains(ContextualRole.NodeHead) && headsANode)
        {
            return headedNodeIsRoot
                ? ShellPositions.HeadTop
                : headedNodeHasChildren ? ShellPositions.HeadBranch : ShellPositions.HeadLeaf;
        }

        if (roles.Contains(ContextualRole.ProductOwner) || roles.Contains(ContextualRole.ProjectLead))
        {
            return ShellPositions.ProductOwner;
        }

        return ShellPositions.Member;
    }

    public static ShellNavigation For(string position) => position switch
    {
        ShellPositions.Admin => new ShellNavigation(
            position,
            ShellSections.Admin,
            ShellSections.Admin,
            [
                ShellSections.Admin, ShellSections.Node, ShellSections.Portfolio, ShellSections.Reports,
                ShellSections.Problems, ShellSections.Meetings, ShellSections.Strategy, ShellSections.Finance,
            ],
            [ShellSections.Week, ShellSections.Directory, ShellSections.Kudos]),

        ShellPositions.Pmo => new ShellNavigation(
            position,
            ShellSections.Portfolio,
            ShellSections.Portfolio,
            [ShellSections.Portfolio, ShellSections.Strategy, ShellSections.Meetings, ShellSections.Reports],
            [ShellSections.Problems, ShellSections.Finance, ShellSections.Directory]),

        ShellPositions.HeadTop => new ShellNavigation(
            position,
            ShellSections.Node,
            ShellSections.Node,
            [
                ShellSections.Node, ShellSections.Meetings, ShellSections.Strategy, ShellSections.Reports,
                ShellSections.Finance,
            ],
            [ShellSections.Portfolio, ShellSections.Problems, ShellSections.Directory, ShellSections.Kudos]),

        ShellPositions.HeadBranch => new ShellNavigation(
            position,
            ShellSections.Node,
            ShellSections.Node,
            [
                ShellSections.Node, ShellSections.Portfolio, ShellSections.Finance, ShellSections.Problems,
                ShellSections.Meetings, ShellSections.Reports, ShellSections.Strategy, ShellSections.Admin,
            ],
            [ShellSections.Week, ShellSections.Kudos, ShellSections.Directory]),

        ShellPositions.HeadLeaf => new ShellNavigation(
            position,
            ShellSections.Node,
            ShellSections.Node,
            [
                ShellSections.Week, ShellSections.Node, ShellSections.Portfolio, ShellSections.Reports,
                ShellSections.Problems,
            ],
            [
                ShellSections.Kudos, ShellSections.Meetings, ShellSections.Strategy,
                ShellSections.Directory,
            ]),

        ShellPositions.ProductOwner => new ShellNavigation(
            position,
            ShellSections.Portfolio,
            ShellSections.Portfolio,
            [ShellSections.Portfolio, ShellSections.Node, ShellSections.Meetings, ShellSections.Problems],
            [
                ShellSections.Week, ShellSections.Reports, ShellSections.Strategy,
                ShellSections.Directory,
            ]),

        // Three sections sit in a member's secondary group rather than being hidden outright, because the slices
        // after 02 widened who may read them: the catalog is discoverable org-wide so duplicates surface
        // (01 3.1), a CR is read by everybody the meeting reached (07.6), and a member reads the objectives their
        // work serves (06.5). Hidden means unreachable, and none of those three should be. One click away.
        _ => new ShellNavigation(
            ShellPositions.Member,
            ShellSections.Week,
            ShellSections.Week,
            [ShellSections.Week, ShellSections.Problems, ShellSections.Kudos],
            [
                ShellSections.Node, ShellSections.Portfolio, ShellSections.Meetings,
                ShellSections.Strategy, ShellSections.Directory,
            ]),
    };

    /// <summary>Sections a capability removes outright, whatever the position (v2 10.3).</summary>
    public static ShellNavigation ApplyCapabilities(ShellNavigation navigation, NodeProfileSnapshot? profile)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        bool Allows(string capability) =>
            profile is null || profile.Capabilities.GetValueOrDefault(capability, true);

        var removed = new HashSet<string>(StringComparer.Ordinal);

        if (!Allows(NodeCapabilities.Budget))
        {
            removed.Add(ShellSections.Finance);
        }

        if (!Allows(NodeCapabilities.Kudos))
        {
            removed.Add(ShellSections.Kudos);
        }

        if (!Allows(NodeCapabilities.Strategy))
        {
            removed.Add(ShellSections.Strategy);
        }

        if (removed.Count == 0)
        {
            return navigation;
        }

        var primary = navigation.Primary.Where(id => !removed.Contains(id)).ToArray();
        var secondary = navigation.Secondary.Where(id => !removed.Contains(id)).ToArray();
        var landing = removed.Contains(navigation.LandingId) ? ShellSections.Week : navigation.LandingId;

        return navigation with
        {
            LandingId = landing,
            FocusId = removed.Contains(navigation.FocusId) ? landing : navigation.FocusId,
            Primary = primary,
            Secondary = secondary,
        };
    }
}
