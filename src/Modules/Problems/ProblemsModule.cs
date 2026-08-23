using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Problems.Application;
using Contracts = Cracra.Modules.Problems.Contracts;
using Cracra.Modules.Problems.Data;
using Cracra.Modules.Problems.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Problems;

public static class ProblemsModule
{
    public static IServiceCollection AddProblemsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ProblemsDbContext>(ProblemsDbContext.SchemaName);

        services.AddScoped<IProblemService, ProblemService>();
        services.AddScoped<IPortfolioPort, PortfolioAdapter>();

        // Problem references, read by Strategy when a pain is linked to an objective (v2 §06.1).
        services.AddScoped<Contracts.IProblemLookupReader, ProblemLookupReader>();

        return services;
    }
}
