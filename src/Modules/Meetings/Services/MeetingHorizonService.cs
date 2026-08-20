using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Meetings.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Meetings.Services;

/// <summary>Rolls every active series' materialized window forward. Kept behind an interface so tests can drive it.</summary>
public interface IMeetingHorizonSweeper
{
    Task<int> SweepAsync(CancellationToken ct);
}

/// <summary>
/// Extends the materialized calendar as time passes.
/// </summary>
/// <remarks>
/// <para>
/// Without this, a series materialized six months ahead on the day it was created would quietly run out. Nothing
/// would break — no error, no empty board, just a calendar that stops — which is the failure mode worth spending
/// a background service on, because it surfaces months after the change that caused it.
/// </para>
/// <para>
/// Runs under the system context, like every other background job (architecture.md §4). It has to: it is writing
/// occurrences for series belonging to departments no human session would have both the read and write scope for
/// at once.
/// </para>
/// </remarks>
internal sealed class MeetingHorizonSweeper(
    IServiceScopeFactory scopes,
    ILogger<MeetingHorizonSweeper> logger,
    IOptions<MeetingsOptions> options) : IMeetingHorizonSweeper
{
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();

        // Set before anything opens a connection: the RLS interceptor stamps whatever the context holds at that
        // moment, and a scope stamped after the first query would run the rest of the sweep as nobody.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var context = scope.ServiceProvider.GetRequiredService<MeetingsDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IOccurrenceMaterializer>();

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var horizon = today.AddDays(7 * options.Value.HorizonWeeks);

        var series = await context.Series.Where(candidate => candidate.Active).ToListAsync(ct);

        var added = 0;

        foreach (var candidate in series)
        {
            added += await materializer.SynchronizeAsync(candidate, today, horizon, ct);
        }

        // Unconditionally, not only when something was added: a sweep also removes occurrences a rule no longer
        // produces, and "added nothing" is not the same as "changed nothing".
        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "Meeting horizon swept to {Horizon:yyyy-MM-dd}: {Added} occurrences materialized across {Series} series.",
            horizon,
            added,
            series.Count);

        return added;
    }
}

/// <summary>The timer around <see cref="IMeetingHorizonSweeper"/>. Separated so a test can sweep without one.</summary>
internal sealed class MeetingHorizonHostedService(
    IMeetingHorizonSweeper sweeper,
    ILogger<MeetingHorizonHostedService> logger,
    IOptions<MeetingsOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (settings.SweepOnStartup)
        {
            await SafeSweepAsync(stoppingToken);
        }

        if (settings.SweepInterval <= TimeSpan.Zero)
        {
            return;
        }

        using var timer = new PeriodicTimer(settings.SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SafeSweepAsync(stoppingToken);
        }
    }

    /// <summary>
    /// A failed sweep logs and waits for the next tick.
    /// </summary>
    /// <remarks>
    /// The calendar is already materialized months ahead, so one missed pass costs nothing; letting the exception
    /// escape would take the hosted service down for good and turn a transient database blip into a calendar that
    /// silently stops growing.
    /// </remarks>
    private async Task SafeSweepAsync(CancellationToken ct)
    {
        try
        {
            await sweeper.SweepAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Sweeping the meeting horizon failed. The next pass will retry.");
        }
    }
}
