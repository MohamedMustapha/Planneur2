using Cracra.BuildingBlocks.Messaging;

namespace Cracra.Modules.Meetings.Contracts;

// =================================================================================================================
// The Meetings module's public surface. Other modules may reference this assembly and nothing else of Meetings'
// (architecture.md §2). Everything here is additive-only once released: another module is deserializing these from
// outbox rows written before the current deploy.
// =================================================================================================================

/// <summary>
/// Who a series or a special day is aimed at.
/// </summary>
/// <remarks>
/// Stored as a stable code, never localized (conventions.md §5). The pair (scope type, scope id) is the whole of
/// the targeting model: it decides who attends, who may edit, and — through <c>access.can_read_meeting</c> — who
/// sees the row at all.
/// </remarks>
public static class MeetingScopeTypes
{
    /// <summary>One unit. The stand-up case.</summary>
    public const string Unit = "unit";

    /// <summary>A whole department. The copil and the patch-party case.</summary>
    public const string Department = "department";

    /// <summary>One project's team, across every contributing department.</summary>
    public const string Project = "project";

    /// <summary>Everybody. <see cref="ScopeId"/> is null; only the PMO may create one.</summary>
    public const string Org = "org";

    public static readonly IReadOnlyList<string> All = [Unit, Department, Project, Org];

    /// <summary>True where this scope type must carry a target id. Org is the one that must not.</summary>
    public static bool RequiresScopeId(string scopeType) =>
        !string.Equals(scopeType, Org, StringComparison.Ordinal);
}

/// <summary>
/// The meeting kinds the platform names.
/// </summary>
/// <remarks>
/// Not an enum. S7 states the list is extensible per department, and an enum would make "we also run a
/// bilan-mensuel" a code change — which is precisely the department-agnostic promise the platform makes. The
/// codes below are the ones the UI offers by default and the ones reporting knows how to talk about; anything
/// else is stored verbatim and rendered through its own key.
/// </remarks>
public static class MeetingKinds
{
    public const string Weekly = "weekly";

    /// <summary>Comité de pilotage — the steering meeting (glossary).</summary>
    public const string Copil = "copil";

    public const string Retro = "retro";
    public const string OneOnOne = "one-on-one";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> All = [Weekly, Copil, Retro, OneOnOne, Custom];

    /// <summary>Kinds where knowing who is coming actually matters, so the client offers the RSVP control.</summary>
    public static readonly IReadOnlyList<string> Attended = [Copil, OneOnOne];
}

/// <summary>The special-day kinds. Extensible for the same reason <see cref="MeetingKinds"/> is.</summary>
public static class SpecialDayKinds
{
    public const string PatchParty = "patch-party";
    public const string Audit = "audit";
    public const string GoLive = "go-live";
    public const string Deadline = "deadline";
    public const string Freeze = "freeze";
    public const string Holiday = "holiday";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> All =
        [PatchParty, Audit, GoLive, Deadline, Freeze, Holiday, Custom];
}

/// <summary>
/// How loudly a special day shows on a board.
/// </summary>
/// <remarks>
/// Three levels rather than five: severity drives a colour and nothing else, and a scale finer than the eye can
/// distinguish invites arguments about whether an audit is a 3 or a 4.
/// </remarks>
public static class SpecialDaySeverities
{
    /// <summary>A holiday, a go-live already agreed. Context.</summary>
    public const string Info = "info";

    /// <summary>A patch party, a freeze. Plan around it.</summary>
    public const string Warning = "warning";

    /// <summary>An audit, a hard deadline. Something is due.</summary>
    public const string Critical = "critical";

    public static readonly IReadOnlyList<string> All = [Info, Warning, Critical];
}

/// <summary>Whether a materialized occurrence is still going ahead.</summary>
public static class OccurrenceStatuses
{
    public const string Scheduled = "scheduled";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Scheduled, Cancelled];
}

/// <summary>An attendee's answer. Tentative exists because a copil invitation is rarely a yes or a no.</summary>
public static class AttendanceResponses
{
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Tentative = "tentative";

    public static readonly IReadOnlyList<string> All = [Accepted, Declined, Tentative];
}

// --- Read DTOs ---------------------------------------------------------------------------------------------------

