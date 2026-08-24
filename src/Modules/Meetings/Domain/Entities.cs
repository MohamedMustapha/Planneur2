using Cracra.Modules.Meetings.Contracts;

namespace Cracra.Modules.Meetings.Domain;

// =================================================================================================================
// Meetings is a 2-layer module (conventions.md §2): tables, forms, and a couple of local rules. There are no
// aggregates here and no domain events — the one genuinely interesting piece of behaviour, expanding a recurrence
// rule into dates, is a pure function and lives in RecurrenceRule beside these.
//
// "Domain" is still the folder these sit in, because the architecture test that keeps a Domain namespace free of
// EF and ASP.NET is worth having even where the domain is thin.
// =================================================================================================================

/// <summary>
/// A recurring meeting: a weekly stand-up, a copil, a retro.
/// </summary>
/// <remarks>
/// <para>
/// A series is a rule, not a list. What is stored is the recurrence and its anchor; the dated instances are
/// materialized from it (see <see cref="MeetingOccurrence"/>) so that changing "every Monday" to "every other
/// Tuesday" is one row rather than a year of rows to rewrite.
/// </para>
/// <para>
/// The scope pair is the whole targeting model. It answers three questions at once — who attends, who may edit,
/// and who may see it — and keeping them one answer is what stops the board and the meeting manager disagreeing
/// about whose copil this is.
/// </para>
/// </remarks>
public sealed class MeetingSeries
{
    public required Guid Id { get; init; }

    /// <summary>weekly / copil / retro / one-on-one / custom, or whatever a department added. See <see cref="MeetingKinds"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>A Transloco key or free text — see <see cref="MeetingSeriesView.NameKey"/>.</summary>
    public required string NameKey { get; set; }

    /// <summary>unit / department / project / org — <see cref="MeetingScopeTypes"/>.</summary>
    public required string ScopeType { get; set; }

    /// <summary>
    /// How wide the meeting reaches: unit / node / cross-node / service / project (v2 §07.1).
    /// </summary>
    /// <remarks>
    /// Not a duplicate of <see cref="ScopeType"/>, which answers a different question. The scope says <em>who</em>
    /// the meeting targets and is what RLS reads; the level says <em>how wide</em> it is, and is what decides how
    /// its CR is distributed, how a brief composes upward, and which cadence a report is describing. Before this
    /// existed, a unit stand-up and a service COPIL were indistinguishable to everything downstream.
    /// </remarks>
    public string Level { get; set; } = MeetingLevels.Node;

    /// <summary>
    /// The child nodes a cross-node series targets, beyond its own scope. Empty at every other level.
    /// </summary>
    /// <remarks>
    /// Three bureaux meeting about a shared platform is the one case where "who is this for" is genuinely a list.
    /// Forcing it into a single scope id would either hide the CR from two of them or publish it to the whole
    /// service, and both answers are wrong in a way somebody notices immediately.
    /// </remarks>
    public Guid[] ScopeIds { get; set; } = [];

    /// <summary>The targeted unit, department or project. Null, and only null, for an org-wide series.</summary>
    public Guid? ScopeId { get; set; }

    /// <summary>
    /// The department this series belongs to, resolved when it is written.
    /// </summary>
    /// <remarks>
    /// Denormalized so the RLS predicate can answer "is this in a department I head" without an access function
    /// reaching into <c>directory.unit</c>. S2 is explicit that a predicate crossing into another module's schema
    /// is a coupling no architecture test can see; carrying the answer on the row keeps the predicate local.
    /// Null for project- and org-scoped series, which are not a single department's to begin with.
    /// </remarks>
    public Guid? DepartmentId { get; set; }

    /// <summary>iCal RRULE. See <see cref="RecurrenceRule"/> for the supported subset and why it is a subset.</summary>
    public required string RecurrenceRule { get; set; }

    /// <summary>
    /// The first candidate date. An RRULE describes a pattern relative to a start and cannot supply one, so this
    /// is not redundant with the rule — "every Monday" is meaningless without saying from when.
    /// </summary>
    public required DateOnly StartsOn { get; set; }

    /// <summary>
    /// Local wall-clock start, in <see cref="TimeZoneId"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately not a UTC instant, even though every other timestamp in the system is (architecture.md §5).
    /// A 09:00 stand-up is 09:00 in March and 09:00 in July; anchoring it in UTC would move it by an hour twice a
    /// year. The wall-clock time is the fact, and the instant is derived from it at expansion.
    /// </remarks>
    public required TimeOnly StartTime { get; set; }

    /// <summary>IANA zone the wall-clock time is read in. Defaults to the org zone.</summary>
    public string TimeZoneId { get; set; } = "Europe/Paris";

    public required int DurationMinutes { get; set; }

