using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Access.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Access.Endpoints;

/// <summary>
/// The caller's own effective roles and scopes.
/// </summary>
/// <remarks>
/// Deliberately available to everyone authenticated: it only ever describes the caller to themselves, and the
/// client needs it to decide which nav entries to draw. Drawing is all it decides — every data call is still
/// filtered by RLS, so this response cannot widen anything.
/// </remarks>
public sealed class WhoAmIEndpoint(IUserContext user, IEffectiveRoleResolver resolver)
    : EndpointWithoutRequest<WhoAmIResponse>
{
    public override void Configure()
    {
        Get("/access/whoami");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Access").WithSummary("The caller's effective roles and scopes."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var effective = await resolver.ResolveAsync(user.UserId, ct);

        await Send.OkAsync(
            new WhoAmIResponse(
                user.UserId,
                user.UserName,
                user.UnitId,
                user.DepartmentIds,
                user.Roles,
                [.. effective.Roles.Select(role => new EffectiveRoleDto(
                    role.Role,
                    role.ScopeType.ToString(),
                    role.ScopeId,
                    role.Source.ToString()))],
                user.Language),
            ct);
    }
}

public sealed class PersonRolesRequest
{
    public Guid PersonId { get; set; }
}

/// <summary>Someone else's effective roles. Heads and PMO only — this is the RBAC admin's read side.</summary>
public sealed class GetPersonRolesEndpoint(IEffectiveRoleResolver resolver)
    : Endpoint<PersonRolesRequest, EffectiveRolesResponse>
{
    public override void Configure()
    {
        Get("/access/roles/{personId}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Access").WithSummary("A person's effective roles."));
    }

    public override async Task HandleAsync(PersonRolesRequest request, CancellationToken ct)
    {
        var effective = await resolver.ResolveAsync(request.PersonId, ct);

        await Send.OkAsync(
            new EffectiveRolesResponse(
                effective.PersonId,
                [.. effective.Roles.Select(role => new EffectiveRoleDto(
                    role.Role,
                    role.ScopeType.ToString(),
                    role.ScopeId,
                    role.Source.ToString()))],
                effective.RoleNames),
            ct);
    }
}

public sealed class ListOverridesRequest
{
    [QueryParam]
    public Guid? PersonId { get; set; }
}

public sealed class ListOverridesEndpoint(IRbacOverrideService overrides)
    : Endpoint<ListOverridesRequest, IReadOnlyList<RbacOverrideDto>>
{
    public override void Configure()
    {
        Get("/access/overrides");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Access").WithSummary("RBAC overrides visible to the caller."));
    }

    public override async Task HandleAsync(ListOverridesRequest request, CancellationToken ct) =>
        await Send.OkAsync(await overrides.ListAsync(request.PersonId, ct), ct);
}

public sealed class CreateOverrideCommand
{
    public Guid PersonId { get; set; }

    public string Role { get; set; } = string.Empty;

    public string ScopeType { get; set; } = "Global";

    public Guid? ScopeId { get; set; }

    /// <summary>False denies. A deny always beats a grant of the same role and scope.</summary>
    public bool IsGrant { get; set; } = true;

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class CreateOverrideEndpoint(IRbacOverrideService overrides)
    : Endpoint<CreateOverrideCommand, RbacOverrideDto>
{
    public override void Configure()
    {
        Post("/access/overrides");
        // Heads and PMO at the door. Which people a head may actually write an override for is decided by the RLS
        // write policy, because the endpoint cannot know the target's department from the route alone.
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Access").WithSummary("Grant or deny a contextual role by hand."));
    }

    public override async Task HandleAsync(CreateOverrideCommand request, CancellationToken ct)
    {
        var created = await overrides.CreateAsync(
            new CreateOverrideRequest(
                request.PersonId,
                request.Role,
                request.ScopeType,
                request.ScopeId,
                request.IsGrant,
                request.Reason,
                request.ExpiresAt),
            ct);

        await Send.ResponseAsync(created, StatusCodes.Status201Created, ct);
    }
}

public sealed class RevokeOverrideRequest
{
    public Guid Id { get; set; }
}

public sealed class RevokeOverrideEndpoint(IRbacOverrideService overrides) : Endpoint<RevokeOverrideRequest>
{
    public override void Configure()
    {
        Delete("/access/overrides/{id}");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Access").WithSummary("Revoke an RBAC override."));
    }

    public override async Task HandleAsync(RevokeOverrideRequest request, CancellationToken ct)
    {
        // Revoked, not deleted: the row and its audit trail are how "who granted this, and who took it away"
        // stays answerable.
        await overrides.RevokeAsync(request.Id, ct);

        await Send.NoContentAsync(ct);
    }
}
