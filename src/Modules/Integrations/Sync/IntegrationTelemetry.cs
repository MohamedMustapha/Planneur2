using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Cracra.BuildingBlocks.Observability;

namespace Cracra.Modules.Integrations.Sync;

/// <summary>
/// The sync meters S10 asks for: latency, failures, and a last-sync gauge per connection.
/// </summary>
/// <remarks>
/// <para>
/// Per connection, not per provider. Two DevOps connections in one department fail independently, and an
/// aggregate gauge would go on looking healthy while one of them had been dead for a week — which is precisely
/// the failure this slice is most exposed to, because a stale mirror looks exactly like a quiet sprint.
/// </para>
/// <para>
/// The gauge reports age rather than a timestamp. "Seconds since the last successful pull" is a number an alert
/// rule can be written against without knowing what time it is on the scraper.
/// </para>
/// </remarks>
public static class IntegrationTelemetry
{
    private static readonly ConcurrentDictionary<Guid, ConnectionState> States = new();

    static IntegrationTelemetry()
    {
        CracraTelemetry.Meter.CreateObservableGauge(
            "cracra.integrations.sync.age",
            ObserveAgeSeconds,
            unit: "s",
            description: "Seconds since the last successful pull, per connection. Rising without bound means the "
                         + "mirror is stale and every feed built on it is quietly wrong.");
    }

    public static readonly Counter<long> Failures = CracraTelemetry.Meter.CreateCounter<long>(
        "cracra.integrations.sync.failures",
        unit: "{run}",
        description: "Pulls that threw.");

    public static readonly Counter<long> Items = CracraTelemetry.Meter.CreateCounter<long>(
        "cracra.integrations.sync.items",
        unit: "{item}",
        description: "Mirror rows created, updated or closed by a pull.");

    public static readonly Histogram<double> Duration = CracraTelemetry.Meter.CreateHistogram<double>(
        "cracra.integrations.sync.duration",
        unit: "ms",
        description: "Wall-clock time of one connection's pull, including the provider call.");

    public static void RecordSuccess(Guid connectionId, string provider, TimeSpan elapsed, int changes)
    {
        States[connectionId] = new ConnectionState(provider, DateTimeOffset.UtcNow);

        Duration.Record(elapsed.TotalMilliseconds, Tag(provider));
        Items.Add(changes, Tag(provider));
    }

    public static void RecordFailure(Guid connectionId, string provider, TimeSpan elapsed)
    {
        // The connection stays in the dictionary with its previous success time, so the age gauge keeps climbing
        // instead of disappearing. A metric that vanishes when things break is a metric nobody can alert on.
        States.TryAdd(connectionId, new ConnectionState(provider, null));

        Duration.Record(elapsed.TotalMilliseconds, Tag(provider));
        Failures.Add(1, Tag(provider));
    }

    /// <summary>Drops a connection's gauge when it is deleted, so a removed integration stops alerting.</summary>
    public static void Forget(Guid connectionId) => States.TryRemove(connectionId, out _);

    private static KeyValuePair<string, object?> Tag(string provider) => new("provider", provider);

    private static IEnumerable<Measurement<double>> ObserveAgeSeconds()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var (connectionId, state) in States)
        {
            if (state.LastSuccess is not { } success)
            {
                // Never succeeded in this process. Reporting nothing beats reporting zero, which would read as
                // "just synced" on the dashboard.
                continue;
            }

            yield return new Measurement<double>(
                (now - success).TotalSeconds,
                new KeyValuePair<string, object?>("connection", connectionId.ToString()),
                new KeyValuePair<string, object?>("provider", state.Provider));
        }
    }

    private sealed record ConnectionState(string Provider, DateTimeOffset? LastSuccess);
}
