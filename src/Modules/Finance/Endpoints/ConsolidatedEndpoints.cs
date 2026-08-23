using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Finance.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Finance.Endpoints;

// =================================================================================================================
// The consolidated view and the two calendars (v2 §04.3).
//
// AnyHead throughout, matching §04.4: members and POs are refused the module rather than shown an empty one. The
// node they may actually see is RLS's answer, so a head asking for a branch above them gets their own share of it
// and not somebody else's.
// =================================================================================================================

public sealed class ConsolidatedRequestModel
{
    /// <summary>Optional. Omitted, the server lands on the highest node the caller heads.</summary>
    [QueryParam]
    public Guid? NodeId { get; set; }

    [QueryParam]
    public int? Fy { get; set; }

    [QueryParam]
    public string? Mode { get; set; }

    [QueryParam]
    public int? Depth { get; set; }
}

public sealed class GetConsolidatedEndpoint(IConsolidationService consolidation)
    : Endpoint<ConsolidatedRequestModel, ConsolidatedView>
{
    public override void Configure()
    {
        Get("/finance/consolidated");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance")
            .WithSummary("Costs rolled up through the node tree, from wherever the caller stands."));
    }

    public override async Task HandleAsync(ConsolidatedRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            await consolidation.GetAsync(
                new ConsolidatedRequest(request.NodeId, request.Fy, request.Mode, request.Depth),
                ct),
            ct);
}

public sealed class ComponentsRequestModel
{
    [QueryParam]
    public Guid? ItemId { get; set; }

    [QueryParam]
    public Guid? NodeId { get; set; }
}

