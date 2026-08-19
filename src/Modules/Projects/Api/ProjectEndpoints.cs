using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Projects.Application;
using Cracra.Modules.Projects.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Projects.Api;

// =================================================================================================================
// Thin by design (conventions.md §2): map the request to a command or query, send it, map the result. No business
// rules, no row filtering — the aggregate owns the first and RLS owns the second.
// =================================================================================================================

public sealed class ListProjectsRequest
{
    [QueryParam]
    public Guid? DepartmentId { get; set; }

    [QueryParam]
    public string? Classification { get; set; }
}

public sealed class ListProjectsEndpoint(ISender sender)
    : Endpoint<ListProjectsRequest, IReadOnlyList<ProjectSummary>>
{
    public override void Configure()
    {
        Get("/projects");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Projects").WithSummary("Projects visible to the caller."));
    }

    public override async Task HandleAsync(ListProjectsRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await sender.Send(new ListProjectsQuery(request.DepartmentId, request.Classification), ct),
            ct);
}

public sealed class ProjectByIdRequest
{
    public Guid Id { get; set; }
}

public sealed class GetProjectEndpoint(ISender sender) : Endpoint<ProjectByIdRequest, ProjectDetail>
{
    public override void Configure()
    {
        Get("/projects/{id}");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Projects").WithSummary("One project."));
    }

    public override async Task HandleAsync(ProjectByIdRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetProjectQuery(request.Id), ct), ct);
}

/// <summary>The team grouped by department, then function — the shape the project view renders.</summary>
public sealed class GetProjectTeamEndpoint(ISender sender) : Endpoint<ProjectByIdRequest, ProjectTeam>
{
    public override void Configure()
    {
        Get("/projects/{id}/team");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Projects").WithSummary("The project team, grouped by department."));
    }

    public override async Task HandleAsync(ProjectByIdRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetProjectTeamQuery(request.Id), ct), ct);
}

public class ProjectWriteRequest
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Classification { get; set; } = "build";

    public decimal CostAmount { get; set; }

    public string CostCurrency { get; set; } = "EUR";

    public string? CostNotes { get; set; }

    public Guid LeadDepartmentId { get; set; }

    public IReadOnlyList<Guid> ContributingDepartmentIds { get; set; } = [];
}

public sealed class CreateProjectEndpoint(ISender sender) : Endpoint<CreateProjectRequest, ProjectCreatedResponse>
{
    public override void Configure()
    {
        Post("/projects");
        // Anyone who can lead delivery. Which department they may create it under is not something the policy can
        // see, so the handler validates the department exists and RLS decides who can read the result.
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Projects").WithSummary("Create a project."));
    }

    public override async Task HandleAsync(CreateProjectRequest request, CancellationToken ct)
    {
        var id = await sender.Send(
            new CreateProjectCommand(
                request.Code,
                request.Name,
                request.Description,
                request.Classification,
                request.CostAmount,
                request.CostCurrency,
                request.CostNotes,
                request.LeadDepartmentId,
                request.ContributingDepartmentIds),
            ct);

        await Send.ResponseAsync(new ProjectCreatedResponse(id), StatusCodes.Status201Created, ct);
    }
}

public sealed record ProjectCreatedResponse(Guid Id);

public sealed class CreateProjectRequest : ProjectWriteRequest;

public sealed class UpdateProjectRequest : ProjectWriteRequest
{
    public Guid Id { get; set; }
}

public sealed class UpdateProjectEndpoint(ISender sender) : Endpoint<UpdateProjectRequest>
{
    public override void Configure()
    {
        Put("/projects/{id}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Projects").WithSummary("Update a project's details and cost."));
    }

    public override async Task HandleAsync(UpdateProjectRequest request, CancellationToken ct)
    {
        await sender.Send(
            new UpdateProjectCommand(
                request.Id,
                request.Name,
                request.Description,
                request.Classification,
                request.CostAmount,
                request.CostCurrency,
                request.CostNotes),
            ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class AddMemberRequest
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public Guid DepartmentId { get; set; }

    public Guid FunctionalRoleId { get; set; }

    public int? AllocationPercent { get; set; }

    public DateOnly? From { get; set; }
}

public sealed class AddProjectMemberEndpoint(ISender sender) : Endpoint<AddMemberRequest>
{
    public override void Configure()
    {
        Post("/projects/{id}/members");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Projects").WithSummary("Add someone to the project team."));
    }

    public override async Task HandleAsync(AddMemberRequest request, CancellationToken ct)
    {
        await sender.Send(
            new AddProjectMemberCommand(
                request.Id,
                request.PersonId,
                request.DepartmentId,
                request.FunctionalRoleId,
                request.AllocationPercent,
                request.From),
            ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class RemoveMemberRequest
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    [QueryParam]
    public DateOnly? To { get; set; }
}

public sealed class RemoveProjectMemberEndpoint(ISender sender) : Endpoint<RemoveMemberRequest>
{
    public override void Configure()
    {
        Delete("/projects/{id}/members/{personId}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Projects").WithSummary("Take someone off the project team."));
    }

    public override async Task HandleAsync(RemoveMemberRequest request, CancellationToken ct)
    {
        await sender.Send(new RemoveProjectMemberCommand(request.Id, request.PersonId, request.To), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class ProjectDepartmentRequest
{
    public Guid Id { get; set; }

    public Guid DepartmentId { get; set; }
}

public sealed class AddProjectDepartmentEndpoint(ISender sender) : Endpoint<ProjectDepartmentRequest>
{
    public override void Configure()
    {
        Post("/projects/{id}/departments");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Projects").WithSummary("Add a contributing department."));
    }

    public override async Task HandleAsync(ProjectDepartmentRequest request, CancellationToken ct)
    {
        await sender.Send(new AddProjectDepartmentCommand(request.Id, request.DepartmentId), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class RemoveProjectDepartmentEndpoint(ISender sender) : Endpoint<ProjectDepartmentRequest>
{
    public override void Configure()
    {
        Delete("/projects/{id}/departments/{departmentId}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Projects").WithSummary("Remove a contributing department."));
    }

    public override async Task HandleAsync(ProjectDepartmentRequest request, CancellationToken ct)
    {
        await sender.Send(new RemoveProjectDepartmentCommand(request.Id, request.DepartmentId), ct);

        await Send.NoContentAsync(ct);
    }
}

/// <summary>Who can still be added to this project's team.</summary>
public sealed class GetTeamCandidatesEndpoint(ISender sender)
    : Endpoint<ProjectByIdRequest, IReadOnlyList<TeamCandidate>>
{
    public override void Configure()
    {
        Get("/projects/{id}/team/candidates");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder
            .WithTags("Projects")
            .WithSummary("People in the project's contributing departments who are not yet on the team."));
    }

    public override async Task HandleAsync(ProjectByIdRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetTeamCandidatesQuery(request.Id), ct), ct);
}
