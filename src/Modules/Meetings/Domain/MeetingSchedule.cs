namespace Cracra.Modules.Meetings.Domain;

/// <summary>
/// Turning a series into instants: the recurrence, the wall clock and the time zone, combined.
/// </summary>
/// <remarks>
/// Pure and static, separate from the materializer that persists what it produces. The materializer needs a
/// database; this needs nothing, which is what lets the awkward half — daylight saving — be tested directly
/// rather than through a container.
/// </remarks>
public static class MeetingSchedule
{
    /// <summary>The (start, end) instants a series produces inside a window, ascending.</summary>
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Expand(
        MeetingSeries series,
        DateOnly from,
        DateOnly through,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(series);

        var rule = RecurrenceRule.Parse(series.RecurrenceRule);
        var duration = TimeSpan.FromMinutes(series.DurationMinutes);

        var slots = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        foreach (var day in rule.Expand(series.StartsOn, from, through))
        {
            var start = ToInstant(day, series.StartTime, zone);

            slots.Add((start, start + duration));
        }

        return slots;
    }

    /// <summary>
    /// A local wall-clock time on a date, as an instant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wall clock is the fact and the instant is derived from it, which is the opposite of how every other
    /// timestamp in this system works (architecture.md §5). It has to be: a 09:00 stand-up is 09:00 in March and
    /// 09:00 in July, and anchoring it as a UTC instant would move it by an hour twice a year.
    /// </para>
    /// <para>
    /// Two awkward cases follow from that, and both happen twice a year. On the spring-forward day the wall-clock
    /// time may not exist at all — 02:30 in Paris in late March — and the meeting is pushed to the first instant
    /// that does, which is what a person reading "02:30" would do. On the autumn day it exists twice, and the
    /// first of the two is taken, because that is what a calendar client picks and disagreeing with every other
    /// calendar is worse than either choice on its own merits.
    /// </para>
    /// </remarks>
    public static DateTimeOffset ToInstant(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = DateTime.SpecifyKind(day.ToDateTime(time), DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            // The gap is an hour in every zone that has one, but reading the adjustment rule rather than assuming
            // costs nothing and survives the zones where it is not.
            var shift = zone.GetAdjustmentRules()
                .FirstOrDefault(rule => local >= rule.DateStart && local <= rule.DateEnd)?.DaylightDelta
                ?? TimeSpan.FromHours(1);

            local = local.Add(shift);
        }

        // Max of the ambiguous offsets is the earlier instant: a larger offset from UTC means the same wall clock
        // happened sooner. That is the summer-time reading, and it is the first of the two.
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
