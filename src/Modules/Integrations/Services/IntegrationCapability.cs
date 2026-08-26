using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;

namespace Cracra.Modules.Integrations.Services;

public interface IIntegrationCapability
{
    /// <summary>Throws where the caller's branch does not do integrations at all (v2 10.3).</summary>
    Task EnsureAvailableAsync(CancellationToken ct);
}

/// <summary>
/// The capability gate, on the way in.
/// </summary>
/// <remarks>
/// <para>
/// v2 10.3 is explicit that a capability which is off means the control is <em>absent</em>, and that absence has
/// to be the server's answer rather than the nav's. A branch whose profile says it does no integrations still had
/// working endpoints until now: the rail hid them and a typed URL did not.
/// </para>
/// <para>
/// A 404 rather than a 403, deliberately. The endpoints do not exist for this branch — there is no permission
/// they could be granted to reach them — and a refusal would send somebody looking for a role to add.
/// </para>
/// <para>
/// In the service layer rather than on each endpoint, so a new endpoint inherits the gate instead of having to
/// remember it.
/// </para>
/// </remarks>
internal sealed class IntegrationCapability(IUserContext user, INodeProfileReader profiles) : IIntegrationCapability
{
    public async Task EnsureAvailableAsync(CancellationToken ct)
    {
        // A background sync runs as the system and is not somebody's branch. It is scoped by the connection rows
        // it reads, which are gated where they were written.
        if (user.Has(ContextualRole.System))
        {
            return;
        }

        if (user.NodeId is not { } node)
        {
            return;
        }

        var profile = await profiles.ResolveForNodeAsync(node, ct);

        // No profile means nothing has been configured, which must not take a feature away.
        if (profile is not null && !profile.Allows(NodeCapabilities.Integrations))
        {
            throw new ResourceNotFoundException("This branch does not use integrations.");
        }
    }
}
