using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Integrations.Data;
using Cracra.Modules.Integrations.Providers;
using Cracra.Modules.Integrations.Services;
using Cracra.Modules.Integrations.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Integrations;

public static class IntegrationsModule
{
    /// <summary>
    /// The module's single entry point into the composition root. Everything Integrations needs is registered here
    /// and nothing of its internals escapes — the Host calls this and knows nothing else about the module.
    /// </summary>
    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<IntegrationsDbContext>(IntegrationsDbContext.SchemaName);

        services.AddOptions<IntegrationsOptions>()
            .BindConfiguration(IntegrationsOptions.SectionName)
            .ValidateOnStart();

        services.AddScoped<IConnectionService, ConnectionService>();
        services.AddSingleton<IIntegrationCredentials, IntegrationCredentials>();

        // One implementation behind the module's own endpoint and behind the contract S5 and S6a consume.
        // Registered once and forwarded, so a request that both opens the dropdown and composes a board shares
        // one instance and therefore one RLS session.
        services.AddScoped<WorkItemQueryService>();
        services.AddScoped<IExternalWorkItemReader>(provider =>
            provider.GetRequiredService<WorkItemQueryService>());

        AddProviders(services);

        // Singleton: a pull creates its own scope per run, because a reconciliation outlives any request scope
        // and needs the system user context rather than a caller's.
        services.AddSingleton<IExternalWorkItemSynchronizer, ExternalWorkItemSynchronizer>();

        // The dispatcher is registered concretely as well as behind its interface: the drain service reads the
        // channel off the same instance the endpoint writes to, and two registrations would give it a second,
        // permanently empty one.
        services.AddSingleton<IntegrationSyncDispatcher>();
        services.AddSingleton<IIntegrationSyncDispatcher>(provider =>
            provider.GetRequiredService<IntegrationSyncDispatcher>());

        services.AddHostedService<IntegrationSyncDispatchService>();
        services.AddHostedService<IntegrationSyncHostedService>();

        // Reports on the connections, not on the systems behind them — see the type for why probing DevOps every
        // few seconds would be the wrong reading of "a health check per integration".
        services.AddHealthChecks()
            .AddCheck<IntegrationsHealthCheck>("integrations", tags: ["ready", "integrations"]);

        // Scans this assembly only. A module never picks up another module's handlers (conventions.md §1).
        services.AddMediatorHandlersFrom(typeof(IntegrationsModule).Assembly);

        return services;
    }

    /// <summary>
    /// One typed client per provider, each behind the read-only handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler is registered as the primary delegating handler rather than being remembered by each adapter,
    /// so a provider added in a year is guarded whether or not its author read this file. That is the point of
    /// putting the guarantee in the pipeline: it is not something an adapter opts into.
    /// </para>
    /// <para>
    /// No <c>BaseAddress</c>: the address is per connection, not per process. Every request builds an absolute
    /// URL from the connection row, which is also why the handler checks the allow-list on the request rather
    /// than on the client.
    /// </para>
    /// <para>
    /// Credentials are attached per request too. A typed client is shared across every connection of its
    /// provider, and a token set on <c>DefaultRequestHeaders</c> would be one department's PAT used for another
    /// department's pull — occasionally successfully, which is the worst version of that bug.
    /// </para>
    /// </remarks>
    private static void AddProviders(IServiceCollection services)
    {
        services.AddTransient<ReadOnlyHttpHandler>();

        services.AddHttpClient<IExternalWorkItemProvider, AzureDevOpsProvider>(Configure)
            .AddHttpMessageHandler<ReadOnlyHttpHandler>()
            .AddStandardResilienceHandler();

        services.AddHttpClient<IExternalWorkItemProvider, ServiceNowProvider>(Configure)
            .AddHttpMessageHandler<ReadOnlyHttpHandler>()
            .AddStandardResilienceHandler();
    }

    private static void Configure(IServiceProvider provider, HttpClient client)
    {
        client.Timeout = provider.GetRequiredService<IOptions<IntegrationsOptions>>().Value.RequestTimeout;

        // Identifies the platform in the other system's access log, which is what the person on the far side of
        // an unexplained 40 000 requests an hour needs in order to come and ask about it.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("cracra-integrations/1.0");
    }
}
