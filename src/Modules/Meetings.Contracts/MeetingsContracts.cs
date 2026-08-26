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

    /// <summary>A node's weekly, as opposed to a unit's stand-up (v2 §07.1).</summary>
    public const string WeeklyNode = "weekly-node";

    /// <summary>The service-wide review. Distinct from a COPIL: it reports, where a COPIL decides.</summary>
    public const string ServiceReview = "service-review";

    public static readonly IReadOnlyList<string> All =
        [Weekly, Copil, Retro, OneOnOne, Custom, WeeklyNode, ServiceReview];

    /// <summary>Kinds where knowing who is coming actually matters, so the client offers the RSVP control.</summary>
    public static readonly IReadOnlyList<string> Attended = [Copil, OneOnOne, ServiceReview];
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
    bool Active,
    string Level,
    IReadOnlyList<Guid> ScopeIds);

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
    string? MyResponse,
    string Level,
    Guid? MinutesId,
    bool MinutesPublished);

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

    /// <summary>
    /// The meetings and special days falling inside a window, flat and ordered.
    /// </summary>
    /// <remarks>
    /// Added for S8, which needs "upcoming copil" and "audits and patch parties ahead" as report sections rather
    /// than as board decoration. Deliberately the same <see cref="UpcomingEntry"/> the strip already renders: a
    /// report that invented its own shape for the same rows would drift from the strip within a slice or two.
    ///
    /// Caller-scoped like everything else here, so a report can only ever mention meetings its reader may see.
    /// </remarks>
    Task<IReadOnlyList<UpcomingEntry>> GetInWindowAsync(DateOnly from, DateOnly to, CancellationToken ct);
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

// =================================================================================================================
// Meeting levels and minutes (v2 §07).
//
// A meeting has always had a scope — who it targets. What it lacked was a level: how wide it reaches. The two are
// not the same question, and collapsing them is why a weekly stand-up and a COPIL used to be indistinguishable to
// everything downstream. The scope still decides visibility; the level decides how the CR is distributed, how the
// brief composes upward, and which cadence a report is describing.
// =================================================================================================================

/// <summary>How wide a meeting reaches (v2 §07.1).</summary>
public static class MeetingLevels
{
    /// <summary>One unit's own. The stand-up.</summary>
    public const string Unit = "unit";

    /// <summary>One node's, whatever depth it sits at.</summary>
    public const string Node = "node";

    /// <summary>Several sibling nodes together. <c>ScopeIds</c> names them.</summary>
    public const string CrossNode = "cross-node";

    /// <summary>The whole service. The COPIL case.</summary>
    public const string Service = "service";

    /// <summary>One project's team, across every contributing node.</summary>
    public const string Project = "project";

    public static readonly IReadOnlyList<string> All = [Unit, Node, CrossNode, Service, Project];

    /// <summary>True where the level targets several nodes at once and therefore needs the array.</summary>
    public static bool IsMultiScope(string level) => string.Equals(level, CrossNode, StringComparison.Ordinal);
}

/// <summary>What an action item points at, so a CR stays connected to real work (v2 §07.1).</summary>
public static class ActionLinkTypes
{
    public const string None = "none";
    public const string Problem = "problem";
    public const string Item = "item";
    public const string Objective = "objective";

    public static readonly IReadOnlyList<string> All = [None, Problem, Item, Objective];
}

public static class ActionStatuses
{
    public const string Open = "open";
    public const string Done = "done";
    public const string Dropped = "dropped";

    public static readonly IReadOnlyList<string> All = [Open, Done, Dropped];
}

public sealed record DecisionView(Guid Id, string Text, string? Rationale, string? DecidedBy);

/// <param name="Overdue">
/// Computed server-side against the same clock the tracker sorts by. A client working it out from the due date
/// would disagree with the badge count on the overview the moment somebody's timezone differed.
/// </param>
public sealed record ActionItemView(
    Guid Id,
    Guid MinutesId,
    string Title,
    Guid OwnerPersonId,
    string? OwnerName,
    DateOnly? Due,
    string Status,
    string LinkType,
    Guid? LinkId,
    bool Overdue);

/// <param name="Attendees">Person ids, resolved to names by the client through the directory it already loads.</param>
public sealed record MinutesView(
    Guid Id,
    Guid OccurrenceId,
    Guid SeriesId,
    string Level,
    string ScopeType,
    Guid? ScopeId,
    DateTimeOffset OccurredAt,
    Guid AuthorPersonId,
    string? AuthorName,
    string? Agenda,
    IReadOnlyList<Guid> Attendees,
    IReadOnlyList<Guid> Absentees,
    string? Summary,
    bool Published,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<DecisionView> Decisions,
    IReadOnlyList<ActionItemView> Actions);

/// <summary>One line of the "Derniers CR" strip: enough to recognise a minute and open it.</summary>
public sealed record MinutesDigest(
    Guid Id,
    Guid OccurrenceId,
    string Level,
    string NameKey,
    DateTimeOffset OccurredAt,
    string? Summary,
    int DecisionCount,
    int OpenActionCount,
    bool Published);

public sealed record MinutesPublished(
    Guid MinutesId,
    Guid OccurrenceId,
    string Level,
    string ScopeType,
    Guid? ScopeId,
    IReadOnlyList<Guid> ScopeIds) : IntegrationEvent;

public sealed record ActionItemClosed(Guid ActionId, Guid MinutesId, Guid OwnerPersonId, string LinkType)
    : IntegrationEvent;

/// <summary>The caller's own open actions, for the shell's obligation chip (v2 02.2).</summary>
public interface IActionItemReader
{
    Task<IReadOnlyList<ActionItemView>> GetOpenForCallerAsync(CancellationToken ct);
}
