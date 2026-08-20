using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Integrations.Data;
using Cracra.Modules.Integrations.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Integrations.Services;

/// <summary>
/// The mirror, queried.
/// </summary>
/// <remarks>
/// <para>
/// One implementation behind the module's own endpoint and behind the <see cref="IExternalWorkItemReader"/>
/// contract S5 and S6a consume, because they are the same question asked from two places. Registering it twice
/// would let the two drift, and the drift would show up as a dropdown and a pool disagreeing about what is
/// assigned to whom.
/// </para>
/// <para>
/// Every predicate below narrows; none widens. Whether the caller may see a row at all was decided by RLS before
/// this code ran, and the difference matters: the <c>UnitId</c> filter here is a convenience for a caller who
/// wants one unit's queue, not the thing that stops them reading another unit's.
/// </para>
/// </remarks>
internal sealed class WorkItemQueryService(IntegrationsDbContext context, IUserContext user)
    : IExternalWorkItemReader
{
    /// <summary>A dropdown is a list somebody reads. Past this, the answer is "use a filter".</summary>
    private const int MaxLimit = 500;

    public async Task<IReadOnlyList<ExternalWorkItemView>> QueryAsync(
        ExternalWorkItemQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var provider = string.IsNullOrWhiteSpace(query.Provider)
            ? null
            : query.Provider.Trim().ToLowerInvariant();

        var items = context.WorkItems.AsQueryable();

        if (!query.IncludeClosed)
        {
            items = items.Where(item => item.MirrorState == MirrorStates.Open);
        }

        if (provider is not null)
        {
            items = items.Where(item => item.Provider == provider);
        }

        if (query.ConnectionId is { } connectionId)
        {
            items = items.Where(item => item.ConnectionId == connectionId);
        }

        if (query.AssignedToMe)
        {
            // The caller, from the session — never a parameter. S5's endpoint documents why: handing one person
            // another person's assigned tickets is a disclosure the external system never agreed to, and the way
            // to guarantee it cannot happen is for the id to have no way in.
            items = items.Where(item => item.AssignedPersonId == user.UserId);
        }

        if (query.Unassigned)
        {
            items = items.Where(item => item.AssignedPersonId == null);
        }

        if (query.UnitId is { } unitId)
        {
            items = items.Where(item => item.UnitId == unitId);
        }

        if (query.ProjectId is { } projectId)
        {
            items = items.Where(item => item.ProjectId == projectId);
        }

        if (query.CurrentSprint)
        {
            // A column, not a join to the connection. The connection row is readable only by whoever administers
            // the department, so a join would quietly return nothing for every developer — the exact people this
            // filter exists for. Sync stamps the flag; see ExternalWorkItem.IsCurrentSprint.
            items = items.Where(item => item.IsCurrentSprint);
        }

        var limit = Math.Clamp(query.Limit, 1, MaxLimit);

        var rows = await items
            // Freshest first, and by external id after that so a page boundary is stable when a hundred items
            // share a sync timestamp — which is every item of one pull.
            .OrderByDescending(item => item.UpdatedAtSource ?? item.SyncedAt)
            .ThenBy(item => item.ExternalId)
            .Take(limit)
            .ToListAsync(ct);

        return [.. rows.Select(Project)];
    }

    private static ExternalWorkItemView Project(ExternalWorkItem item) => new(
        item.Id,
        item.Provider,
        item.ExternalId,
        item.Reference,
        item.Title,
        item.Type,
        item.State,
        item.SprintOrQueue,
        item.ProjectId,
        item.UnitId,
        item.AssignedPersonId,
        item.Url,
        item.EstimatedHours,
        item.UpdatedAtSource,
        item.SyncedAt,
        item.MirrorState);
}
