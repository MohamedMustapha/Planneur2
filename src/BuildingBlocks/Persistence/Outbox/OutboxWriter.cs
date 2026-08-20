using System.Text.Json;
using Cracra.BuildingBlocks.Messaging;

namespace Cracra.BuildingBlocks.Persistence.Outbox;

public static class OutboxWriter
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // The payload is read back by a future deploy, so the shape must not depend on today's defaults.
        WriteIndented = false,
    };

    /// <summary>
    /// Queues an integration event for publication. Call this inside the same unit of work as the state change it
    /// describes and let the single <c>SaveChanges</c> commit both — that atomicity is the entire point of the
    /// outbox, and an event enqueued in its own transaction is just an unreliable message with extra steps.
    /// </summary>
    public static void Enqueue(this ModuleDbContext context, IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var type = integrationEvent.GetType();

        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            Type = type.AssemblyQualifiedName ?? type.FullName ?? type.Name,
            Payload = JsonSerializer.Serialize(integrationEvent, type, SerializerOptions),
            OccurredAt = integrationEvent.OccurredAt,
        });
    }
}
