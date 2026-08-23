using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Application;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Activities.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Activities.Infrastructure;

/// <summary>
/// Activities' side of the scheduling contract S6 calls.
/// </summary>
/// <remarks>
/// <para>
/// Every write here goes through the same aggregate, the same taxonomy resolution and the same repository as an
/// entry typed in by hand. The only thing that differs is who initiated it — which is exactly what should differ,
/// and nothing else.
/// </para>
/// <para>
/// Runs on the caller's own connection, so RLS decides whether a lead may plan for a given person, using the same
/// write policy that governs the API. Scheduling never gets a bypass; if a lead may not write that row through
/// <c>POST /api/activities</c>, dragging a card does not change the answer.
/// </para>
/// </remarks>
internal sealed class ActivityScheduler(
    ActivitiesDbContext context,
    IActivityRepository repository,
    IDirectoryPort directory,
    IProjectsPort projects,
    IUserContext user) : IActivityScheduler
{
    public async Task<Guid> PlanAsync(
        Guid personId,
        string activityTypeCode,
        Guid? projectId,
        DateTimeOffset start,
        DateTimeOffset end,
        string? note,
        string source,
        string? externalRef,
        int? percentComplete,
        CancellationToken ct)
    {
        var placement = await directory.GetPlacementAsync(personId, ct)
            ?? throw new DomainRuleViolationException("That person is not in the directory.");

        var policy = await directory.GetPolicyAsync(placement.DepartmentId, placement.UnitId, ct);

        SourceCodes.TryParse(source, out var parsedSource);

        var entry = ActivityEntry.Log(
            personId,
            placement.UnitId,
            placement.DepartmentId,
            policy.Taxonomy,
            activityTypeCode,
            projectId,
            iterationId: null,
            // Planned, always. A board schedules intent; only the person who did the work, or their lead acting
            // for them, may assert that it actually happened.
            ActivityKind.Planned,
            parsedSource,
            externalRef,
            new TimeSlot(start, end),
            hours: null,
            note,
            user.UserId,
            DateTimeOffset.UtcNow);

        if (percentComplete is not null)
        {
            entry.SetProgress(percentComplete, user.UserId, DateTimeOffset.UtcNow);
        }

        await repository.AddAsync(entry, ct);

        // Written now rather than at the end of the caller's transaction, because the caller needs the id to store
        // on the work order — and both statements are inside the same transaction either way, so a later failure
        // still takes this with it.
        await context.SaveChangesAsync(ct);

        return entry.Id;
    }

    public async Task SetProgressAsync(Guid entryId, int? percentComplete, CancellationToken ct)
    {
        var entry = await repository.GetAsync(entryId, ct);

        entry.SetProgress(percentComplete, user.UserId, DateTimeOffset.UtcNow);

        await context.SaveChangesAsync(ct);
    }

    public async Task RescheduleAsync(Guid entryId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var entry = await repository.GetAsync(entryId, ct);

        if (entry.Kind is not ActivityKind.Planned)
        {
            // Dragging a block on a board must not silently rewrite what someone recorded they actually did.
            throw new DomainRuleViolationException("Only a planned slot can be rescheduled from a board.");
        }

        var policy = await directory.GetPolicyAsync(entry.DepartmentId, entry.UnitId, ct);

        entry.Amend(
            policy.Taxonomy,
            entry.ActivityTypeCode,
            entry.ProjectId,
            entry.IterationId,
            new TimeSlot(start, end),
            hours: null,
            entry.Note,
            user.UserId,
            DateTimeOffset.UtcNow);

        await context.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(Guid entryId, CancellationToken ct)
    {
        // Tolerates a missing row on purpose. Unassigning a work order whose planned entry somebody already
        // deleted by hand should return the card to the pool, not fail halfway and leave it assigned to nobody.
        var deleted = await context.Entries
            .Where(entry => entry.Id == entryId && entry.Kind == ActivityKind.Planned)
            .ExecuteDeleteAsync(ct);

        if (deleted == 0)
        {
            return;
        }
    }

    public async Task<IReadOnlyList<ActivityEntryView>> GetForPeopleAsync(
        IReadOnlyList<Guid> personIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (personIds.Count == 0)
        {
            return [];
        }

        return await ProjectAsync(
            context.Entries.Where(entry => personIds.Contains(entry.PersonId)),
            from,
            to,
            ct);
    }

    public async Task<IReadOnlyList<ActivityEntryView>> GetForProjectAsync(
        Guid projectId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        await ProjectAsync(context.Entries.Where(entry => entry.ProjectId == projectId), from, to, ct);

    /// <summary>
    /// Shapes entries for a board.
    /// </summary>
    /// <remarks>
    /// Names and labels are resolved here rather than left to the board, because whether a caller may see a
    /// person's name is Directory's answer and not Scheduling's. Anyone RLS hid comes back unnamed rather than
    /// omitted: their hours still occupy the slot, and a board that quietly dropped them would mislead a lead
    /// about how full someone's week is.
    /// </remarks>
    private async Task<IReadOnlyList<ActivityEntryView>> ProjectAsync(
        IQueryable<ActivityEntry> query,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await query
            .Where(entry => entry.SlotEnd > start && entry.SlotStart < end)
            .OrderBy(entry => entry.SlotStart)
            .Take(5000)
            .ToListAsync(ct);

        var names = await directory.GetPersonNamesAsync([.. rows.Select(row => row.PersonId).Distinct()], ct);

        // Resolved here rather than left null, as the S5 feed already does. A board that groups project hours by
        // project has to name the rows it groups them into, and the code is that name — one lookup for the whole
        // page, not one per row.
        var codes = await projects.GetProjectCodesAsync(
            [.. rows.Where(row => row.ProjectId is not null).Select(row => row.ProjectId!.Value).Distinct()],
            ct);

        var policies = new Dictionary<Guid, DepartmentPolicy>();

        foreach (var departmentId in rows.Select(row => row.DepartmentId).Distinct())
        {
            policies[departmentId] = await directory.GetPolicyAsync(departmentId, null, ct);
        }

        return
        [
            .. rows.Select(row => new ActivityEntryView(
                row.Id,
                row.PersonId,
                names.GetValueOrDefault(row.PersonId),
                row.UnitId,
                row.DepartmentId,
                row.ActivityTypeCode,
                policies.TryGetValue(row.DepartmentId, out var policy) && policy.Taxonomy.Contains(row.ActivityTypeCode)
                    ? policy.Taxonomy.Get(row.ActivityTypeCode).LabelKey
                    : $"activity.type.{row.ActivityTypeCode}",
                row.ProjectId,
                row.ProjectId is { } projectId ? codes.GetValueOrDefault(projectId) : null,
                row.IterationId,
                row.Kind.ToString().ToLowerInvariant(),
                SourceCodes.ToCode(row.Source),
                row.ExternalRef,
                row.SlotStart,
                row.SlotEnd,
                row.Hours,
                row.SupersedesEntryId,
                row.Reconciled,
                row.Note,
                row.PercentComplete)),
        ];
    }
}
