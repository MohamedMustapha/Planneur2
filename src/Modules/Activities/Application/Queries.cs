using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Activities.Domain;
using Cracra.Modules.Activities.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Activities.Application;

// =================================================================================================================
// Queries read projections straight off the DbContext. None of them filters by role: RLS removed what the caller
// may not see before the rows arrived, and a second filter here would either duplicate the policy or disagree
// with it. The scope parameter narrows within that ceiling; it can never lift it.
// =================================================================================================================

public sealed record ListActivitiesQuery(
    string? Scope,
    Guid? ProjectId,
    DateOnly? From,
    DateOnly? To) : IRequest<IReadOnlyList<ActivityEntryView>>;

public sealed record GetWeeklySummaryQuery(Guid? PersonId, int? IsoYear, int? IsoWeek) : IRequest<WeeklySummary>;

public sealed record GetActivityTypesQuery(Guid? DepartmentId) : IRequest<IReadOnlyList<ActivityTypeOption>>;

public sealed record GetAssignableTasksQuery(string? Source) : IRequest<IReadOnlyList<AssignableTask>>;

internal sealed class ListActivitiesHandler(
    ActivitiesDbContext context,
    IDirectoryPort directory,
    IProjectsPort projects,
    IUserContext user) : IRequestHandler<ListActivitiesQuery, IReadOnlyList<ActivityEntryView>>
{
    public async Task<IReadOnlyList<ActivityEntryView>> Handle(ListActivitiesQuery request, CancellationToken ct)
    {
        var query = context.Entries.AsQueryable();

        // Each of these narrows. "department" does not grant a member the department's feed — it asks for it, and
        // RLS hands back only their own unit, which is the correct answer to that question for them.
        query = (request.Scope ?? "me").Trim().ToLowerInvariant() switch
        {
            "unit" => query.Where(entry => entry.UnitId == user.UnitId),
            "department" => query.Where(entry => user.DepartmentIds.Contains(entry.DepartmentId)),
            "project" => request.ProjectId is { } scoped
                ? query.Where(entry => entry.ProjectId == scoped)
                : query.Where(entry => entry.ProjectId != null),
            "all" => query,
            _ => query.Where(entry => entry.PersonId == user.UserId),
        };

        if (request.ProjectId is { } projectId)
        {
            query = query.Where(entry => entry.ProjectId == projectId);
        }

        if (request.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            query = query.Where(entry => entry.SlotEnd > start);
        }

        if (request.To is { } to)
        {
            // Exclusive of the day after, so a range ending on Friday includes all of Friday.
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            query = query.Where(entry => entry.SlotStart < end);
        }

        var rows = await query
            .OrderBy(entry => entry.SlotStart)
            .Take(2000)
            .ToListAsync(ct);

        var names = await directory.GetPersonNamesAsync(
            [.. rows.Select(row => row.PersonId).Distinct()],
            ct);

        var codes = await projects.GetProjectCodesAsync(
            [.. rows.Where(row => row.ProjectId is not null).Select(row => row.ProjectId!.Value).Distinct()],
            ct);

        // The taxonomy is per department, and a unit or project feed can span several, so the label keys are
        // resolved per department rather than once.
        var policies = new Dictionary<Guid, DepartmentPolicy>();

        foreach (var departmentId in rows.Select(row => row.DepartmentId).Distinct())
        {
            policies[departmentId] = await directory.GetPolicyAsync(departmentId, ct);
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
                LabelFor(policies, row.DepartmentId, row.ActivityTypeCode),
                row.ProjectId,
                row.ProjectId is { } id ? codes.GetValueOrDefault(id) : null,
                row.IterationId,
                row.Kind.ToString().ToLowerInvariant(),
                SourceCodes.ToCode(row.Source),
                row.ExternalRef,
                row.SlotStart,
                row.SlotEnd,
                row.Hours,
                row.SupersedesEntryId,
                row.Reconciled,
                row.Note)),
        ];
    }

    /// <summary>
    /// The label key for a code, falling back to the code's own key.
    /// </summary>
    /// <remarks>
    /// A department can remove a subtype it once offered, and the entries logged under it remain. Showing them
    /// under a generated key beats hiding rows whose type no longer exists.
    /// </remarks>
    private static string LabelFor(
        IReadOnlyDictionary<Guid, DepartmentPolicy> policies,
        Guid departmentId,
        string code) =>
        policies.TryGetValue(departmentId, out var policy) && policy.Taxonomy.Contains(code)
            ? policy.Taxonomy.Get(code).LabelKey
            : $"activity.type.{code}";
}

