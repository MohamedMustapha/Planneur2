using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.BuildingBlocks.Persistence.Rls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Cracra.BuildingBlocks.Persistence;

public static class PersistenceExtensions
{
    /// <summary>
    /// Registers the shared persistence machinery: connection identities, the RLS session interceptor, the
    /// startup initializer and the outbox drain loop. Modules then add themselves with
    /// <see cref="AddModuleDbContext{TContext}"/>.
    /// </summary>
    public static IServiceCollection AddCracraPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CracraDatabaseOptions>()
            .Bind(configuration.GetSection(CracraDatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Scoped, because it reads the request's IUserContext.
        services.TryAddScoped<RlsSessionInterceptor>();

        services.AddSingleton<IHostedService, PlatformDatabaseInitializer>();
        services.AddHostedService<OutboxDrainService>();

        return services;
    }

    /// <summary>
    /// Adds one module's DbContext, pointed at its own schema, connected as the non-owner runtime role, with the
    /// RLS interceptor attached and its own migration-history table so modules can migrate independently.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((serviceProvider, builder) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<CracraDatabaseOptions>>().Value;

            builder.UseNpgsql(settings.RuntimeConnectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", schema);
                npgsql.EnableRetryOnFailure(3);
            });

            builder.AddInterceptors(serviceProvider.GetRequiredService<RlsSessionInterceptor>());

            // conventions.md §6 and the database-performance guidance: reads are the overwhelming majority and
            // tracking them is pure cost. A write path opts back in per query.
            builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        services.AddSingleton<IModuleMigrator, ModuleMigrator<TContext>>();
        services.AddSingleton<IOutboxDrainer, OutboxDrainer<TContext>>();

        return services;
    }
}
