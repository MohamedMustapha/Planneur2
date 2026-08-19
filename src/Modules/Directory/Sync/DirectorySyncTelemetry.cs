using System.Diagnostics.Metrics;
using Cracra.BuildingBlocks.Observability;

namespace Cracra.Modules.Directory.Sync;

/// <summary>
/// The sync meters S1 asks for: last-sync age and error count.
/// </summary>
/// <remarks>
/// Age matters more than duration. A sync that fails silently looks identical to one that has not run, and both
/// show up as a directory that is quietly stale — people who joined last week never appear, and their boards are
/// empty for reasons nobody connects to the directory. The gauge makes staleness alertable.
/// </remarks>
public static class DirectorySyncTelemetry
{
    private static long _lastSuccessTicks;

    static DirectorySyncTelemetry()
    {
        CracraTelemetry.Meter.CreateObservableGauge(
            "cracra.directory.sync.age",
            ObserveAgeSeconds,
            unit: "s",
            description: "Seconds since the last successful directory sync. Rising without bound means sync is dead.");
    }

    public static readonly Counter<long> Failures = CracraTelemetry.Meter.CreateCounter<long>(
        "cracra.directory.sync.failures",
        unit: "{run}",
        description: "Directory sync runs that threw.");

    public static readonly Counter<long> Changes = CracraTelemetry.Meter.CreateCounter<long>(
        "cracra.directory.sync.changes",
        unit: "{change}",
        description: "People and org units created, updated or deactivated by sync.");

    public static readonly Histogram<double> Duration = CracraTelemetry.Meter.CreateHistogram<double>(
        "cracra.directory.sync.duration",
        unit: "ms",
        description: "Wall-clock time of one directory reconciliation.");

    public static void RecordSuccess(TimeSpan elapsed, int changes)
    {
        Interlocked.Exchange(ref _lastSuccessTicks, DateTimeOffset.UtcNow.UtcTicks);

        Duration.Record(elapsed.TotalMilliseconds);
        Changes.Add(changes);
    }

    public static void RecordFailure() => Failures.Add(1);

    private static IEnumerable<Measurement<double>> ObserveAgeSeconds()
    {
        var ticks = Interlocked.Read(ref _lastSuccessTicks);

        if (ticks == 0)
        {
            // Never succeeded in this process. Reporting nothing is better than reporting zero, which would read
            // as "just synced" on the dashboard.
            yield break;
        }

        var age = DateTimeOffset.UtcNow - new DateTimeOffset(ticks, TimeSpan.Zero);

        yield return new Measurement<double>(age.TotalSeconds);
    }
}
