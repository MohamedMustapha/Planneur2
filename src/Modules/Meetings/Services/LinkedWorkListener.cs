using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Data;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Problems.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cracra.Modules.Meetings.Services;

// v2 §07.2: closing the linked thing closes the action. Without this the tracker is a second place to record the
// same completion, and a tracker people have to maintain twice is one they abandon.
//
// The handlers run under the drain's system identity, which is what lets them reach actions across every node —
// nobody in particular is closing these, the work resolving is.
internal sealed class LinkedWorkListener(MeetingsDbContext context, ILogger<LinkedWorkListener> logger)
    : INotificationHandler<ProblemResolved>,
      INotificationHandler<ItemArchived>
{
    public Task Handle(ProblemResolved notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return CloseAsync(ActionLinkTypes.Problem, notification.ProblemId, ct);
    }

    public Task Handle(ItemArchived notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return CloseAsync(ActionLinkTypes.Item, notification.ItemId, ct);
    }

    private async Task CloseAsync(string linkType, Guid linkId, CancellationToken ct)
    {
        var actions = await context.Actions
            .AsTracking()
            .Where(action => action.LinkType == linkType && action.LinkId == linkId)
            .Where(action => action.Status == ActionStatuses.Open)
            .ToListAsync(ct);

        if (actions.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var action in actions)
        {
            if (action.CloseBecauseLinkResolved(now))
            {
                context.Enqueue(new ActionItemClosed(
                    action.Id, action.MinutesId, action.OwnerPersonId, action.LinkType));
            }
        }

        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "Closed {ActionCount} action(s) because {LinkType} {LinkId} resolved.",
            actions.Count,
            linkType,
            linkId);
    }
}
