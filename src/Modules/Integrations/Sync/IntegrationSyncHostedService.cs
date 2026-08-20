using Cracra.Modules.Integrations.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Integrations.Sync;

/// <summary>
/// The timer around the synchronizer.
/// </summary>
/// <remarks>
/// <para>
/// Two intervals, and they mean different things. This loop's tick is how often the platform <em>asks</em> which
/// connections are due; each connection's <c>poll_interval</c> is how often it actually gets pulled. A single
/// shared interval would force every integration onto the same cadence, and a ServiceNow queue a helpdesk works
/// from minute to minute has nothing in common with a DevOps project that changes twice a day.
/// </para>
/// <para>
/// Failures never reach here: the synchronizer catches per connection, records the failure on the row and counts
/// it. That is what keeps one dead integration from stopping the other nine, and what makes the age gauge — not
/// an exception — the thing that tells somebody about it.
/// </para>
/// </remarks>
internal sealed class IntegrationSyncHostedService(
    IExternalWorkItemSynchronizer synchronizer,
    IOptions<IntegrationsOptions> options,
    ILogger<IntegrationSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (settings.SyncOnStartup)
        {
            // A short delay so the host finishes starting and the directory sync has had a chance to run: an
            // item pulled before the people exist resolves to nobody, and would stay that way until its next
            // pull. Ten seconds is the same grace the directory sync takes for the same kind of reason.
            await Delay(TimeSpan.FromSeconds(10), stoppingToken);

            await RunAsync(stoppingToken);
        }

        if (settings.SchedulerInterval <= TimeSpan.Zero)
        {
            logger.LogInformation("The integrations scheduler interval is zero; pulls are on demand only");
            return;
        }

        using var timer = new PeriodicTimer(settings.SchedulerInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunAsync(stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var results = await synchronizer.SynchronizeDueAsync(ct);

            if (results.Count > 0)
            {
                logger.LogDebug("Pulled {Count} due connection(s)", results.Count);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the enumeration of due connections can land here — a per-connection failure is handled and
            // recorded inside. Which means this is the database being unreachable, and the next tick retries.
            logger.LogError(exception, "Could not determine which integration connections are due");
        }
    }

    private static async Task Delay(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
            // Shutting down during the grace period. Nothing to do and nothing worth logging.
        }
    }
}
