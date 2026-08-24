using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Data;
using Cracra.Modules.Meetings.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Meetings;

public static class MeetingsModule
{
    /// <summary>
    /// The module's single entry point into the composition root. Everything Meetings needs is registered here and
    /// nothing of its internals escapes — the Host calls this and knows nothing else about the module.
    /// </summary>
    public static IServiceCollection AddMeetingsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<MeetingsDbContext>(MeetingsDbContext.SchemaName);

        services.AddOptions<MeetingsOptions>()
            .BindConfiguration(MeetingsOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IMeetingScopeResolver, MeetingScopeResolver>();
        services.AddScoped<IOccurrenceMaterializer, OccurrenceMaterializer>();
        services.AddScoped<IMeetingSeriesService, MeetingSeriesService>();
        services.AddScoped<ISpecialDayService, SpecialDayService>();
        services.AddScoped<IMinutesService, MinutesService>();
        services.AddScoped<MeetingCalendarService>();

        // One implementation behind two interfaces: the endpoints' service and the contract other modules consume.
        // Registered by forwarding rather than twice over, so a request that both reads the strip and composes a
        // board shares one instance and therefore one RLS session.
        services.AddScoped<IMeetingCalendarService>(provider => provider.GetRequiredService<MeetingCalendarService>());
        services.AddScoped<IMeetingCalendarReader>(provider => provider.GetRequiredService<MeetingCalendarService>());

        // Singleton: it creates its own scope per run, because rolling the horizon outlives any request scope and
        // needs the system user context rather than a caller's.
        services.AddSingleton<IMeetingHorizonSweeper, MeetingHorizonSweeper>();
        services.AddHostedService<MeetingHorizonHostedService>();

        // Scans this assembly only. A module never picks up another module's handlers (conventions.md §1).
        services.AddMediatorHandlersFrom(typeof(MeetingsModule).Assembly);

        return services;
    }
}
