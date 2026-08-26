using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Strategy.Application;
using Cracra.Modules.Strategy.Data;
using Cracra.Modules.Strategy.Infrastructure;
using Contracts = Cracra.Modules.Strategy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Strategy;

public static class StrategyModule
{
    /// <summary>
    /// The module's single entry point into the composition root. The Host calls this and knows nothing else.
    /// </summary>
    public static IServiceCollection AddStrategyModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<StrategyDbContext>(StrategyDbContext.SchemaName);

        services.AddScoped<IStrategyService, StrategyService>();
        services.AddScoped<IPortfolioFactsPort, PortfolioFactsAdapter>();
        services.AddScoped<IProblemFactsPort, ProblemFactsAdapter>();

        // The block a COPIL minute (§07) and a node report (S8) embed. One rollup, three screens.
        services.AddScoped<Contracts.IStrategyRollupReader, StrategyRollupReader>();

        return services;
    }
}
