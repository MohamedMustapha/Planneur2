using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Directory.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Directory.Endpoints;

// The administration surface (v2 §08.3).
//
// Reshaping a branch is OrgAdministrator rather than admin-only, and that is deliberate: §08.1 gives a node-head
// their own admin over the branch beneath them. Which branch is access.can_write_org_node's answer — it admits the
// PMO and a global administrator everywhere, and a head strictly below their own node. The policy is the door; the
// predicate is the lock, and only the lock knows *which* branch (conventions.md §3).
//
// The levels are the exception: they are the shape of the organisation rather than a branch of it, so they are a
// global administrator's alone, and the table's own policy says the same thing.

public sealed class ListLevelsEndpoint(IOrgAdminService org) : EndpointWithoutRequest<IReadOnlyList<OrgLevelView>>
{
    public override void Configure()
    {
        Get("/admin/org/levels");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Admin").WithSummary("How many levels the organisation has, and what they are called."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await org.LevelsAsync(ct), ct);
}

public sealed class SaveLevelCommand
{
    public int LevelNo { get; set; }

    public string Code { get; set; } = string.Empty;

    public string LabelKey { get; set; } = string.Empty;

    public string LabelPluralKey { get; set; } = string.Empty;

    public string HeadLabelKey { get; set; } = string.Empty;

    /// <summary>Whether people may attach at this level, or whether it only ever holds other branches.</summary>
    public bool PeopleAllowed { get; set; } = true;

    public bool IsOptional { get; set; }
}

public sealed class SaveLevelEndpoint(IOrgAdminService org) : Endpoint<SaveLevelCommand, OrgLevelView>
{
    public override void Configure()
    {
        Put("/admin/org/levels");
        Policies(CracraPolicies.Administrator);
        Description(builder => builder.WithTags("Admin").WithSummary("Define or rename a level."));
    }

    public override async Task HandleAsync(SaveLevelCommand request, CancellationToken ct) =>
        await Send.OkAsync(
            await org.SaveLevelAsync(
                new SaveLevelRequest(
                    request.LevelNo,
                    request.Code,
                    request.LabelKey,
                    request.LabelPluralKey,
                    request.HeadLabelKey,
                    request.PeopleAllowed,
                    request.IsOptional),
                ct),
            ct);
}

public sealed class ListNodesEndpoint(IOrgAdminService org) : EndpointWithoutRequest<IReadOnlyList<OrgNodeAdminView>>
{
    public override void Configure()
    {
        Get("/admin/org/nodes");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Admin").WithSummary("The tree, depth-first, with each row's depth."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await org.TreeAsync(ct), ct);
}

public sealed class CreateNodeCommand
{
    public Guid? ParentId { get; set; }

    public int LevelNo { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed record CreatedNode(Guid Id);

public sealed class CreateNodeEndpoint(IOrgAdminService org) : Endpoint<CreateNodeCommand, CreatedNode>
{
    public override void Configure()
    {
        Post("/admin/org/nodes");
        Policies(CracraPolicies.OrgAdministrator);
        Description(builder => builder.WithTags("Admin").WithSummary("Create a branch."));
    }

    public override async Task HandleAsync(CreateNodeCommand request, CancellationToken ct) =>
        await Send.ResponseAsync(
            new CreatedNode(await org.CreateNodeAsync(
                new CreateNodeRequest(request.ParentId, request.LevelNo, request.Code, request.Name),
                ct)),
            StatusCodes.Status201Created,
            ct);
}

public sealed class AmendNodeCommand
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    /// <summary>Present, even as null, means "move it"; absent means "leave it where it is".</summary>
    public Guid? ParentId { get; set; }

    public bool Reparent { get; set; }

    public Guid? HeadPersonId { get; set; }

    public bool SetHead { get; set; }

    public bool? Active { get; set; }
}

/// <summary>
/// One PATCH for the four things an administrator does to a branch.
/// </summary>
/// <remarks>
/// Four endpoints would each need the same lookup, the same refusal and the same audit line. What differs between
/// renaming and re-parenting is one call in the service, not a resource.
/// </remarks>
public sealed class AmendNodeEndpoint(IOrgAdminService org) : Endpoint<AmendNodeCommand>
{
    public override void Configure()
    {
        Patch("/admin/org/nodes/{id}");
        Policies(CracraPolicies.OrgAdministrator);
        Description(builder => builder.WithTags("Admin").WithSummary("Rename, move, re-head or deactivate a branch."));
    }

