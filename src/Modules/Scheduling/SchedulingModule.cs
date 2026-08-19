using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.Modules.Scheduling.Application;
using Cracra.Modules.Scheduling.Infrastructure;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Scheduling;

public static class SchedulingModule
{
    public static IServiceCollection AddSchedulingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDbContext<SchedulingDbContext>(SchedulingDbContext.SchemaName);

        services.AddModuleTransactions<SchedulingDbContext>(typeof(SchedulingModule).Assembly);

        services.AddValidatorsFromAssemblyContaining<CreateWorkOrderValidator>(ServiceLifetime.Scoped);

        services.AddScoped<IWorkOrderRepository, WorkOrderRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();

        services.AddScoped<IDirectoryPort, DirectoryAdapter>();
        services.AddScoped<IProjectsPort, ProjectsAdapter>();
        services.AddScoped<IActivitiesPort, ActivitiesAdapter>();
        services.AddScoped<IPortfolioPort, PortfolioAdapter>();

        // The S7 seam. Registered now, answering nothing, so that slice replaces one line rather than five board
        // payloads and their templates.
        services.AddScoped<ICalendarOverlaySource, NoCalendarOverlays>();

        // The composer is the read side in one class. Scoped rather than transient because every board it builds
        // runs several caller-scoped queries that must share the one RLS session.
        services.AddScoped<BoardComposer>();

        // --- The S10 seam -----------------------------------------------------------------------------------
        services.Configure<WorkOrderPoolOptions>(configuration.GetSection(WorkOrderPoolOptions.SectionName));

        services.AddScoped<IEnumerable<IWorkOrderPoolSource>>(provider =>
            provider.GetRequiredService<IOptions<WorkOrderPoolOptions>>().Value.SeedSampleWorkOrders
                ? [new SamplePoolSource("servicenow")]
                : [new UnconfiguredPoolSource("servicenow",
                    provider.GetRequiredService<ILogger<UnconfiguredPoolSource>>())]);

        services.AddMediatorHandlersFrom(typeof(SchedulingModule).Assembly);

        return services;
    }
}
