using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Data;
using Cracra.Modules.Meetings.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cracra.Modules.Meetings.Services;

/// <summary>
/// Expands a series' recurrence into dated rows, and keeps them in step with the rule.
/// </summary>
/// <remarks>
/// <para>
/// The alternative was to expand on every read and never store anything. It is tempting — no materialization, no
/// horizon, no sweeper — and it fails on the two things S7 asks for: attendance needs an occurrence with an
/// identity to hang off, and a board window would have to expand every active series in the organization on every
/// request rather than run one indexed range scan.
/// </para>
/// <para>
/// So occurrences are rows, written by exactly two callers: the series service when a series is created or
/// changed, and the horizon sweeper rolling the window forward. Reads never write. That matters more than it
/// looks: a read that materializes would have to write under the reader's own RLS session, and the reader is
/// usually somebody with no write permission at all — a member opening their board would fail or, worse, be
/// granted a write policy wide enough to succeed.
/// </para>
/// </remarks>
public interface IOccurrenceMaterializer
{
    /// <summary>
    /// Brings a series' occurrences from <paramref name="from"/> to <paramref name="through"/> in line with its rule.
    /// </summary>
    /// <remarks>
    /// Adds what the rule now produces and removes what it no longer does, leaving anything before
    /// <paramref name="from"/> untouched. History is not rewritten: a meeting that happened, happened, and the
    /// notes attached to it stay attached even after somebody moves the series to Tuesdays.
    /// </remarks>
    Task<int> SynchronizeAsync(MeetingSeries series, DateOnly from, DateOnly through, CancellationToken ct);

    /// <summary>The instants a rule produces in a window, without touching the database. The unit-testable half.</summary>
    IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Expand(
        MeetingSeries series,
        DateOnly from,
        DateOnly through);
}

internal sealed class OccurrenceMaterializer(MeetingsDbContext context, ILogger<OccurrenceMaterializer> logger)
    : IOccurrenceMaterializer
{
    public async Task<int> SynchronizeAsync(
        MeetingSeries series,
        DateOnly from,
        DateOnly through,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(series);

        var boundary = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var existing = await context.Occurrences
            .Where(occurrence => occurrence.SeriesId == series.Id && occurrence.StartsAt >= boundary)
            .ToListAsync(ct);

        // A deactivated series keeps its past and loses its future. "Stopped happening" and "never happened" are
        // different facts, and only one of them is true.
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> expected = series.Active
            ? Expand(series, from, through)
            : [];

        var expectedByStart = expected.ToDictionary(slot => slot.Start);
        var existingByStart = existing
            .GroupBy(occurrence => occurrence.StartsAt)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var stale in existing.Where(occurrence => !expectedByStart.ContainsKey(occurrence.StartsAt)))
        {
            // Cascades to attendance. Correct: the instance is no longer on the calendar, so an RSVP to it is an
            // answer to a question nobody is asking any more.
            context.Occurrences.Remove(stale);
        }

        var added = 0;

        foreach (var (start, end) in expected)
        {
            if (existingByStart.TryGetValue(start, out var current))
            {
                // Kept, and re-stamped. Duration and scope live on the series; a copil that moved from the IS
                // department to the whole org must not leave rows behind that still say otherwise, because those
                // columns are what the RLS predicate reads.
                current.EndsAt = end;
                current.ScopeType = series.ScopeType;
                current.ScopeId = series.ScopeId;
                current.DepartmentId = series.DepartmentId;

                context.Occurrences.Update(current);

                continue;
            }

            context.Occurrences.Add(new MeetingOccurrence
            {
                Id = Guid.CreateVersion7(),
                SeriesId = series.Id,
                ScopeType = series.ScopeType,
                ScopeId = series.ScopeId,
                DepartmentId = series.DepartmentId,
                StartsAt = start,
                EndsAt = end,
                Status = OccurrenceStatuses.Scheduled,
            });

            added++;
        }

        return added;
    }

    public IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Expand(
        MeetingSeries series,
        DateOnly from,
        DateOnly through)
    {
        ArgumentNullException.ThrowIfNull(series);

        // The arithmetic itself lives in the domain, pure and static, so daylight saving can be tested without a
        // container. All this adds is resolving the zone, which is the one part that can fail on a given host.
        return MeetingSchedule.Expand(series, from, through, ZoneOf(series));
    }

    /// <summary>
    /// Resolves the series' zone, falling back to UTC rather than failing.
    /// </summary>
    /// <remarks>
    /// A zone id that a host cannot resolve is a configuration problem, not a reason for the board to 500. The
    /// meeting still exists and still has a time; it is shown an hour or two out until somebody fixes the id, and
    /// the log says so.
    /// </remarks>
    private TimeZoneInfo ZoneOf(MeetingSeries series)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(series.TimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning(
                exception,
                "Series {SeriesId} names time zone {TimeZoneId}, which this host cannot resolve. Falling back to UTC.",
                series.Id,
                series.TimeZoneId);

            return TimeZoneInfo.Utc;
        }
    }
}
