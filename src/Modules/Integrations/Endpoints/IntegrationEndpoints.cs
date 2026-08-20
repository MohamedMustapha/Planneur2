using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Integrations.Services;
using Cracra.Modules.Integrations.Sync;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Integrations.Endpoints;

// =================================================================================================================
// One endpoint per file is the convention; these are grouped because each is a two-line delegation to a service
// and splitting them would spread one readable surface over a dozen files of boilerplate.
//
// Two policies and no third. Configuration is AnyHead — the door a department head or the PMO comes through, with
// access.can_write_connection deciding which department's rows they actually reach. The mirror is Authenticated,
// because the question "what is assigned to me" is everybody's, and the answer is entirely RLS's.
//
// Note what is absent: there is no endpoint that writes a work item, and none that pushes anything outward. The
// only write in this file is a configuration change; the only pull is a 202.
// =================================================================================================================

// --- Connections -------------------------------------------------------------------------------------------------

public sealed class ListConnectionsRequest
{
    /// <summary>Narrow to one department. Omitted returns every connection the caller may see.</summary>
    [QueryParam]
    public Guid? DepartmentId { get; set; }
}

public sealed class ListConnectionsEndpoint(IConnectionService connections)
    : Endpoint<ListConnectionsRequest, IReadOnlyList<ConnectionView>>
{
    public override void Configure()
    {
        Get("/integrations/connections");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Integrations")
            .WithSummary("Configured links to Azure DevOps and ServiceNow."));
    }

    public override async Task HandleAsync(ListConnectionsRequest request, CancellationToken ct) =>
        await Send.OkAsync(await connections.ListAsync(request.DepartmentId, ct), ct);
}

public sealed class ConnectionByIdRequest
{
    public Guid Id { get; set; }
}

public sealed class GetConnectionEndpoint(IConnectionService connections)
    : Endpoint<ConnectionByIdRequest, ConnectionView>
{
    public override void Configure()
    {
        Get("/integrations/connections/{id}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Integrations").WithSummary("One connection and its mappings."));
    }

    public override async Task HandleAsync(ConnectionByIdRequest request, CancellationToken ct) =>
        await Send.OkAsync(await connections.GetAsync(request.Id, ct), ct);
}

/// <summary>The body of a create or an update. Identical both ways — a connection has nothing worth patching.</summary>
public class ConnectionBody
{
    public Guid DepartmentId { get; set; }

    public string Provider { get; set; } = ExternalProviders.AzureDevOps;

    public string Name { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The name of the secret this connection authenticates with. Never the secret.</summary>
    public string AuthRef { get; set; } = string.Empty;

    /// <summary>DevOps team project, or ServiceNow assignment group.</summary>
    public string ProjectOrQueue { get; set; } = string.Empty;

    public string? CurrentSprint { get; set; }

    /// <summary>Zero for on-demand only; otherwise between a minute and a day.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(15);

    public bool Active { get; set; } = true;

    internal ConnectionRequest ToRequest() => new(
        DepartmentId,
        Provider,
        Name,
        BaseUrl,
        AuthRef,
        ProjectOrQueue,
        CurrentSprint,
        PollInterval,
        Active);
}

public sealed class CreateConnectionCommand : ConnectionBody;

public sealed class CreateConnectionEndpoint(IConnectionService connections)
    : Endpoint<CreateConnectionCommand, ConnectionView>
{
    public override void Configure()
    {
        Post("/integrations/connections");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Integrations").WithSummary("Configure a connection."));
    }

    public override async Task HandleAsync(CreateConnectionCommand request, CancellationToken ct)
    {
        var created = await connections.CreateAsync(request.ToRequest(), ct);

        await Send.ResponseAsync(created, StatusCodes.Status201Created, ct);
    }
}

/// <summary>Split from the route parameter so the body binds cleanly without the id appearing twice.</summary>
public sealed class UpdateConnectionCommand : ConnectionBody
{
    public Guid Id { get; set; }
}

public sealed class UpdateConnectionEndpoint(IConnectionService connections)
    : Endpoint<UpdateConnectionCommand, ConnectionView>
{
    public override void Configure()
    {
        Put("/integrations/connections/{id}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Integrations").WithSummary("Update a connection."));
    }

    public override async Task HandleAsync(UpdateConnectionCommand request, CancellationToken ct) =>
        await Send.OkAsync(await connections.UpdateAsync(request.Id, request.ToRequest(), ct), ct);
}

public sealed class DeleteConnectionEndpoint(IConnectionService connections) : Endpoint<ConnectionByIdRequest>
{
    public override void Configure()
    {
        Delete("/integrations/connections/{id}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Integrations")
            .WithSummary("Remove a connection and everything it mirrors."));
    }

    public override async Task HandleAsync(ConnectionByIdRequest request, CancellationToken ct)
    {
        await connections.DeleteAsync(request.Id, ct);

        await Send.NoContentAsync(ct);
    }
}

// --- Mappings ----------------------------------------------------------------------------------------------------

public sealed class AddMappingCommand
{
    public Guid Id { get; set; }

    /// <summary>area-path | iteration | assignment-group.</summary>
    public string Kind { get; set; } = string.Empty;

    public string ExternalValue { get; set; } = string.Empty;

    public Guid? ProjectId { get; set; }

    public Guid? UnitId { get; set; }
}

