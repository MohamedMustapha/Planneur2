using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Strategy.Application;
using Cracra.Modules.Strategy.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Strategy.Api;

// =================================================================================================================
// Strategy (v2 §06.3).
//
// Reads are Authenticated and writes are AnyHead, and the asymmetry is the point of the slice. A member must be
// able to see what their branch committed to and which of it their work serves — a strategy nobody below the head
// can read is a poster, not a spine. Writing one is a head's or the PMO's act, and RLS narrows both ends further
// so that being allowed to call an endpoint is never the same as being allowed to see a row.
// =================================================================================================================

public sealed class ListStrategiesRequest
{
    [QueryParam]
    public Guid? ScopeId { get; set; }

    [QueryParam]
    public string? Status { get; set; }
}

public sealed class ListStrategiesEndpoint(IStrategyService strategies)
    : Endpoint<ListStrategiesRequest, IReadOnlyList<StrategyView>>
{
    public override void Configure()
    {
        Get("/strategy");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Strategy").WithSummary("The strategies the caller may see."));
    }

    public override async Task HandleAsync(ListStrategiesRequest request, CancellationToken ct) =>
        await Send.OkAsync(await strategies.ListAsync(request.ScopeId, request.Status, ct), ct);
}

public sealed record SavedStrategyResponse(Guid Id);

public sealed class OpenStrategyRequestModel
{
    public string? ScopeType { get; set; }

    public Guid? ScopeId { get; set; }

    public DateOnly? PeriodFrom { get; set; }

    public DateOnly? PeriodTo { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Narrative { get; set; }
}

public sealed class OpenStrategyEndpoint(IStrategyService strategies)
    : Endpoint<OpenStrategyRequestModel, SavedStrategyResponse>
{
    public override void Configure()
    {
        Post("/strategy");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Strategy").WithSummary("Set out what a scope is trying to do."));
    }

    public override async Task HandleAsync(OpenStrategyRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            new SavedStrategyResponse(
                await strategies.OpenAsync(
                    new OpenStrategyRequest(
                        request.ScopeType,
                        request.ScopeId,
                        request.PeriodFrom,
                        request.PeriodTo,
                        request.Title,
                        request.Narrative),
                    ct)),
            ct);
}

public sealed class AmendStrategyRequestModel
{
    public Guid Id { get; set; }

    public string? Title { get; set; }

    public string? Narrative { get; set; }

    public DateOnly? PeriodFrom { get; set; }

    public DateOnly? PeriodTo { get; set; }

    public string? Status { get; set; }
}

public sealed class AmendStrategyEndpoint(IStrategyService strategies) : Endpoint<AmendStrategyRequestModel>
{
    public override void Configure()
    {
        Patch("/strategy/{id}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Strategy").WithSummary("Retitle, re-date, publish or close it."));
    }

    public override async Task HandleAsync(AmendStrategyRequestModel request, CancellationToken ct)
    {
        await strategies.AmendAsync(
            request.Id,
            new AmendStrategyRequest(
                request.Title,
                request.Narrative,
                request.PeriodFrom,
                request.PeriodTo,
                request.Status),
            ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class StrategyByIdRequest
{
    public Guid Id { get; set; }
}

public sealed class GetRollupEndpoint(IStrategyService strategies) : Endpoint<StrategyByIdRequest, StrategyRollup>
{
    public override void Configure()
    {
        Get("/strategy/{id}/rollup");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Strategy")
            .WithSummary("Objectives with progress, and the work behind each."));
    }

    public override async Task HandleAsync(StrategyByIdRequest request, CancellationToken ct) =>
        await Send.OkAsync(await strategies.RollupAsync(request.Id, ct), ct);
}

// --- Objectives ----------------------------------------------------------------------------------------------

public sealed class AddObjectiveRequestModel
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? MetricKind { get; set; }

    public decimal? Baseline { get; set; }

    public decimal? Target { get; set; }

    public decimal? Current { get; set; }

    public string? Unit { get; set; }

    public DateOnly? Due { get; set; }

    public decimal? Weight { get; set; }
}

public sealed class AddObjectiveEndpoint(IStrategyService strategies)
    : Endpoint<AddObjectiveRequestModel, SavedStrategyResponse>
{
    public override void Configure()
    {
        Post("/strategy/{id}/objectives");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Strategy").WithSummary("Add a measurable commitment."));
    }

    public override async Task HandleAsync(AddObjectiveRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            new SavedStrategyResponse(
                await strategies.AddObjectiveAsync(
                    request.Id,
                    new AddObjectiveRequest(
                        request.Title,
                        request.Description,
                        request.MetricKind,
                        request.Baseline,
                        request.Target,
                        request.Current,
                        request.Unit,
                        request.Due,
                        request.Weight),
                    ct)),
            ct);
}

public sealed class AmendObjectiveRequestModel
{
    public Guid ObjectiveId { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public decimal? Baseline { get; set; }

    public decimal? Target { get; set; }

    /// <summary>Where the measure stands now. Sent on its own, this is the monthly reading.</summary>
    public decimal? Current { get; set; }

    public string? Unit { get; set; }

    public DateOnly? Due { get; set; }

    public decimal? Weight { get; set; }

    public string? Status { get; set; }

    /// <summary>Hands the status back to the arithmetic after somebody had overridden it.</summary>
    public bool ClearStatusOverride { get; set; }
}

public sealed class AmendObjectiveEndpoint(IStrategyService strategies) : Endpoint<AmendObjectiveRequestModel>
{
    public override void Configure()
    {
        Patch("/strategy/objectives/{objectiveId}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Strategy")
            .WithSummary("Record a reading, or restate the commitment."));
    }

