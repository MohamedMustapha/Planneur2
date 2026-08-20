using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Mediator;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Portfolio.Domain;
using Cracra.Modules.Portfolio.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Portfolio.Application;

// =================================================================================================================
// Queries read projections straight off the DbContext. As everywhere else, nothing here filters by role or
// department: RLS removed those rows before they arrived, and a second filter would either duplicate the policy or
// quietly disagree with it.
// =================================================================================================================

/// <summary>
/// The board, optionally narrowed.
/// </summary>
/// <remarks>
/// <para>
/// Both filters narrow; neither widens. "All" is not a privilege the query grants — it is simply the absence of a
/// filter, and what comes back is still whatever RLS allowed. That is why <c>mine</c> can be answered here with a
/// plain predicate rather than a role check: it is a convenience, not a boundary.
/// </para>
/// </remarks>
public sealed record GetBoardQuery(Guid? DepartmentId, string? Scope) : IRequest<PortfolioBoard>;

public sealed record GetItemQuery(Guid ItemId) : IRequest<PortfolioItemDetail>;

/// <summary>Just the iteration timeline. S6 asks for this without needing the decision history.</summary>
public sealed record GetIterationsQuery(Guid ItemId) : IRequest<IReadOnlyList<IterationSummary>>;

/// <summary>
/// The board, grouped into lanes server-side.
/// </summary>
/// <remarks>
/// Grouped here rather than in the client so every lane exists even when empty — a board missing its "considered"
/// column because nothing is in it reads as a broken screen, and it is also where you drop new candidates.
/// </remarks>
internal sealed class GetBoardHandler(
    PortfolioDbContext context,
    IProjectsPort projects,
    IDirectoryPort directory,
    Cracra.BuildingBlocks.Web.Users.IUserContext user) : IRequestHandler<GetBoardQuery, PortfolioBoard>
{
    private static readonly PortfolioState[] LaneOrder =
    [
        PortfolioState.Considered,
        PortfolioState.Committed,
        PortfolioState.Active,
        PortfolioState.Dephase,
    ];

    public async Task<PortfolioBoard> Handle(GetBoardQuery request, CancellationToken ct)
    {
        var query = context.Items.AsQueryable();

        if (request.DepartmentId is { } departmentId)
        {
            query = query.Where(item => item.DepartmentId == departmentId);
        }

        // "Mine" means the items this person raised or is delivering. Anything else is left to RLS, which has
        // already removed what they may not see — a second filter for "all" would be a filter that disagrees.
        if (string.Equals(request.Scope, "mine", StringComparison.OrdinalIgnoreCase))
        {
            var me = user.UserId;

            query = query.Where(item =>
                item.CreatedBy == me
                || context.Transitions.Any(transition =>
                    transition.PortfolioItemId == item.Id && transition.DecidedBy == me));
        }

        var rows = await query
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                item.ProjectId,
                item.Name,
                item.State,
                item.Priority,
                item.DepartmentId,
                item.DecisionNotes,
                IterationCount = context.Iterations.Count(iteration => iteration.PortfolioItemId == item.Id),
                Current = context.Iterations
                    .Where(iteration => iteration.PortfolioItemId == item.Id
                        && (iteration.State == IterationState.Active || iteration.State == IterationState.Planned))
                    // Active first, then the next planned one: exactly what CurrentIteration means on the
                    // aggregate, kept in step so the card and the detail screen never disagree.
                    .OrderBy(iteration => iteration.State == IterationState.Active ? 0 : 1)
                    .ThenBy(iteration => iteration.Sequence)
                    .Select(iteration => new IterationSummary(
                        iteration.Id,
                        iteration.Sequence,
                        iteration.Name,
                        iteration.Length.ToString().ToLower(),
                        iteration.StartsOn,
                        iteration.EndsOn,
                        iteration.State.ToString().ToLower()))
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var facts = await projects.GetFactsAsync(
            [.. rows.Where(row => row.ProjectId is not null).Select(row => row.ProjectId!.Value).Distinct()],
            ct);

        var departmentNames = await directory.GetDepartmentNameKeysAsync(
            [.. rows.Select(row => row.DepartmentId).Distinct()],
            ct);

        var summaries = rows.Select(row =>
        {
            // A project the caller cannot read leaves the cost blank rather than hiding the item. The item's own
            // visibility is a separate question from the project's, and RLS already answered it.
            var fact = row.ProjectId is { } projectId && facts.TryGetValue(projectId, out var found) ? found : null;

            return new PortfolioItemSummary(
                row.Id,
                row.ProjectId,
                row.Name,
                row.State.ToString().ToLowerInvariant(),
                row.Priority,
                row.DepartmentId,
                departmentNames.GetValueOrDefault(row.DepartmentId, "department.unknown"),
                row.DecisionNotes,
                fact?.CostAmount,
                fact?.CostCurrency,
                fact?.Classification,
                row.Current,
                row.IterationCount);
        }).ToList();

        var lanes = LaneOrder
            .Select(state => new PortfolioLane(
                state.ToString().ToLowerInvariant(),
                [.. summaries.Where(summary =>
                    summary.State == state.ToString().ToLowerInvariant())]))
            .ToList();

        return new PortfolioBoard(lanes);
    }
}

