using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Data;
using Cracra.Modules.Meetings.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Meetings.Services;

/// <summary>What the caller supplies to create or reshape a series. Same shape both ways — there is nothing to patch.</summary>
public sealed record MeetingSeriesRequest(
    string Kind,
    string NameKey,
    string ScopeType,
    Guid? ScopeId,
    string RecurrenceRule,
    DateOnly StartsOn,
    TimeOnly StartTime,
    string? TimeZoneId,
    int DurationMinutes,
    Guid? OwnerPersonId,
    string? Location,
    string? VideoLink,
    bool Active,
    string? Level,
    IReadOnlyList<Guid>? ScopeIds);

/// <summary>
/// The recurring half of S7.
/// </summary>
/// <remarks>
/// Not one method here filters by role or by scope. RLS has already removed every series the caller may not see,
/// and the write policy refuses every one they may not change; a second check in C# would be either redundant or
/// subtly different, and then nobody could say which was authoritative (conventions.md §3).
/// </remarks>
public interface IMeetingSeriesService
{
    Task<IReadOnlyList<MeetingSeriesView>> ListAsync(
        string? scopeType,
        Guid? scopeId,
        bool includeInactive,
        CancellationToken ct);

    Task<MeetingSeriesView> GetAsync(Guid id, CancellationToken ct);

    Task<MeetingSeriesView> CreateAsync(MeetingSeriesRequest request, CancellationToken ct);

    Task<MeetingSeriesView> UpdateAsync(Guid id, MeetingSeriesRequest request, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}

internal sealed class MeetingSeriesService(
    MeetingsDbContext context,
    IMeetingScopeResolver scopes,
    IOccurrenceMaterializer materializer,
    IDirectoryReader directory,
    IUserContext user,
    IOptions<MeetingsOptions> options) : IMeetingSeriesService
{
    /// <summary>A meeting shorter than this is a corridor conversation; longer than a day it is not a meeting.</summary>
    private const int MinimumDurationMinutes = 5;

    private const int MaximumDurationMinutes = 24 * 60;

    private readonly MeetingsOptions settings = options.Value;

    public async Task<IReadOnlyList<MeetingSeriesView>> ListAsync(
        string? scopeType,
        Guid? scopeId,
        bool includeInactive,
        CancellationToken ct)
    {
        var normalized = Normalize(scopeType);

        var series = await context.Series
            .Where(candidate => includeInactive || candidate.Active)
            .Where(candidate => normalized == null || candidate.ScopeType == normalized)
            .Where(candidate => scopeId == null || candidate.ScopeId == scopeId)
            .OrderBy(candidate => candidate.Kind)
            .ThenBy(candidate => candidate.NameKey)
            .ToListAsync(ct);

        return await ProjectAsync(series, ct);
    }

    public async Task<MeetingSeriesView> GetAsync(Guid id, CancellationToken ct)
    {
        var series = await FindAsync(id, ct);

        return (await ProjectAsync([series], ct))[0];
    }

    public async Task<MeetingSeriesView> CreateAsync(MeetingSeriesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = await scopes.ResolveAsync(request.ScopeType, request.ScopeId, ct);
        var rule = Validate(request);
        var now = DateTimeOffset.UtcNow;

        var series = new MeetingSeries
        {
            Id = Guid.CreateVersion7(),
            Kind = NormalizeCode(request.Kind, nameof(request.Kind)),
            NameKey = request.NameKey.Trim(),
            ScopeType = scope.ScopeType,
            ScopeId = scope.ScopeId,
            DepartmentId = scope.DepartmentId,
            Level = Level(request, scope),
            ScopeIds = Targets(request, scope),
            // Stored canonical, not verbatim. Two people writing the same rule two ways should produce one string,
            // and anything the parser did not understand was already refused rather than silently dropped.
            RecurrenceRule = rule.ToString(),
            StartsOn = request.StartsOn,
            StartTime = request.StartTime,
            TimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId)
                ? settings.DefaultTimeZoneId
                : request.TimeZoneId.Trim(),
            DurationMinutes = request.DurationMinutes,
            OwnerPersonId = request.OwnerPersonId ?? user.UserId,
            Location = Trimmed(request.Location),
            VideoLink = Trimmed(request.VideoLink),
            Active = request.Active,
            CreatedBy = user.UserId,
            CreatedAt = now,
            ModifiedAt = now,
        };

        context.Series.Add(series);

        // Materialized in the same unit of work as the series itself. A series that saved but whose calendar did
        // not would look, to every board in the system, exactly like a series that was never created.
        //
        // From the backfill boundary, not from today: a series being recorded now may well have been running for
        // months, and a copil whose calendar starts on the day somebody typed it in is missing its own history.
        // There is no past here to protect — the series did not exist a moment ago.
        await materializer.SynchronizeAsync(series, Backfill(), Horizon(), ct);

        context.Enqueue(new MeetingScheduled(
            series.Id, series.Kind, series.ScopeType, series.ScopeId, series.RecurrenceRule));

        await context.SaveChangesAsync(ct);

        return (await ProjectAsync([series], ct))[0];
    }

