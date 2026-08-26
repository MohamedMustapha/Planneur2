using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Finance.Data;
using Cracra.Modules.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Finance.Services;

public sealed record ConsolidatedRequest(Guid? NodeId, int? FiscalYear, string? Mode, int? Depth);

public interface IConsolidationService
{
    Task<ConsolidatedView> GetAsync(ConsolidatedRequest request, CancellationToken ct);
}

/// <param name="Node">The node the caller landed on, with its children already rolled up.</param>
/// <param name="Landed">
/// True when the server picked the node rather than the caller naming one — the "never starts empty" property
/// §04.2 asks for.
/// </param>
public sealed record ConsolidatedView(
    int FiscalYear,
    string Mode,
    ConsolidatedNode Node,
    bool Landed);

/// <summary>
/// The consolidated view (v2 §04.2).
/// </summary>
/// <remarks>
/// <para>
/// The v1 finance page was empty until you picked a project. This one lands on the highest node the caller heads
/// and drills down, so it always shows something — which is why the node id is optional and why the answer says
/// whether it chose for you.
/// </para>
/// <para>
/// Three reads and one fold: the subtree's nodes, the costs grouped by owning node, and the envelopes. Everything
/// after that is arithmetic, so the rollup invariant holds by construction rather than by two aggregations
/// agreeing — the same reason the node brief is built the way it is.
/// </para>
/// </remarks>
internal sealed class ConsolidationService(
    FinanceDbContext context,
    IOrgNodeReader nodes,
    BuildingBlocks.Web.Users.IUserContext user) : IConsolidationService
{
    public async Task<ConsolidatedView> GetAsync(ConsolidatedRequest request, CancellationToken ct)
    {
        var mode = Normalize(request.Mode);
        var fiscalYear = request.FiscalYear ?? DateTimeOffset.UtcNow.Year;

        var landed = request.NodeId is null;
        var rootId = request.NodeId ?? Highest()
            ?? throw new ResourceNotFoundException("You do not head a node, so there is nothing to consolidate.");

        var subtree = await nodes.GetSubtreeAsync(rootId, ct);

        if (subtree.All(node => node.Id != rootId))
        {
            throw new ResourceNotFoundException($"No node {rootId}.");
        }

        var ids = subtree.Select(node => node.Id).ToArray();

        var own = await OwnCostsAsync(ids, fiscalYear, mode, ct);
        var budgets = await BudgetsAsync(ids, fiscalYear, ct);

        var folded = Consolidation.Fold(
            rootId,
            [.. subtree.Select(node => new ConsolidationNode(node.Id, node.ParentId, node.LevelNo, node.Code, node.Name))],
            own,
            budgets,
            request.Depth ?? int.MaxValue);

        return new ConsolidatedView(fiscalYear, mode, folded, landed);
    }

    /// <summary>
    /// The highest node the caller heads.
    /// </summary>
    /// <remarks>
    /// Highest means nearest the root, which for a person heading several is the one whose path is shortest. The
    /// caller's own path is the tiebreaker's source, so a head of two sibling branches lands on whichever the
    /// session ordered first rather than on an arbitrary one — and either answer is a node they run.
    /// </remarks>
    private Guid? Highest()
    {
        if (user.HeadedNodes.Count == 0)
        {
            return null;
        }

        var path = user.NodePath;

        return user.HeadedNodes
            .OrderBy(node => path.Contains(node) ? path.ToList().IndexOf(node) : int.MaxValue)
            .First();
    }

    private async Task<IReadOnlyDictionary<Guid, NodeOwnCost>> OwnCostsAsync(
        IReadOnlyList<Guid> nodeIds,
        int fiscalYear,
        string mode,
        CancellationToken ct)
    {
        var components = await context.Components
            .AsNoTracking()
            .Where(component => nodeIds.Contains(component.OwnerNodeId))
            .Where(component => component.PeriodStart.Year <= fiscalYear && component.PeriodEnd.Year >= fiscalYear)
            .Select(component => new
            {
                component.OwnerNodeId,
                component.Treatment,
                component.Amount,
                IsItem = component.ItemId != null,
            })
            .ToListAsync(ct);

        var own = new Dictionary<Guid, NodeOwnCost>();

        foreach (var component in components)
        {
            if (!Included(component.Treatment, mode))
            {
                continue;
            }

            var split = CostSplit.Of(component.Treatment, component.Amount);

            var current = own.TryGetValue(component.OwnerNodeId, out var found)
                ? found
                : new NodeOwnCost(component.OwnerNodeId, CostSplit.Zero, CostSplit.Zero);

            own[component.OwnerNodeId] = component.IsItem
                ? current with { Items = current.Items + split }
                : current with { Direct = current.Direct + split };
        }

        return own;
    }

    private async Task<IReadOnlyDictionary<Guid, decimal>> BudgetsAsync(
        IReadOnlyList<Guid> nodeIds,
        int fiscalYear,
        CancellationToken ct) =>
        await context.Budgets
            .AsNoTracking()
            .Where(budget => budget.ScopeType == BudgetScope.Node
                             && nodeIds.Contains(budget.ScopeId)
                             && budget.FiscalYear == fiscalYear)
            .GroupBy(budget => budget.ScopeId)
            .Select(group => new { NodeId = group.Key, Planned = group.Sum(budget => budget.PlannedAmount) })
            .ToDictionaryAsync(entry => entry.NodeId, entry => entry.Planned, ct);

    /// <summary>
    /// Whether a component counts under the requested mode.
    /// </summary>
    /// <remarks>
    /// Excluded stays out of both capex-only and opex-only, and is carried in its own column under "both" rather
    /// than dropped. A total that quietly omits what the rule excluded would not reconcile with the hours the
    /// same period reports, and reconciling those two is the first thing anybody does with this page.
    /// </remarks>
    private static bool Included(string treatment, string mode) => mode switch
    {
        Modes.Capex => treatment == Treatments.Capex,
        Modes.Opex => treatment == Treatments.Opex,
        _ => true,
    };

    private static string Normalize(string? mode)
    {
        var given = mode?.Trim().ToLowerInvariant();

        return given switch
        {
            null or "" => Modes.Both,
            Modes.Capex or Modes.Opex or Modes.Both => given,
            _ => throw new DomainRuleViolationException($"'{mode}' is not a mode. Use capex, opex or both."),
        };
    }
}

public static class Modes
{
    public const string Capex = "capex";
    public const string Opex = "opex";
    public const string Both = "both";

    public static readonly IReadOnlyList<string> All = [Capex, Opex, Both];
}
