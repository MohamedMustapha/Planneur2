using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.Modules.Activities.Application;
using Cracra.Modules.Activities.Infrastructure;
using Contracts = Cracra.Modules.Activities.Contracts;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Integrations.Contracts;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Activities;

public static class ActivitiesModule
{
    public static IServiceCollection AddActivitiesModule(this IServiceCollection services)
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

        // --- The S10 seam, filled ---------------------------------------------------------------------------
        // The one line S5's comment promised would change. The dropdown, its query, the pre-fill and every test
        // around them stayed exactly as they were; what moved is where the tasks come from — a mirror table
        // instead of a stand-in. Still a collection, because the dropdown may ask for one source or for all.
        services.AddScoped<IEnumerable<IAssignableTaskSource>>(provider =>
            AssignableTaskRegistration.Build(
                provider.GetRequiredService<IExternalWorkItemReader>(),
                provider.GetRequiredService<IUserContext>()));

        services.AddMediatorHandlersFrom(typeof(ActivitiesModule).Assembly);

        return services;
    }
}
