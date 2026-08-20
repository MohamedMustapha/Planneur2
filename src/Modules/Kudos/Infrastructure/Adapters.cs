using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Kudos.Application;
using Cracra.Modules.Kudos.Domain;
using Cracra.Modules.Projects.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cracra.Modules.Kudos.Infrastructure;

/// <summary>
/// The Directory port.
/// </summary>
/// <remarks>
/// <para>
/// Placement uses the reference reader, for the same reason S5 does: a project teammate in a contributing
/// department is somebody the directory scopes away from the caller, and their unit is still needed to stamp on
/// the row. The authority comes from afterwards — the insert policy refuses anything the caller did not author,
/// and the eligibility rule already established they work together.
/// </para>
/// <para>
/// Names and people, on the other hand, go through the caller-scoped reader. Those decide what appears on a
/// screen, and a screen is exactly where the directory's own scoping is supposed to apply.
/// </para>
/// </remarks>
internal sealed class DirectoryAdapter(
    IDirectoryReader reader,
    IDirectoryReferenceReader reference,
    IDepartmentConfigReader configs) : IDirectoryPort
{
    public async Task<(Guid UnitId, Guid DepartmentId)?> GetPlacementAsync(Guid personId, CancellationToken ct)
    {
        var person = await reference.GetPersonAsync(personId, ct);

        return person is { UnitId: { } unitId, DepartmentId: { } departmentId }
            ? (unitId, departmentId)
            : null;
    }

    public async Task<KudoRules> GetRulesAsync(Guid departmentId, CancellationToken ct)
    {
        var config = await configs.TryGetAsync(departmentId, ct);

        // No readable config means the platform defaults: the five canonical categories, a counter, ten a month.
        // Failing here instead would mean one unreadable row stops a unit thanking each other.
        return KudoRules.Resolve(config?.KudoRulesJson);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct) =>
        await reader.GetPersonNamesAsync(personIds, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetTeammateNamesAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct) =>
        await reference.GetPersonNamesAsync(personIds, ct);

    public async Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(
        Guid? unitId,
        Guid? departmentId,
        CancellationToken ct) =>
        unitId is null && departmentId is null ? [] : await reader.GetPeopleAsync(unitId, departmentId, ct);

    public async Task<Guid?> GetUnitDepartmentAsync(Guid unitId, CancellationToken ct)
    {
        var units = await reader.GetUnitsAsync(null, ct);

        return units.FirstOrDefault(unit => unit.Id == unitId)?.DepartmentId;
    }
}

/// <summary>The Projects port. One method, delegating to the contract Projects publishes.</summary>
internal sealed class ProjectsAdapter(IProjectMembershipReader members) : IProjectsPort
{
    public async Task<IReadOnlyList<Guid>> GetPeersAsync(Guid personId, CancellationToken ct) =>
        await members.GetProjectPeersAsync(personId, ct);
}

/// <summary>
/// Kudos' answer to the question S8 has been asking since it shipped.
/// </summary>
/// <remarks>
/// The unit report has counted kudos and rendered the figure since S8, against a seam that returned zero. This is
/// that seam filled: one registration in the Reporting module changes, and nothing about the report's contract,
/// its PDF or its Angular view moves at all.
/// </remarks>
internal sealed class KudosReader(KudosDbContext context) : Contracts.IKudosReader
{
    public async Task<int> CountAsync(
        Guid? unitId,
        Guid? departmentId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var query = context.Kudos.Where(kudo => kudo.CreatedAt >= start && kudo.CreatedAt < end);

        if (unitId is { } unit)
        {
            query = query.Where(kudo => kudo.UnitId == unit);
        }

        if (departmentId is { } department)
        {
            query = query.Where(kudo => kudo.DepartmentId == department);
        }

        // No mode check. A count of recognitions is what the counter mode <em>is</em>, so every department has one
        // and the report may always show it; what a mode decides is whether anybody sees a score.
        return await query.CountAsync(ct);
    }
}

/// <summary>Design-time only. Scaffolding needs a provider, not a server.</summary>
public sealed class KudosDbContextFactory : IDesignTimeDbContextFactory<KudosDbContext>
{
    public KudosDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRACRA_DESIGN_TIME_CONNECTION")
                               ?? "Host=localhost;Port=5432;Database=cracra;Username=app_owner;Password=design-time";

        var options = new DbContextOptionsBuilder<KudosDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", KudosDbContext.SchemaName))
            .Options;

        return new KudosDbContext(options);
    }
}
