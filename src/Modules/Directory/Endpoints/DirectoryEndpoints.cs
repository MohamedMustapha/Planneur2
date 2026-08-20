using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Services;
using Cracra.Modules.Directory.Sync;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Directory.Endpoints;

// =================================================================================================================
// One endpoint per file is the convention; these are grouped because each is a two-line delegation to a service
// and splitting them would spread one readable surface over seven files of boilerplate.
//
// Every endpoint declares a policy, and none of them filters rows. The policy decides whether you may call; RLS
// decides what comes back (conventions.md §3).
// =================================================================================================================

/// <summary>The caller's own directory record. The client bootstraps its whole context from this.</summary>
public sealed class GetMeEndpoint(IDirectoryQueryService directory) : EndpointWithoutRequest<MeResponse>
{
    public override void Configure()
    {
        Get("/directory/me");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Directory").WithSummary("The signed-in person, their units and roles."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await directory.GetMeAsync(ct), ct);
}

public sealed class ListDepartmentsEndpoint(IDirectoryQueryService directory)
    : EndpointWithoutRequest<IReadOnlyList<DepartmentSummary>>
{
    public override void Configure()
    {
        Get("/directory/departments");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Directory").WithSummary("Departments visible to the caller."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await directory.GetDepartmentsAsync(ct), ct);
}

public sealed class ListUnitsRequest
{
    [QueryParam]
    public Guid? DepartmentId { get; set; }
}

public sealed class ListUnitsEndpoint(IDirectoryQueryService directory)
    : Endpoint<ListUnitsRequest, IReadOnlyList<UnitSummary>>
{
    public override void Configure()
    {
        Get("/directory/units");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Directory").WithSummary("Units visible to the caller."));
    }

    public override async Task HandleAsync(ListUnitsRequest request, CancellationToken ct) =>
        await Send.OkAsync(await directory.GetUnitsAsync(request.DepartmentId, ct), ct);
}

public sealed class ListPeopleRequest
{
    [QueryParam]
    public Guid? UnitId { get; set; }

    [QueryParam]
    public Guid? DepartmentId { get; set; }
}

public sealed class ListPeopleEndpoint(IDirectoryQueryService directory)
    : Endpoint<ListPeopleRequest, IReadOnlyList<PersonSummary>>
{
    public override void Configure()
    {
        Get("/directory/people");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Directory").WithSummary("People visible to the caller."));
    }

    public override async Task HandleAsync(ListPeopleRequest request, CancellationToken ct) =>
        await Send.OkAsync(await directory.GetPeopleAsync(request.UnitId, request.DepartmentId, ct), ct);
}

/// <summary>
/// The functional roles, for a picker that has to name one by id.
/// </summary>
/// <remarks>
/// Authenticated rather than head-only: a job title is not confidential, the list is the same for everybody, and
/// the screens that consume it are gated by their own policies. Making this one head-only would mean the finance
/// screen were the only thing that could ever offer a role, which is a coupling nobody asked for.
/// </remarks>
public sealed class ListFunctionalRolesEndpoint(IDirectoryQueryService directory)
    : EndpointWithoutRequest<IReadOnlyList<FunctionalRoleSummary>>
{
    public override void Configure()
    {
        Get("/directory/functional-roles");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Directory").WithSummary("Job identities the platform knows."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await directory.GetFunctionalRolesAsync(ct), ct);
}

public sealed class DepartmentConfigRequest
{
    public Guid Id { get; set; }
}

public sealed class GetDepartmentConfigEndpoint(IDepartmentConfigService configs)
    : Endpoint<DepartmentConfigRequest, DepartmentConfigSnapshot>
{
    public override void Configure()
    {
        Get("/directory/departments/{id}/config");
        // Readable by anyone in the department — the boards need the taxonomy to render at all. The RLS policy,
        // not this attribute, is what limits it to their own department.
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Directory").WithSummary("A department's configuration."));
    }

    public override async Task HandleAsync(DepartmentConfigRequest request, CancellationToken ct) =>
        await Send.OkAsync(await configs.GetAsync(request.Id, ct), ct);
}

public sealed class UpdateDepartmentConfigCommand : UpdateDepartmentConfigRequestBase
{
    public Guid Id { get; set; }
}

/// <summary>Split from the route parameter so the body binds cleanly without the id appearing twice.</summary>
public class UpdateDepartmentConfigRequestBase
{
    public string ActivityTaxonomyJson { get; set; } = "{}";

    public string RoleLabelsJson { get; set; } = "{}";

    public string KudoRulesJson { get; set; } = "{}";

    public string DefaultBoardLayout { get; set; } = "week";

    public string IterationPresetsJson { get; set; } = """["1w","2w","1m"]""";

    public decimal WeeklyTargetHours { get; set; } = 35m;

    public bool EnforceWeeklyTarget { get; set; }

    /// <summary>Shift slots offered by the S6 scheduler. An empty object means the platform defaults.</summary>
    public string ShiftTemplatesJson { get; set; } = "{}";
}

public sealed class UpdateDepartmentConfigEndpoint(IDepartmentConfigService configs)
    : Endpoint<UpdateDepartmentConfigCommand, DepartmentConfigSnapshot>
{
    public override void Configure()
    {
        Put("/directory/departments/{id}/config");
        // Gate at the door for heads and PMO; the policy cannot know *which* department, so the RLS write policy
        // is what stops a Finance head retuning IT.
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Directory").WithSummary("Update a department's configuration."));
    }

    public override async Task HandleAsync(UpdateDepartmentConfigCommand request, CancellationToken ct)
    {
        var updated = await configs.UpdateAsync(
            request.Id,
            new UpdateDepartmentConfigRequest(
                request.ActivityTaxonomyJson,
                request.RoleLabelsJson,
                request.KudoRulesJson,
                request.DefaultBoardLayout,
                request.IterationPresetsJson,
                request.WeeklyTargetHours,
                request.EnforceWeeklyTarget,
                request.ShiftTemplatesJson),
            ct);

        await Send.OkAsync(updated, ct);
    }
}

/// <summary>
/// Triggers a reconciliation. Returns 202 with the result rather than 200: the spec calls for an async pull, and
/// even though this implementation awaits it, callers should not build in an assumption that it is synchronous.
/// </summary>
public sealed class TriggerSyncEndpoint(IDirectorySynchronizer synchronizer)
    : EndpointWithoutRequest<DirectorySyncResult>
{
    public override void Configure()
    {
        Post("/directory/sync");
        Policies(CracraPolicies.Pmo);
        Description(builder => builder.WithTags("Directory").WithSummary("Reconcile the directory against Keycloak."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await synchronizer.SynchronizeAsync(ct);

        await Send.ResponseAsync(result, StatusCodes.Status202Accepted, ct);
    }
}
