using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.BuildingBlocks.Web.Errors;
using Cracra.BuildingBlocks.Web.Http;
using Cracra.BuildingBlocks.Web.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.BuildingBlocks.Web;

public static class WebBuildingBlockExtensions
{
    /// <summary>
    /// The web cross-cutting concerns every host shares: ambient user context, ProblemDetails, the contextual-role
    /// policies and a correlation id on every request.
    /// </summary>
    public static IServiceCollection AddCracraWeb(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.TryAddScoped<IUserContextAccessor, UserContextAccessor>();

        // Deliberately a forwarding proxy, not `sp => accessor.Current`. A scoped snapshot would capture whatever
        // the accessor held at first resolution, which for anything constructed before the middleware runs is
        // Anonymous — and an anonymous RLS context silently returns zero rows instead of failing loudly.
        services.TryAddScoped<IUserContext, UserContextProxy>();

        services.AddProblemDetails();
        services.AddExceptionHandler<CracraExceptionHandler>();

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IAuthorizationHandler, ContextualRoleHandler>());

        services.AddCracraAuthorization();

        return services;
    }

    /// <summary>
    /// Order is load-bearing: correlation first so every later log line carries it, then authentication, then the
    /// user context, and only then anything that opens a database connection — the RLS interceptor reads the
    /// context the middleware writes.
    /// </summary>
    public static IApplicationBuilder UseCracraWeb(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<UserContextMiddleware>();
        app.UseAuthorization();

        return app;
    }
}
