using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Portfolio.Application;
using Cracra.Modules.Portfolio.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Portfolio.Api;

// =================================================================================================================
// The catalog (v2 §03.4).
//
// Reads are Authenticated, not head-gated. The catalog exists so that anybody about to ask for a new build can
// first find out whether the thing exists, and a policy that let only heads look would defeat that — what each
// caller actually sees is RLS's answer, which is their own items and their node's, widening to cross-branch for
// heads. Writes are gated at the door as well as by RLS, because a refusal is a better answer than an empty list.
// =================================================================================================================

public sealed class BrowseCatalogRequest
{
    [QueryParam]
    public string? Type { get; set; }

    [QueryParam]
    public string? Category { get; set; }

    [QueryParam]
    public string? Classification { get; set; }

    [QueryParam]
    public string? State { get; set; }

    [QueryParam]
    public Guid? Owner { get; set; }

    [QueryParam]
    public bool SharedOnly { get; set; }

    [QueryParam]
    public int? Limit { get; set; }
}

public sealed class BrowseCatalogEndpoint(ISender sender)
    : Endpoint<BrowseCatalogRequest, IReadOnlyList<CatalogCard>>
{
    public override void Configure()
    {
        Get("/portfolio/catalog");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Portfolio")
            .WithSummary("The catalog: every item the caller may see, as identity cards."));
    }

    public override async Task HandleAsync(BrowseCatalogRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await sender.Send(
                new BrowseCatalogQuery(
                    request.Type,
                    request.Category,
                    request.Classification,
                    request.State,
                    request.Owner,
                    request.SharedOnly,
                    request.Limit),
                ct),
            ct);
}

public sealed class SearchCatalogRequest
{
    [QueryParam]
    public string? Q { get; set; }

    [QueryParam]
    public int? Limit { get; set; }
}

public sealed class SearchCatalogEndpoint(ISender sender)
    : Endpoint<SearchCatalogRequest, IReadOnlyList<CatalogCard>>
{
    public override void Configure()
    {
        Get("/portfolio/catalog/search");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Portfolio")
            .WithSummary("Does something similar already exist?"));
    }

    public override async Task HandleAsync(SearchCatalogRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new SearchCatalogQuery(request.Q, request.Limit), ct), ct);
}

public sealed class GetCatalogItemRequest
{
    public Guid Id { get; set; }
}

public sealed class GetCatalogItemEndpoint(ISender sender) : Endpoint<GetCatalogItemRequest, CatalogItemDetail>
{
    public override void Configure()
    {
        Get("/portfolio/items/{id}");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Portfolio").WithSummary("The full identity card."));
    }

    public override async Task HandleAsync(GetCatalogItemRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetCatalogItemQuery(request.Id), ct), ct);
}

public sealed class CreateItemRequest
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? Category { get; set; }

    public string? Classification { get; set; }

    public Guid? OwnerNodeId { get; set; }

    public Guid? LeadPersonId { get; set; }

    public Guid? PoPersonId { get; set; }

    public string? Summary { get; set; }

    public decimal? EstimateAmount { get; set; }

    public string? Currency { get; set; }

    public int Priority { get; set; } = 100;

    public Guid? OriginProblemId { get; set; }
}

/// <summary>
/// The create wizard's one call (v2 §03.3).
/// </summary>
/// <remarks>
/// DeliveryLead rather than AnyHead: a project lead or PO proposing an item is the ordinary case, and requiring a
/// head for it is what made v1's "can't create a project" complaint true. RLS still decides which node the item
/// may be owned by.
/// </remarks>
public sealed class CreateItemEndpoint(ISender sender) : Endpoint<CreateItemRequest, CreatedItemResponse>
{
    public override void Configure()
    {
        Post("/portfolio/items");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Create a portfolio item."));
    }

    public override async Task HandleAsync(CreateItemRequest request, CancellationToken ct)
    {
        var id = await sender.Send(
            new CreateItemCommand(
                request.Name,
                request.Type,
                request.Code,
                request.Category,
                request.Classification,
                request.OwnerNodeId,
                request.LeadPersonId,
                request.PoPersonId,
                request.Summary,
                request.EstimateAmount,
                request.Currency,
                request.Priority,
                request.OriginProblemId),
            ct);

        await Send.CreatedAtAsync<GetCatalogItemEndpoint>(new { id }, new CreatedItemResponse(id), cancellation: ct);
    }
}

public sealed record CreatedItemResponse(Guid Id);

public sealed class UpdateItemRequest
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    public string? Category { get; set; }

    public string? Classification { get; set; }

    public Guid? LeadPersonId { get; set; }

    public Guid? PoPersonId { get; set; }

    public string? Summary { get; set; }

    public decimal? EstimateAmount { get; set; }

    public bool? Confidential { get; set; }
}

