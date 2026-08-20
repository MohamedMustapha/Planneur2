using Cracra.Modules.Portfolio.Domain;

namespace Cracra.Modules.Portfolio.Application;

/// <summary>
/// Loads and stores the aggregate whole, iterations included.
/// </summary>
/// <remarks>
/// Every command mutates the item through its own methods, and several of them (activation, archiving, closing an
/// iteration) touch the iterations as part of the same act — so a partial load would let a guard pass on evidence
/// it did not have.
/// </remarks>
public interface IPortfolioRepository
{
    Task<PortfolioItem> GetAsync(Guid itemId, CancellationToken ct);

    Task AddAsync(PortfolioItem item, CancellationToken ct);

    /// <summary>Appends to the audit trail. Separate from the aggregate because it is append-only.</summary>
    Task RecordTransitionAsync(PortfolioTransition transition, CancellationToken ct);

    Task<bool> IsLinkedToProjectAsync(Guid projectId, CancellationToken ct);
}

/// <summary>
/// What Portfolio needs from Projects, as a port.
/// </summary>
/// <remarks>
/// The adapter forwards to <c>Projects.Contracts.IProjectProvisioner</c>. Wrapped rather than used directly so the
/// commitment and activation rules stay unit-testable without dragging the Projects module in behind them.
/// </remarks>
public interface IProjectsPort
{
    Task<Guid> ProvisionAsync(string code, string name, string? description, Guid departmentId, CancellationToken ct);

    Task<bool> ExistsAsync(Guid projectId, CancellationToken ct);

    /// <summary>The activation guard's evidence: only Projects knows whether anyone is staffed on it.</summary>
    Task<bool> HasTeamAsync(Guid projectId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, ProjectFacts>> GetFactsAsync(
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct);
}

/// <summary>The bits of a project the board cards show. Deliberately not the whole ProjectSummary.</summary>
public sealed record ProjectFacts(decimal CostAmount, string CostCurrency, string Classification);

public interface IDirectoryPort
{
    Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetDepartmentNameKeysAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct);
}
