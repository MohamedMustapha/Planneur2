using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Data;
using Cracra.Modules.Meetings.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Meetings.Services;

public sealed record MinutesRequest(
    string? Agenda,
    IReadOnlyList<Guid>? Attendees,
    IReadOnlyList<Guid>? Absentees,
    string? Summary);

public sealed record DecisionRequest(string Text, string? Rationale, string? DecidedBy);

public sealed record ActionRequest(
    string Title,
    Guid? OwnerPersonId,
    DateOnly? Due,
    string? LinkType,
    Guid? LinkId);

public sealed record ActionPatch(string? Title, Guid? OwnerPersonId, DateOnly? Due, string? Status);

public interface IMinutesService
{
    Task<MinutesView?> FindAsync(Guid occurrenceId, CancellationToken ct);

    Task<MinutesView> OpenAsync(Guid occurrenceId, CancellationToken ct);

    Task<MinutesView> AmendAsync(Guid minutesId, MinutesRequest request, CancellationToken ct);

    Task<MinutesView> PublishAsync(Guid minutesId, CancellationToken ct);

    Task<MinutesView> DecideAsync(Guid minutesId, DecisionRequest request, CancellationToken ct);

    Task<MinutesView> AssignAsync(Guid minutesId, ActionRequest request, CancellationToken ct);

    Task<MinutesView> AmendActionAsync(Guid minutesId, Guid actionId, ActionPatch patch, CancellationToken ct);

    Task<IReadOnlyList<MinutesDigest>> LatestAsync(string? scopeType, Guid? scopeId, int take, CancellationToken ct);

    Task<IReadOnlyList<ActionItemView>> TrackAsync(string? owner, string? status, CancellationToken ct);
}

