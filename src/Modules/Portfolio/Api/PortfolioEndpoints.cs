using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Portfolio.Application;
using Cracra.Modules.Portfolio.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Portfolio.Api;

// =================================================================================================================
// Thin by design: map to a command or query, send, map back. The lifecycle guards are the aggregate's and the row
// filtering is RLS's; what the policies here decide is only who may attempt a transition at all.
// =================================================================================================================

public sealed class GetBoardRequest
{
    [QueryParam]
    public Guid? DepartmentId { get; set; }

    /// <summary>"mine" narrows to what the caller raised or decided; anything else is the full visible board.</summary>
    [QueryParam]
    public string? Scope { get; set; }
}

public sealed class GetBoardEndpoint(ISender sender) : Endpoint<GetBoardRequest, PortfolioBoard>
{
    public override void Configure()
    {
        Get("/portfolio");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Portfolio").WithSummary("The portfolio board, grouped by lane."));
    }

    public override async Task HandleAsync(GetBoardRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetBoardQuery(request.DepartmentId, request.Scope), ct), ct);
}

public sealed class PortfolioItemByIdRequest
{
    public Guid Id { get; set; }
}

public sealed class GetItemEndpoint(ISender sender) : Endpoint<PortfolioItemByIdRequest, PortfolioItemDetail>
{
    public override void Configure()
    {
        Get("/portfolio/{id}");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Portfolio")
            .WithSummary("One portfolio item, with its iterations and decision history."));
    }

    public override async Task HandleAsync(PortfolioItemByIdRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetItemQuery(request.Id), ct), ct);
}

public sealed class ConsiderItemRequest
{
    public string Name { get; set; } = string.Empty;

    public Guid DepartmentId { get; set; }

    public int Priority { get; set; } = 100;

    public string? Notes { get; set; }
}

public sealed record ItemCreatedResponse(Guid Id);

public sealed class ConsiderItemEndpoint(ISender sender) : Endpoint<ConsiderItemRequest, ItemCreatedResponse>
{
    public override void Configure()
    {
        Post("/portfolio/considered");
        // Registering a candidate commits nobody to anything, so it is open to anyone who runs delivery work
        // rather than reserved to the PMO. Committing to it, below, is where the bar rises.
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Register a candidate."));
    }

    public override async Task HandleAsync(ConsiderItemRequest request, CancellationToken ct)
    {
        var id = await sender.Send(
            new ConsiderItemCommand(request.Name, request.DepartmentId, request.Priority, request.Notes),
            ct);

        await Send.ResponseAsync(new ItemCreatedResponse(id), StatusCodes.Status201Created, ct);
    }
}

public sealed class CommitItemRequest
{
    public Guid Id { get; set; }

    public Guid? ProjectId { get; set; }

    public string? ProjectCode { get; set; }

    public string? ProjectName { get; set; }

    public string DecisionNotes { get; set; } = string.Empty;
}

public sealed record ItemCommittedResponse(Guid ProjectId);

public sealed class CommitItemEndpoint(ISender sender) : Endpoint<CommitItemRequest, ItemCommittedResponse>
{
    public override void Configure()
    {
        Post("/portfolio/{id}/commit");
        // Heads and the PMO only. Committing allocates budget and people; a project lead can propose a candidate
        // but should not be the one deciding the department will fund it.
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Portfolio")
            .WithSummary("Commit to a candidate, linking or provisioning its project."));
    }

    public override async Task HandleAsync(CommitItemRequest request, CancellationToken ct)
    {
        var projectId = await sender.Send(
            new CommitItemCommand(
                request.Id,
                request.ProjectId,
                request.ProjectCode,
                request.ProjectName,
                request.DecisionNotes),
            ct);

        await Send.OkAsync(new ItemCommittedResponse(projectId), ct);
    }
}

