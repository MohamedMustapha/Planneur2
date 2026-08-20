using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Domain;

namespace Cracra.Tests.Unit.Meetings;

/// <summary>
/// Placing a wall-clock meeting on the timeline, daylight saving included.
/// </summary>
/// <remarks>
/// The reason this is worth its own class: a 09:00 stand-up stored as a UTC instant silently moves by an hour
/// twice a year, and nobody notices until the Monday after the clocks change. Storing the wall clock is the fix,
/// and these are the cases that prove it works.
/// </remarks>
public sealed class MeetingScheduleTests
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    [Fact]
    public void A_nine_oclock_meeting_is_nine_oclock_in_winter_and_in_summer()
    {
        var winter = MeetingSchedule.ToInstant(new DateOnly(2026, 1, 12), new TimeOnly(9, 0), Paris);
        var summer = MeetingSchedule.ToInstant(new DateOnly(2026, 7, 13), new TimeOnly(9, 0), Paris);

        // One UTC hour apart, precisely because the local hour did not move. A UTC-anchored series would show
        // these as identical instants and therefore as 09:00 and 10:00 to the people attending.
        winter.ShouldBe(new DateTimeOffset(2026, 1, 12, 8, 0, 0, TimeSpan.Zero));
        summer.ShouldBe(new DateTimeOffset(2026, 7, 13, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_time_the_spring_forward_skipped_moves_to_the_first_one_that_exists()
    {
        // 02:30 on 29 March 2026 does not happen in Paris: the clocks go from 02:00 to 03:00.
        var instant = MeetingSchedule.ToInstant(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), Paris);

        // 03:30 local, which is 01:30 UTC. Refusing instead would mean a weekly series that throws once a year.
        instant.ShouldBe(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_time_the_autumn_fall_back_repeats_takes_the_first_of_the_two()
    {
        // 02:30 on 25 October 2026 happens twice in Paris. The earlier one is still on summer time, UTC+2.
        var instant = MeetingSchedule.ToInstant(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), Paris);

        instant.ShouldBe(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Expansion_carries_the_duration_onto_every_occurrence()
    {
        var series = Series("FREQ=WEEKLY;BYDAY=MO", new TimeOnly(9, 30), durationMinutes: 45);

        var slots = MeetingSchedule.Expand(
            series,
            new DateOnly(2026, 8, 17),
            new DateOnly(2026, 8, 31),
            Paris);

        slots.Count.ShouldBe(3);
        slots.ShouldAllBe(slot => slot.End - slot.Start == TimeSpan.FromMinutes(45));
        slots[0].Start.ShouldBe(new DateTimeOffset(2026, 8, 17, 7, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_series_spanning_the_clock_change_keeps_its_local_hour()
    {
        var series = Series("FREQ=WEEKLY;BYDAY=SU", new TimeOnly(9, 0), durationMinutes: 60);
        series.StartsOn = new DateOnly(2026, 10, 18);

        var slots = MeetingSchedule.Expand(
            series,
            new DateOnly(2026, 10, 18),
            new DateOnly(2026, 11, 1),
            Paris);

        // Three Sundays either side of the 25th. The UTC hour steps, the local hour does not — which is the whole
        // reason StartTime is a TimeOnly rather than part of a DateTimeOffset.
        slots.Select(slot => slot.Start).ShouldBe(
        [
            new DateTimeOffset(2026, 10, 18, 7, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero),
        ]);
    }

    private static MeetingSeries Series(string rule, TimeOnly startTime, int durationMinutes) => new()
    {
        Id = Guid.CreateVersion7(),
        Kind = MeetingKinds.Weekly,
        NameKey = "meetings.kind.weekly",
        ScopeType = MeetingScopeTypes.Unit,
        ScopeId = Guid.CreateVersion7(),
        RecurrenceRule = rule,
        StartsOn = new DateOnly(2026, 8, 17),
        StartTime = startTime,
        TimeZoneId = "Europe/Paris",
        DurationMinutes = durationMinutes,
        OwnerPersonId = Guid.CreateVersion7(),
        CreatedBy = Guid.CreateVersion7(),
    };
}