    public override async Task HandleAsync(AmendObjectiveRequestModel request, CancellationToken ct)
    {
        await strategies.AmendObjectiveAsync(
            request.ObjectiveId,
            new AmendObjectiveRequest(
                request.Title,
                request.Description,
                request.Baseline,
                request.Target,
                request.Current,
                request.Unit,
                request.Due,
                request.Weight,
                request.Status,
                request.ClearStatusOverride),
            ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class ObjectiveByIdRequest
{
    public Guid ObjectiveId { get; set; }
}

public sealed class RemoveObjectiveEndpoint(IStrategyService strategies) : Endpoint<ObjectiveByIdRequest>
{
    public override void Configure()
    {
        Delete("/strategy/objectives/{objectiveId}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Strategy").WithSummary("Drop an objective."));
    }

    public override async Task HandleAsync(ObjectiveByIdRequest request, CancellationToken ct)
    {
        await strategies.RemoveObjectiveAsync(request.ObjectiveId, ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class AddKeyResultRequestModel
{
    public Guid ObjectiveId { get; set; }

    public string Title { get; set; } = string.Empty;

    public decimal Target { get; set; }

    public decimal? Current { get; set; }
}

public sealed class AddKeyResultEndpoint(IStrategyService strategies)
    : Endpoint<AddKeyResultRequestModel, SavedStrategyResponse>
{
    public override void Configure()
    {
        Post("/strategy/objectives/{objectiveId}/key-results");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Strategy").WithSummary("Break an objective into sub-measures."));
    }

    public override async Task HandleAsync(AddKeyResultRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            new SavedStrategyResponse(
                await strategies.AddKeyResultAsync(
                    request.ObjectiveId,
                    new AddKeyResultRequest(request.Title, request.Target, request.Current),
                    ct)),
            ct);
}

public sealed class RemoveKeyResultRequest
{
    public Guid ObjectiveId { get; set; }

    public Guid KeyResultId { get; set; }
}

public sealed class RemoveKeyResultEndpoint(IStrategyService strategies) : Endpoint<RemoveKeyResultRequest>
{
    public override void Configure()
    {
        Delete("/strategy/objectives/{objectiveId}/key-results/{keyResultId}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Strategy").WithSummary("Drop a sub-measure."));
    }

    public override async Task HandleAsync(RemoveKeyResultRequest request, CancellationToken ct)
    {
        await strategies.RemoveKeyResultAsync(request.ObjectiveId, request.KeyResultId, ct);

        await Send.NoContentAsync(ct);
    }
}

// --- Contributions -------------------------------------------------------------------------------------------

public sealed class LinkContributionRequestModel
{
    public Guid ObjectiveId { get; set; }

    /// <summary>item or problem. Defaults to item — most contributions are work already in the portfolio.</summary>
    public string? SourceType { get; set; }

    public Guid SourceId { get; set; }

    public decimal? Weight { get; set; }

    public string? Note { get; set; }
}

public sealed class LinkContributionEndpoint(IStrategyService strategies)
    : Endpoint<LinkContributionRequestModel, SavedStrategyResponse>
{
    public override void Configure()
    {
        Post("/strategy/objectives/{objectiveId}/contributions");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Strategy")
            .WithSummary("Say which work moves this objective."));
    }

    public override async Task HandleAsync(LinkContributionRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            new SavedStrategyResponse(
                await strategies.LinkAsync(
                    request.ObjectiveId,
                    new LinkContributionRequest(
                        request.SourceType,
                        request.SourceId,
                        request.Weight,
                        request.Note),
                    ct)),
            ct);
}

public sealed class UnlinkContributionRequest
{
    public Guid ObjectiveId { get; set; }

    public Guid ContributionId { get; set; }
}

public sealed class UnlinkContributionEndpoint(IStrategyService strategies) : Endpoint<UnlinkContributionRequest>
{
    public override void Configure()
    {
        Delete("/strategy/objectives/{objectiveId}/contributions/{contributionId}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Strategy").WithSummary("This work no longer serves it."));
    }

    public override async Task HandleAsync(UnlinkContributionRequest request, CancellationToken ct)
    {
        await strategies.UnlinkAsync(request.ObjectiveId, request.ContributionId, ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class AlignmentRequest
{
    [QueryParam]
    public Guid? ScopeId { get; set; }
}

/// <summary>
/// The gaps in both directions (v2 §06.3).
/// </summary>
/// <remarks>
/// Authenticated rather than head-gated, because both halves are already narrowed by RLS and because a member who
/// can see that their project serves no objective is a member who can go and ask why. Hiding that from them would
/// keep alignment a management activity, which is how it stops happening.
/// </remarks>
public sealed class GetAlignmentEndpoint(IStrategyService strategies) : Endpoint<AlignmentRequest, AlignmentGaps>
{
    public override void Configure()
    {
        Get("/strategy/alignment");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Strategy")
            .WithSummary("Objectives nobody is working on, and work serving no objective."));
    }

    public override async Task HandleAsync(AlignmentRequest request, CancellationToken ct) =>
        await Send.OkAsync(await strategies.AlignmentAsync(request.ScopeId, ct), ct);
}
