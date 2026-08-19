using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace Cracra.BuildingBlocks.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Logs to SEQ, traces over OTLP, metrics to Prometheus (<c>architecture.md §6</c>). Every log line carries
    /// user, department and correlation id so a support question can be answered from SEQ alone.
    /// </summary>
    public static IHostApplicationBuilder AddCracraObservability(this IHostApplicationBuilder builder, string serviceName)
    {
        builder.AddCracraSerilog(serviceName);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceNamespace: CracraTelemetry.ServiceName)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)]))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(CracraTelemetry.MeterName)
                .AddMeter("Npgsql")
                .AddPrometheusExporter())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    // Health and metrics scrapes are the loudest thing in the system and say nothing about users.
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health")
                                                && !context.Request.Path.StartsWithSegments("/alive")
                                                && !context.Request.Path.StartsWithSegments("/metrics"))
                .AddHttpClientInstrumentation()
                .AddSource(CracraTelemetry.ActivitySourceName)
                .AddSource("Npgsql"));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    private static void AddCracraSerilog(this IHostApplicationBuilder builder, string serviceName)
    {
        var seqUrl = builder.Configuration.GetConnectionString("seq")
                     ?? builder.Configuration["Cracra:Seq:Url"];

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProperty("service.name", serviceName)
            .WriteTo.Console();

        if (!string.IsNullOrWhiteSpace(seqUrl))
        {
            configuration.WriteTo.Seq(seqUrl);
        }

        builder.Services.AddSerilog(configuration.CreateLogger(), dispose: true);
    }

    /// <summary>
    /// Exposes <c>/metrics</c> for Prometheus. Kept off the authenticated pipeline deliberately — the scrape comes
    /// from inside the cluster and must not require a user session — so bind it somewhere Prometheus can reach and
    /// users cannot.
    /// </summary>
    public static WebApplication MapCracraMetrics(this WebApplication app)
    {
        app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous();

        return app;
    }
}