internal sealed class GetWeeklySummaryHandler(
    ActivitiesDbContext context,
    IDirectoryPort directory,
    IUserContext user) : IRequestHandler<GetWeeklySummaryQuery, WeeklySummary>
{
    public async Task<WeeklySummary> Handle(GetWeeklySummaryQuery request, CancellationToken ct)
    {
        var personId = request.PersonId ?? user.UserId;

        var week = request.IsoYear is { } year && request.IsoWeek is { } number
            ? new IsoWeek(year, number)
            : IsoWeek.Of(DateTimeOffset.UtcNow);

        var placement = await directory.GetPlacementAsync(personId, ct);

        var policy = placement is { } found
            ? await directory.GetPolicyAsync(found.DepartmentId, ct)
            // Someone no longer in the directory still has a history worth reading; the canonical taxonomy and no
            // target is the honest answer rather than a failure.
            : new DepartmentPolicy(ActivityTaxonomy.Resolve(null), 0m, false);

        var rows = await context.Entries
            .Where(entry => entry.PersonId == personId
                && entry.IsoYear == week.Year
                && entry.IsoWeekNumber == week.Week)
            .Select(entry => new { entry.ActivityTypeCode, entry.Kind, entry.Hours })
            .ToListAsync(ct);

        var actual = rows.Where(row => row.Kind == ActivityKind.Actual).Sum(row => row.Hours);
        var planned = rows.Where(row => row.Kind == ActivityKind.Planned).Sum(row => row.Hours);

        // Checked, not enforced: this is a report on where the week stands, and asking for the number must never
        // be the thing that throws.
        var verdict = WeeklyGuardrail.Check(actual, 0m, policy.WeeklyTargetHours, policy.EnforceWeeklyTarget);

        var byType = rows
            .GroupBy(row => row.ActivityTypeCode, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new WeeklyTypeTotal(
                group.Key,
                policy.Taxonomy.Contains(group.Key)
                    ? policy.Taxonomy.Get(group.Key).LabelKey
                    : $"activity.type.{group.Key}",
                group.Where(row => row.Kind == ActivityKind.Planned).Sum(row => row.Hours),
                group.Where(row => row.Kind == ActivityKind.Actual).Sum(row => row.Hours)))
            .ToList();

        return new WeeklySummary(
            week.Year,
            week.Week,
            week.Monday,
            week.Sunday,
            policy.WeeklyTargetHours,
            policy.EnforceWeeklyTarget,
            actual,
            planned,
            verdict.Overtime,
            verdict.Outcome.ToString().ToLowerInvariant(),
            byType);
    }
}

internal sealed class GetActivityTypesHandler(IDirectoryPort directory, IUserContext user)
    : IRequestHandler<GetActivityTypesQuery, IReadOnlyList<ActivityTypeOption>>
{
    public async Task<IReadOnlyList<ActivityTypeOption>> Handle(GetActivityTypesQuery request, CancellationToken ct)
    {
        var departmentId = request.DepartmentId
            ?? user.DepartmentIds.FirstOrDefault();

        var policy = departmentId == Guid.Empty
            ? new DepartmentPolicy(ActivityTaxonomy.Resolve(null), 0m, false)
            : await directory.GetPolicyAsync(departmentId, ct);

        return
        [
            .. policy.Taxonomy.Types
                // Canonical buckets first, then their subtypes, so the picker reads as a hierarchy without the
                // client having to sort one.
                .OrderBy(type => type.ParentCode ?? type.Code, StringComparer.Ordinal)
                .ThenBy(type => type.ParentCode is null ? 0 : 1)
                .ThenBy(type => type.Code, StringComparer.Ordinal)
                .Select(type => new ActivityTypeOption(
                    type.Code,
                    type.ParentCode,
                    type.LabelKey,
                    type.RequiresProject)),
        ];
    }
}

/// <summary>
/// The dropdown.
/// </summary>
/// <remarks>
/// Always scoped to the caller — the person id is taken from the session and never from the request. A parameter
/// there would be an endpoint that hands one person another person's assigned tickets, which is a disclosure the
/// external system never agreed to.
/// </remarks>
internal sealed class GetAssignableTasksHandler(
    IEnumerable<IAssignableTaskSource> sources,
    IUserContext user) : IRequestHandler<GetAssignableTasksQuery, IReadOnlyList<AssignableTask>>
{
    public async Task<IReadOnlyList<AssignableTask>> Handle(GetAssignableTasksQuery request, CancellationToken ct)
    {
        var wanted = request.Source?.Trim().ToLowerInvariant();

        var selected = string.IsNullOrEmpty(wanted)
            ? sources
            : sources.Where(source => source.Source == wanted);

        var results = new List<AssignableTask>();

        foreach (var source in selected)
        {
            results.AddRange(await source.GetAssignableAsync(user.UserId, ct));
        }

        return [.. results.OrderBy(task => task.Source, StringComparer.Ordinal).ThenBy(task => task.Title, StringComparer.Ordinal)];
    }
}
