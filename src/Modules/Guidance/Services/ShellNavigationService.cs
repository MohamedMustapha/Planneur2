using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;

namespace Cracra.Modules.Guidance.Services;

public interface IShellNavigationService
{
    Task<ShellNavigation> ResolveAsync(CancellationToken ct);
}

internal sealed class ShellNavigationService(
    IUserContext user,
    IOrgNodeReader nodes,
    INodeProfileReader profiles) : IShellNavigationService
{
    private const int MaxHeadedNodesInspected = 8;

    public async Task<ShellNavigation> ResolveAsync(CancellationToken ct)
    {
        var widest = await WidestHeadedAsync(ct);

        var position = NavigationMap.PositionFor(
            user.Roles,
            widest is not null,
            widest?.HasChildren ?? false,
            widest?.IsRoot ?? false);

        var profile = user.NodeId is { } home ? await profiles.ResolveForNodeAsync(home, ct) : null;

        return NavigationMap.ApplyCapabilities(NavigationMap.For(position), profile);
    }

    private async Task<HeadedNode?> WidestHeadedAsync(CancellationToken ct)
    {
        HeadedNode? widest = null;

        foreach (var headed in user.HeadedNodes.Take(MaxHeadedNodesInspected))
        {
            var subtree = await nodes.GetSubtreeAsync(headed, ct);
            var self = subtree.FirstOrDefault(node => node.Id == headed);

            if (self is null)
            {
                continue;
            }

            var candidate = new HeadedNode(
                self.LevelNo,
                // Active children only: a deactivated branch keeps its history and takes nobody, so it must not
                // be what turns a head over people into a head over branches.
                subtree.Any(node => node.ParentId == headed && node.Active),
                self.ParentId is null);

            if (widest is null || candidate.LevelNo < widest.LevelNo)
            {
                widest = candidate;
            }
        }

        return widest;
    }

    private sealed record HeadedNode(int LevelNo, bool HasChildren, bool IsRoot);
}
