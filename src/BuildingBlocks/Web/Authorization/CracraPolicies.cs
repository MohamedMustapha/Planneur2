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
    public const string UnitHead = "cracra:unit-head";
    public const string DepartmentHead = "cracra:dept-head";
    public const string ProjectLead = "cracra:project-lead";
    public const string Pmo = "cracra:pmo";

    /// <summary>Any role that governs beyond a single person — unit-head, dept-head or PMO.</summary>
    public const string AnyHead = "cracra:any-head";

    public static IServiceCollection AddCracraAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Authenticated, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(Member, policy => policy.RequireContextualRole(ContextualRole.Member))
            .AddPolicy(UnitHead, policy => policy.RequireContextualRole(ContextualRole.UnitHead))
            .AddPolicy(DepartmentHead, policy => policy.RequireContextualRole(ContextualRole.DepartmentHead))
            .AddPolicy(ProjectLead, policy => policy.RequireContextualRole(ContextualRole.ProjectLead, ContextualRole.ProductOwner))
            .AddPolicy(Pmo, policy => policy.RequireContextualRole(ContextualRole.Pmo))
            .AddPolicy(AnyHead, policy => policy.RequireContextualRole([.. ContextualRole.Heads]));

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
