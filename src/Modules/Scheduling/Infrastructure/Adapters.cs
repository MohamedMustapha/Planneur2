using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Projects.Contracts;
using Cracra.Modules.Scheduling.Application;
using Cracra.Modules.Scheduling.Contracts;
using Cracra.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cracra.Modules.Scheduling.Infrastructure;

/// <summary>
/// Directory, through its contracts.
/// </summary>
/// <remarks>
/// Everything here is caller-scoped. A board's rows are supposed to be the people this caller may see — that is
/// the whole "join of already-authorized sets" idea — so using the reference reader here would silently widen
/// every board past what the matrix allows.
/// </remarks>
internal sealed class DirectoryAdapter(IDirectoryReader directory, IDepartmentConfigReader configs)
    : Application.IDirectoryPort
{
    public async Task<PersonSummary?> GetPersonAsync(Guid personId, CancellationToken ct) =>
        await directory.GetPersonAsync(personId, ct);

    public async Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(
        Guid? unitId,
        Guid? departmentId,
        CancellationToken ct) =>
        await directory.GetPeopleAsync(unitId, departmentId, ct);

    public async Task<IReadOnlyList<UnitSummary>> GetUnitsAsync(Guid? departmentId, CancellationToken ct) =>
        await directory.GetUnitsAsync(departmentId, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDepartmentNameKeysAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct) =>
        await directory.GetDepartmentNameKeysAsync(departmentIds, ct);

    public async Task<IReadOnlyList<Domain.ShiftTemplate>> GetShiftTemplatesAsync(
        Guid departmentId,
        CancellationToken ct)
    {
        var config = await configs.TryGetAsync(departmentId, ct);

        // Unreadable config means the platform defaults, not an error. A shift board that refuses to render
        // because a settings row is out of reach helps nobody standing in front of it.
        return Domain.ShiftTemplate.Resolve(config?.ShiftTemplatesJson);
    }

    public async Task<string> GetDefaultBoardLayoutAsync(Guid departmentId, CancellationToken ct)
    {
        var config = await configs.TryGetAsync(departmentId, ct);

        return config?.DefaultBoardLayout ?? "week";
    }
}

