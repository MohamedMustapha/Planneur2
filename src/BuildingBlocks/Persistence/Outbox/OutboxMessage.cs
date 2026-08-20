using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cracra.BuildingBlocks.Persistence.Outbox;

/// <summary>
/// One integration event, durably queued in the publishing module's own schema. Writing it in the same
/// transaction as the state change is what makes "the fact happened" and "the fact was announced" atomic.
/// </summary>
public sealed class OutboxMessage
{
    public required Guid Id { get; init; }

    /// <summary>Assembly-qualified CLR type, used to rehydrate <see cref="Payload"/> on the way out.</summary>
    public required string Type { get; init; }

    public required string Payload { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int AttemptCount { get; set; }

    /// <summary>Last failure, kept for triage. Cleared on a successful attempt.</summary>
    public string? Error { get; set; }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_message");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Type).HasMaxLength(512).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.OccurredAt).IsRequired();
        builder.Property(message => message.Error).HasMaxLength(4000);

        // The drainer's only query: unprocessed, oldest first. A partial index keeps it O(pending) rather than
        // O(everything ever published), which matters because processed rows are retained for audit.
        builder.HasIndex(message => message.OccurredAt)
            .HasDatabaseName("ix_outbox_message_pending")
            .HasFilter("processed_at is null");
    }
}
