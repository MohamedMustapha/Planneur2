using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Strategy.Contracts;
using Cracra.Modules.Strategy.Data;
using Cracra.Modules.Strategy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Strategy.Application;

public sealed record OpenStrategyRequest(
    string? ScopeType,
    Guid? ScopeId,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string Title,
    string? Narrative);

public sealed record AmendStrategyRequest(
    string? Title,
    string? Narrative,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? Status);

public sealed record AddObjectiveRequest(
    string Title,
    string? Description,
    string? MetricKind,
    decimal? Baseline,
    decimal? Target,
    decimal? Current,
    string? Unit,
    DateOnly? Due,
    decimal? Weight);

public sealed record AmendObjectiveRequest(
    string? Title,
    string? Description,
    decimal? Baseline,
    decimal? Target,
    decimal? Current,
    string? Unit,
    DateOnly? Due,
    decimal? Weight,
    string? Status,
    bool ClearStatusOverride);

public sealed record LinkContributionRequest(string? SourceType, Guid SourceId, decimal? Weight, string? Note);

public sealed record AddKeyResultRequest(string Title, decimal Target, decimal? Current);

public interface IStrategyService
{
    Task<IReadOnlyList<StrategyView>> ListAsync(Guid? scopeId, string? status, CancellationToken ct);

    Task<Guid> OpenAsync(OpenStrategyRequest request, CancellationToken ct);

    Task AmendAsync(Guid strategyId, AmendStrategyRequest request, CancellationToken ct);

    Task<StrategyRollup> RollupAsync(Guid strategyId, CancellationToken ct);

    Task<Guid> AddObjectiveAsync(Guid strategyId, AddObjectiveRequest request, CancellationToken ct);

    Task AmendObjectiveAsync(Guid objectiveId, AmendObjectiveRequest request, CancellationToken ct);

    Task RemoveObjectiveAsync(Guid objectiveId, CancellationToken ct);

    Task<Guid> AddKeyResultAsync(Guid objectiveId, AddKeyResultRequest request, CancellationToken ct);

    Task RemoveKeyResultAsync(Guid objectiveId, Guid keyResultId, CancellationToken ct);

    Task<Guid> LinkAsync(Guid objectiveId, LinkContributionRequest request, CancellationToken ct);

    Task UnlinkAsync(Guid objectiveId, Guid contributionId, CancellationToken ct);

    Task<AlignmentGaps> AlignmentAsync(Guid? scopeId, CancellationToken ct);
}

