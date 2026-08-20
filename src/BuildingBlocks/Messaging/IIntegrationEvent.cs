using Cracra.BuildingBlocks.Mediator;

namespace Cracra.BuildingBlocks.Messaging;

/// <summary>
/// A fact one module publishes for others to react to. Integration events cross module boundaries; domain events
/// do not. They travel through the publishing module's outbox, so a consumer runs only once the producing
/// transaction has actually committed.
/// </summary>
/// <remarks>
/// Implementations must be serializable to JSON and, once released, effectively immutable: another module is
/// deserializing them, possibly from a row written before the last deploy. Add properties, never remove or
/// repurpose one.
/// </remarks>
public interface IIntegrationEvent : INotification
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}

/// <summary>Convenience base that stamps the identity and time every integration event needs.</summary>
public abstract record IntegrationEvent : IIntegrationEvent
{
    protected IntegrationEvent()
    {
        EventId = Guid.CreateVersion7();
        OccurredAt = DateTimeOffset.UtcNow;
    }

    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}
