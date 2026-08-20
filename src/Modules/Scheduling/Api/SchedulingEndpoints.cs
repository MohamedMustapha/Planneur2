using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Scheduling.Application;
using Cracra.Modules.Scheduling.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Scheduling.Api;

// =================================================================================================================
// Reads are Authenticated: asking for a board you have no rows in is not an error, it is an empty board, and RLS
// is what decides which. Writes are gated to people who run delivery, and then re-checked by RLS on the way down.
// =================================================================================================================

public sealed class GetBoardRequest
{
    /// <summary>my | team | unit | project | department.</summary>
    [QueryParam]
    public string? Type { get; set; }

    /// <summary>The unit, project or department the board is about. Defaults to the caller's own.</summary>
    [QueryParam]
    public Guid? ScopeId { get; set; }

    [QueryParam]
    public DateOnly? From { get; set; }

    [QueryParam]
    public DateOnly? To { get; set; }
}

public sealed class GetBoardEndpoint(ISender sender) : Endpoint<GetBoardRequest, BoardPayload>
{
    public override void Configure()
    {
        Get("/scheduling/board");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Scheduling")
            .WithSummary("The composed timeline: rows, events and overlays, shaped for the client."));
    }

    public override async Task HandleAsync(GetBoardRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await sender.Send(new GetBoardQuery(request.Type, request.ScopeId, request.From, request.To), ct),
            ct);
}

public sealed class GetPoolRequest
{
    [QueryParam]
    public Guid? UnitId { get; set; }

    [QueryParam]
    public string? Source { get; set; }
}

/// <summary>6a's fixed top row: what nobody has picked up yet.</summary>
public sealed class GetPoolEndpoint(ISender sender) : Endpoint<GetPoolRequest, IReadOnlyList<WorkOrderView>>
{
    public override void Configure()
    {
        Get("/scheduling/work-orders/pool");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Scheduling").WithSummary("Unassigned work orders for a unit."));
    }

    public override async Task HandleAsync(GetPoolRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetPoolQuery(request.UnitId, request.Source), ct), ct);
}

public sealed class RefreshPoolRequest
{
    public Guid UnitId { get; set; }

    public string? Source { get; set; }
}

public sealed record RefreshPoolResponse(int Created);

/// <summary>
/// Pulls whatever the external system has waiting into the pool.
/// </summary>
/// <remarks>
/// A read from their side, a write on ours: the pull creates local shadows so a lead can drag them. Nothing is
/// ever sent back the other way, which is the one rule this integration has.
/// </remarks>
public sealed class RefreshPoolEndpoint(ISender sender) : Endpoint<RefreshPoolRequest, RefreshPoolResponse>
{
    public override void Configure()
    {
        Post("/scheduling/work-orders/pool/refresh");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Scheduling")
            .WithSummary("Pull unassigned items from the configured source into the pool."));
    }

    public override async Task HandleAsync(RefreshPoolRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            new RefreshPoolResponse(await sender.Send(new RefreshPoolCommand(request.UnitId, request.Source), ct)),
            ct);
}

public sealed class CreateWorkOrderRequest
{
    public string Reference { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid UnitId { get; set; }

    public Guid? ProjectId { get; set; }

    /// <summary>What an assignment books against. Defaults to project-run, which needs a project.</summary>
    public string? ActivityTypeCode { get; set; }

    public decimal EstimatedHours { get; set; } = 1m;
}

public sealed record WorkOrderCreatedResponse(Guid Id);

public sealed class CreateWorkOrderEndpoint(ISender sender)
    : Endpoint<CreateWorkOrderRequest, WorkOrderCreatedResponse>
{
    public override void Configure()
    {
        Post("/scheduling/work-orders");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Scheduling").WithSummary("Put a work order in a unit's pool."));
    }

    public override async Task HandleAsync(CreateWorkOrderRequest request, CancellationToken ct)
    {
        var id = await sender.Send(
            new CreateWorkOrderCommand(
                request.Reference,
                request.Title,
                request.Description,
                request.UnitId,
                request.ProjectId,
                request.ActivityTypeCode,
                request.EstimatedHours),
            ct);

        await Send.ResponseAsync(new WorkOrderCreatedResponse(id), StatusCodes.Status201Created, ct);
    }
}

public sealed class AssignWorkOrderRequest
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public DateTimeOffset Start { get; set; }

