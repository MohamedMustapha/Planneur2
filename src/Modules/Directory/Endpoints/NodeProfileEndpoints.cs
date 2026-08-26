using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Domain;
using Cracra.Modules.Directory.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Directory.Endpoints;

// =================================================================================================================
// v2 §10's authoring surface, grouped for the same reason the rest of Directory's endpoints are: each is a
// delegation to the service, and one file reads better than eight.
//
// Read is Authenticated and write is PMO, matching access.can_author_node_profile(). The policy decides whether
// you may call; the RLS policy on directory.node_profile decides what comes back and what lands. Both exist on
// purpose — the endpoint policy gives an honest 403 instead of a silently empty write.
// =================================================================================================================

/// <summary>The profiles an administrator can attach or clone. Vocabulary, so everyone scoped may read it.</summary>
public sealed class ListNodeProfilesEndpoint(INodeProfileService profiles)
    : EndpointWithoutRequest<IReadOnlyList<NodeProfileDetail>>
{
    public override void Configure()
    {
        Get("/directory/profiles");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Directory").WithSummary("Node profiles available to attach."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await profiles.ListAsync(ct), ct);
}

/// <summary>The capabilities a profile may switch, so an admin screen is never a hardcoded checkbox list.</summary>
/// <remarks>
/// Served from <see cref="NodeCapabilities.Defaults"/>, which is the same registry the resolver reads. That is the
/// point: a capability added in code appears in the authoring UI without anyone remembering to add it there, which
/// is what stops the two drifting into a control nobody can switch off.
/// </remarks>
public sealed class ListNodeCapabilitiesEndpoint : EndpointWithoutRequest<IReadOnlyDictionary<string, bool>>
{
    public override void Configure()
    {
        Get("/directory/profiles/capabilities");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder
            .WithTags("Directory")
            .WithSummary("Every capability a profile can switch, with its platform default."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(NodeCapabilities.Defaults, ct);
}

public sealed class NodeProfileByIdRequest
{
    public Guid Id { get; set; }
}

public sealed class GetNodeProfileEndpoint(INodeProfileService profiles)
    : Endpoint<NodeProfileByIdRequest, NodeProfileDetail>
{
    public override void Configure()
    {
        Get("/directory/profiles/{id}");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Directory").WithSummary("One profile, as stored — nulls included."));
    }

    public override async Task HandleAsync(NodeProfileByIdRequest request, CancellationToken ct) =>
        await Send.OkAsync(await profiles.GetAsync(request.Id, ct), ct);
}

public sealed class CreateNodeProfileEndpoint(INodeProfileService profiles)
    : Endpoint<SaveNodeProfileRequest, NodeProfileDetail>
{
    public override void Configure()
    {
        Post("/directory/profiles");
        Policies(CracraPolicies.Pmo);
        Description(builder => builder
            .WithTags("Directory")
            .WithSummary("Author a profile with its own taxonomy, boards and capabilities."));
    }

    public override async Task HandleAsync(SaveNodeProfileRequest request, CancellationToken ct) =>
        await Send.ResponseAsync(await profiles.CreateAsync(request, ct), StatusCodes.Status201Created, ct);
}

public sealed class CloneNodeProfileRequest
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string LabelKey { get; set; } = string.Empty;
}

/// <summary>Clone, which v2 §10.6 calls the expected authoring path — a new branch starts from a close profile.</summary>
public sealed class CloneNodeProfileEndpoint(INodeProfileService profiles)
    : Endpoint<CloneNodeProfileRequest, NodeProfileDetail>
{
    public override void Configure()
    {
        Post("/directory/profiles/{id}/clone");
        Policies(CracraPolicies.Pmo);
        Description(builder => builder.WithTags("Directory").WithSummary("Copy a profile under a new code."));
    }

    public override async Task HandleAsync(CloneNodeProfileRequest request, CancellationToken ct) =>
        await Send.ResponseAsync(
            await profiles.CloneAsync(request.Id, request.Code, request.LabelKey, ct),
            StatusCodes.Status201Created,
            ct);
}

/// <summary>
/// The editable fields, as a bindable class rather than the service's record.
/// </summary>
/// <remarks>
/// The nullable collections and strings are the whole contract here: omitting a field means "inherit", and a
/// binder that helpfully defaulted them to empty would turn every save into a full override of the parent. They
/// are settable properties because model binding needs them to be, and the service is what re-imposes the shape.
/// </remarks>
public sealed class UpdateNodeProfileRequest
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string LabelKey { get; set; } = string.Empty;

    public string? ActivityTaxonomyJson { get; set; }

    public IReadOnlyList<string>? BoardArchetypes { get; set; }

    public IReadOnlyList<string>? ItemTypes { get; set; }

    public string? CapabilitiesJson { get; set; }

    public IReadOnlyList<string>? SolvesCategories { get; set; }

    public string? BudgetDefaultsJson { get; set; }

    public string? HeadlinePattern { get; set; }

    public SaveNodeProfileRequest ToSaveRequest() =>
        new(
            Code,
            LabelKey,
            ActivityTaxonomyJson,
            BoardArchetypes,
            ItemTypes,
            CapabilitiesJson,
            SolvesCategories,
            BudgetDefaultsJson,
            HeadlinePattern);
}

public sealed class UpdateNodeProfileEndpoint(INodeProfileService profiles)
    : Endpoint<UpdateNodeProfileRequest, NodeProfileDetail>
{
    public override void Configure()
    {
        Put("/directory/profiles/{id}");
        Policies(CracraPolicies.Pmo);
        Description(builder => builder.WithTags("Directory").WithSummary("Edit a profile's fields."));
    }

    public override async Task HandleAsync(UpdateNodeProfileRequest request, CancellationToken ct) =>
        await Send.OkAsync(await profiles.UpdateAsync(request.Id, request.ToSaveRequest(), ct), ct);
}

public sealed class AttachDepartmentProfileRequest
{
    public Guid Id { get; set; }

    /// <summary>Null detaches, so the department inherits from its parent again.</summary>
    public Guid? ProfileId { get; set; }
}

/// <summary>
/// Points a department at a profile.
/// </summary>
/// <remarks>
/// AnyHead rather than PMO, and gated further by <c>access.can_attach_node_profile</c> in the policy: attaching is
/// a decision about a branch, so the head who owns that branch makes it. Authoring the profile itself stays with
/// the PMO because it is shared vocabulary — editing one changes every branch pointing at it.
/// </remarks>
public sealed class AttachDepartmentProfileEndpoint(INodeProfileService profiles)
    : Endpoint<AttachDepartmentProfileRequest>
{
    public override void Configure()
    {
        Put("/directory/departments/{id}/profile");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Directory").WithSummary("Attach or detach a department's profile."));
    }

    public override async Task HandleAsync(AttachDepartmentProfileRequest request, CancellationToken ct)
    {
        await profiles.AttachToDepartmentAsync(request.Id, new AttachNodeProfileRequest(request.ProfileId), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class AttachUnitProfileRequest
{
    public Guid Id { get; set; }

    public Guid? ProfileId { get; set; }
}

public sealed class AttachUnitProfileEndpoint(INodeProfileService profiles) : Endpoint<AttachUnitProfileRequest>
{
    public override void Configure()
    {
        Put("/directory/units/{id}/profile");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Directory").WithSummary("Attach or detach a unit's profile."));
    }

    public override async Task HandleAsync(AttachUnitProfileRequest request, CancellationToken ct)
    {
        await profiles.AttachToUnitAsync(request.Id, new AttachNodeProfileRequest(request.ProfileId), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class ResolveUnitProfileRequest
{
    public Guid Id { get; set; }
}

/// <summary>
/// The profile actually in force at a unit, after the walk.
/// </summary>
/// <remarks>
/// This is what v2 §10.6's "preview the resulting week grid and brief" needs: an administrator attaching a profile
/// mid-tree wants to see what a member of that branch will get, which is not the row they just edited but the
/// composition of it with everything above.
/// </remarks>
public sealed class ResolveUnitProfileEndpoint(INodeProfileReader profiles)
    : Endpoint<ResolveUnitProfileRequest, NodeProfileSnapshot>
{
    public override void Configure()
    {
        Get("/directory/units/{id}/profile");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder
            .WithTags("Directory")
            .WithSummary("The effective profile at a unit, resolved through inheritance."));
    }

    public override async Task HandleAsync(ResolveUnitProfileRequest request, CancellationToken ct)
    {
        var resolved = await profiles.ResolveForUnitAsync(request.Id, ct);

        // 404 for "nothing in force here", which reads correctly for both causes: no profile anywhere in the
        // ancestry, and a unit RLS hid. A prober learns nothing from the difference, and a client that wants
        // platform defaults gets the same answer either way.
        if (resolved is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(resolved, ct);
    }
}
