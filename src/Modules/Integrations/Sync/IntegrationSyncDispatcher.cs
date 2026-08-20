using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cracra.Modules.Integrations.Sync;

/// <summary>Accepts an on-demand pull and returns immediately. What the 202 endpoint hands the work to.</summary>
public interface IIntegrationSyncDispatcher
{
    /// <summary>Queues a pull. Returns false when the queue is full, which is a signal worth surfacing.</summary>
    bool Enqueue(Guid connectionId);
}

/// <summary>
/// A queue of one, per connection, drained by a single worker.
/// </summary>
/// <remarks>
/// <para>
/// A channel rather than <c>Task.Run</c> at the endpoint, for two reasons that both bite in production. The first
/// is lifetime: work started off a request lives in a scope ASP.NET is about to dispose, and the pull would race
/// the disposal of everything it resolved. The second is concurrency: an administrator clicking "sync" three
/// times would run three reconciliations of the same connection at once, each closing what the others had just
/// written.
/// </para>
/// <para>
/// Bounded, and the bound is small. A backlog of pending pulls has no value — every one of them reconciles the
/// same connection to the same current state — so a full queue means the pull that matters is already coming and
/// dropping the request is the correct answer rather than a degradation.
/// </para>
/// </remarks>
internal sealed class IntegrationSyncDispatcher(ILogger<IntegrationSyncDispatcher> logger)
    : IIntegrationSyncDispatcher
{
    private readonly Channel<Guid> _queue = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(32)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    public ChannelReader<Guid> Reader => _queue.Reader;

    public bool Enqueue(Guid connectionId)
    {
        if (_queue.Writer.TryWrite(connectionId))
        {
            return true;
        }

        logger.LogWarning(
            "The on-demand sync queue is full; the request for connection {ConnectionId} was dropped. "
            + "A pull is already pending and will reconcile it.",
            connectionId);

        return false;
    }
}

/// <summary>Drains the on-demand queue, one pull at a time.</summary>
/// <remarks>
/// One at a time on purpose. Pulls are I/O-bound against systems that belong to somebody else, and the platform
/// has no business opening as many parallel connections to a ServiceNow instance as it has department heads with
/// an itchy finger.
/// </remarks>
internal sealed class IntegrationSyncDispatchService(
    IntegrationSyncDispatcher dispatcher,
    IExternalWorkItemSynchronizer synchronizer,
    ILogger<IntegrationSyncDispatchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var connectionId in dispatcher.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await synchronizer.SynchronizeAsync(connectionId, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The synchronizer already records per-connection failures on the row; anything reaching here is
                // the machinery around it. Logged and swallowed, because letting it escape would end the drain
                // loop and every later "sync now" would silently do nothing.
                logger.LogError(exception, "An on-demand pull of connection {ConnectionId} failed", connectionId);
            }
        }
    }
}
