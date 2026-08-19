using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Projects.Contracts;
using Cracra.Modules.Scheduling.Application;
using Cracra.Modules.Scheduling.Contracts;
using Cracra.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging;

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
        CancellationToken ct) =>
        await scheduler.PlanAsync(personId, activityTypeCode, projectId, start, end, note, source, externalRef, ct);

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
/// The S7 seam, answering nothing.
/// </summary>
/// <remarks>
/// Special days, deadlines and meeting bands land in S7. The boards already ask for them and already render
/// whatever comes back, so that slice becomes a registration change here rather than a change to five board
/// payloads and their client templates.
/// </remarks>
internal sealed class NoCalendarOverlays : ICalendarOverlaySource
{
    public Task<IReadOnlyList<BoardOverlay>> GetOverlaysAsync(
        Guid? unitId,
        Guid? departmentId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BoardOverlay>>([]);
}

/// <summary>
/// The pool's external half, pending S10.
/// </summary>
/// <remarks>
/// Mirrors the shape S5 already established for its per-person dropdown: unconfigured returns nothing, and the
/// dev box turns on samples so the drag-from-pool journey is exercisable before the adapters exist. Samples are
/// deterministic and scoped to the unit that asked, so they behave like a real queue rather than like noise.
/// </remarks>
public sealed class WorkOrderPoolOptions
{
    public const string SectionName = "Cracra:Scheduling:Pool";

    public bool SeedSampleWorkOrders { get; set; }
}

internal sealed class UnconfiguredPoolSource(string source, ILogger<UnconfiguredPoolSource> logger)
    : IWorkOrderPoolSource
{
    public string Source => source;

    public Task<IReadOnlyList<PooledWorkOrder>> GetUnassignedAsync(Guid unitId, CancellationToken ct)
    {
        logger.LogDebug("No adapter is configured for {Source}; the pool stays empty", source);

        return Task.FromResult<IReadOnlyList<PooledWorkOrder>>([]);
    }
}

internal sealed class SamplePoolSource(string source) : IWorkOrderPoolSource
{
    public string Source => source;

    public Task<IReadOnlyList<PooledWorkOrder>> GetUnassignedAsync(Guid unitId, CancellationToken ct)
    {
        // Derived from the unit's own id, so two units never see each other's tickets and the same unit sees the
        // same three every time — which is what lets an E2E assertion name one.
        var prefix = unitId.ToString("N")[..4].ToUpperInvariant();

        return Task.FromResult<IReadOnlyList<PooledWorkOrder>>(
        [
            new($"INC-{prefix}-001", "Poste bloqué au démarrage", "L'utilisateur ne peut plus ouvrir sa session.", 1m),
            new($"INC-{prefix}-002", "Imprimante hors service", "Bourrage papier récurrent au 3e étage.", 0.5m),
            new($"INC-{prefix}-003", "Accès VPN refusé", "Certificat expiré côté client.", 2m),
        ]);
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
