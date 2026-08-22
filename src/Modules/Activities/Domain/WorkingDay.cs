using System.Text.Json;
using System.Text.Json.Serialization;
using Cracra.Modules.Activities.Contracts;

namespace Cracra.Modules.Activities.Domain;

/// <summary>
/// Reads a department's configured working day, falling back to the platform defaults.
/// </summary>
/// <remarks>
/// <para>
/// The shape of a day is department policy, not a platform constant: a helpdesk on 07:00-15:00 and a studio on
/// 10:00-19:00 are both normal, and a board built around somebody else's hours wastes half its width on rows
/// nobody works in. So the bounds and the two sessions travel with the weekly target, through the same port.
/// </para>
/// <para>
/// Malformed configuration falls back rather than throwing, exactly as the activity taxonomy and the shift
/// templates do: one bad edit in a settings screen must not take a person's board offline.
/// </para>
/// </remarks>
public static class WorkingDayPolicy
{
    /// <summary>
    /// What a day looks like where the department has said nothing.
    /// </summary>
    /// <remarks>
    /// 06:00 to 20:00 is the outer bound rather than the working day itself — wide enough that an early shift or a
    /// late release still fits inside it, narrow enough that the canvas is not mostly empty night. The sessions
    /// inside it are the ordinary French office day, which is what the seeded departments run.
    /// </remarks>
    public static readonly WorkingDay Default = new(
        new TimeOnly(6, 0),
        new TimeOnly(20, 0),
        new TimeOnly(9, 0),
        new TimeOnly(13, 0),
        new TimeOnly(14, 0),
        new TimeOnly(18, 0));

    public static WorkingDay Resolve(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return Default;
        }

        try
        {
            var configured = JsonSerializer.Deserialize<ConfiguredWorkingDay>(configJson);

            if (configured is null)
            {
                return Default;
            }

            var candidate = new WorkingDay(
                Parse(configured.DayStart) ?? Default.DayStart,
                Parse(configured.DayEnd) ?? Default.DayEnd,
                Parse(configured.Morning?.Start) ?? Default.MorningStart,
                Parse(configured.Morning?.End) ?? Default.MorningEnd,
                Parse(configured.Afternoon?.Start) ?? Default.AfternoonStart,
                Parse(configured.Afternoon?.End) ?? Default.AfternoonEnd);

            // A half-valid day is worse than the default: an afternoon that starts before it ends but sits outside
            // the bounds would put the quick-add's own preset off the axis it is drawn on.
            return IsCoherent(candidate) ? candidate : Default;
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    /// <summary>
    /// Every session ends after it starts, and both sit inside the day's bounds.
    /// </summary>
    /// <remarks>
    /// The settings screen refuses the same shapes at the door — see <c>DepartmentConfigValidator</c> — but it
    /// cannot call this: Directory may reference another module's contracts and nothing else of it, and this rule
    /// is domain. So the check exists twice on purpose, once to reject an edit and once to ignore a row that got
    /// in before the rule did. Change one and change the other.
    /// </remarks>
    public static bool IsCoherent(WorkingDay day) =>
        day.DayStart < day.DayEnd
        && day.MorningStart < day.MorningEnd
        && day.AfternoonStart < day.AfternoonEnd
        && day.MorningStart >= day.DayStart
        && day.AfternoonEnd <= day.DayEnd
        // The morning may run straight into the afternoon — a department without a lunch break is unusual, not
        // invalid — but it may not overlap it.
        && day.MorningEnd <= day.AfternoonStart;

    private static TimeOnly? Parse(string? value) =>
        TimeOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private sealed record ConfiguredWorkingDay
    {
        [JsonPropertyName("dayStart")]
        public string? DayStart { get; init; }

        [JsonPropertyName("dayEnd")]
        public string? DayEnd { get; init; }

        [JsonPropertyName("morning")]
        public ConfiguredSession? Morning { get; init; }

        [JsonPropertyName("afternoon")]
        public ConfiguredSession? Afternoon { get; init; }
    }

    private sealed record ConfiguredSession
    {
        [JsonPropertyName("start")]
        public string? Start { get; init; }

        [JsonPropertyName("end")]
        public string? End { get; init; }
    }
}