/// <summary>
/// The Strategy module's one service.
/// </summary>
/// <remarks>
/// Thin, like Problems': the arithmetic is in <see cref="ObjectiveMath"/> and the lifecycle is on the aggregate.
/// What is left here is the two things neither can do for itself — resolving where the caller sits, and asking the
/// other modules what the linked work is currently doing so an objective card shows an item's real state rather
/// than a copy of it taken when somebody linked it.
/// </remarks>
internal sealed class StrategyService(
    StrategyDbContext context,
    IOrgNodeReader nodes,
    IDirectoryReader directory,
    IPortfolioFactsPort portfolio,
    IProblemFactsPort problems,
    TimeProvider clock,
    IUserContext user) : IStrategyService
{
    public async Task<IReadOnlyList<StrategyView>> ListAsync(Guid? scopeId, string? status, CancellationToken ct)
    {
        var query = context.Strategies.AsNoTracking().Include(strategy => strategy.Objectives);

        var strategies = await query.ToListAsync(ct);

        if (scopeId is { } scope)
        {
            strategies = [.. strategies.Where(strategy => strategy.ScopeId == scope)];
        }

        if (status is { Length: > 0 } wanted)
        {
            strategies = [.. strategies.Where(strategy => strategy.Status == wanted)];
        }

        return await ViewsAsync(strategies, ct);
    }

    public async Task<Guid> OpenAsync(OpenStrategyRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = request.ScopeId
                    ?? user.NodeId
                    ?? (await nodes.GetHomeScopeAsync(user.UserId, ct))?.NodeId
                    ?? throw new DomainRuleViolationException(
                        "Your session does not say where you sit, so this strategy has nowhere to belong.");

        var today = Today();
        var now = clock.GetUtcNow();

        var strategy = StrategyPlan.Open(
            request.ScopeType ?? StrategyScopeTypes.Node,
            scope,
            request.PeriodFrom ?? today,
            request.PeriodTo ?? today.AddYears(1),
            request.Title,
            request.Narrative,
            user.UserId,
            now);

        context.Strategies.Add(strategy);

        await context.SaveChangesAsync(ct);

        return strategy.Id;
    }

    public async Task AmendAsync(Guid strategyId, AmendStrategyRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var strategy = await LoadAsync(strategyId, ct);

        strategy.Amend(
            request.Title,
            request.Narrative,
            request.PeriodFrom,
            request.PeriodTo,
            request.Status,
            clock.GetUtcNow());

        await context.SaveChangesAsync(ct);
    }

    public async Task<StrategyRollup> RollupAsync(Guid strategyId, CancellationToken ct)
    {
        var strategy = await LoadAsync(strategyId, ct);

        return new StrategyRollup(
            (await ViewsAsync([strategy], ct)).Single(),
            await ObjectiveViewsAsync(strategy, ct));
    }

    public async Task<Guid> AddObjectiveAsync(Guid strategyId, AddObjectiveRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var strategy = await LoadAsync(strategyId, ct);

        var objective = strategy.AddObjective(
            request.Title,
            request.Description,
            request.MetricKind ?? MetricKinds.Number,
            request.Baseline,
            request.Target,
            request.Current,
            request.Unit,
            request.Due,
            request.Weight ?? 1m,
            clock.GetUtcNow());

        await context.SaveChangesAsync(ct);

        return objective.Id;
    }

    public async Task AmendObjectiveAsync(Guid objectiveId, AmendObjectiveRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var strategy = await LoadByObjectiveAsync(objectiveId, ct);
        var objective = strategy.ObjectiveById(objectiveId);
        var now = clock.GetUtcNow();

        objective.Amend(
            request.Title,
            request.Description,
            request.Baseline,
            request.Target,
            request.Unit,
            request.Due,
            request.Weight,
            now);

        if (request.Current is { } reading)
        {
            objective.Measure(reading, now);
        }

        // Clearing comes after setting, so a request that does both ends derived rather than in whichever order
        // the fields happened to be read.
        if (request.Status is { Length: > 0 } wanted)
        {
            objective.OverrideStatus(wanted, now);
        }

        if (request.ClearStatusOverride)
        {
            objective.OverrideStatus(null, now);
        }

        context.Enqueue(new ObjectiveProgressed(objective.Id, objective.Progress(), objective.Status));

        await context.SaveChangesAsync(ct);
    }

    public async Task RemoveObjectiveAsync(Guid objectiveId, CancellationToken ct)
    {
        var strategy = await LoadByObjectiveAsync(objectiveId, ct);

        strategy.RemoveObjective(objectiveId, clock.GetUtcNow());

        await context.SaveChangesAsync(ct);
    }

    public async Task<Guid> AddKeyResultAsync(Guid objectiveId, AddKeyResultRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var strategy = await LoadByObjectiveAsync(objectiveId, ct);

        var keyResult = strategy.ObjectiveById(objectiveId)
            .AddKeyResult(request.Title, request.Target, request.Current ?? 0m, clock.GetUtcNow());

        await context.SaveChangesAsync(ct);

        return keyResult.Id;
    }

    public async Task RemoveKeyResultAsync(Guid objectiveId, Guid keyResultId, CancellationToken ct)
    {
        var strategy = await LoadByObjectiveAsync(objectiveId, ct);

        strategy.ObjectiveById(objectiveId).RemoveKeyResult(keyResultId, clock.GetUtcNow());

        await context.SaveChangesAsync(ct);
    }

    public async Task<Guid> LinkAsync(Guid objectiveId, LinkContributionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var strategy = await LoadByObjectiveAsync(objectiveId, ct);
        var sourceType = request.SourceType ?? ContributionSources.Item;

        await RefuseUnknownSourceAsync(sourceType, request.SourceId, ct);

        var contribution = strategy.ObjectiveById(objectiveId)
            .Link(sourceType, request.SourceId, request.Weight ?? 1m, request.Note, clock.GetUtcNow());

        context.Enqueue(new ContributionLinked(objectiveId, sourceType, request.SourceId));

        await context.SaveChangesAsync(ct);

        return contribution.Id;
    }

    public async Task UnlinkAsync(Guid objectiveId, Guid contributionId, CancellationToken ct)
    {
        var strategy = await LoadByObjectiveAsync(objectiveId, ct);

        var removed = strategy.ObjectiveById(objectiveId).Unlink(contributionId, clock.GetUtcNow());

        context.Enqueue(new ContributionUnlinked(objectiveId, removed.SourceType, removed.SourceId));

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The gaps between intent and execution (v2 §06.2).
    /// </summary>
    /// <remarks>
    /// Both halves are computed from what the caller may see, and that is not a limitation to apologise for: an
    /// alignment view that counted items its reader cannot open would tell a bureau head their branch is
    /// misaligned because of work in a branch they have never heard of.
    /// </remarks>
    public async Task<AlignmentGaps> AlignmentAsync(Guid? scopeId, CancellationToken ct)
    {
        var scope = scopeId
                    ?? user.NodeId
                    ?? (await nodes.GetHomeScopeAsync(user.UserId, ct))?.NodeId
                    ?? throw new DomainRuleViolationException("There is no scope to check alignment against.");

        var strategies = await context.Strategies
            .AsNoTracking()
            .Include(strategy => strategy.Objectives)
                .ThenInclude(objective => objective.Contributions)
            .Where(strategy => strategy.Status != StrategyStatuses.Closed)
            .ToListAsync(ct);

        var objectives = strategies.SelectMany(strategy => strategy.Objectives).ToList();

        var unlinkedObjectives = objectives.Where(objective => objective.Contributions.Count == 0).ToList();

        var linkedItemIds = objectives
            .SelectMany(objective => objective.Contributions)
            .Where(contribution => contribution.SourceType == ContributionSources.Item)
            .Select(contribution => contribution.SourceId)
            .ToHashSet();

        var inScope = await portfolio.GetItemsInScopeAsync(scope, ct);

        return new AlignmentGaps(
            [
                .. unlinkedObjectives.Select(objective => ToView(objective, [], Today())),
            ],
            [
                .. inScope
                    .Where(item => !linkedItemIds.Contains(item.Id))
                    .Select(item => new UnlinkedItem(
                        item.Id,
                        item.Code,
                        item.Name,
                        item.Type,
                        item.State,
                        item.NodeId)),
            ]);
    }

    // --- Projections -----------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<StrategyView>> ViewsAsync(
        IReadOnlyList<StrategyPlan> strategies,
        CancellationToken ct)
    {
        if (strategies.Count == 0)
        {
            return [];
        }

        var names = await directory.GetPersonNamesAsync(
            [.. strategies.Select(strategy => strategy.OwnerPersonId).Distinct()],
            ct);

        var scopes = await ScopeNamesAsync(
            [.. strategies.Select(strategy => strategy.ScopeId).Distinct()],
            ct);

        return
        [
            .. strategies
                .OrderByDescending(strategy => strategy.PeriodFrom)
                .Select(strategy => new StrategyView(
                    strategy.Id,
                    strategy.ScopeType,
                    strategy.ScopeId,
                    scopes.GetValueOrDefault(strategy.ScopeId),
                    strategy.PeriodFrom,
                    strategy.PeriodTo,
                    strategy.Title,
                    strategy.Narrative,
                    strategy.OwnerPersonId,
                    names.GetValueOrDefault(strategy.OwnerPersonId),
                    strategy.Status,
                    strategy.Objectives.Count,
                    strategy.Progress())),
        ];
    }

    private async Task<IReadOnlyList<ObjectiveView>> ObjectiveViewsAsync(
        StrategyPlan strategy,
        CancellationToken ct)
    {
        var contributions = strategy.Objectives.SelectMany(objective => objective.Contributions).ToList();

        var items = await portfolio.GetItemsAsync(
            [
                .. contributions
                    .Where(contribution => contribution.SourceType == ContributionSources.Item)
                    .Select(contribution => contribution.SourceId)
                    .Distinct(),
            ],
            ct);

        var pains = await problems.GetProblemsAsync(
            [
                .. contributions
                    .Where(contribution => contribution.SourceType == ContributionSources.Problem)
                    .Select(contribution => contribution.SourceId)
                    .Distinct(),
            ],
            ct);

        var today = Today();

        return
        [
            .. strategy.Objectives
                .OrderByDescending(objective => objective.Weight)
                .ThenBy(objective => objective.CreatedAt)
                .Select(objective => ToView(
                    objective,
                    [
                        .. objective.Contributions.Select(contribution =>
                        {
                            var facts = contribution.SourceType == ContributionSources.Item
                                ? items.GetValueOrDefault(contribution.SourceId)
                                : pains.GetValueOrDefault(contribution.SourceId);

                            return new ContributionView(
                                contribution.Id,
                                contribution.SourceType,
                                contribution.SourceId,
                                facts?.Code,
                                facts?.Name,
                                facts?.State,
                                contribution.Weight,
                                contribution.Note);
                        }),
                    ],
                    today)),
        ];
    }

    private static ObjectiveView ToView(
        Objective objective,
        IReadOnlyList<ContributionView> contributions,
        DateOnly today) => new(
        objective.Id,
        objective.StrategyId,
        objective.Title,
        objective.Description,
        objective.MetricKind,
        objective.Baseline,
        objective.Target,
        objective.Current,
        objective.Unit,
        objective.Due,
        // Derived at read time as well as at write time. An objective nobody touched since March must not still
        // claim it is on track in December just because nothing wrote to it.
        ObjectiveMath.Derive(objective, today),
        objective.StatusOverridden,
        objective.Weight,
        objective.Progress(),
        ObjectiveMath.Expected(objective.CreatedOn, objective.Due, today),
        [
            .. objective.KeyResults.Select(keyResult => new KeyResultView(
                keyResult.Id,
                keyResult.Title,
                keyResult.Target,
                keyResult.Current,
                keyResult.Progress)),
        ],
        contributions);

    // --- Plumbing --------------------------------------------------------------------------------------------

    private async Task RefuseUnknownSourceAsync(string sourceType, Guid sourceId, CancellationToken ct)
    {
        var known = sourceType == ContributionSources.Item
            ? (await portfolio.GetItemsAsync([sourceId], ct)).ContainsKey(sourceId)
            : (await problems.GetProblemsAsync([sourceId], ct)).ContainsKey(sourceId);

        if (!known)
        {
            // Deliberately the same answer whether it does not exist or the caller may not see it. Anything else
            // turns the link picker into a way of probing for confidential items by id.
            throw new ResourceNotFoundException($"No {sourceType} {sourceId} you can link.");
        }
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ScopeNamesAsync(
        IReadOnlyList<Guid> nodeIds,
        CancellationToken ct)
    {
        var found = new Dictionary<Guid, string>();

        foreach (var nodeId in nodeIds)
        {
            var subtree = await nodes.GetSubtreeAsync(nodeId, ct);
            var root = subtree.FirstOrDefault(node => node.Id == nodeId);

            if (root is not null)
            {
                found[nodeId] = root.Name;
            }
        }

        return found;
    }

    private async Task<StrategyPlan> LoadAsync(Guid strategyId, CancellationToken ct) =>
        await context.Strategies
            .Include(strategy => strategy.Objectives)
                .ThenInclude(objective => objective.KeyResults)
            .Include(strategy => strategy.Objectives)
                .ThenInclude(objective => objective.Contributions)
            .AsSplitQuery()
            .AsTracking()
            .SingleOrDefaultAsync(strategy => strategy.Id == strategyId, ct)
        ?? throw new ResourceNotFoundException($"No strategy {strategyId}.");

    private async Task<StrategyPlan> LoadByObjectiveAsync(Guid objectiveId, CancellationToken ct)
    {
        var strategyId = await context.Objectives
            .AsNoTracking()
            .Where(objective => objective.Id == objectiveId)
            .Select(objective => objective.StrategyId)
            .SingleOrDefaultAsync(ct);

        return strategyId == Guid.Empty
            ? throw new ResourceNotFoundException($"No objective {objectiveId}.")
            : await LoadAsync(strategyId, ct);
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}
