using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.Modules.Projects.Application;
using Cracra.Modules.Projects.Infrastructure;
using Contracts = Cracra.Modules.Projects.Contracts;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Projects;

public static class ProjectsModule
{
    public static IServiceCollection AddProjectsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ProjectsDbContext>(ProjectsDbContext.SchemaName);

        // The first DDD module, so the first to use the full pipeline. Registration order is the execution order:
        // Logging (from AddMediator) -> Validation -> Transaction -> Handler.
        services.AddModuleTransactions<ProjectsDbContext>(typeof(ProjectsModule).Assembly);

        services.AddValidatorsFromAssemblyContaining<CreateProjectValidator>(ServiceLifetime.Scoped);

        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IDirectoryPort, DirectoryAdapter>();
        services.AddScoped<IProjectAccessProjection, ProjectAccessProjectionAdapter>();

        // The provisioning contract Portfolio calls when a candidate is committed.
        services.AddScoped<Contracts.IProjectProvisioner, ProjectProvisioner>();

        // Asked on every hour S5 books against a project.
        services.AddScoped<Contracts.IProjectMembershipReader, ProjectMembershipReader>();

        // The team rows S6 groups by department on the project board.
        services.AddScoped<Contracts.IProjectTeamReader, ProjectTeamReader>();

        services.AddMediatorHandlersFrom(typeof(ProjectsModule).Assembly);

        return services;
    }
}
