using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Finance.Data;
using Cracra.Modules.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Finance.Services;

public sealed record SaveBudgetRequest(
    string? ScopeType,
    Guid ScopeId,
    Guid? OwnerNodeId,
    int FiscalYear,
    decimal PlannedAmount,
    string? Currency,
    string? Notes);

public sealed record SaveComponentRequest(
    Guid? ItemId,
    Guid? NodeId,
    Guid? OwnerNodeId,
    string Kind,
    string Label,
    string? Treatment,
    decimal Amount,
    string? Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? LicenseId,
    Guid? ExternalWorkerId,
    string? Notes);

public sealed record SaveLicenseRequest(
    Guid NodeId,
    Guid? ItemId,
    string ProductName,
    string? Vendor,
    int Seats,
    decimal UnitCost,
    string? Currency,
    string? BillingCycle,
    DateOnly? RenewalDate,
    string? Notes);

public sealed record SaveExternalWorkerRequest(
    Guid NodeId,
    Guid? ItemId,
    Guid? PersonId,
    string DisplayName,
    string? Vendor,
    string? Role,
    decimal Rate,
    string? RateUnit,
    string? Currency,
    DateOnly ContractStart,
    DateOnly? ContractEnd);

public sealed record LicenseView(
    Guid Id,
    Guid NodeId,
    Guid? ItemId,
    string ProductName,
    string? Vendor,
    int Seats,
    decimal UnitCost,
    decimal AnnualCost,
    string Currency,
    string BillingCycle,
    DateOnly? RenewalDate,
    bool Active);

public sealed record ExternalWorkerView(
    Guid Id,
    Guid NodeId,
    Guid? ItemId,
    string DisplayName,
    string? Vendor,
    string? Role,
    decimal Rate,
    string RateUnit,
    string Currency,
    DateOnly ContractStart,
    DateOnly? ContractEnd,
    bool Active);

public sealed record CostComponentView(
    Guid Id,
    Guid? ItemId,
    Guid? NodeId,
    Guid OwnerNodeId,
    string Kind,
    string Label,
    string Treatment,
    bool TreatmentOverridden,
    decimal Amount,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);

public interface ICommitmentService
{
    Task<Guid> SaveBudgetAsync(SaveBudgetRequest request, CancellationToken ct);

    Task<Guid> AddComponentAsync(SaveComponentRequest request, CancellationToken ct);

    Task<IReadOnlyList<CostComponentView>> ComponentsAsync(Guid? itemId, Guid? nodeId, CancellationToken ct);

    Task<Guid> AddLicenseAsync(SaveLicenseRequest request, CancellationToken ct);

    Task<IReadOnlyList<LicenseView>> LicensesAsync(Guid? nodeId, int? renewingWithinDays, CancellationToken ct);

    Task<Guid> AddExternalWorkerAsync(SaveExternalWorkerRequest request, CancellationToken ct);

    Task<IReadOnlyList<ExternalWorkerView>> ExternalWorkersAsync(
        Guid? nodeId,
        int? expiringWithinDays,
        CancellationToken ct);
}

