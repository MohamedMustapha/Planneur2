using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Directory.Sync;

/// <summary>
/// Runs the reconciliation on a schedule.
/// </summary>
/// <remarks>
/// Failures are logged and counted, never rethrown: a Keycloak blip must not take the API process down, and the
/// next tick retries anyway. What makes that safe rather than negligent is the sync-age gauge — a silently failing
/// loop shows up as an age that climbs, which is alertable, instead of as nothing at all.
/// </remarks>
internal sealed class DirectorySyncHostedService(
    IDirectorySynchronizer synchronizer,
    IOptions<DirectorySyncOptions> options,
    ILogger<DirectorySyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (settings.SyncOnStartup)
        {
            // A short delay so the host finishes starting and Keycloak has settled; on a cold dev box both are
            // coming up at once.
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            await RunAsync(stoppingToken);
        }

        if (settings.Interval <= TimeSpan.Zero)
        {
            logger.LogInformation("Directory sync interval is zero; scheduled reconciliation is disabled");
            return;
        }

        using var timer = new PeriodicTimer(settings.Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunAsync(stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await synchronizer.SynchronizeAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DirectorySyncTelemetry.RecordFailure();

            logger.LogError(ex, "Directory sync failed; the next scheduled run will retry");
        }
    }
}