internal sealed class GetItemHandler(
    PortfolioDbContext context,
    IProjectsPort projects,
    IDirectoryPort directory) : IRequestHandler<GetItemQuery, PortfolioItemDetail>
{
    public async Task<PortfolioItemDetail> Handle(GetItemQuery request, CancellationToken ct)
    {
        var item = await context.Items
            .Where(candidate => candidate.Id == request.ItemId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.ProjectId,
                candidate.Name,
                candidate.State,
                candidate.Priority,
                candidate.DepartmentId,
                candidate.DecisionNotes,
            })
            .SingleOrDefaultAsync(ct)
            // Filtered by RLS or absent; the caller cannot tell the two apart, by design.
            ?? throw new ResourceNotFoundException($"No portfolio item {request.ItemId}.");

        var iterations = await context.Iterations
            .Where(iteration => iteration.PortfolioItemId == request.ItemId)
            .OrderBy(iteration => iteration.Sequence)
            .Select(iteration => new IterationSummary(
                iteration.Id,
                iteration.Sequence,
                iteration.Name,
                iteration.Length.ToString().ToLower(),
                iteration.StartsOn,
                iteration.EndsOn,
                iteration.State.ToString().ToLower()))
            .ToListAsync(ct);

        var history = await context.Transitions
            .Where(transition => transition.PortfolioItemId == request.ItemId)
            .OrderByDescending(transition => transition.DecidedAt)
            .Select(transition => new TransitionRecord(
                transition.FromState == null ? null : transition.FromState.ToString()!.ToLower(),
                transition.ToState.ToString().ToLower(),
                transition.IsReversal,
                transition.Reason,
                transition.DecidedBy,
                transition.DecidedAt))
            .ToListAsync(ct);

        var facts = item.ProjectId is { } projectId
            ? await projects.GetFactsAsync([projectId], ct)
            : new Dictionary<Guid, ProjectFacts>();

        var fact = item.ProjectId is { } id ? facts.GetValueOrDefault(id) : null;

        var departmentNames = await directory.GetDepartmentNameKeysAsync([item.DepartmentId], ct);

        var current = iterations.FirstOrDefault(iteration => iteration.State == "active")
            ?? iterations.FirstOrDefault(iteration => iteration.State == "planned");

        var summary = new PortfolioItemSummary(
            item.Id,
            item.ProjectId,
            item.Name,
            item.State.ToString().ToLowerInvariant(),
            item.Priority,
            item.DepartmentId,
            departmentNames.GetValueOrDefault(item.DepartmentId, "department.unknown"),
            item.DecisionNotes,
            fact?.CostAmount,
            fact?.CostCurrency,
            fact?.Classification,
            current,
            iterations.Count);

        return new PortfolioItemDetail(summary, iterations, history);
    }
}

internal sealed class GetIterationsHandler(PortfolioDbContext context)
    : IRequestHandler<GetIterationsQuery, IReadOnlyList<IterationSummary>>
{
    public async Task<IReadOnlyList<IterationSummary>> Handle(GetIterationsQuery request, CancellationToken ct)
    {
        // No existence check on the item: if RLS hid it, its iterations are hidden by the same EXISTS in their own
        // policy, and the answer is an empty timeline rather than a different error for a row you cannot see.
        return await context.Iterations
            .Where(iteration => iteration.PortfolioItemId == request.ItemId)
            .OrderBy(iteration => iteration.Sequence)
            .Select(iteration => new IterationSummary(
                iteration.Id,
                iteration.Sequence,
                iteration.Name,
                iteration.Length.ToString().ToLower(),
                iteration.StartsOn,
                iteration.EndsOn,
                iteration.State.ToString().ToLower()))
            .ToListAsync(ct);
    }
}
