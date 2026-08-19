using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Services;
using Cracra.Modules.Directory.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Directory;

public static class DirectoryModule
{
    /// <summary>
    /// The module's single entry point into the composition root. Everything Directory needs is registered here and
    /// nothing of Directory's internals escapes — the Host calls this and knows nothing else about the module.
    /// </summary>
    public static IServiceCollection AddDirectoryModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<DirectoryDbContext>(DirectoryDbContext.SchemaName);

        services.AddOptions<DirectorySyncOptions>()
            .BindConfiguration(DirectorySyncOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IDirectoryQueryService, DirectoryQueryService>();
        services.AddScoped<IDepartmentConfigService, DepartmentConfigService>();

        services.AddHttpClient<IKeycloakDirectoryClient, KeycloakDirectoryClient>((serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<DirectorySyncOptions>>().Value;

                client.BaseAddress = new Uri(settings.KeycloakBaseUrl.TrimEnd('/') + '/');
            })
            .AddStandardResilienceHandler();

        // Singleton: it creates its own scope per run, because a reconciliation outlives any request scope and
        // needs the system user context rather than a caller's.
        services.AddSingleton<IDirectorySynchronizer, DirectorySynchronizer>();
        services.AddHostedService<DirectorySyncHostedService>();

        // Scans this assembly only. A module never picks up another module's handlers (conventions.md §1).
        services.AddMediatorHandlersFrom(typeof(DirectoryModule).Assembly);

        return services;
    }
}
