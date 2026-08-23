using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.Modules.Portfolio.Application;
using Cracra.Modules.Portfolio.Infrastructure;
using Contracts = Cracra.Modules.Portfolio.Contracts;
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
        services.AddScoped<IDirectoryNodePort, DirectoryNodeAdapter>();
        services.AddScoped<ICatalogReader, CatalogReader>();
        services.AddScoped<Contracts.IPortfolioItemProvisioner, ItemProvisioner>();

        // Iteration ranges, drawn as overlays on the S6 project board.
        services.AddScoped<Contracts.IPortfolioIterationReader, PortfolioIterationReader>();

        // The lifecycle board, counted by S8's department and portfolio reports.
        services.AddScoped<Contracts.IPortfolioBoardReader, PortfolioBoardReader>();

        // Item references, read by Strategy when it draws an objective's contributions (v2 §06).
        services.AddScoped<Contracts.ICatalogLookupReader, CatalogLookupReader>();

        services.AddMediatorHandlersFrom(typeof(PortfolioModule).Assembly);

        return services;
    }
}