public sealed class GetComponentsEndpoint(ICommitmentService commitments)
    : Endpoint<ComponentsRequestModel, IReadOnlyList<CostComponentView>>
{
    public override void Configure()
    {
        Get("/finance/components");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance").WithSummary("The cost lines behind a total."));
    }

    public override async Task HandleAsync(ComponentsRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(await commitments.ComponentsAsync(request.ItemId, request.NodeId, ct), ct);
}

public sealed class AddComponentRequestModel
{
    public Guid? ItemId { get; set; }

    public Guid? NodeId { get; set; }

    public Guid? OwnerNodeId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string? Treatment { get; set; }

    public decimal Amount { get; set; }

    public string? Currency { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public Guid? LicenseId { get; set; }

    public Guid? ExternalWorkerId { get; set; }

    public string? Notes { get; set; }
}

public sealed class AddComponentEndpoint(ICommitmentService commitments)
    : Endpoint<AddComponentRequestModel, SavedResponse>
{
    public override void Configure()
    {
        Post("/finance/components");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance").WithSummary("Record a cost."));
    }

    public override async Task HandleAsync(AddComponentRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            new SavedResponse(await commitments.AddComponentAsync(
                new SaveComponentRequest(
                    request.ItemId,
                    request.NodeId,
                    request.OwnerNodeId,
                    request.Kind,
                    request.Label,
                    request.Treatment,
                    request.Amount,
                    request.Currency,
                    request.PeriodStart,
                    request.PeriodEnd,
                    request.LicenseId,
                    request.ExternalWorkerId,
                    request.Notes),
                ct)),
            ct);
}

public sealed record SavedResponse(Guid Id);

public sealed class SaveBudgetRequestModel
{
    public string? ScopeType { get; set; }

    public Guid ScopeId { get; set; }

    public Guid? OwnerNodeId { get; set; }

    public int FiscalYear { get; set; }

    public decimal PlannedAmount { get; set; }

    public string? Currency { get; set; }

    public string? Notes { get; set; }
}

public sealed class SaveBudgetEndpoint(ICommitmentService commitments)
    : Endpoint<SaveBudgetRequestModel, SavedResponse>
{
    public override void Configure()
    {
        Post("/finance/budgets");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance").WithSummary("Set or revise an envelope."));
    }

    public override async Task HandleAsync(SaveBudgetRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            new SavedResponse(await commitments.SaveBudgetAsync(
                new SaveBudgetRequest(
                    request.ScopeType,
                    request.ScopeId,
                    request.OwnerNodeId,
                    request.FiscalYear,
                    request.PlannedAmount,
                    request.Currency,
                    request.Notes),
                ct)),
            ct);
}

public sealed class LicensesRequestModel
{
    [QueryParam]
    public Guid? NodeId { get; set; }

    /// <summary>Days. Present, the answer is the renewal calendar rather than the whole list.</summary>
    [QueryParam]
    public int? Within { get; set; }
}

public sealed class GetLicensesEndpoint(ICommitmentService commitments)
    : Endpoint<LicensesRequestModel, IReadOnlyList<LicenseView>>
{
    public override void Configure()
    {
        Get("/finance/licenses");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance").WithSummary("Licences, or the ones falling due."));
    }

    public override async Task HandleAsync(LicensesRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(await commitments.LicensesAsync(request.NodeId, request.Within, ct), ct);
}

public sealed class AddLicenseRequestModel
{
    public Guid NodeId { get; set; }

    public Guid? ItemId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string? Vendor { get; set; }

    public int Seats { get; set; }

    public decimal UnitCost { get; set; }

    public string? Currency { get; set; }

    public string? BillingCycle { get; set; }

    public DateOnly? RenewalDate { get; set; }

    public string? Notes { get; set; }
}

public sealed class AddLicenseEndpoint(ICommitmentService commitments)
    : Endpoint<AddLicenseRequestModel, SavedResponse>
{
    public override void Configure()
    {
        Post("/finance/licenses");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance").WithSummary("Record a licence and what it costs."));
    }

    public override async Task HandleAsync(AddLicenseRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            new SavedResponse(await commitments.AddLicenseAsync(
                new SaveLicenseRequest(
                    request.NodeId,
                    request.ItemId,
                    request.ProductName,
                    request.Vendor,
                    request.Seats,
                    request.UnitCost,
                    request.Currency,
                    request.BillingCycle,
                    request.RenewalDate,
                    request.Notes),
                ct)),
            ct);
}

public sealed class ExternalWorkersRequestModel
{
    [QueryParam]
    public Guid? NodeId { get; set; }

    [QueryParam]
    public int? Within { get; set; }
}

public sealed class GetExternalWorkersEndpoint(ICommitmentService commitments)
    : Endpoint<ExternalWorkersRequestModel, IReadOnlyList<ExternalWorkerView>>
{
    public override void Configure()
    {
        Get("/finance/external-workers");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance")
            .WithSummary("Consultants, or the ones whose contract is ending."));
    }

    public override async Task HandleAsync(ExternalWorkersRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(await commitments.ExternalWorkersAsync(request.NodeId, request.Within, ct), ct);
}

public sealed class AddExternalWorkerRequestModel
{
    public Guid NodeId { get; set; }

    public Guid? ItemId { get; set; }

    public Guid? PersonId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Vendor { get; set; }

    public string? Role { get; set; }

    public decimal Rate { get; set; }

    public string? RateUnit { get; set; }

    public string? Currency { get; set; }

    public DateOnly ContractStart { get; set; }

    public DateOnly? ContractEnd { get; set; }
}

public sealed class AddExternalWorkerEndpoint(ICommitmentService commitments)
    : Endpoint<AddExternalWorkerRequestModel, SavedResponse>
{
    public override void Configure()
    {
        Post("/finance/external-workers");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance").WithSummary("Engage a consultant."));
    }

    public override async Task HandleAsync(AddExternalWorkerRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            new SavedResponse(await commitments.AddExternalWorkerAsync(
                new SaveExternalWorkerRequest(
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
                    request.ContractEnd),
                ct)),
            ct);
}

public sealed class ExportConsolidatedRequestModel
{
    [QueryParam]
    public Guid? NodeId { get; set; }

    [QueryParam]
    public int? Fy { get; set; }

    [QueryParam]
    public string? Mode { get; set; }

    [QueryParam]
    public string? Format { get; set; }
}

/// <summary>
/// The consolidated view as a workbook (v2 §04.3).
/// </summary>
/// <remarks>
/// Returns where the file is rather than the bytes: the object lands in storage under the caller's own session
/// and the answer is a presigned link with an expiry, which is what keeps a forwarded URL from outliving the
/// entitlement that produced it.
/// </remarks>
public sealed class ExportConsolidatedEndpoint(IConsolidatedExporter exporter)
    : Endpoint<ExportConsolidatedRequestModel, ConsolidatedExportView>
{
    public override void Configure()
    {
        Get("/finance/consolidated/export");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance").WithSummary("Export the consolidated view."));
    }

    public override async Task HandleAsync(ExportConsolidatedRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            await exporter.ExportAsync(
                new ConsolidatedRequest(request.NodeId, request.Fy, request.Mode, null),
                request.Format,
                ct),
            ct);
}
