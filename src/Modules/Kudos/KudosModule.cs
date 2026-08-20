using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.Modules.Kudos.Application;
using Cracra.Modules.Kudos.Infrastructure;
using Contracts = Cracra.Modules.Kudos.Contracts;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Kudos;

public static class KudosModule
{
    public static IServiceCollection AddKudosModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<KudosDbContext>(KudosDbContext.SchemaName);

        services.AddModuleTransactions<KudosDbContext>(typeof(KudosModule).Assembly);

        services.AddValidatorsFromAssemblyContaining<GiveKudoValidator>(ServiceLifetime.Scoped);

        services.AddScoped<IKudoRepository, KudoRepository>();
        services.AddScoped<IDirectoryPort, DirectoryAdapter>();
        services.AddScoped<IProjectsPort, ProjectsAdapter>();

        // Both halves of "who may recognise whom", and the scope resolution every read shares. Concrete rather
        // than behind an interface: they are this module's own logic, and a seam nobody else implements is a seam
        // that only makes the wiring harder to read.
        services.AddScoped<KudoEligibility>();
        services.AddScoped<ScopeResolver>();

        services.AddSingleton<KudosTelemetry>();

        // The contract S8's report has been asking since it shipped.
        services.AddScoped<Contracts.IKudosReader, KudosReader>();

        services.AddMediatorHandlersFrom(typeof(KudosModule).Assembly);

        return services;
    }
}
