using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Finance.Data;
using Cracra.Modules.Finance.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Finance;

public static class FinanceModule
{
    /// <summary>
    /// The module's single entry point into the composition root. Everything Finance needs is registered here and
    /// nothing of its internals escapes — the Host calls this and knows nothing else about the module.
    /// </summary>
    public static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<FinanceDbContext>(FinanceDbContext.SchemaName);

        services.AddScoped<IFinanceConfigService, FinanceConfigService>();
        services.AddScoped<ICapexOpexService, CapexOpexService>();
        services.AddScoped<ICapexOpexExporter, CapexOpexExporter>();

        // No hosted service, no outbox consumer, no integration event. Finance derives and exports; it changes
        // nothing anybody else can observe, which is what "emits nothing" in the spec means in practice.
        services.AddMediatorHandlersFrom(typeof(FinanceModule).Assembly);

        return services;
    }
}
