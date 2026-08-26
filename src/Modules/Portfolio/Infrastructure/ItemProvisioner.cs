using Cracra.BuildingBlocks.Mediator;
using Cracra.Modules.Portfolio.Application;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Portfolio.Domain;

namespace Cracra.Modules.Portfolio.Infrastructure;

/// <summary>
/// The provisioning contract, routed through the same command the wizard uses.
/// </summary>
/// <remarks>
/// Going through the command rather than the repository is what keeps a converted problem and a hand-created item
/// identical: same validation, same code derivation, same transaction behaviour. A second creation path would be
/// a second set of rules for anybody to fall out of step with.
/// </remarks>
internal sealed class ItemProvisioner(ISender sender) : IPortfolioItemProvisioner
{
    public async Task<Guid> CreateAsync(
        string name,
        string? summary,
        string? type,
        string? category,
        Guid ownerNodeId,
        CancellationToken ct) =>
        await sender.Send(
            new CreateItemCommand(
                name,
                type ?? ItemTypes.Project,
                null,
                category,
                null,
                ownerNodeId,
                null,
                null,
                summary,
                null,
                null,
                100,
                null),
            ct);
}