internal sealed class MinutesService(
    MeetingsDbContext context,
    IDirectoryReader directory,
    IUserContext user) : IMinutesService
{
    private const int DefaultDigestCount = 8;

    public async Task<MinutesView?> FindAsync(Guid occurrenceId, CancellationToken ct)
    {
        var minutes = await Loaded().SingleOrDefaultAsync(row => row.OccurrenceId == occurrenceId, ct);

        return minutes is null ? null : (await ProjectAsync([minutes], ct))[0];
    }

    public async Task<MinutesView> OpenAsync(Guid occurrenceId, CancellationToken ct)
    {
        var existing = await FindAsync(occurrenceId, ct);

        // Opening twice is how the editor navigates back to a draft, so it returns rather than refuses.
        if (existing is not null)
        {
            return existing;
        }

        var row = await context.Occurrences
            .Where(occurrence => occurrence.Id == occurrenceId)
            .Join(
                context.Series,
                occurrence => occurrence.SeriesId,
                series => series.Id,
                (occurrence, series) => new { occurrence, series })
            .SingleOrDefaultAsync(ct)
            ?? throw new ResourceNotFoundException("That meeting does not exist.");

        var now = DateTimeOffset.UtcNow;

        var minutes = MeetingMinutes.Open(
            row.occurrence.Id,
            row.series.Id,
            row.series.Level,
            row.occurrence.ScopeType,
            row.occurrence.ScopeId,
            row.series.ScopeIds,
            row.occurrence.StartsAt,
            user.UserId,
            now);

        context.Minutes.Add(minutes);

        await context.SaveChangesAsync(ct);

        return (await ProjectAsync([minutes], ct))[0];
    }

    public async Task<MinutesView> AmendAsync(Guid minutesId, MinutesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var minutes = await TrackedAsync(minutesId, ct);

        minutes.Amend(request.Agenda, request.Attendees, request.Absentees, request.Summary, DateTimeOffset.UtcNow);

        await SaveAsync(ct);

        return (await ProjectAsync([minutes], ct))[0];
    }

    public async Task<MinutesView> PublishAsync(Guid minutesId, CancellationToken ct)
    {
        var minutes = await TrackedAsync(minutesId, ct);

        minutes.Publish(DateTimeOffset.UtcNow);

        context.Enqueue(new MinutesPublished(
            minutes.Id,
            minutes.OccurrenceId,
            minutes.Level,
            minutes.ScopeType,
            minutes.ScopeId,
            minutes.ScopeIds));

        await SaveAsync(ct);

        return (await ProjectAsync([minutes], ct))[0];
    }

    public async Task<MinutesView> DecideAsync(Guid minutesId, DecisionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var minutes = await TrackedAsync(minutesId, ct);

        minutes.Decide(request.Text, request.Rationale, request.DecidedBy, DateTimeOffset.UtcNow);

        await SaveAsync(ct);

        return (await ProjectAsync([minutes], ct))[0];
    }

    public async Task<MinutesView> AssignAsync(Guid minutesId, ActionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var minutes = await TrackedAsync(minutesId, ct);

        minutes.Assign(
            request.Title,
            request.OwnerPersonId ?? user.UserId,
            request.Due,
            request.LinkType ?? ActionLinkTypes.None,
            request.LinkId,
            DateTimeOffset.UtcNow);

        await SaveAsync(ct);

        return (await ProjectAsync([minutes], ct))[0];
    }

    public async Task<MinutesView> AmendActionAsync(
        Guid minutesId,
        Guid actionId,
        ActionPatch patch,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(patch);

        // Not TrackedAsync: an action's owner may close it without being able to change the minutes it sits on,
        // which is the difference between a tracker people use and one only the author can maintain.
        var minutes = await Loaded().AsTracking().SingleOrDefaultAsync(row => row.Id == minutesId, ct)
            ?? throw new ResourceNotFoundException("Those minutes do not exist.");

        var action = minutes.Action(actionId);
        var now = DateTimeOffset.UtcNow;

        action.Retitle(patch.Title, patch.OwnerPersonId, patch.Due, now);

        if (!string.IsNullOrWhiteSpace(patch.Status))
        {
            action.Settle(patch.Status, now);
        }

        await SaveAsync(ct);

        return (await ProjectAsync([minutes], ct))[0];
    }

    public async Task<IReadOnlyList<MinutesDigest>> LatestAsync(
        string? scopeType,
        Guid? scopeId,
        int take,
        CancellationToken ct)
    {
        var normalized = string.IsNullOrWhiteSpace(scopeType) ? null : scopeType.Trim().ToLowerInvariant();
        var count = Math.Clamp(take <= 0 ? DefaultDigestCount : take, 1, 50);

        var rows = await context.Minutes
            .Where(minutes => minutes.Published)
            .Where(minutes => normalized == null || minutes.ScopeType == normalized)
            .Where(minutes => scopeId == null || minutes.ScopeId == scopeId || minutes.ScopeIds.Contains(scopeId.Value))
            .OrderByDescending(minutes => minutes.OccurredAt)
            .Take(count)
            .Join(
                context.Series,
                minutes => minutes.SeriesId,
                series => series.Id,
                (minutes, series) => new { minutes, series.NameKey })
            .ToListAsync(ct);

        var ids = rows.Select(row => row.minutes.Id).ToList();

        var decisions = await context.Decisions
            .Where(decision => ids.Contains(decision.MinutesId))
            .GroupBy(decision => decision.MinutesId)
            .Select(group => new { MinutesId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.MinutesId, group => group.Count, ct);

        var open = await context.Actions
            .Where(action => ids.Contains(action.MinutesId) && action.Status == ActionStatuses.Open)
            .GroupBy(action => action.MinutesId)
            .Select(group => new { MinutesId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.MinutesId, group => group.Count, ct);

        return
        [
            .. rows.Select(row => new MinutesDigest(
                row.minutes.Id,
                row.minutes.OccurrenceId,
                row.minutes.Level,
                row.NameKey,
                row.minutes.OccurredAt,
                row.minutes.Summary,
                decisions.GetValueOrDefault(row.minutes.Id),
                open.GetValueOrDefault(row.minutes.Id),
                row.minutes.Published)),
        ];
    }

    public async Task<IReadOnlyList<ActionItemView>> TrackAsync(string? owner, string? status, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var mine = !string.Equals(owner, "scope", StringComparison.OrdinalIgnoreCase);
        var wanted = (status ?? "open").Trim().ToLowerInvariant();

        var query = context.Actions.AsQueryable();

        if (mine)
        {
            query = query.Where(action => action.OwnerPersonId == user.UserId);
        }

        query = wanted switch
        {
            "overdue" => query.Where(action =>
                action.Status == ActionStatuses.Open && action.Due != null && action.Due < today),
            "all" => query,
            _ => query.Where(action => action.Status == ActionStatuses.Open),
        };

        var actions = await query
            .OrderBy(action => action.Due == null)
            .ThenBy(action => action.Due)
            .Take(200)
            .ToListAsync(ct);

        return await ProjectActionsAsync(actions, today, ct);
    }

    private IQueryable<MeetingMinutes> Loaded() =>
        context.Minutes.Include(minutes => minutes.Decisions).Include(minutes => minutes.Actions);

    private async Task<MeetingMinutes> TrackedAsync(Guid minutesId, CancellationToken ct)
    {
        var minutes = await Loaded().AsTracking().SingleOrDefaultAsync(row => row.Id == minutesId, ct)
            ?? throw new ResourceNotFoundException("Those minutes do not exist.");

        // Asked before the write rather than inferred from its failure: an UPDATE the policy refuses affects no
        // rows and raises nothing, so without this the caller would be told their change succeeded.
        if (!await IsWritableAsync(minutesId, ct))
        {
            throw new UnauthorizedAccessException(
                "You can read these minutes but not change them. Their author or the meeting's head can.");
        }

        return minutes;
    }

    private async Task<bool> IsWritableAsync(Guid minutesId, CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();

        command.CommandText = """
            select exists (
                select 1 from meetings.meeting_minutes m
                where m.id = @id
                  and access.can_write_minutes(
                      m.scope_id, m.scope_ids, m.node_ancestor_ids, m.author_person_id))
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = minutesId;
        command.Parameters.Add(parameter);

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(ct);
        }

        return await command.ExecuteScalarAsync(ct) is true;
    }

    private async Task SaveAsync(CancellationToken ct) => await context.SaveChangesAsync(ct);

    private async Task<IReadOnlyList<MinutesView>> ProjectAsync(
        IReadOnlyList<MeetingMinutes> minutes,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        var names = await directory.GetPersonNamesAsync(
            [
                .. minutes.Select(row => row.AuthorPersonId)
                    .Concat(minutes.SelectMany(row => row.Actions).Select(action => action.OwnerPersonId))
                    .Distinct(),
            ],
            ct);

        return
        [
            .. minutes.Select(row => new MinutesView(
                row.Id,
                row.OccurrenceId,
                row.SeriesId,
                row.Level,
                row.ScopeType,
                row.ScopeId,
                row.OccurredAt,
                row.AuthorPersonId,
                names.GetValueOrDefault(row.AuthorPersonId),
                row.Agenda,
                row.Attendees,
                row.Absentees,
                row.Summary,
                row.Published,
                row.PublishedAt,
                [
                    .. row.Decisions
                        .OrderBy(decision => decision.CreatedAt)
                        .Select(decision => new DecisionView(
                            decision.Id, decision.Text, decision.Rationale, decision.DecidedBy)),
                ],
                [
                    .. row.Actions
                        .OrderBy(action => action.Due == null)
                        .ThenBy(action => action.Due)
                        .Select(action => View(action, names, today)),
                ])),
        ];
    }

    private async Task<IReadOnlyList<ActionItemView>> ProjectActionsAsync(
        IReadOnlyList<ActionItem> actions,
        DateOnly today,
        CancellationToken ct)
    {
        var names = await directory.GetPersonNamesAsync(
            [.. actions.Select(action => action.OwnerPersonId).Distinct()],
            ct);

        return [.. actions.Select(action => View(action, names, today))];
    }

    private static ActionItemView View(ActionItem action, IReadOnlyDictionary<Guid, string> names, DateOnly today) =>
        new(
            action.Id,
            action.MinutesId,
            action.Title,
            action.OwnerPersonId,
            names.GetValueOrDefault(action.OwnerPersonId),
            action.Due,
            action.Status,
            action.LinkType,
            action.LinkId,
            action.IsOverdueAt(today));
}

internal sealed class ActionItemReader(IMinutesService minutes) : IActionItemReader
{
    public async Task<IReadOnlyList<ActionItemView>> GetOpenForCallerAsync(CancellationToken ct) =>
        await minutes.TrackAsync("me", "open", ct);
}
