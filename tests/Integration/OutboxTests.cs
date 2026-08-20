using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Messaging;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Tests.Integration;

/// <summary>An integration event that exists only for these tests.</summary>
public sealed record UnitRenamed(Guid UnitId, string Name) : IntegrationEvent;

public sealed class UnitRenamedRecorder(RecordedEvents recorded) : INotificationHandler<UnitRenamed>
{
    public Task Handle(UnitRenamed notification, CancellationToken ct)
    {
        recorded.Names.Add(notification.Name);

        return Task.CompletedTask;
    }
}

public sealed class RecordedEvents
{
    public List<string> Names { get; } = [];
}

/// <summary>
/// The outbox is what makes "the state changed" and "the other modules were told" the same transaction. These
/// tests drive the drainer directly rather than waiting on its timer, so they assert behaviour instead of timing.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OutboxTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_queued_event_is_published_and_marked_processed()
    {
        var recorded = new RecordedEvents();

        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
            ConfigureAdditionalServices = services =>
            {
                services.AddSingleton(recorded);
                services.AddScoped<INotificationHandler<UnitRenamed>, UnitRenamedRecorder>();
            },
        };

        await ClearOutboxAsync(factory);

        var queued = new UnitRenamed(SeedOrganisation.Units.Infrastructure, "Infrastructure & Réseaux");

        await EnqueueAsync(factory, queued);

        var published = await DrainAsync(factory);

        published.ShouldBe(1);
        recorded.Names.ShouldBe([queued.Name]);

        var message = await SingleMessageAsync(factory, queued.EventId);
        message.ProcessedAt.ShouldNotBeNull();
        message.AttemptCount.ShouldBe(1);
        message.Error.ShouldBeNull();
    }

    [Fact]
    public async Task A_processed_event_is_not_published_twice()
    {
        var recorded = new RecordedEvents();

        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
            ConfigureAdditionalServices = services =>
            {
                services.AddSingleton(recorded);
                services.AddScoped<INotificationHandler<UnitRenamed>, UnitRenamedRecorder>();
            },
        };

        await ClearOutboxAsync(factory);

        await EnqueueAsync(factory, new UnitRenamed(SeedOrganisation.Units.Development, "Études & Développement"));

        await DrainAsync(factory);
        var second = await DrainAsync(factory);

        // Delivery is at-least-once by design, but a drain that re-published everything on every tick would make
        // "at least once" mean "forever".
        second.ShouldBe(0);
        recorded.Names.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_handler_leaves_the_message_queued_with_its_error_recorded()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString)
        {
            ConfigureAdditionalServices = services =>
                services.AddScoped<INotificationHandler<UnitRenamed>, FailingRecorder>(),
        };

        await ClearOutboxAsync(factory);

        var queued = new UnitRenamed(SeedOrganisation.Units.Accounting, "Comptabilité");

        await EnqueueAsync(factory, queued);

        var published = await DrainAsync(factory);

        published.ShouldBe(0);

        var message = await SingleMessageAsync(factory, queued.EventId);

        // Still unprocessed, so the next tick retries — and the error is on the row, so a human can see why
        // without reading logs from three days ago.
        message.ProcessedAt.ShouldBeNull();
        message.AttemptCount.ShouldBe(1);
        message.Error.ShouldNotBeNull();
    }

    /// <summary>
    /// Empties the queue so one scenario cannot observe another's rows.
    /// </summary>
    /// <remarks>
    /// The Postgres container is shared across the assembly for speed, and the drainer's whole job is "process
    /// everything pending" — so a message a previous test deliberately left unprocessed would be picked up here
    /// and counted. Resetting is cheaper and clearer than making every assertion filter by event id.
    /// </remarks>
    private static async Task ClearOutboxAsync(CracraApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        await context.OutboxMessages.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    private static async Task EnqueueAsync(CracraApplicationFactory factory, IIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();

        // Enqueuing happens inside a user's transaction, so the writing session is a user session — which is why
        // the insert policy accepts any scoped session while read and update stay system-only.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = SeedOrganisation.Thomas;

        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        context.Enqueue(integrationEvent);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<int> DrainAsync(CracraApplicationFactory factory)
    {
        var drainer = factory.Services.GetServices<IOutboxDrainer>()
            .Single(candidate => candidate.ModuleName == nameof(PlatformDbContext));

        return await drainer.DrainAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<OutboxMessage> SingleMessageAsync(CracraApplicationFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();

        // Reading the queue back requires the system context; a user session is denied by policy, which is
        // exactly what AccessSchemaTests asserts.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        return await context.OutboxMessages.SingleAsync(
            message => message.Id == id,
            TestContext.Current.CancellationToken);
    }
}

internal sealed class FailingRecorder : INotificationHandler<UnitRenamed>
{
    public Task Handle(UnitRenamed notification, CancellationToken ct) =>
        throw new InvalidOperationException("the consumer is unavailable");
}
