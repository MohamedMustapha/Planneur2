using Cracra.BuildingBlocks.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Cracra.ServiceDefaults;

/// <summary>
/// What every process in the system gets for free — the API and the BFF alike. Aspire wires the endpoints in
/// development; the same code runs unchanged in a compose deployment, which is the point of keeping the defaults
/// here rather than in the AppHost.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public static IHostApplicationBuilder AddCracraServiceDefaults(this IHostApplicationBuilder builder, string serviceName)
    {
        builder.AddCracraObservability(serviceName);

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        builder.Services.AddHealthChecks()
            // "alive" answers "is the process up"; anything with a dependency is tagged "ready" instead, so a slow
            // database can never make the orchestrator kill an otherwise healthy process.
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["alive"]);

        return builder;
    }

    public static WebApplication MapCracraDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        }).AllowAnonymous();

        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("alive"),
        }).AllowAnonymous();

        app.MapCracraMetrics();

        return app;
    }
}