    public async Task<MeetingSeriesView> UpdateAsync(Guid id, MeetingSeriesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var series = await FindAsync(id, ct, tracked: true);
        var scope = await scopes.ResolveAsync(request.ScopeType, request.ScopeId, ct);
        var rule = Validate(request);

        series.Kind = NormalizeCode(request.Kind, nameof(request.Kind));
        series.NameKey = request.NameKey.Trim();
        series.ScopeType = scope.ScopeType;
        series.ScopeId = scope.ScopeId;
        series.DepartmentId = scope.DepartmentId;
        series.Level = Level(request, scope);
        series.ScopeIds = Targets(request, scope);
        series.RecurrenceRule = rule.ToString();
        series.StartsOn = request.StartsOn;
        series.StartTime = request.StartTime;
        series.TimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? settings.DefaultTimeZoneId
            : request.TimeZoneId.Trim();
        series.DurationMinutes = request.DurationMinutes;
        series.OwnerPersonId = request.OwnerPersonId ?? series.OwnerPersonId;
        series.Location = Trimmed(request.Location);
        series.VideoLink = Trimmed(request.VideoLink);
        series.Active = request.Active;
        series.ModifiedAt = DateTimeOffset.UtcNow;

        // From today, never from the series' start: the future follows the new rule and the past keeps its notes
        // and its attendance. Somebody moving the copil to Tuesdays is not saying it was always on a Tuesday.
        await materializer.SynchronizeAsync(series, Today(), Horizon(), ct);

        // Two facts, not one with a flag. A consumer redrawing a calendar and a consumer that stops reporting on
        // a cadence want opposite things from this, and giving them one event with a boolean would make every
        // handler start by branching on it.
        BuildingBlocks.Messaging.IIntegrationEvent change = series.Active
            ? new MeetingScheduled(series.Id, series.Kind, series.ScopeType, series.ScopeId, series.RecurrenceRule)
            : new MeetingSeriesCancelled(series.Id, series.ScopeType, series.ScopeId);

        context.Enqueue(change);

        await context.SaveChangesAsync(ct);

        return (await ProjectAsync([series], ct))[0];
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var series = await FindAsync(id, ct, tracked: true);

        // A real delete, cascading to occurrences and attendance. Deactivation is the softer option and it is
        // available on the same screen; a caller reaching for DELETE has said which one they meant.
        context.Series.Remove(series);

        context.Enqueue(new MeetingSeriesCancelled(series.Id, series.ScopeType, series.ScopeId));

        await context.SaveChangesAsync(ct);
    }

    private async Task<MeetingSeries> FindAsync(Guid id, CancellationToken ct, bool tracked = false)
    {
        var query = tracked ? context.Series.AsTracking() : context.Series;

        var series = await query.SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        // "Filtered out by RLS" and "does not exist" arrive here as the same thing, and leave as the same 404.
        return series ?? throw new ResourceNotFoundException("That meeting series does not exist.");
    }

    private async Task<IReadOnlyList<MeetingSeriesView>> ProjectAsync(
        IReadOnlyList<MeetingSeries> series,
        CancellationToken ct)
    {
        var names = await directory.GetPersonNamesAsync(
            [.. series.Select(candidate => candidate.OwnerPersonId).Distinct()],
            ct);

        return
        [
            .. series.Select(candidate => new MeetingSeriesView(
                candidate.Id,
                candidate.Kind,
                candidate.NameKey,
                candidate.ScopeType,
                candidate.ScopeId,
                candidate.RecurrenceRule,
                candidate.StartsOn,
                candidate.StartTime,
                candidate.TimeZoneId,
                candidate.DurationMinutes,
                candidate.OwnerPersonId,
                // Absent rather than wrong: an owner the caller cannot see in the directory is simply unnamed,
                // which is what IDirectoryReader already does by returning nothing for them.
                names.GetValueOrDefault(candidate.OwnerPersonId),
                candidate.Location,
                candidate.VideoLink,
                candidate.Active,
                candidate.Level,
                candidate.ScopeIds)),
        ];
    }

    private static string Level(MeetingSeriesRequest request, ResolvedScope scope)
    {
        if (string.IsNullOrWhiteSpace(request.Level))
        {
            return scope.ScopeType switch
            {
                MeetingScopeTypes.Project => MeetingLevels.Project,
                MeetingScopeTypes.Unit => MeetingLevels.Unit,
                _ => MeetingLevels.Node,
            };
        }

        var wanted = request.Level.Trim().ToLowerInvariant();

        if (!MeetingLevels.All.Contains(wanted, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException(
                $"'{request.Level}' is not a level. Use one of: {string.Join(", ", MeetingLevels.All)}.");
        }

        return wanted;
    }

    private static Guid[] Targets(MeetingSeriesRequest request, ResolvedScope scope)
    {
        if (!MeetingLevels.IsMultiScope(Level(request, scope)))
        {
            return [];
        }

        var targets = (request.ScopeIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();

        if (targets.Length == 0)
        {
            throw new DomainRuleViolationException(
                "A cross-node meeting has to say which nodes it brings together.");
        }

        return targets;
    }

    private static RecurrenceRule Validate(MeetingSeriesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NameKey))
        {
            throw new DomainRuleViolationException("A meeting needs a name.");
        }

        if (request.DurationMinutes is < MinimumDurationMinutes or > MaximumDurationMinutes)
        {
            throw new DomainRuleViolationException(
                $"A meeting lasts between {MinimumDurationMinutes} minutes and {MaximumDurationMinutes / 60} hours.");
        }

        // Parsed here rather than at the endpoint so that the same rule applies to every caller of the service,
        // and so the refusal carries the parser's own sentence about which part it did not understand.
        return RecurrenceRule.Parse(request.RecurrenceRule);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

    private DateOnly Backfill() => Today().AddDays(-7 * settings.BackfillWeeks);

    private DateOnly Horizon() => Today().AddDays(7 * settings.HorizonWeeks);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string NormalizeCode(string value, string field)
    {
        var code = Normalize(value)
            ?? throw new DomainRuleViolationException($"{field} is required.");

        // Codes travel into report groupings and translation keys, so they stay in the small alphabet everything
        // downstream can handle rather than becoming whatever a form submitted.
        if (!code.All(character => char.IsAsciiLetterOrDigit(character) || character is '-'))
        {
            throw new DomainRuleViolationException($"{field} may contain only letters, digits and hyphens.");
        }

        return code;
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