    public override async Task HandleAsync(AmendNodeCommand request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            await org.RenameAsync(request.Id, request.Name, ct);
        }

        if (request.Reparent)
        {
            await org.ReparentAsync(request.Id, request.ParentId, ct);
        }

        if (request.SetHead)
        {
            await org.SetHeadAsync(request.Id, request.HeadPersonId, ct);
        }

        if (request.Active is { } active)
        {
            await org.SetActiveAsync(request.Id, active, ct);
        }

        await Send.NoContentAsync(ct);
    }
}

public sealed class NodeMembersRequest
{
    public Guid Id { get; set; }
}

/// <summary>
/// Who sits on a branch (v2 §08.1's node admin).
/// </summary>
/// <remarks>
/// Authenticated, like the tree itself: what comes back is what this caller may already read about these people,
/// and a list that refused everybody but a head would make the members screen unusable for the person the branch
/// belongs to.
/// </remarks>
public sealed class NodeMembersEndpoint(IOrgAdminService org) : Endpoint<NodeMembersRequest, IReadOnlyList<OrgMemberView>>
{
    public override void Configure()
    {
        Get("/admin/org/nodes/{id}/members");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Admin").WithSummary("The people whose home is this branch."));
    }

    public override async Task HandleAsync(NodeMembersRequest request, CancellationToken ct) =>
        await Send.OkAsync(await org.MembersAsync(request.Id, ct), ct);
}

public sealed class MovePersonCommand
{
    public Guid PersonId { get; set; }

    public Guid NodeId { get; set; }
}

/// <summary>
/// Put somebody where they actually work (v2 §08.1).
/// </summary>
/// <remarks>
/// This is the team-member management the boards used to own. It writes an override rather than the derived node,
/// so the next directory sync leaves the correction alone instead of undoing it every night.
/// </remarks>
public sealed class MovePersonEndpoint(IOrgAdminService org) : Endpoint<MovePersonCommand>
{
    public override void Configure()
    {
        Patch("/admin/org/people/{personId}");
        Policies(CracraPolicies.OrgAdministrator);
        Description(builder => builder.WithTags("Admin").WithSummary("Move a person to another branch."));
    }

    public override async Task HandleAsync(MovePersonCommand request, CancellationToken ct)
    {
        await org.MovePersonAsync(request.PersonId, request.NodeId, ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class AuditRequest
{
    [QueryParam]
    public Guid? NodeId { get; set; }

    [QueryParam]
    public string? Action { get; set; }

    [QueryParam]
    public DateOnly? From { get; set; }

    [QueryParam]
    public DateOnly? To { get; set; }
}

/// <summary>
/// The administrative trail (v2 §08.3).
/// </summary>
/// <remarks>
/// Authenticated rather than head-gated, because what a caller sees is RLS's answer: a head reads the acts that
/// landed on their own branches, the PMO and a global administrator read everything including the org-wide acts
/// that carry no branch at all, and everybody else reads an empty list.
/// </remarks>
public sealed class ReadAuditEndpoint(IAdminAudit audit) : Endpoint<AuditRequest, IReadOnlyList<AdminAuditDto>>
{
    public override void Configure()
    {
        Get("/admin/audit");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Admin").WithSummary("Who changed what, and when."));
    }

    public override async Task HandleAsync(AuditRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await audit.ReadAsync(request.NodeId, request.Action, request.From, request.To, ct),
            ct);
}
