using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Problems.Application;

namespace Cracra.Modules.Problems.Infrastructure;

/// <summary>
/// The Portfolio port, implemented against Portfolio's contract.
/// </summary>
/// <remarks>
/// Problems never touches the portfolio schema — an architecture test asserts it cannot even reference that
/// assembly. The call runs on the caller's own connection, so whether they may own an item at that node is
/// Portfolio's answer and its RLS, exactly as if they had used the wizard.
/// </remarks>
internal sealed class PortfolioAdapter(IPortfolioItemProvisioner provisioner) : IPortfolioPort
{
    public async Task<Guid> CreateFromProblemAsync(
        string title,
        string? description,
        string? type,
        string? category,
        Guid ownerNodeId,
        CancellationToken ct) =>
        await provisioner.CreateAsync(title, description, type, category, ownerNodeId, ct);
}