public sealed class AddMappingEndpoint(IConnectionService connections) : Endpoint<AddMappingCommand, MappingView>
{
    public override void Configure()
    {
        Post("/integrations/connections/{id}/mappings");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Integrations")
            .WithSummary("Map an area path, iteration or assignment group to a local project or unit."));
    }

    public override async Task HandleAsync(AddMappingCommand request, CancellationToken ct)
    {
        var created = await connections.AddMappingAsync(
            request.Id,
            new MappingRequest(request.Kind, request.ExternalValue, request.ProjectId, request.UnitId),
            ct);

        await Send.ResponseAsync(created, StatusCodes.Status201Created, ct);
    }
}

public sealed class RemoveMappingRequest
{
    public Guid Id { get; set; }

    public Guid MappingId { get; set; }
}

public sealed class RemoveMappingEndpoint(IConnectionService connections) : Endpoint<RemoveMappingRequest>
{
    public override void Configure()
    {
        Delete("/integrations/connections/{id}/mappings/{mappingId}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Integrations").WithSummary("Remove a mapping."));
    }

    public override async Task HandleAsync(RemoveMappingRequest request, CancellationToken ct)
    {
        await connections.RemoveMappingAsync(request.Id, request.MappingId, ct);

        await Send.NoContentAsync(ct);
    }
}

// --- Sync --------------------------------------------------------------------------------------------------------

public sealed record SyncAcceptedResponse(Guid ConnectionId, string Status)
{
    /// <summary>Where to look for the outcome: the connection carries its own last-sync state.</summary>
    public string StatusUrl { get; } = $"/api/integrations/connections/{ConnectionId}";
}

/// <summary>
/// Pulls one connection now.
/// </summary>
/// <remarks>
/// <para>
/// 202 and a status resource, per conventions.md §3: a pull is a round trip to somebody else's server and may
/// take a while, and holding an HTTP request open for it makes the platform's responsiveness a function of
/// theirs.
/// </para>
/// <para>
/// The work is started on a background scope rather than awaited, which is deliberate and has one consequence
/// worth stating: the request's cancellation token is not the pull's. A user navigating away must not abort a
/// reconciliation halfway through, leaving the mirror half-restamped and the connection claiming neither success
/// nor failure.
/// </para>
/// <para>
/// Existence and authorization are checked <em>before</em> accepting. Otherwise a Finance head could 202 a pull
/// of the IS department's DevOps project and learn nothing from the response — but the pull would run.
/// </para>
/// </remarks>
public sealed class SyncConnectionEndpoint(
    IConnectionService connections,
    IIntegrationSyncDispatcher dispatcher) : Endpoint<ConnectionByIdRequest, SyncAcceptedResponse>
{
    public override void Configure()
    {
        Post("/integrations/{id}/sync");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Integrations")
            .WithSummary("Pull this connection now. Returns 202; the connection reports the outcome."));
    }

    public override async Task HandleAsync(ConnectionByIdRequest request, CancellationToken ct)
    {
        // Reads through the caller's own RLS session, so a connection they may not administer 404s here rather
        // than being pulled by a job that runs as the system.
        var connection = await connections.GetAsync(request.Id, ct);

        dispatcher.Enqueue(connection.Id);

        await Send.ResponseAsync(
            new SyncAcceptedResponse(connection.Id, "accepted"),
            StatusCodes.Status202Accepted,
            ct);
    }
}

// --- The mirror --------------------------------------------------------------------------------------------------

public sealed class WorkItemsRequest
{
    [QueryParam]
    public string? Provider { get; set; }

    /// <summary>One connection's own items — what an administrator looking at a connection is asking.</summary>
    [QueryParam]
    public Guid? ConnectionId { get; set; }

    [QueryParam]
    public bool AssignedToMe { get; set; }

    [QueryParam]
    public bool Unassigned { get; set; }

    [QueryParam]
    public bool SprintCurrent { get; set; }

    [QueryParam]
    public Guid? UnitId { get; set; }

    [QueryParam]
    public Guid? ProjectId { get; set; }

    [QueryParam]
    public bool IncludeClosed { get; set; }

    [QueryParam]
    public int? Limit { get; set; }
}

/// <summary>
/// The mirror as a feed.
/// </summary>
/// <remarks>
/// The same rows S5's dropdown and S6a's pool read through the contract, exposed directly for anything that wants
/// them without going through those modules — a support engineer asking what the platform believes, most often.
/// RLS-filtered like every other read here.
/// </remarks>
public sealed class GetWorkItemsEndpoint(IExternalWorkItemReader mirror)
    : Endpoint<WorkItemsRequest, IReadOnlyList<ExternalWorkItemView>>
{
    public override void Configure()
    {
        Get("/integrations/work-items");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Integrations")
            .WithSummary("Mirrored external work items visible to the caller."));
    }

    public override async Task HandleAsync(WorkItemsRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await mirror.QueryAsync(
                new ExternalWorkItemQuery
                {
                    Provider = request.Provider,
                    ConnectionId = request.ConnectionId,
                    AssignedToMe = request.AssignedToMe,
                    Unassigned = request.Unassigned,
                    CurrentSprint = request.SprintCurrent,
                    UnitId = request.UnitId,
                    ProjectId = request.ProjectId,
                    IncludeClosed = request.IncludeClosed,
                    Limit = request.Limit ?? 200,
                },
                ct),
            ct);
}