    /// <summary>Omitted lets the estimate decide, which is what a card dropped on a row supplies.</summary>
    public DateTimeOffset? End { get; set; }
}

public sealed class AssignWorkOrderEndpoint(ISender sender) : Endpoint<AssignWorkOrderRequest, AssignmentResult>
{
    public override void Configure()
    {
        Post("/scheduling/work-orders/{id}/assign");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Scheduling")
            .WithSummary("Assign a work order, creating the planned activity for it."));
    }

    public override async Task HandleAsync(AssignWorkOrderRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await sender.Send(
                new AssignWorkOrderCommand(request.Id, request.PersonId, request.Start, request.End),
                ct),
            ct);
}

public sealed class UnassignWorkOrderEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/scheduling/work-orders/{id}/unassign");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Scheduling")
            .WithSummary("Return a work order to the pool and remove its planned activity."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await sender.Send(new UnassignWorkOrderCommand(Route<Guid>("id")), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class ShiftTemplatesRequest
{
    [QueryParam]
    public Guid? DepartmentId { get; set; }
}

public sealed class GetShiftTemplatesEndpoint(ISender sender)
    : Endpoint<ShiftTemplatesRequest, IReadOnlyList<ShiftTemplate>>
{
    public override void Configure()
    {
        Get("/scheduling/shifts/templates");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Scheduling")
            .WithSummary("Shift slots a department offers, and their minimum staffing."));
    }

    public override async Task HandleAsync(ShiftTemplatesRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetShiftTemplatesQuery(request.DepartmentId), ct), ct);
}

public sealed class PlanShiftRequest
{
    public Guid PersonId { get; set; }

    public string TemplateCode { get; set; } = string.Empty;

    public DateOnly Day { get; set; }
}

public sealed record ShiftCreatedResponse(Guid Id);

public sealed class PlanShiftEndpoint(ISender sender) : Endpoint<PlanShiftRequest, ShiftCreatedResponse>
{
    public override void Configure()
    {
        Post("/scheduling/shifts");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Scheduling").WithSummary("Roster someone onto a shift."));
    }

    public override async Task HandleAsync(PlanShiftRequest request, CancellationToken ct)
    {
        var id = await sender.Send(
            new PlanShiftCommand(request.PersonId, request.TemplateCode, request.Day),
            ct);

        await Send.ResponseAsync(new ShiftCreatedResponse(id), StatusCodes.Status201Created, ct);
    }
}

public sealed class MoveShiftRequest
{
    public Guid Id { get; set; }

    public string TemplateCode { get; set; } = string.Empty;

    public DateOnly Day { get; set; }
}

public sealed class MoveShiftEndpoint(ISender sender) : Endpoint<MoveShiftRequest>
{
    public override void Configure()
    {
        Put("/scheduling/shifts/{id}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Scheduling").WithSummary("Move a shift to another slot or day."));
    }

    public override async Task HandleAsync(MoveShiftRequest request, CancellationToken ct)
    {
        await sender.Send(new MoveShiftCommand(request.Id, request.TemplateCode, request.Day), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class DeleteShiftEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/scheduling/shifts/{id}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Scheduling").WithSummary("Remove a shift."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await sender.Send(new DeleteShiftCommand(Route<Guid>("id")), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class RescheduleTaskRequest
{
    public Guid Id { get; set; }

    public DateTimeOffset Start { get; set; }

    public DateTimeOffset End { get; set; }
}

/// <summary>6c: a planned block dragged to a new time.</summary>
public sealed class RescheduleTaskEndpoint(ISender sender) : Endpoint<RescheduleTaskRequest>
{
    public override void Configure()
    {
        Put("/scheduling/tasks/{id}/schedule");
        // Not DeliveryLead: moving your own planned slot is something anyone does on their own board, and RLS
        // already refuses the ones that are not theirs to move.
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Scheduling").WithSummary("Move a planned slot."));
    }

    public override async Task HandleAsync(RescheduleTaskRequest request, CancellationToken ct)
    {
        await sender.Send(new RescheduleTaskCommand(request.Id, request.Start, request.End), ct);

        await Send.NoContentAsync(ct);
    }
}
