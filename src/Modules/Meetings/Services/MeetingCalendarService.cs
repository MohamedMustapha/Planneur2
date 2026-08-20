using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Data;
using Cracra.Modules.Meetings.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Meetings.Services;

/// <summary>
/// The read side: occurrences in a window, the overlays boards draw, and the "coming up" strip.
/// </summary>
/// <remarks>
/// One service for all three because they are one query with three shapes. Splitting them would give three places
/// that each decide what "upcoming" means, and they would not stay in agreement.
/// </remarks>
public interface IMeetingCalendarService
{
    Task<IReadOnlyList<MeetingOccurrenceView>> GetOccurrencesAsync(
        DateOnly from,
        DateOnly to,
        string? scopeType,
        Guid? scopeId,
        CancellationToken ct);

    /// <summary>Meetings and special days between now and <paramref name="days"/> ahead, in one ordered list.</summary>
    Task<IReadOnlyList<UpcomingEntry>> GetUpcomingAsync(int? days, CancellationToken ct);

    /// <summary>The same list for an arbitrary window rather than from now — what S8's report sections read.</summary>
    Task<IReadOnlyList<UpcomingEntry>> GetInWindowAsync(DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>Records the caller's own answer, and nobody else's.</summary>
    Task RespondAsync(Guid occurrenceId, string response, CancellationToken ct);
}

internal sealed class MeetingCalendarService(
    MeetingsDbContext context,
    IUserContext user,
    IOptions<MeetingsOptions> options) : IMeetingCalendarService, IMeetingCalendarReader
{
    /// <summary>
    /// Board colours per severity, and one for meetings.
    /// </summary>
    /// <remarks>
    /// CSS custom properties rather than literal colours, exactly as the board composer does for activity
    /// buckets: the palette is the design's to change, in one file, for both themes at once. A hex here would be
    /// a colour that is right in light mode and unreadable in dark.
    /// </remarks>
    private static readonly Dictionary<string, string> SeverityColors = new(StringComparer.Ordinal)
    {
        [SpecialDaySeverities.Info] = "var(--event-info)",
        [SpecialDaySeverities.Warning] = "var(--event-warning)",
        [SpecialDaySeverities.Critical] = "var(--event-critical)",
    };

    private readonly MeetingsOptions settings = options.Value;

    public async Task<IReadOnlyList<MeetingOccurrenceView>> GetOccurrencesAsync(
        DateOnly from,
        DateOnly to,
        string? scopeType,
        Guid? scopeId,
        CancellationToken ct)
    {
        if (to < from)
        {
            throw new DomainRuleViolationException("The end of the window cannot be before its start.");
        }

        var normalized = string.IsNullOrWhiteSpace(scopeType) ? null : scopeType.Trim().ToLowerInvariant();
        var (start, end) = Window(from, to);

        var rows = await context.Occurrences
            .Where(occurrence => occurrence.StartsAt >= start && occurrence.StartsAt < end)
            .Where(occurrence => normalized == null || occurrence.ScopeType == normalized)
            .Where(occurrence => scopeId == null || occurrence.ScopeId == scopeId)
            .Join(
                context.Series,
                occurrence => occurrence.SeriesId,
                series => series.Id,
                (occurrence, series) => new { occurrence, series })
            .OrderBy(row => row.occurrence.StartsAt)
            .ToListAsync(ct);

        var ids = rows.Select(row => row.occurrence.Id).ToList();

        // One round trip rather than a correlated subquery per row — the same N+1 the directory's people query
        // avoids, and a copil-heavy fortnight is exactly where it would show.
        var mine = await context.Attendance
            .Where(attendance => attendance.PersonId == user.UserId && ids.Contains(attendance.OccurrenceId))
            .ToDictionaryAsync(attendance => attendance.OccurrenceId, attendance => attendance.Response, ct);

        return
        [
            .. rows.Select(row => new MeetingOccurrenceView(
                row.occurrence.Id,
                row.series.Id,
                row.series.Kind,
                row.series.NameKey,
                row.occurrence.ScopeType,
                row.occurrence.ScopeId,
                row.occurrence.StartsAt,
                row.occurrence.EndsAt,
                row.occurrence.Status,
                row.series.Location,
                row.series.VideoLink,
                row.occurrence.NotesRef,
                mine.GetValueOrDefault(row.occurrence.Id))),
        ];
    }

    public async Task<IReadOnlyList<UpcomingEntry>> GetUpcomingAsync(int? days, CancellationToken ct)
    {
        var horizon = Math.Clamp(days ?? settings.UpcomingDays, 1, 90);
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var until = today.AddDays(horizon);

        // From now rather than from midnight: a strip called "coming up" that still lists this morning's
        // stand-up at four in the afternoon is a strip people stop reading.
        var meetings = await context.Occurrences
            .Where(occurrence => occurrence.StartsAt >= now)
            .Where(occurrence => occurrence.StartsAt < new DateTimeOffset(until.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            .Where(occurrence => occurrence.Status == OccurrenceStatuses.Scheduled)
            .Join(
                context.Series,
                occurrence => occurrence.SeriesId,
                series => series.Id,
                (occurrence, series) => new { occurrence, series })
            .OrderBy(row => row.occurrence.StartsAt)
            .Take(50)
            .ToListAsync(ct);

        var specialDays = await context.SpecialDays
            .Where(day => day.Date >= today && day.Date <= until)
            .OrderBy(day => day.Date)
            .Take(50)
            .ToListAsync(ct);

        return Merge(meetings.Select(row => (row.occurrence, row.series)), specialDays);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UpcomingEntry>> GetInWindowAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (to < from)
        {
            throw new DomainRuleViolationException("The end of the window cannot be before its start.");
        }

        var (start, end) = Window(from, to);

        // Cancelled occurrences are excluded here as they are everywhere else. A report listing a meeting that was
        // called off would have somebody preparing for it.
        var meetings = await context.Occurrences
            .Where(occurrence => occurrence.StartsAt >= start && occurrence.StartsAt < end)
            .Where(occurrence => occurrence.Status == OccurrenceStatuses.Scheduled)
            .Join(
                context.Series,
                occurrence => occurrence.SeriesId,
                series => series.Id,
                (occurrence, series) => new { occurrence, series })
            .OrderBy(row => row.occurrence.StartsAt)
            .Take(200)
            .ToListAsync(ct);

        var specialDays = await context.SpecialDays
            .Where(day => day.Date >= from && day.Date <= to)
            .OrderBy(day => day.Date)
            .Take(200)
            .ToListAsync(ct);

        return Merge(meetings.Select(row => (row.occurrence, row.series)), specialDays);
    }

    public async Task RespondAsync(Guid occurrenceId, string response, CancellationToken ct)
    {
        var answer = (response ?? string.Empty).Trim().ToLowerInvariant();

        if (!AttendanceResponses.All.Contains(answer, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException(
                $"'{response}' is not an answer. Use one of: {string.Join(", ", AttendanceResponses.All)}.");
        }

        var occurrence = await context.Occurrences
            .SingleOrDefaultAsync(candidate => candidate.Id == occurrenceId, ct)
            ?? throw new ResourceNotFoundException("That meeting does not exist.");

        if (occurrence.Status != OccurrenceStatuses.Scheduled)
        {
            throw new DomainRuleViolationException("That meeting has been cancelled.");
        }

        var existing = await context.Attendance.AsTracking().SingleOrDefaultAsync(
            attendance => attendance.OccurrenceId == occurrenceId && attendance.PersonId == user.UserId,
            ct);

        if (existing is null)
        {
            context.Attendance.Add(new MeetingAttendance
            {
                OccurrenceId = occurrenceId,
                // The caller's own id, never one from the request. There is no delegation in S7 and answering on
                // somebody else's behalf is not a gap to be filled later — it is a thing this endpoint must not do.
                PersonId = user.UserId,
                Response = answer,
                RespondedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.Response = answer;
            existing.RespondedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarOverlay>> GetOverlaysAsync(
        Guid? unitId,
        Guid? departmentId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var (start, end) = Window(from, to);

        var meetings = await context.Occurrences
            .Where(occurrence => occurrence.StartsAt >= start && occurrence.StartsAt < end)
            .Where(occurrence => occurrence.Status == OccurrenceStatuses.Scheduled)
            // Narrowed to the board that asked. Not a security filter — RLS settled that before this query ran —
            // but a relevance one: a department head sees every unit's stand-up, and drawing all of them on one
            // unit's board would bury the audit day that board exists to show.
            .Where(occurrence =>
                (unitId == null && departmentId == null)
                || occurrence.ScopeType == MeetingScopeTypes.Org
                || (unitId != null && occurrence.ScopeType == MeetingScopeTypes.Unit && occurrence.ScopeId == unitId)
                || (unitId != null && occurrence.ScopeType != MeetingScopeTypes.Unit)
                || (departmentId != null && occurrence.DepartmentId == departmentId))
            .Join(
                context.Series,
                occurrence => occurrence.SeriesId,
                series => series.Id,
                (occurrence, series) => new { occurrence.StartsAt, occurrence.Id, series.NameKey, series.Kind })
            .ToListAsync(ct);

        var specialDays = await context.SpecialDays
            .Where(day => day.Date >= from && day.Date <= to)
            .Where(day =>
                (unitId == null && departmentId == null)
                || day.ScopeType == MeetingScopeTypes.Org
                || (unitId != null && day.ScopeType == MeetingScopeTypes.Unit && day.ScopeId == unitId)
                || (unitId != null && day.ScopeType != MeetingScopeTypes.Unit)
                || (departmentId != null && day.DepartmentId == departmentId))
            .ToListAsync(ct);

        // Meetings first, special days after. Overlapping ranges resolve last-wins on the canvas, and an audit
        // day must not be painted over by the stand-up that happens to fall on it.
        return
        [
            .. meetings
                .OrderBy(meeting => meeting.StartsAt)
                .Select(meeting => new CalendarOverlay(
                    meeting.Id.ToString(),
                    "meeting",
                    meeting.NameKey,
                    DateOnly.FromDateTime(meeting.StartsAt.UtcDateTime),
                    DateOnly.FromDateTime(meeting.StartsAt.UtcDateTime),
                    "var(--event-meeting)")),
            .. specialDays
                .OrderBy(day => day.Date)
                .Select(day => new CalendarOverlay(
                    day.Id.ToString(),
                    day.Kind,
                    day.NameKey,
                    day.Date,
                    day.Date,
                    SeverityColors.GetValueOrDefault(day.Severity, "var(--event-info)"))),
        ];
    }

    /// <summary>
    /// Flattens meetings and special days into one ordered list.
    /// </summary>
    /// <remarks>
    /// Shared by the dashboard strip and by S8's report sections, because they are the same question asked over
    /// different windows. Two copies would drift on exactly the detail that matters — whether a special day is an
    /// instant or a date — and the report and the strip would then disagree about the same day.
    /// </remarks>
    private static IReadOnlyList<UpcomingEntry> Merge(
        IEnumerable<(MeetingOccurrence Occurrence, MeetingSeries Series)> meetings,
        IEnumerable<SpecialDay> specialDays)
    {
        IEnumerable<UpcomingEntry> entries =
        [
            .. meetings.Select(row => new UpcomingEntry(
                row.Occurrence.Id.ToString(),
                row.Series.Kind,
                row.Series.NameKey,
                row.Occurrence.ScopeType,
                row.Occurrence.StartsAt,
                AllDay: false,
                Severity: null,
                row.Series.Location,
                row.Series.VideoLink)),
            .. specialDays.Select(day => new UpcomingEntry(
                day.Id.ToString(),
                day.Kind,
                day.NameKey,
                day.ScopeType,
                // Midnight UTC. A special day is a date, not an instant, and AllDay tells the client to render it
                // as one rather than as "00:00" in whatever zone the browser is in.
                new DateTimeOffset(day.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                day.AllDay,
                day.Severity,
                Location: null,
                VideoLink: null)),
        ];

        return [.. entries.OrderBy(entry => entry.At).ThenBy(entry => entry.NameKey, StringComparer.Ordinal)];
    }

    /// <summary>
    /// A date window as instants.
    /// </summary>
    /// <remarks>
    /// Half-open, ending at midnight after <paramref name="to"/>: a meeting at 18:00 on the last day of the week
    /// belongs to that week, and an inclusive end at midnight would drop it.
    /// </remarks>
    private static (DateTimeOffset Start, DateTimeOffset End) Window(DateOnly from, DateOnly to) =>
        (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
         new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
}
