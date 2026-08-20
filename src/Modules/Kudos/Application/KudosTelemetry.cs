using System.Diagnostics.Metrics;
using Cracra.BuildingBlocks.Observability;

namespace Cracra.Modules.Kudos.Application;

/// <summary>
/// The meter <c>architecture.md §6</c> asks for: kudos per department.
/// </summary>
/// <remarks>
/// <para>
/// Tagged by department and by mode, and not by person. A per-person counter would be a leaderboard nobody
/// consented to, sitting in Grafana where the department's mode setting cannot reach it — the one place the
/// "counter, unless we choose otherwise" decision would silently not hold.
/// </para>
/// <para>
/// The number worth watching is a department going quiet. Recognition is voluntary and its absence is invisible in
/// every other signal the platform emits.
/// </para>
/// </remarks>
public sealed class KudosTelemetry
{
    private static readonly Counter<long> GivenCounter = CracraTelemetry.Meter.CreateCounter<long>(
        "cracra.kudos.given",
        unit: "{kudo}",
        description: "Kudos given, by department and category.");

    private static readonly Counter<long> RefusedCounter = CracraTelemetry.Meter.CreateCounter<long>(
        "cracra.kudos.refused",
        unit: "{kudo}",
        description: "Kudos the platform refused, by reason — cap, eligibility or self.");

    private static readonly Counter<long> BadgeCounter = CracraTelemetry.Meter.CreateCounter<long>(
        "cracra.kudos.badges_awarded",
        unit: "{badge}",
        description: "Thresholds crossed, by department and badge.");

    public void Given(Guid departmentId, string category, string mode) =>
        GivenCounter.Add(
            1,
            new KeyValuePair<string, object?>("department", departmentId),
            new KeyValuePair<string, object?>("category", category),
            new KeyValuePair<string, object?>("mode", mode));

    public void Refused(string reason) =>
        RefusedCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void Badge(Guid departmentId, string badgeCode) =>
        BadgeCounter.Add(
            1,
            new KeyValuePair<string, object?>("department", departmentId),
            new KeyValuePair<string, object?>("badge", badgeCode));
}
