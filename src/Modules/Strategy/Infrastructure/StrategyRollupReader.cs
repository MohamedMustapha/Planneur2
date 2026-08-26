using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Strategy.Contracts;
using Cracra.Modules.Strategy.Data;
using Cracra.Modules.Strategy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Strategy.Infrastructure;

/// <summary>
/// The embeddable rollup (v2 §06.5).
/// </summary>
/// <remarks>
/// <para>
/// One implementation, consumed by the COPIL minutes (§07) and by node reports (S8). Neither recomputes anything:
/// a CR whose strategy block disagreed with the strategy screen it was written from would be worse than no block
/// at all, because somebody would act on the wrong one.
/// </para>
/// <para>
/// Caller-scoped, so the block a minute embeds is the block its reader may see. A member reading a published CR
/// gets whatever RLS lets through, which for a service-level COPIL is generally the whole thing.
/// </para>
/// </remarks>
internal sealed class StrategyRollupReader(
    StrategyDbContext context,
    IOrgNodeReader nodes,
    TimeProvider clock) : IStrategyRollupReader
{
    /// <summary>How many objectives a block names before it stops being a summary.</summary>
    private const int AttentionLimit = 3;

    public async Task<IReadOnlyList<StrategySummaryBlock>> GetBlocksForNodeAsync(
        Guid nodeId,
        CancellationToken ct)
    {
        // The node and everything under it. A COPIL at a service reviews the strategies its bureaux set as well
        // as its own, and the subtree is what says which those are without the code naming a level.
        var subtree = await nodes.GetSubtreeAsync(nodeId, ct);
        var scopeIds = subtree.Select(node => node.Id).Append(nodeId).ToHashSet();

        var strategies = await context.Strategies
            .AsNoTracking()
            .Include(strategy => strategy.Objectives)
            .Where(strategy => strategy.Status == StrategyStatuses.Active)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        return
        [
            .. strategies
                .Where(strategy => scopeIds.Contains(strategy.ScopeId))
                .OrderBy(strategy => strategy.Title, StringComparer.Ordinal)
                .Select(strategy => Block(strategy, today)),
        ];
    }

    public async Task<IReadOnlyList<ObjectiveLink>> GetObjectivesForSourceAsync(
        string sourceType,
        Guid sourceId,
        CancellationToken ct)
    {
        var objectiveIds = await context.Contributions
            .AsNoTracking()
            .Where(contribution => contribution.SourceType == sourceType && contribution.SourceId == sourceId)
            .Select(contribution => contribution.ObjectiveId)
            .ToListAsync(ct);

        if (objectiveIds.Count == 0)
        {
            return [];
        }

        var objectives = await context.Objectives
            .AsNoTracking()
            .Where(objective => objectiveIds.Contains(objective.Id))
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        return
        [
            .. objectives.Select(objective => new ObjectiveLink(
                objective.Id,
                objective.StrategyId,
                objective.Title,
                ObjectiveMath.Derive(objective, today),
                ObjectiveMath.Progress(objective))),
        ];
    }

    private static StrategySummaryBlock Block(StrategyPlan strategy, DateOnly today)
    {
        var statuses = strategy.Objectives
            .Select(objective => new
            {
                objective.Title,
                Status = ObjectiveMath.Derive(objective, today),
            })
            .ToList();

        return new StrategySummaryBlock(
            strategy.Id,
            strategy.Title,
            strategy.Progress(),
            statuses.Count,
            statuses.Count(entry => entry.Status == ObjectiveStatuses.OnTrack),
            statuses.Count(entry => entry.Status == ObjectiveStatuses.AtRisk),
            statuses.Count(entry => entry.Status == ObjectiveStatuses.OffTrack),
            statuses.Count(entry => entry.Status == ObjectiveStatuses.Done),
            [
                // What a COPIL should talk about, worst first and capped. A block that listed every objective
                // would be the strategy screen pasted into a minute, which is the thing the block replaces.
                .. statuses
                    .Where(entry => entry.Status is ObjectiveStatuses.OffTrack or ObjectiveStatuses.AtRisk)
                    .OrderBy(entry => entry.Status == ObjectiveStatuses.OffTrack ? 0 : 1)
                    .ThenBy(entry => entry.Title, StringComparer.Ordinal)
                    .Take(AttentionLimit)
                    .Select(entry => entry.Title),
            ]);
    }
}
