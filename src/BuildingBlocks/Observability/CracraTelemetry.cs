using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Cracra.BuildingBlocks.Observability;

/// <summary>
/// The platform's single ActivitySource and Meter. Every slice adds its counters here rather than minting its own
/// meter, so one Grafana datasource and one scrape config cover the whole system.
/// </summary>
public static class CracraTelemetry
{
    public const string ServiceName = "cracra";

    public const string ActivitySourceName = "Cracra";

    public const string MeterName = "Cracra";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// The baseline meters S0 owns. Slices add theirs alongside — activity logging rate (S5), sync latency and
    /// failures (S10), AI summary duration and tokens (S8), kudos per department (S9).
    /// </summary>
    public static class Metrics
    {
        /// <summary>
        /// Counts queries that returned nothing because RLS filtered every row. A sustained rise here usually
        /// means a role mapping broke, and it is the fastest signal that a whole department has gone blind.
        /// </summary>
        public static readonly Counter<long> RlsDeniedQueries =
            Meter.CreateCounter<long>("cracra.rls.denied_queries", unit: "{query}",
                description: "Queries that returned no rows because row-level security excluded them.");

        public static readonly Counter<long> OutboxPublished =
            Meter.CreateCounter<long>("cracra.outbox.published", unit: "{message}",
                description: "Integration events successfully published from a module outbox.");

        public static readonly Counter<long> OutboxFailures =
            Meter.CreateCounter<long>("cracra.outbox.failures", unit: "{message}",
                description: "Outbox delivery attempts that threw.");

        public static readonly Histogram<double> MediatorRequestDuration =
            Meter.CreateHistogram<double>("cracra.mediator.request.duration", unit: "ms",
                description: "Time spent handling one mediator request, including behaviors.");
    }
}
