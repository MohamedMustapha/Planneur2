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
    /// user context, then <paramref name="enrichUserContext"/>, and only then authorization and anything that opens
    /// a database connection — the RLS interceptor reads the context the middleware writes.
    /// </summary>
    /// <param name="enrichUserContext">
    /// Anything that has the final word on who the caller is, run between the token-derived context and the gate.
    /// </param>
    /// <remarks>
    /// The seam exists because authorization is the first thing that <em>reads</em> the roles, and whatever resolves
    /// them has to be upstream of it. Registering the enrichment after this method leaves the coarse policies
    /// judging the raw token: a head whose realm still says <c>dept-head</c> is refused at the door, and an RBAC
    /// override that revokes a role does not bite until the handler is already running. Both were invisible to the
    /// integration suite, whose seeded contexts arrive pre-resolved, and showed up only end-to-end.
    /// </remarks>
    public static IApplicationBuilder UseCracraWeb(
        this IApplicationBuilder app,
        Action<IApplicationBuilder>? enrichUserContext = null)
    {
        app.UseExceptionHandler();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<UserContextMiddleware>();
        enrichUserContext?.Invoke(app);
        app.UseAuthorization();

        return app;
    }
}