/// <summary>
/// Starts delivery. Everything it needs is in the route.
/// </summary>
/// <remarks>
/// EndpointWithoutRequest rather than a DTO holding only the route id: with a request type FastEndpoints expects a
/// JSON body and answers 415 to a bodiless POST, which is exactly how a client would reasonably call an action
/// endpoint that takes no arguments.
/// </remarks>
public sealed class ActivateItemEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/portfolio/{id}/activate");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Start delivery."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await sender.Send(new ActivateItemCommand(Route<Guid>("id")), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class ArchiveItemRequest
{
    public Guid Id { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed class ArchiveItemEndpoint(ISender sender) : Endpoint<ArchiveItemRequest>
{
    public override void Configure()
    {
        Post("/portfolio/{id}/archive");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Retire the item from production."));
    }

    public override async Task HandleAsync(ArchiveItemRequest request, CancellationToken ct)
    {
        await sender.Send(new ArchiveItemCommand(request.Id, request.Reason), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class RevertItemRequest
{
    public Guid Id { get; set; }

    public string TargetState { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

public sealed class RevertItemEndpoint(ISender sender) : Endpoint<RevertItemRequest>
{
    public override void Configure()
    {
        Post("/portfolio/{id}/revert");
        // PMO only. Every other transition moves the portfolio forward and leaves the record intact; this one
        // rewrites where an item sits, and it needs to be one desk that answers for it.
        Policies(CracraPolicies.Pmo);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Move an item back to an earlier state."));
    }

    public override async Task HandleAsync(RevertItemRequest request, CancellationToken ct)
    {
        await sender.Send(new RevertItemCommand(request.Id, request.TargetState, request.Reason), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class AddIterationRequest
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Length { get; set; } = "twoweeks";

    public DateOnly StartsOn { get; set; }

    public DateOnly? EndsOn { get; set; }
}

public sealed record IterationCreatedResponse(Guid Id);

public sealed class AddIterationEndpoint(ISender sender) : Endpoint<AddIterationRequest, IterationCreatedResponse>
{
    public override void Configure()
    {
        Post("/portfolio/{id}/iterations");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Plan an iteration."));
    }

    public override async Task HandleAsync(AddIterationRequest request, CancellationToken ct)
    {
        var id = await sender.Send(
            new AddIterationCommand(request.Id, request.Name, request.Length, request.StartsOn, request.EndsOn),
            ct);

        await Send.ResponseAsync(new IterationCreatedResponse(id), StatusCodes.Status201Created, ct);
    }
}

public sealed class RescheduleIterationRequest
{
    public Guid Id { get; set; }

    public Guid IterationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Length { get; set; } = "custom";

    public DateOnly StartsOn { get; set; }

    public DateOnly? EndsOn { get; set; }
}

public sealed class RescheduleIterationEndpoint(ISender sender) : Endpoint<RescheduleIterationRequest>
{
    public override void Configure()
    {
        Put("/portfolio/{id}/iterations/{iterationId}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Move an iteration."));
    }

    public override async Task HandleAsync(RescheduleIterationRequest request, CancellationToken ct)
    {
        await sender.Send(
            new RescheduleIterationCommand(
                request.Id,
                request.IterationId,
                request.Name,
                request.Length,
                request.StartsOn,
                request.EndsOn),
            ct);

        await Send.NoContentAsync(ct);
    }
}

/// <summary>Closes an iteration; the next planned one takes over. Bodiless, like activation.</summary>
public sealed class CloseIterationEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/portfolio/{id}/iterations/{iterationId}/close");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio")
            .WithSummary("Close an iteration; the next planned one starts."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await sender.Send(new CloseIterationCommand(Route<Guid>("id"), Route<Guid>("iterationId")), ct);

        await Send.NoContentAsync(ct);
    }
}

/// <summary>The iteration timeline on its own, without the decision history.</summary>
public sealed class GetIterationsEndpoint(ISender sender) : EndpointWithoutRequest<IReadOnlyList<IterationSummary>>
{
    public override void Configure()
    {
        Get("/portfolio/{id}/iterations");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Portfolio").WithSummary("An item's iterations, in sequence."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetIterationsQuery(Route<Guid>("id")), ct), ct);
}

/// <summary>
/// Drops a planned iteration.
/// </summary>
/// <remarks>
/// DELETE cancels rather than erases. A plan that changed is history worth keeping — "we scheduled three sprints
/// and dropped one" is a different story from "we always planned two", and the row is the only thing that knows
/// which is true.
/// </remarks>
public sealed class CancelIterationEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/portfolio/{id}/iterations/{iterationId}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Cancel a planned iteration."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await sender.Send(new CancelIterationCommand(Route<Guid>("id"), Route<Guid>("iterationId")), ct);

        await Send.NoContentAsync(ct);
    }
}
