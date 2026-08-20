using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cracra.BuildingBlocks.Observability;

namespace Cracra.Modules.Reporting.Application;

/// <summary>
/// The meters S8 asks for: how long a summary takes, how often the cache saves one, and what exports weigh.
/// </summary>
/// <remarks>
/// <para>
/// Generation duration is the one that matters operationally. The on-prem model runs on hardware sized for the
/// building, and the first sign of it being under-provisioned is Monday-morning summaries taking a minute rather
/// than five seconds — which nobody reports as a bug, they just stop pressing the button.
/// </para>
/// <para>
/// The cache-hit counter is its companion: a hit rate that collapses means the figures are churning between
/// requests, and the fix is upstream rather than in the model.
/// </para>
/// <para>
/// An instance rather than statics, unlike the directory's, because it is injected into handlers and a test that
/// wants to observe it should be able to hold one. The meters underneath are still process-wide.
/// </para>
/// </remarks>
public sealed class ReportingTelemetry
{
    private static readonly Histogram<double> GenerationDuration = CracraTelemetry.Meter.CreateHistogram<double>(
        "cracra.reporting.summary.duration",
        unit: "ms",
        description: "Wall-clock time for the on-prem model to write one narrative.");

    private static readonly Counter<long> Generated = CracraTelemetry.Meter.CreateCounter<long>(
        "cracra.reporting.summary.generated",
        unit: "{summary}",
        description: "Narratives written by the model.");

    private static readonly Counter<long> Reused = CracraTelemetry.Meter.CreateCounter<long>(
        "cracra.reporting.summary.reused",
        unit: "{summary}",
        description: "Narratives served from the cache because the figures had not moved.");

    private static readonly Histogram<long> ExportBytes = CracraTelemetry.Meter.CreateHistogram<long>(
        "cracra.reporting.export.bytes",
        unit: "By",
        description: "Size of one rendered report.");

    public void SummaryReused() => Reused.Add(1);

    public void Exported(string format, long bytes) =>
        ExportBytes.Record(bytes, new KeyValuePair<string, object?>("format", format));

    /// <summary>Times a generation and counts it. Dispose at the end of the call — a <c>using</c> does it.</summary>
    public GenerationScope MeasureGeneration(string scope) => new(scope);

    public readonly struct GenerationScope(string scope) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();

        public void Dispose()
        {
            var tag = new KeyValuePair<string, object?>("scope", scope);

            GenerationDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tag);
            Generated.Add(1, tag);
        }
    }
}
