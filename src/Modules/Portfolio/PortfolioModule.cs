using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.Modules.Portfolio.Application;
using Cracra.Modules.Portfolio.Infrastructure;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Portfolio;

public static class PortfolioModule
{
    public static IServiceCollection AddPortfolioModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<PortfolioDbContext>(PortfolioDbContext.SchemaName);

        services.AddModuleTransactions<PortfolioDbContext>(typeof(PortfolioModule).Assembly);

        services.AddValidatorsFromAssemblyContaining<ConsiderItemValidator>(ServiceLifetime.Scoped);

        services.AddScoped<IPortfolioRepository, PortfolioRepository>();
        services.AddScoped<IProjectsPort, ProjectsAdapter>();
        services.AddScoped<IDirectoryPort, DirectoryAdapter>();

        services.AddMediatorHandlersFrom(typeof(PortfolioModule).Assembly);

        return services;
    }
}
