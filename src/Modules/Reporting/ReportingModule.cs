using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Reporting.Application;
using Cracra.Modules.Reporting.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Reporting;

public static class ReportingModule
{
    /// <summary>
    /// The module's single entry point into the composition root.
    /// </summary>
    /// <remarks>
    /// No <c>AddModuleTransactions</c>, unlike the other DDD modules. Reporting composes reads and writes exactly
    /// one row — a cached narrative — in its own <c>SaveChanges</c>. Wrapping a report in a transaction would open
    /// one around six modules' worth of queries for no invariant that spans them.
    /// </remarks>
    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ReportingDbContext>(ReportingDbContext.SchemaName);

        services.AddSingleton<ReportingTelemetry>();

        // The composer and the request service are scoped rather than transient: one report runs a dozen
        // caller-scoped queries that must share the one RLS session.
        services.AddScoped<ReportComposer>();
        services.AddScoped<ReportRequestService>();

        services.AddScoped<IActivityQueries, ActivityAdapter>();
        services.AddScoped<IDirectoryQueries, DirectoryAdapter>();
        services.AddScoped<IProjectQueries, ProjectAdapter>();
        services.AddScoped<IPortfolioQueries, PortfolioAdapter>();
        services.AddScoped<IMeetingQueries, MeetingAdapter>();
        services.AddScoped<IScheduleQueries, ScheduleAdapter>();

        // The S9 seam. Registered now, answering nothing, so that slice replaces one line rather than the report
        // contract, the PDF renderer and the Angular view.
        services.AddScoped<IKudosQueries, NoKudos>();

        services.AddScoped<IAiSummarizer, AiSummarizer>();
        services.AddScoped<ISummaryStore, SummaryStore>();

        // A collection, because "which formats can this export" is a question the handler asks rather than
        // assumes. Today the answer is PDF; the shape does not have to change for it not to be.
        services.AddScoped<IReportRenderer, PdfReportRenderer>();

        services.AddMediatorHandlersFrom(typeof(ReportingModule).Assembly);

        return services;
    }
}
