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
        services.AddScoped<IRoleAssignmentWriter, RoleAssignmentWriter>();
        services.AddScoped<IRoleMaterializer, RoleMaterializer>();

        services.AddMediatorHandlersFrom(typeof(AccessModule).Assembly);

        return services;
    }

    /// <summary>
    /// Inserts the effective-role enrichment into the request pipeline.
    /// </summary>
    /// <remarks>
    /// Must be called after <c>UseCracraWeb</c> — which authenticates and builds the token-derived context — and
    /// before anything opens a database connection. The RLS interceptor stamps <c>app.roles</c> on connection
    /// open, and it can only stamp what is in the context by then.
    /// </remarks>
    public static IApplicationBuilder UseAccessModule(this IApplicationBuilder app) =>
        app.UseMiddleware<EffectiveRoleEnricher>();
}
