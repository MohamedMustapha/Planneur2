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

/// <summary>
/// Portfolio's side of the iteration-reading contract S6 draws ranges from.
/// </summary>
/// <remarks>
/// Keyed by project and answers with nothing where the project has no portfolio item — which is the common case
/// for a project created directly through S3, and not a condition any board should have to handle specially.
/// </remarks>
internal sealed class PortfolioIterationReader(PortfolioDbContext context)
    : Cracra.Modules.Portfolio.Contracts.IPortfolioIterationReader
{
    public async Task<IReadOnlyList<Cracra.Modules.Portfolio.Contracts.IterationSummary>> GetForProjectAsync(Guid projectId, CancellationToken ct) =>
        await context.Iterations
            .Where(iteration => context.Items.Any(item => item.Id == iteration.PortfolioItemId
                && item.ProjectId == projectId))
            .OrderBy(iteration => iteration.Sequence)
            .Select(iteration => new Cracra.Modules.Portfolio.Contracts.IterationSummary(
                iteration.Id,
                iteration.Sequence,
                iteration.Name,
                iteration.Length.ToString().ToLower(),
                iteration.StartsOn,
                iteration.EndsOn,
                iteration.State.ToString().ToLower()))
            .ToListAsync(ct);
}

/// <summary>
/// The board, for S8's reports.
/// </summary>
/// <remarks>
/// Delegates to the same query the screen uses rather than re-querying the items. That is the whole point of the
/// port: a report counting "three active" and a board showing four would be a bug nobody could locate, because
/// both would look correct in isolation.
/// </remarks>
internal sealed class PortfolioBoardReader(Cracra.BuildingBlocks.Mediator.ISender sender)
    : Cracra.Modules.Portfolio.Contracts.IPortfolioBoardReader
{
    public async Task<Cracra.Modules.Portfolio.Contracts.PortfolioBoard> GetBoardAsync(
        Guid? departmentId,
        CancellationToken ct) =>
        await sender.Send(new Application.GetBoardQuery(departmentId, null), ct);
}