/// <summary>
/// A recurring meeting.
/// </summary>
/// <param name="NameKey">
/// A Transloco key for a standard series, free text for one somebody named. The client renders the key when it
/// resolves and the literal when it does not — the same fallback the board timeline already uses for bucket
/// labels, so a department calling their copil "Comité SI" is not forced through the dictionary.
/// </param>
/// <param name="RecurrenceRule">iCal RRULE. See <c>meetings.recurrence_rule</c> for the supported subset.</param>
/// <param name="StartsOn">The anchor date. An RRULE places occurrences relative to a start; it cannot supply one.</param>
/// <param name="StartTime">Local wall-clock time in <paramref name="TimeZoneId"/>, not UTC — see the entity.</param>
public sealed record MeetingSeriesView(
    Guid Id,
    string Kind,
    string NameKey,
    string ScopeType,
    Guid? ScopeId,
    string RecurrenceRule,
    DateOnly StartsOn,
    TimeOnly StartTime,
    string TimeZoneId,
    int DurationMinutes,
    Guid OwnerPersonId,
    string? OwnerName,
    string? Location,
    string? VideoLink,
    bool Active);

/// <summary>One dated instance of a series.</summary>
/// <param name="MyResponse">The caller's own answer, or null if they have not been asked or have not replied.</param>
public sealed record MeetingOccurrenceView(
    Guid Id,
    Guid SeriesId,
    string Kind,
    string NameKey,
    string ScopeType,
    Guid? ScopeId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string? Location,
    string? VideoLink,
    string? NotesRef,
    string? MyResponse);

public sealed record SpecialDayView(
    Guid Id,
    string Kind,
    string NameKey,
    string ScopeType,
    Guid? ScopeId,
    DateOnly Date,
    bool AllDay,
    string Severity,
    string? Description);

/// <summary>
/// Something to draw across a board rather than on one of its rows.
/// </summary>
/// <remarks>
/// Meetings' own shape, deliberately not Scheduling's <c>BoardOverlay</c>. The dependency runs Scheduling →
/// Meetings; having Meetings return Scheduling's type would invert it and make the calendar unusable by anything
/// that is not a board — Reporting wants the same list for "upcoming deadlines".
/// </remarks>
public sealed record CalendarOverlay(
    string Id,
    string Kind,
    string Title,
    DateOnly From,
    DateOnly To,
    string? Color);

/// <summary>One entry of the "coming up" strip: a meeting occurrence or a special day, flattened.</summary>
/// <param name="Severity">Set for special days, null for meetings — a meeting has no severity to render.</param>
public sealed record UpcomingEntry(
    string Id,
    string Kind,
    string NameKey,
    string ScopeType,
    DateTimeOffset At,
    bool AllDay,
    string? Severity,
    string? Location,
    string? VideoLink);

// --- Read port for other modules ---------------------------------------------------------------------------------

/// <summary>
/// The calendar, as the rest of the system consumes it.
/// </summary>
/// <remarks>
/// Caller-scoped like every other cross-module reader: each call runs inside the caller's RLS session, so a board
/// composed for a member cannot pick up a meeting the member may not see. The consumer never restates the
/// visibility rule, which is the only way the board and the meeting manager can be guaranteed to agree.
/// </remarks>
public interface IMeetingCalendarReader
{
    /// <summary>
    /// What to draw over a board window.
    /// </summary>
    /// <remarks>
    /// Both arguments are hints about which board is asking, not filters that widen anything: RLS has already
    /// decided what this caller may see, and passing a unit they are not in simply returns less.
    /// </remarks>
    Task<IReadOnlyList<CalendarOverlay>> GetOverlaysAsync(
        Guid? unitId,
        Guid? departmentId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}

// --- Integration events ------------------------------------------------------------------------------------------

/// <summary>
/// Raised when a series is created or its schedule changes, so Scheduling can drop whatever it cached and
/// Reporting can restate the cadence.
/// </summary>
public sealed record MeetingScheduled(
    Guid SeriesId,
    string Kind,
    string ScopeType,
    Guid? ScopeId,
    string RecurrenceRule) : IntegrationEvent;

/// <summary>Raised when a series is deactivated or deleted. Consumers must not cascade a delete off this.</summary>
public sealed record MeetingSeriesCancelled(Guid SeriesId, string ScopeType, Guid? ScopeId) : IntegrationEvent;

/// <summary>
/// Raised on every special-day write.
/// </summary>
/// <remarks>
/// Upserted rather than Created and Updated: consumers of this event redraw a window, and both cases mean exactly
/// the same thing to them. Two events would give every consumer the same two identical handlers.
/// </remarks>
public sealed record SpecialDayUpserted(
    Guid SpecialDayId,
    string Kind,
    string ScopeType,
    Guid? ScopeId,
    DateOnly Date,
    string Severity) : IntegrationEvent;

public sealed record SpecialDayRemoved(Guid SpecialDayId, string ScopeType, Guid? ScopeId) : IntegrationEvent;