internal sealed class ProjectsAdapter(IProjectProvisioner projects, IProjectTeamReader teams)
    : Application.IProjectsPort
{
    public async Task<ProjectSummaryView?> GetProjectAsync(Guid projectId, CancellationToken ct)
    {
        var summaries = await projects.GetSummariesAsync([projectId], ct);

        return summaries.TryGetValue(projectId, out var summary)
            ? new ProjectSummaryView(summary.Id, summary.Code, summary.Name, summary.Classification)
            : null;
    }

    public async Task<IReadOnlyList<ProjectMemberView>> GetTeamAsync(Guid projectId, CancellationToken ct)
    {
        var members = await teams.GetTeamAsync(projectId, ct);

        return
        [
            .. members.Select(member => new ProjectMemberView(
                member.PersonId,
                member.DisplayName,
                member.DepartmentId,
                member.DepartmentNameKey,
                member.FunctionCode)),
        ];
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetProjectCodesAsync(
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct)
    {
        var summaries = await projects.GetSummariesAsync(projectIds, ct);

        return summaries.ToDictionary(entry => entry.Key, entry => entry.Value.Code);
    }
}

internal sealed class ActivitiesAdapter(IActivityScheduler scheduler) : IActivitiesPort
{
    public async Task<IReadOnlyList<ActivityEntryView>> GetForPeopleAsync(
        IReadOnlyList<Guid> personIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        await scheduler.GetForPeopleAsync(personIds, from, to, ct);

    public async Task<IReadOnlyList<ActivityEntryView>> GetForProjectAsync(
        Guid projectId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        await scheduler.GetForProjectAsync(projectId, from, to, ct);

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
        CancellationToken ct) =>
        await scheduler.PlanAsync(
            personId,
            activityTypeCode,
            projectId,
            start,
            end,
            note,
            source,
            externalRef,
            percentComplete,
            ct);

    public async Task SetProgressAsync(Guid entryId, int? percentComplete, CancellationToken ct) =>
        await scheduler.SetProgressAsync(entryId, percentComplete, ct);

    public async Task RescheduleAsync(Guid entryId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) =>
        await scheduler.RescheduleAsync(entryId, start, end, ct);

    public async Task CancelAsync(Guid entryId, CancellationToken ct) => await scheduler.CancelAsync(entryId, ct);
}

internal sealed class PortfolioAdapter(IPortfolioIterationReader iterations) : IPortfolioPort
{
    public async Task<IReadOnlyList<IterationRange>> GetIterationsAsync(Guid projectId, CancellationToken ct)
    {
        var found = await iterations.GetForProjectAsync(projectId, ct);

        return
        [
            .. found.Select(iteration => new IterationRange(
                iteration.Id,
                iteration.Name,
                iteration.StartsOn,
                iteration.EndsOn,
                iteration.State)),
        ];
    }
}

/// <summary>
/// Meetings and special days, through their contract.
/// </summary>
/// <remarks>
/// <para>
/// The seam S6 left open, now filled. The boards already asked for overlays and already rendered whatever came
/// back, so S7 landed here as one registration and this translation, rather than as a change to five board
/// payloads and their client templates.
/// </para>
/// <para>
/// A translation and nothing else. Meetings returns its own <see cref="CalendarOverlay"/> rather than a
/// <see cref="BoardOverlay"/>, because the dependency runs Scheduling → Meetings and inverting it would make the
/// calendar unusable by anything that is not a board — Reporting wants the same list for "upcoming deadlines".
/// Mapping one to the other is this class's whole reason to exist.
/// </para>
/// <para>
/// Caller-scoped like every other adapter here: the reader runs inside the caller's RLS session, so a board
/// composed for a member cannot pick up a meeting the member may not see.
/// </para>
/// </remarks>
internal sealed class MeetingCalendarOverlays(IMeetingCalendarReader meetings) : ICalendarOverlaySource
{
    public async Task<IReadOnlyList<BoardOverlay>> GetOverlaysAsync(
        Guid? unitId,
        Guid? departmentId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var overlays = await meetings.GetOverlaysAsync(unitId, departmentId, from, to, ct);

        return
        [
            .. overlays.Select(overlay => new BoardOverlay(
                overlay.Id,
                overlay.Kind,
                overlay.Title,
                overlay.From,
                overlay.To,
                overlay.Color)),
        ];
    }
}

/// <summary>
/// The RUN load of a unit, for S8's report.
/// </summary>
/// <remarks>
/// Six numbers, computed the same way the board computes them — the coverage count comes from
/// <c>ShiftCoverage.Check</c> rather than from a second implementation, so a report saying "no gaps" and a board
/// showing three would be impossible rather than merely unlikely.
///
/// Caller-scoped: every query underneath runs in the reader's own RLS session, so a report can only ever count
/// what its reader was already allowed to see.
/// </remarks>
internal sealed class ScheduleLoadReader(
    IWorkOrderRepository workOrders,
    IShiftRepository shifts,
    IDirectoryPort directory) : IScheduleLoadReader
{
    public async Task<ScheduleLoad> GetLoadAsync(
        Guid unitId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var orders = await workOrders.GetForUnitAsync(unitId, ct);
        var planned = await shifts.GetForUnitAsync(unitId, from, to, ct);

        var units = await directory.GetUnitsAsync(null, ct);
        var department = units.FirstOrDefault(unit => unit.Id == unitId)?.DepartmentId;

        var gaps = 0;

        if (department is { } departmentId)
        {
            var templates = await directory.GetShiftTemplatesAsync(departmentId, ct);

            gaps = ShiftCoverage.Check(templates, planned, from, to).Count;
        }

        return new ScheduleLoad(
            unitId,
            orders.Count(order => order.State == WorkOrderState.Unassigned),
            orders.Count(order => order.State == WorkOrderState.Assigned),
            orders.Sum(order => order.EstimatedHours),
            planned.Count,
            planned.Sum(shift => shift.Hours),
            gaps);
    }
}

/// <summary>
/// The pool's external half, filled by S10.
/// </summary>
/// <remarks>
/// <para>
/// What stood here was a pair of stand-ins — one returning nothing, one inventing three plausible tickets for the
/// dev box — and this replaces them rather than sitting behind them. A deployment with no ServiceNow connection
/// now has an empty pool because the mirror is empty, which is the same answer for a better reason.
/// </para>
/// <para>
/// The mirror is a table, so refreshing the pool is an indexed query against Postgres rather than a call to
/// somebody else's instance. That matters here more than it does for S5's dropdown: a lead refreshes the pool in
/// front of a room, and a board that waits on a ServiceNow round trip is a board people stop using.
/// </para>
/// <para>
/// Caller-scoped like every other adapter in this file. RLS has already decided which unit's tickets this reader
/// may see; the unit filter below narrows within that, and could not widen past it.
/// </para>
/// </remarks>
internal sealed class MirrorPoolSource(string source, IExternalWorkItemReader mirror) : IWorkOrderPoolSource
{
    /// <summary>What a work order costs when the source has no estimate. One hour, and visibly a default.</summary>
    private const decimal DefaultEstimate = 1m;

    public string Source => source;

    public async Task<IReadOnlyList<PooledWorkOrder>> GetUnassignedAsync(Guid unitId, CancellationToken ct)
    {
        var items = await mirror.QueryAsync(
            new ExternalWorkItemQuery
            {
                Provider = source,

                // Both, and both matter. Unassigned is what "pool" means; the unit is which queue — a ticket
                // somebody at the source has already picked up is not free work, and another unit's queue is not
                // this board's business.
                Unassigned = true,
                UnitId = unitId,
            },
            ct);

        return
        [
            // Deduplicated on the reference, which is the identity the rest of the platform uses for an external
            // item. The mirror can legitimately hold one ticket twice — two connections covering the same queue
            // is a configuration mistake, not a corrupt state — and a pool that offered both would put two cards
            // on the board for one incident, or, once a lead pressed refresh, fail outright against the unique
            // index the work order carries. One card per ticket is the only answer that is true either way.
            .. items
                .GroupBy(item => item.Reference, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(item => new PooledWorkOrder(
                    item.Reference,
                    item.Title,

                    // The mirror holds a title and no body. ServiceNow's description is often several screens of
                    // pasted email, and a pool card is one line on a timeline — so the type and state go here
                    // instead, which is what a lead triaging the queue actually reads.
                    $"{item.Type} · {item.State}",
                    item.EstimatedHours ?? DefaultEstimate)),
        ];
    }
}

/// <summary>Design-time only. Scaffolding needs a provider, not a server.</summary>
public sealed class SchedulingDbContextFactory : IDesignTimeDbContextFactory<SchedulingDbContext>
{
    public SchedulingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRACRA_DESIGN_TIME_CONNECTION")
                               ?? "Host=localhost;Port=5432;Database=cracra;Username=app_owner;Password=design-time";

        var options = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", SchedulingDbContext.SchemaName))
            .Options;

        return new SchedulingDbContext(options);
    }
}
