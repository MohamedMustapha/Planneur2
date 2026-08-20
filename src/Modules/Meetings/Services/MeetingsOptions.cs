using System.ComponentModel.DataAnnotations;

namespace Cracra.Modules.Meetings.Services;

/// <summary>How far ahead occurrences are kept materialized, and how often that window is rolled forward.</summary>
public sealed class MeetingsOptions
{
    public const string SectionName = "Cracra:Meetings";

    /// <summary>
    /// How far ahead the calendar is materialized.
    /// </summary>
    /// <remarks>
    /// Half a year. Long enough that nobody planning a quarter finds an empty calendar, short enough that a
    /// daily series is a few hundred rows rather than a few thousand. Everything past it is not missing, merely
    /// not written yet — the sweeper extends the window as time passes.
    /// </remarks>
    [Range(1, 260)]
    public int HorizonWeeks { get; set; } = 26;

    /// <summary>
    /// How often the sweeper rolls the horizon forward. Zero disables it.
    /// </summary>
    /// <remarks>
    /// Daily is plenty for a window measured in months, and tests set it to zero so a background pass cannot
    /// materialize rows in the middle of an assertion.
    /// </remarks>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Runs one sweep at startup. Off in tests, which drive the sweeper explicitly.</summary>
    public bool SweepOnStartup { get; set; } = true;

    /// <summary>
    /// How far back a newly created series is materialized.
    /// </summary>
    /// <remarks>
    /// A series is often recorded after it has been running for a while — somebody puts the existing copil into
    /// the platform in March, having held it since January — and its calendar should show those. Bounded because
    /// the alternative, materializing from the anchor whatever it is, turns a daily series anchored six years ago
    /// into a couple of thousand rows nobody will ever read. An <em>update</em> never reaches back at all: the
    /// past keeps its notes and its attendance.
    /// </remarks>
    [Range(0, 260)]
    public int BackfillWeeks { get; set; } = 26;

    /// <summary>The default zone a series' wall-clock time is read in when the caller does not name one.</summary>
    [Required]
    public string DefaultTimeZoneId { get; set; } = "Europe/Paris";

    /// <summary>How far ahead the "coming up" strip looks when the caller does not say.</summary>
    [Range(1, 90)]
    public int UpcomingDays { get; set; } = 14;
}
