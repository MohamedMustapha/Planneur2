using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.Modules.Activities.Application;
using Cracra.Modules.Activities.Infrastructure;
using Contracts = Cracra.Modules.Activities.Contracts;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Activities;

public static class ActivitiesModule
{
    public static IServiceCollection AddActivitiesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDbContext<ActivitiesDbContext>(ActivitiesDbContext.SchemaName);

        services.AddModuleTransactions<ActivitiesDbContext>(typeof(ActivitiesModule).Assembly);

        services.AddValidatorsFromAssemblyContaining<LogActivityValidator>(ServiceLifetime.Scoped);

        services.AddScoped<IActivityRepository, ActivityRepository>();
        services.AddScoped<IDirectoryPort, DirectoryAdapter>();
        services.AddScoped<IProjectsPort, ProjectsAdapter>();

        // The write-back contract S6 schedules through. Planned entries only, on the caller's own connection,
        // so RLS answers "may this lead plan for this person" exactly as it does for the API.
        services.AddScoped<Contracts.IActivityScheduler, ActivityScheduler>();

        // --- The S10 seam -----------------------------------------------------------------------------------
        // Registered as a collection because the dropdown may ask for one source or for all of them, and because
        // S10 replaces these registrations wholesale rather than wrapping them.
        services.Configure<AssignableTaskOptions>(configuration.GetSection(AssignableTaskOptions.SectionName));
        services.AddScoped<IProjectCatalogue, ProjectCatalogue>();

        services.AddScoped<IEnumerable<IAssignableTaskSource>>(provider =>
            AssignableTaskRegistration.Build(
                provider.GetRequiredService<IOptions<AssignableTaskOptions>>(),
                provider.GetRequiredService<IProjectCatalogue>(),
                provider.GetRequiredService<ILogger<UnconfiguredTaskSource>>()));

        services.AddMediatorHandlersFrom(typeof(ActivitiesModule).Assembly);

        return services;
    }
}