    /// <summary>Whoever runs it. Not necessarily whoever created it, and not an access-control input.</summary>
    public required Guid OwnerPersonId { get; set; }

    public string? Location { get; set; }

    public string? VideoLink { get; set; }

    /// <summary>
    /// False stops future occurrences without erasing the past ones.
    /// </summary>
    /// <remarks>
    /// A copil that ran for two years and stopped is history somebody will ask about; deleting the series would
    /// take its occurrences, their notes and their attendance with it.
    /// </remarks>
    public bool Active { get; set; } = true;

    public required Guid CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public ICollection<MeetingOccurrence> Occurrences { get; } = [];
}

/// <summary>
/// One dated instance of a series.
/// </summary>
/// <remarks>
/// Materialized rather than computed at read time, for two reasons that pull the same way. Attendance and notes
/// need something stable to hang off, and a computed instance has no identity to reference. And a board asking
/// for a window would otherwise have to expand every active series on every request, which turns a cheap indexed
/// range scan into an expansion of rules that mostly produce nothing in that window.
/// </remarks>
public sealed class MeetingOccurrence
{
    public required Guid Id { get; init; }

    public required Guid SeriesId { get; init; }

    /// <summary>
    /// Copied from the series when the occurrence is materialized.
    /// </summary>
    /// <remarks>
    /// The alternative — an EXISTS against the series in the occurrence's policy — was the first design and is
    /// the more obviously-correct one. It is also a subquery per candidate row on the single query every board in
    /// the system runs. These three columns are written only by the materializer, and the materializer rebuilds
    /// the future whenever the series changes, so they cannot drift.
    /// </remarks>
    public required string ScopeType { get; set; }

    public Guid? ScopeId { get; set; }

    public Guid? DepartmentId { get; set; }

    public required DateTimeOffset StartsAt { get; set; }

    public required DateTimeOffset EndsAt { get; set; }

    /// <summary>scheduled / cancelled — <see cref="OccurrenceStatuses"/>.</summary>
    public string Status { get; set; } = OccurrenceStatuses.Scheduled;

    /// <summary>Pointer to wherever the minutes live — a wiki page, a RustFS object. Opaque to this module.</summary>
    public string? NotesRef { get; set; }

    public MeetingSeries? Series { get; set; }

    public ICollection<MeetingAttendance> Attendance { get; } = [];
}

/// <summary>
/// Who said they were coming.
/// </summary>
/// <remarks>
/// Optional by design, and offered only for the kinds where it changes anything (<see cref="MeetingKinds.Attended"/>).
/// S7 puts full RSVP workflows out of scope: there is no invitation, no delegation and no organiser view of who
/// has not answered — a person records their own answer, and that is all.
/// </remarks>
public sealed class MeetingAttendance
{
    public required Guid OccurrenceId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>accepted / declined / tentative — <see cref="AttendanceResponses"/>.</summary>
    public required string Response { get; set; }

    public DateTimeOffset RespondedAt { get; set; }

    public MeetingOccurrence? Occurrence { get; set; }
}

/// <summary>
/// A dated event that is not a meeting: a patch party, an audit, a go-live, a freeze, a deadline.
/// </summary>
/// <remarks>
/// Not modelled as a one-off <see cref="MeetingSeries"/>, although it could have been. A special day is read
/// differently — it colours a board column rather than occupying an hour, it carries a severity, and nobody
/// attends it — and collapsing the two would mean every consumer re-deriving which sort of thing it had.
/// </remarks>
public sealed class SpecialDay
{
    public required Guid Id { get; init; }

    /// <summary>patch-party / audit / go-live / … — <see cref="SpecialDayKinds"/>.</summary>
    public required string Kind { get; set; }

    public required string NameKey { get; set; }

    public required string ScopeType { get; set; }

    public Guid? ScopeId { get; set; }

    /// <summary>Denormalized for the RLS predicate, exactly as on <see cref="MeetingSeries"/>.</summary>
    public Guid? DepartmentId { get; set; }

    public required DateOnly Date { get; set; }

    /// <summary>
    /// False lets a day carry a time window without becoming a meeting.
    /// </summary>
    /// <remarks>
    /// A patch party running 20:00–23:00 is still a special day: nobody books hours against it and it belongs on
    /// the board as a marked column, not as an event on someone's row.
    /// </remarks>
    public bool AllDay { get; set; } = true;

    /// <summary>info / warning / critical — <see cref="SpecialDaySeverities"/>. Drives the board colour.</summary>
    public string Severity { get; set; } = SpecialDaySeverities.Info;

    /// <summary>Free text. Unlike <see cref="NameKey"/> this is never a key — it is somebody's explanation.</summary>
    public string? Description { get; set; }

    public required Guid CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }
}
