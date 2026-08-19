using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Messaging;
using Cracra.BuildingBlocks.Web.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Cracra.BuildingBlocks.Persistence.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Range(typeof(TimeSpan), "00:00:00.100", "00:05:00")]
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    /// <summary>Attempts before a message is left alone for a human. It stays queryable; it just stops retrying.</summary>
    [Range(1, 50)]
    public int MaxAttempts { get; set; } = 5;
}

/// <summary>Drains one module's outbox. One implementation is registered per module DbContext.</summary>
public interface IOutboxDrainer
{
    string ModuleName { get; }

    Task<int> DrainAsync(CancellationToken ct);
}

/// <summary>
/// Reads a module's pending outbox rows and republishes them on the in-process mediator under the
/// <c>system</c> context.
/// </summary>
/// <remarks>
/// Delivery is at-least-once, so consumers must be idempotent — <c>architecture.md §5</c> says to key them by
/// event id. Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, which is what lets a second instance of the host
/// run without either duplicating work or blocking on the first.
/// </remarks>
public sealed class OutboxDrainer<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDrainer<TContext>> logger) : IOutboxDrainer
    where TContext : ModuleDbContext
{
    public string ModuleName => typeof(TContext).Name;

    public async Task<int> DrainAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        // Background work is not a user request: give the scope the system identity so the RLS interceptor stamps
        // app.roles = 'system' rather than an empty scope that would read nothing.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
        var settings = options.Value;

        // The connection retries on transient failures, and EF refuses to mix that with a hand-rolled transaction
        // unless the whole unit is handed to the strategy. Doing so also means a dropped connection mid-drain
        // replays the entire batch rather than leaving half of it claimed.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async cancellationToken =>
            await DrainBatchAsync(context, publisher, settings, cancellationToken), ct);
    }

    private async Task<int> DrainBatchAsync(
        TContext context,
        IPublisher publisher,
        OutboxOptions settings,
        CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        // The schema is a compile-time constant on the context, never user input; the two values that do vary are
        // real parameters.
        var claimSql = $"""
            select * from "{context.Schema}".outbox_message
            where processed_at is null and attempt_count < @maxAttempts
            order by occurred_at
            limit @batchSize
            for update skip locked
            """;

        var pending = await context.OutboxMessages
            .FromSqlRaw(
                claimSql,
                new NpgsqlParameter("maxAttempts", settings.MaxAttempts),
                new NpgsqlParameter("batchSize", settings.BatchSize))
            // The context tracks nothing by default; these rows are about to be mutated, so opt back in.
            .AsTracking()
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return 0;
        }

        var published = 0;

        foreach (var message in pending)
        {
            message.AttemptCount++;

            try
            {
                var notification = Deserialize(message);

                // ParallelWhenAll: these already committed, so handlers are independent post-commit reactions and
                // one slow consumer should not hold up the rest of the batch.
                await publisher.Publish(notification, PublishStrategy.ParallelWhenAll, ct);

                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.Error = null;
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Error = $"{ex.GetType().Name}: {ex.Message}";

                logger.LogError(
                    ex,
                    "Outbox message {MessageId} of type {MessageType} failed on attempt {AttemptCount} of {MaxAttempts}",
                    message.Id,
                    message.Type,
                    message.AttemptCount,
                    settings.MaxAttempts);
            }
        }

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return published;
    }

    private static IIntegrationEvent Deserialize(OutboxMessage message)
    {
        var type = Type.GetType(message.Type)
                   ?? throw new InvalidOperationException(
                       $"Outbox message {message.Id} names type '{message.Type}', which is not loadable. "
                       + "An integration event was renamed or removed without a migration.");

        return JsonSerializer.Deserialize(message.Payload, type, OutboxWriter.SerializerOptions) as IIntegrationEvent
               ?? throw new InvalidOperationException(
                   $"Outbox message {message.Id} did not deserialize into an {nameof(IIntegrationEvent)}.");
    }
}

/// <summary>Polls every registered module outbox on a fixed interval.</summary>
public sealed class OutboxDrainService(
    IEnumerable<IOutboxDrainer> drainers,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDrainService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var drainerList = drainers.ToArray();

        if (drainerList.Length == 0)
        {
            logger.LogInformation("No module outboxes registered; the drain service has nothing to do");
            return;
        }

        logger.LogInformation(
            "Outbox drain service started for {ModuleCount} module(s), polling every {PollingInterval}",
            drainerList.Length,
            options.Value.PollingInterval);

        using var timer = new PeriodicTimer(options.Value.PollingInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var drainer in drainerList)
            {
                try
                {
                    var started = Stopwatch.GetTimestamp();
                    var published = await drainer.DrainAsync(stoppingToken);

                    if (published > 0)
                    {
                        logger.LogInformation(
                            "Drained {PublishedCount} outbox message(s) from {ModuleName} in {ElapsedMilliseconds:0.##} ms",
                            published,
                            drainer.ModuleName,
                            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Never let one module's failure stop the loop — the next tick retries, and the messages are
                    // still on disk.
                    logger.LogError(ex, "Draining the {ModuleName} outbox failed; will retry on the next tick", drainer.ModuleName);
                }
            }
        }
    }
}