/// <summary>
/// Everything the finance module writes, and the reads that hang off it.
/// </summary>
/// <remarks>
/// A licence and a consultant are not costs by themselves — they are commitments — so each one also lays down the
/// cost component that carries it into the rollup. Doing that here rather than leaving it to the caller is what
/// stops the renewal calendar and the consolidated total from disagreeing about what is being paid for.
/// </remarks>
internal sealed class CommitmentService(
    FinanceDbContext context,
    IFinanceConfigService config,
    IUserContext user) : ICommitmentService
{
    public async Task<Guid> SaveBudgetAsync(SaveBudgetRequest request, CancellationToken ct)
    {
        var scope = BudgetScopes.Parse(request.ScopeType ?? BudgetScopes.Node);
        var owner = request.OwnerNodeId ?? (scope == BudgetScope.Node ? request.ScopeId : Guid.Empty);
        var now = DateTimeOffset.UtcNow;

        var existing = await context.Budgets
            .AsTracking()
            .SingleOrDefaultAsync(
                budget => budget.ScopeType == scope
                          && budget.ScopeId == request.ScopeId
                          && budget.FiscalYear == request.FiscalYear,
                ct);

        if (existing is not null)
        {
            existing.Revise(request.PlannedAmount, request.Notes, user.UserId, now);

            await context.SaveChangesAsync(ct);

            return existing.Id;
        }

        var budget = Budget.For(
            scope,
            request.ScopeId,
            owner,
            request.FiscalYear,
            request.PlannedAmount,
            request.Currency,
            request.Notes,
            user.UserId,
            now);

        context.Budgets.Add(budget);

        await context.SaveChangesAsync(ct);

        return budget.Id;
    }

    public async Task<Guid> AddComponentAsync(SaveComponentRequest request, CancellationToken ct)
    {
        var owner = request.OwnerNodeId ?? request.NodeId
            ?? throw new DomainRuleViolationException(
                "A cost attached to an item needs the node it rolls up under.");

        var component = CostComponent.Create(
            request.ItemId,
            request.NodeId,
            owner,
            CostKinds.Parse(request.Kind),
            request.Label,
            await TreatmentAsync(request, owner, ct),
            request.Treatment is { Length: > 0 },
            request.Amount,
            request.Currency,
            request.PeriodStart,
            request.PeriodEnd,
            request.LicenseId,
            request.ExternalWorkerId,
            request.Notes,
            user.UserId,
            DateTimeOffset.UtcNow);

        context.Components.Add(component);

        await context.SaveChangesAsync(ct);

        return component.Id;
    }

    public async Task<IReadOnlyList<CostComponentView>> ComponentsAsync(
        Guid? itemId,
        Guid? nodeId,
        CancellationToken ct)
    {
        var query = context.Components.AsNoTracking();

        if (itemId is { } item)
        {
            query = query.Where(component => component.ItemId == item);
        }

        if (nodeId is { } node)
        {
            query = query.Where(component => component.OwnerNodeId == node);
        }

        return await query
            .OrderByDescending(component => component.PeriodStart)
            .Select(component => new CostComponentView(
                component.Id,
                component.ItemId,
                component.NodeId,
                component.OwnerNodeId,
                CostKinds.Wire(component.Kind),
                component.Label,
                component.Treatment,
                component.TreatmentOverridden,
                component.Amount,
                component.Currency,
                component.PeriodStart,
                component.PeriodEnd))
            .ToListAsync(ct);
    }

    public async Task<Guid> AddLicenseAsync(SaveLicenseRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var license = License.Create(
            request.NodeId,
            request.ItemId,
            request.ProductName,
            request.Vendor,
            request.Seats,
            request.UnitCost,
            request.Currency,
            BillingCycles.Parse(request.BillingCycle ?? BillingCycles.Yearly),
            request.RenewalDate,
            request.Notes,
            user.UserId,
            now);

        context.Licenses.Add(license);

        // The cost that carries it into the rollup. A licence with no component would show on the renewal
        // calendar and in nobody's total, which is the discrepancy this module exists to remove.
        context.Components.Add(CostComponent.Create(
            request.ItemId,
            request.ItemId is null ? request.NodeId : null,
            request.NodeId,
            CostKind.License,
            license.ProductName,
            (await config.GetRuleAsync(request.NodeId, ct)).ToDomain().TreatmentFor(Buckets.Run),
            treatmentOverridden: false,
            license.AnnualCost,
            license.Currency,
            Start(license.RenewalDate),
            End(license.RenewalDate),
            license.Id,
            null,
            null,
            user.UserId,
            now));

        await context.SaveChangesAsync(ct);

        return license.Id;
    }

    public async Task<IReadOnlyList<LicenseView>> LicensesAsync(
        Guid? nodeId,
        int? renewingWithinDays,
        CancellationToken ct)
    {
        var query = context.Licenses.AsNoTracking().Where(license => license.Active);

        if (nodeId is { } node)
        {
            query = query.Where(license => license.NodeId == node);
        }

        if (renewingWithinDays is { } within)
        {
            var horizon = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(within);

            query = query.Where(license => license.RenewalDate != null && license.RenewalDate <= horizon);
        }

        return await query
            .OrderBy(license => license.RenewalDate ?? DateOnly.MaxValue)
            .Select(license => new LicenseView(
                license.Id,
                license.NodeId,
                license.ItemId,
                license.ProductName,
                license.Vendor,
                license.Seats,
                license.UnitCost,
                license.AnnualCost,
                license.Currency,
                BillingCycles.Wire(license.BillingCycle),
                license.RenewalDate,
                license.Active))
            .ToListAsync(ct);
    }

    public async Task<Guid> AddExternalWorkerAsync(SaveExternalWorkerRequest request, CancellationToken ct)
    {
        var worker = ExternalWorker.Create(
            request.NodeId,
            request.ItemId,
            request.PersonId,
            request.DisplayName,
            request.Vendor,
            request.Role,
            request.Rate,
            request.RateUnit,
            request.Currency,
            request.ContractStart,
            request.ContractEnd,
            user.UserId,
            DateTimeOffset.UtcNow);

        context.ExternalWorkers.Add(worker);

        await context.SaveChangesAsync(ct);

        return worker.Id;
    }

    public async Task<IReadOnlyList<ExternalWorkerView>> ExternalWorkersAsync(
        Guid? nodeId,
        int? expiringWithinDays,
        CancellationToken ct)
    {
        var query = context.ExternalWorkers.AsNoTracking().Where(worker => worker.Active);

        if (nodeId is { } node)
        {
            query = query.Where(worker => worker.NodeId == node);
        }

        if (expiringWithinDays is { } within)
        {
            var horizon = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(within);

            query = query.Where(worker => worker.ContractEnd != null && worker.ContractEnd <= horizon);
        }

        return await query
            .OrderBy(worker => worker.ContractEnd ?? DateOnly.MaxValue)
            .Select(worker => new ExternalWorkerView(
                worker.Id,
                worker.NodeId,
                worker.ItemId,
                worker.DisplayName,
                worker.Vendor,
                worker.Role,
                worker.Rate,
                worker.RateUnit,
                worker.Currency,
                worker.ContractStart,
                worker.ContractEnd,
                worker.Active))
            .ToListAsync(ct);
    }

    /// <summary>
    /// The treatment the node's rule gives this kind of cost, unless somebody said otherwise.
    /// </summary>
    /// <remarks>
    /// Kinds map onto the same buckets activity does, so one rule governs both and a department cannot end up
    /// treating a bought hour differently from a worked one without saying so deliberately.
    /// </remarks>
    private async Task<string> TreatmentAsync(SaveComponentRequest request, Guid owner, CancellationToken ct)
    {
        if (request.Treatment is { Length: > 0 } chosen)
        {
            return chosen;
        }

        var bucket = CostKinds.Parse(request.Kind) switch
        {
            CostKind.Hardware => Buckets.Build,
            CostKind.InternalEffort => Buckets.Build,
            _ => Buckets.Run,
        };

        var rule = await config.GetRuleAsync(owner, ct);

        return rule.ToDomain().TreatmentFor(bucket);
    }

    /// <summary>
    /// The year a licence's cost sits in.
    /// </summary>
    /// <remarks>
    /// Anchored on the renewal date, because that is the date the money moves. A licence with no renewal recorded
    /// falls in the current year, which is at least the year somebody entered it.
    /// </remarks>
    private static DateOnly Start(DateOnly? renewal) =>
        new(renewal?.Year ?? DateTimeOffset.UtcNow.Year, 1, 1);

    private static DateOnly End(DateOnly? renewal) =>
        new(renewal?.Year ?? DateTimeOffset.UtcNow.Year, 12, 31);
}
