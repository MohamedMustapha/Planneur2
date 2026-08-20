using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Finance.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Finance.Endpoints;

// =================================================================================================================
// One endpoint per file is the convention; these are grouped because each is a two-line delegation to a service
// and splitting them would spread one readable surface over eight files of boilerplate.
//
// Every endpoint here is AnyHead, and that is the whole of the door. The spec asks for "strictly heads/PMO", with
// members and project-leads refused outright — which AnyHead does, at the policy, before a query runs.
//
// The matrix (visibility-matrix.md §4) is narrower than the door: capex/opex is dept-head and PMO, not unit-head.
// That narrowing is RLS's, through access.can_write_department_config, and the difference in outcome is
// deliberate: a project-lead is told no, and a unit-head — a head, but not one who administers a department — is
// simply shown nothing. The nav hides the entry from them for the same reason.
// =================================================================================================================

public class CapexOpexQuery
{
    /// <summary>department | project | portfolio. Omitted means the caller's own department.</summary>
    [QueryParam]
    public string? Scope { get; set; }

    [QueryParam]
    public Guid? ScopeId { get; set; }

    /// <summary>month | quarter | year | custom. Omitted means the current month.</summary>
    [QueryParam]
    public string? Period { get; set; }

    [QueryParam]
    public DateOnly? From { get; set; }

    [QueryParam]
    public DateOnly? To { get; set; }

    internal CapexOpexRequest ToRequest() => new(Scope, ScopeId, Period, From, To);
}

/// <summary>The split: amounts and hours, capex against opex, RLS-filtered to whoever may see them.</summary>
public sealed class GetCapexOpexEndpoint(ICapexOpexService capexOpex) : Endpoint<CapexOpexQuery, CapexOpexView>
{
    public override void Configure()
    {
        Get("/finance/capex-opex");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance")
            .WithSummary("Capex/opex split for a department, a project or the portfolio."));
    }

    public override async Task HandleAsync(CapexOpexQuery request, CancellationToken ct) =>
        await Send.OkAsync(await capexOpex.GetAsync(request.ToRequest(), ct), ct);
}

public sealed class ExportCapexOpexQuery : CapexOpexQuery
{
    /// <summary>xlsx. The only format, and named so a second one is a parameter rather than a new endpoint.</summary>
    [QueryParam]
    public string? Format { get; set; }
}

/// <summary>
/// Renders the split, stores it in RustFS and hands back a short-lived link.
/// </summary>
/// <remarks>
/// The same shape S8's report export takes, for the same reason: a workbook is served through a presigned URL
/// rather than proxied, so a large export never occupies a request thread.
/// </remarks>
public sealed class ExportCapexOpexEndpoint(ICapexOpexExporter exporter)
    : Endpoint<ExportCapexOpexQuery, CapexOpexExportView>
{
    public override void Configure()
    {
        Get("/finance/capex-opex/export");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance")
            .WithSummary("Export the capex/opex split and return a download link."));
    }

    public override async Task HandleAsync(ExportCapexOpexQuery request, CancellationToken ct) =>
        await Send.OkAsync(await exporter.ExportAsync(request.ToRequest(), request.Format, ct), ct);
}

// --- Rules -------------------------------------------------------------------------------------------------------

public sealed class RuleByDepartmentRequest
{
    [QueryParam]
    public Guid? DepartmentId { get; set; }
}

public sealed class GetRuleEndpoint(IFinanceConfigService config)
    : Endpoint<RuleByDepartmentRequest, CapexOpexRuleView>
{
    public override void Configure()
    {
        Get("/finance/rules");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance")
            .WithSummary("How a department treats each activity bucket."));
    }

    public override async Task HandleAsync(RuleByDepartmentRequest request, CancellationToken ct) =>
        await Send.OkAsync(await config.GetRuleAsync(request.DepartmentId, ct), ct);
}

public sealed class SaveRuleCommand
{
    public Guid DepartmentId { get; set; }

    public string BuildTreatment { get; set; } = "capex";

    public string RunTreatment { get; set; } = "opex";

    public string QolTreatment { get; set; } = "opex";

    public string AdminTreatment { get; set; } = "excluded";

    internal RuleRequest ToRequest() =>
        new(DepartmentId, BuildTreatment, RunTreatment, QolTreatment, AdminTreatment);
}

public sealed class SaveRuleEndpoint(IFinanceConfigService config) : Endpoint<SaveRuleCommand, CapexOpexRuleView>
{
    public override void Configure()
    {
        Put("/finance/rules");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance").WithSummary("Set a department's capex/opex rule."));
    }

    public override async Task HandleAsync(SaveRuleCommand request, CancellationToken ct) =>
        await Send.OkAsync(await config.SaveRuleAsync(request.ToRequest(), ct), ct);
}

// --- Rate cards --------------------------------------------------------------------------------------------------

public sealed class ListRateCardsRequest
{
    [QueryParam]
    public Guid? DepartmentId { get; set; }
}

public sealed class ListRateCardsEndpoint(IFinanceConfigService config)
    : Endpoint<ListRateCardsRequest, IReadOnlyList<RateCardView>>
{
    public override void Configure()
    {
        Get("/finance/rate-cards");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance").WithSummary("Hourly rates by functional role."));
    }

    public override async Task HandleAsync(ListRateCardsRequest request, CancellationToken ct) =>
        await Send.OkAsync(await config.GetRateCardsAsync(request.DepartmentId, ct), ct);
}

public sealed class SaveRateCardCommand
{
    /// <summary>Omitted creates; supplied updates. One endpoint, because a rate card has nothing worth patching.</summary>
    public Guid? Id { get; set; }

    public Guid DepartmentId { get; set; }

    public Guid FunctionalRoleId { get; set; }

    public decimal HourlyRate { get; set; }

    public string Currency { get; set; } = "EUR";

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Exclusive, and null for "still current".</summary>
    public DateOnly? EffectiveTo { get; set; }

    internal RateCardRequest ToRequest() =>
        new(DepartmentId, FunctionalRoleId, HourlyRate, Currency, EffectiveFrom, EffectiveTo);
}

public sealed class SaveRateCardEndpoint(IFinanceConfigService config)
    : Endpoint<SaveRateCardCommand, RateCardView>
{
    public override void Configure()
    {
        Put("/finance/rate-cards");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance").WithSummary("Create or update an hourly rate."));
    }

    public override async Task HandleAsync(SaveRateCardCommand request, CancellationToken ct)
    {
        var saved = await config.SaveRateCardAsync(request.Id, request.ToRequest(), ct);

        await Send.ResponseAsync(
            saved,
            request.Id is null ? StatusCodes.Status201Created : StatusCodes.Status200OK,
            ct);
    }
}

public sealed class DeleteRateCardRequest
{
    public Guid Id { get; set; }
}

public sealed class DeleteRateCardEndpoint(IFinanceConfigService config) : Endpoint<DeleteRateCardRequest>
{
    public override void Configure()
    {
        Delete("/finance/rate-cards/{id}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Finance").WithSummary("Remove an hourly rate."));
    }

    public override async Task HandleAsync(DeleteRateCardRequest request, CancellationToken ct)
    {
        await config.DeleteRateCardAsync(request.Id, ct);

        await Send.NoContentAsync(ct);
    }
}

