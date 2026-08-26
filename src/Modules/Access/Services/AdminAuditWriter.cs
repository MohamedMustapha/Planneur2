using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Access.Data;
using Cracra.Modules.Access.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Access.Services;

internal sealed class AdminAuditWriter(AccessDbContext context, IUserContext user) : IAdminAudit
{
    private const int PageSize = 200;

    public async Task RecordAsync(
        string action,
        string targetType,
        Guid targetId,
        Guid? nodeId,
        string detail,
        CancellationToken ct)
    {
        // The actor is the caller, never 'system' — §08.2 is explicit that admin actions run under the person
        // doing them so RLS and the trail both apply. A background job that wrote here would be recording that
        // nobody did it.
        context.AdminAudit.Add(AdminAuditEntry.Of(
            user.UserId,
            action,
            targetType,
            targetId,
            nodeId,
            detail,
            DateTimeOffset.UtcNow));

        await context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AdminAuditDto>> ReadAsync(
        Guid? nodeId,
        string? action,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        var query = context.AdminAudit.AsQueryable();

        if (nodeId is { } node)
        {
            query = query.Where(entry => entry.NodeId == node);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            var wanted = action.Trim().ToLowerInvariant();

            query = query.Where(entry => entry.Action == wanted);
        }

        if (from is { } start)
        {
            var at = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            query = query.Where(entry => entry.OccurredAt >= at);
        }

        if (to is { } end)
        {
            var at = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            query = query.Where(entry => entry.OccurredAt < at);
        }

        var entries = await query
            .OrderByDescending(entry => entry.OccurredAt)
            .Take(PageSize)
            .ToListAsync(ct);

        return
        [
            .. entries.Select(entry => new AdminAuditDto(
                entry.Id,
                entry.ActorPersonId,
                entry.Action,
                entry.TargetType,
                entry.TargetId,
                entry.NodeId,
                entry.Detail,
                entry.OccurredAt)),
        ];
    }
}
