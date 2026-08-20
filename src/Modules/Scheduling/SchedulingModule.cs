using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.Modules.Scheduling.Application;
using Contracts = Cracra.Modules.Scheduling.Contracts;
using Cracra.Modules.Scheduling.Infrastructure;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cracra.Modules.Scheduling;

public static class SchedulingModule
{
    public static IServiceCollection AddSchedulingModule(this IServiceCollection services)
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

        // The read port S8's unit report consumes. Registered against the contracts interface so nothing outside
        // Scheduling ever names the implementation.
        services.AddScoped<Contracts.IScheduleLoadReader, ScheduleLoadReader>();

        // The S7 seam, filled. This is the one line the S6 comment promised would change: the boards' payload
        // shape, the composer and every client template stayed exactly as they were.
        services.AddScoped<ICalendarOverlaySource, MeetingCalendarOverlays>();

        // The composer is the read side in one class. Scoped rather than transient because every board it builds
        // runs several caller-scoped queries that must share the one RLS session.
        services.AddScoped<BoardComposer>();

        // --- The S10 seam, filled ---------------------------------------------------------------------------
        // ServiceNow only, and deliberately: 6a's pool is the RUN queue nobody has picked up. A DevOps sprint
        // task always belongs to a project team, so it reaches a person through S5's dropdown rather than
        // through a pool a lead drags from.
        services.AddScoped<IEnumerable<IWorkOrderPoolSource>>(provider =>
        [
            new MirrorPoolSource(
                Cracra.Modules.Integrations.Contracts.ExternalProviders.ServiceNow,
                provider.GetRequiredService<Cracra.Modules.Integrations.Contracts.IExternalWorkItemReader>()),
        ]);

        services.AddMediatorHandlersFrom(typeof(SchedulingModule).Assembly);

        return services;
    }
}