public sealed class UpdateItemEndpoint(ISender sender) : Endpoint<UpdateItemRequest>
{
    public override void Configure()
    {
        Patch("/portfolio/items/{id}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Edit the identity card."));
    }

    public override async Task HandleAsync(UpdateItemRequest request, CancellationToken ct)
    {
        await sender.Send(
            new UpdateItemCommand(
                request.Id,
                request.Name,
                request.Category,
                request.Classification,
                request.LeadPersonId,
                request.PoPersonId,
                request.Summary,
                request.EstimateAmount,
                request.Confidential),
            ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class AwaitNextVersionRequest
{
    public Guid Id { get; set; }

    public string Version { get; set; } = string.Empty;
}

public sealed class AwaitNextVersionEndpoint(ISender sender) : Endpoint<AwaitNextVersionRequest>
{
    public override void Configure()
    {
        Post("/portfolio/{id}/await-next");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio")
            .WithSummary("Declare a next version queued behind what is live."));
    }

    public override async Task HandleAsync(AwaitNextVersionRequest request, CancellationToken ct)
    {
        await sender.Send(new AwaitNextVersionCommand(request.Id, request.Version), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class AddEpicRequest
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Status { get; set; }

    public string? TargetVersion { get; set; }
}

public sealed class AddEpicEndpoint(ISender sender) : Endpoint<AddEpicRequest, CreatedItemResponse>
{
    public override void Configure()
    {
        Post("/portfolio/{id}/epics");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Add a feature to an item."));
    }

    public override async Task HandleAsync(AddEpicRequest request, CancellationToken ct)
    {
        var id = await sender.Send(
            new AddEpicCommand(request.Id, request.Name, request.Description, request.Status, request.TargetVersion),
            ct);

        await Send.OkAsync(new CreatedItemResponse(id), ct);
    }
}

public sealed class UpdateEpicRequest
{
    public Guid Id { get; set; }

    public Guid EpicId { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? Status { get; set; }

    public string? TargetVersion { get; set; }

    public Guid? IterationId { get; set; }
}

public sealed class UpdateEpicEndpoint(ISender sender) : Endpoint<UpdateEpicRequest>
{
    public override void Configure()
    {
        Patch("/portfolio/{id}/epics/{epicId}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Edit a feature."));
    }

    public override async Task HandleAsync(UpdateEpicRequest request, CancellationToken ct)
    {
        await sender.Send(
            new UpdateEpicCommand(
                request.Id,
                request.EpicId,
                request.Name,
                request.Description,
                request.Status,
                request.TargetVersion,
                request.IterationId),
            ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class AddDependencyRequest
{
    public Guid Id { get; set; }

    public Guid DependsOnItemId { get; set; }

    public string? Kind { get; set; }

    public string? Note { get; set; }
}

public sealed class AddDependencyEndpoint(ISender sender) : Endpoint<AddDependencyRequest, CreatedItemResponse>
{
    public override void Configure()
    {
        Post("/portfolio/items/{id}/dependencies");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Record that this item consumes another."));
    }

    public override async Task HandleAsync(AddDependencyRequest request, CancellationToken ct)
    {
        var id = await sender.Send(
            new AddDependencyCommand(request.Id, request.DependsOnItemId, request.Kind, request.Note),
            ct);

        await Send.OkAsync(new CreatedItemResponse(id), ct);
    }
}

public sealed class RemoveDependencyRequest
{
    public Guid Id { get; set; }

    public Guid DependencyId { get; set; }
}

public sealed class RemoveDependencyEndpoint(ISender sender) : Endpoint<RemoveDependencyRequest>
{
    public override void Configure()
    {
        Delete("/portfolio/items/{id}/dependencies/{dependencyId}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Drop a dependency."));
    }

    public override async Task HandleAsync(RemoveDependencyRequest request, CancellationToken ct)
    {
        await sender.Send(new RemoveDependencyCommand(request.Id, request.DependencyId), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class AddItemMemberRequest
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public Guid? NodeId { get; set; }

    public Guid? FunctionalRoleId { get; set; }

    public int? AllocationPercent { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }
}

public sealed class AddItemMemberEndpoint(ISender sender) : Endpoint<AddItemMemberRequest, CreatedItemResponse>
{
    public override void Configure()
    {
        Post("/portfolio/items/{id}/members");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Put somebody on an item."));
    }

    public override async Task HandleAsync(AddItemMemberRequest request, CancellationToken ct)
    {
        var id = await sender.Send(
            new AddItemMemberCommand(
                request.Id,
                request.PersonId,
                request.NodeId,
                request.FunctionalRoleId,
                request.AllocationPercent,
                request.From,
                request.To),
            ct);

        await Send.OkAsync(new CreatedItemResponse(id), ct);
    }
}

public sealed class RemoveItemMemberRequest
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    [QueryParam]
    public DateOnly? On { get; set; }
}

public sealed class RemoveItemMemberEndpoint(ISender sender) : Endpoint<RemoveItemMemberRequest>
{
    public override void Configure()
    {
        Delete("/portfolio/items/{id}/members/{personId}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Portfolio").WithSummary("Take somebody off an item."));
    }

    public override async Task HandleAsync(RemoveItemMemberRequest request, CancellationToken ct)
    {
        await sender.Send(new RemoveItemMemberCommand(request.Id, request.PersonId, request.On), ct);

        await Send.NoContentAsync(ct);
    }
}
