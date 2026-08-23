using Cracra.BuildingBlocks.Web.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.BuildingBlocks.Web.Authorization;

/// <summary>
/// Coarse gate policies. They decide whether an endpoint may be <em>called</em>; they never decide which rows come
/// back — that is RLS, and only RLS (<c>conventions.md §3</c>). Endpoints fail closed: no policy means no access.
/// </summary>
public static class CracraPolicies
{
    public const string Authenticated = "cracra:authenticated";
    public const string Member = "cracra:member";
    public const string NodeHead = "cracra:node-head";
    public const string ProjectLead = "cracra:project-lead";
    public const string Pmo = "cracra:pmo";

    /// <summary>Any role that governs beyond a single person — a node head at any depth, or the PMO.</summary>
    public const string AnyHead = "cracra:any-head";

    /// <summary>
    /// Anyone who may run delivery work: a project lead, a PO, or any head.
    /// </summary>
    /// <remarks>
    /// One policy rather than two on the endpoint. FastEndpoints combines multiple <c>Policies(...)</c> with AND,
    /// so listing project-lead and any-head there would demand both and refuse a department head who is not also
    /// a named project lead — which is most of them.
    /// </remarks>
    public const string DeliveryLead = "cracra:delivery-lead";

    public static IServiceCollection AddCracraAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Authenticated, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(Member, policy => policy.RequireContextualRole(ContextualRole.Member))
            .AddPolicy(NodeHead, policy => policy.RequireContextualRole(ContextualRole.NodeHead))
            .AddPolicy(ProjectLead, policy => policy.RequireContextualRole(ContextualRole.ProjectLead, ContextualRole.ProductOwner))
            .AddPolicy(Pmo, policy => policy.RequireContextualRole(ContextualRole.Pmo))
            .AddPolicy(AnyHead, policy => policy.RequireContextualRole([.. ContextualRole.Heads]))
            .AddPolicy(DeliveryLead, policy => policy.RequireContextualRole(
                [ContextualRole.ProjectLead, ContextualRole.ProductOwner, .. ContextualRole.Heads]));

        return services;
    }

    /// <summary>
    /// Asserts against the resolved <see cref="IUserContext"/> rather than raw claims, so the token shape can change
    /// (or S2's RBAC fallback view can add a role) without touching a single policy.
    /// </summary>
    private static AuthorizationPolicyBuilder RequireContextualRole(
        this AuthorizationPolicyBuilder builder,
        params string[] roles)
        => builder.RequireAuthenticatedUser().AddRequirements(new ContextualRoleRequirement(roles));
}

public sealed class ContextualRoleRequirement(IReadOnlyList<string> acceptedRoles) : IAuthorizationRequirement
{
    public IReadOnlyList<string> AcceptedRoles { get; } = acceptedRoles;
}

internal sealed class ContextualRoleHandler(IUserContextAccessor accessor)
    : AuthorizationHandler<ContextualRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ContextualRoleRequirement requirement)
    {
        var user = accessor.Current;

        if (user.IsAuthenticated && requirement.AcceptedRoles.Any(user.Has))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
