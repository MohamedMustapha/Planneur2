using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Access.Data;
using Cracra.Modules.Access.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Access;

public static class AccessModule
{
    public static IServiceCollection AddAccessModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<AccessDbContext>(AccessDbContext.SchemaName);

        services.AddMemoryCache();

        // Singleton: it creates its own scope per resolution and holds the cache that every request consults.
        services.AddSingleton<IEffectiveRoleResolver, EffectiveRoleResolver>();

        services.AddScoped<IRbacOverrideService, RbacOverrideService>();
        services.AddScoped<IAdminAudit, AdminAuditWriter>();
        services.AddScoped<IRoleAssignmentWriter, RoleAssignmentWriter>();
        services.AddScoped<IRoleMaterializer, RoleMaterializer>();

        // Singleton: it creates its own system-context scope per call, because the projection's RLS
        // policy is system-write-only and the caller's session must not be able to write it.
        services.AddSingleton<IProjectMembershipProjection, ProjectMembershipProjection>();

        services.AddMediatorHandlersFrom(typeof(AccessModule).Assembly);

        return services;
    }

    /// <summary>
    /// Inserts the effective-role enrichment into the request pipeline.
    /// </summary>
    /// <remarks>
    /// Belongs in the seam <c>UseCracraWeb</c> offers: after authentication, which builds the token-derived
    /// context, and before the authorization gate and anything that opens a database connection. Both read what
    /// this writes — the coarse policies judge these roles, and the RLS interceptor stamps <c>app.roles</c> on
    /// connection open — so running it downstream of either leaves them deciding on the raw token.
    /// </remarks>
    public static IApplicationBuilder UseAccessModule(this IApplicationBuilder app) =>
        app.UseMiddleware<EffectiveRoleEnricher>();
}
