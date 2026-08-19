using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Portfolio.Application;
using Cracra.Modules.Projects.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cracra.Modules.Portfolio.Infrastructure;

/// <summary>
/// The Projects port, implemented against Projects' contracts.
/// </summary>
/// <remarks>
/// Portfolio never touches the Projects schema — an architecture test asserts it cannot even reference that
/// assembly. Everything here goes through the provisioning contract, on the caller's own connection, so RLS
/// governs it exactly as it would a call to the Projects API.
/// </remarks>
internal sealed class ProjectsAdapter(IProjectProvisioner provisioner) : IProjectsPort
{
    public async Task<Guid> ProvisionAsync(
        string code,
        string name,
        string? description,
        Guid departmentId,
        CancellationToken ct) =>
        await provisioner.ProvisionAsync(code, name, description, departmentId, ct);

    public async Task<bool> ExistsAsync(Guid projectId, CancellationToken ct) =>
        await provisioner.ExistsAsync(projectId, ct);

    public async Task<bool> HasTeamAsync(Guid projectId, CancellationToken ct) =>
        await provisioner.HasTeamAsync(projectId, ct);

    public async Task<IReadOnlyDictionary<Guid, ProjectFacts>> GetFactsAsync(
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct)
    {
        var summaries = await provisioner.GetSummariesAsync(projectIds, ct);

        return summaries.ToDictionary(
            entry => entry.Key,
            entry => new ProjectFacts(entry.Value.CostAmount, entry.Value.CostCurrency, entry.Value.Classification));
    }
}

/// <summary>
/// The Directory port.
/// </summary>
/// <remarks>
/// Existence checks use the reference reader for the same reason Projects does: a PMO registering a candidate on
/// behalf of another department has to be able to name that department, and the caller-scoped reader would refuse
/// to confirm one they cannot see. Name keys stay caller-scoped, so labels never leak.
/// </remarks>
internal sealed class DirectoryAdapter(IDirectoryReader directory, IDirectoryReferenceReader reference)
    : IDirectoryPort
{
    public async Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken ct) =>
        await reference.DepartmentExistsAsync(departmentId, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDepartmentNameKeysAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct) =>
        await directory.GetDepartmentNameKeysAsync(departmentIds, ct);
}

/// <summary>Design-time only. Scaffolding needs a provider, not a server.</summary>
public sealed class PortfolioDbContextFactory : IDesignTimeDbContextFactory<PortfolioDbContext>
{
    public PortfolioDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRACRA_DESIGN_TIME_CONNECTION")
                               ?? "Host=localhost;Port=5432;Database=cracra;Username=app_owner;Password=design-time";

        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PortfolioDbContext.SchemaName))
            .Options;

        return new PortfolioDbContext(options);
    }
}
