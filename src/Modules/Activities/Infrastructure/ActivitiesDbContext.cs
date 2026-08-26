using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.Modules.Activities.Application;
using Cracra.Modules.Activities.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Contracts = Cracra.Modules.Activities.Contracts;

namespace Cracra.Modules.Activities.Infrastructure;

public sealed class ActivitiesDbContext(DbContextOptions<ActivitiesDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "activities";

    public DbSet<ActivityEntry> Entries => Set<ActivityEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ActivitiesDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        ActivityOutboxDispatcher.DispatchDomainEvents(this);

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }
}

internal sealed class ActivityEntryConfiguration : IEntityTypeConfiguration<ActivityEntry>
{
    public void Configure(EntityTypeBuilder<ActivityEntry> builder)
    {
        builder.ToTable("activity_entry");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.ActivityTypeCode).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(entry => entry.Source).HasConversion<string>().HasMaxLength(24);
        builder.Property(entry => entry.ExternalRef).HasMaxLength(256);
        builder.Property(entry => entry.Note).HasMaxLength(2000);
        builder.Property(entry => entry.Hours).HasPrecision(6, 2);

        builder.Property(entry => entry.NodeId).ValueGeneratedOnAddOrUpdate();
        builder.Property(entry => entry.NodeAncestorIds)
            .HasColumnType("uuid[]")
            .ValueGeneratedOnAddOrUpdate()
            .IsRequired();

        builder.Ignore(entry => entry.DomainEvents);
        builder.Ignore(entry => entry.Slot);
        builder.Ignore(entry => entry.Week);

        // RLS reads owner, unit, project and department on every candidate row, so all four are indexed.
        builder.HasIndex(entry => entry.PersonId);
        builder.HasIndex(entry => entry.UnitId);
        builder.HasIndex(entry => entry.DepartmentId);
        builder.HasIndex(entry => entry.ProjectId);

        // The guardrail's query, and the one that runs on every single write.
        builder.HasIndex(entry => new { entry.PersonId, entry.IsoYear, entry.IsoWeekNumber })
            .HasDatabaseName("ix_activity_entry_person_week");

        // The feed's query: a date range within a scope.
        builder.HasIndex(entry => new { entry.SlotStart, entry.SlotEnd });

        builder.HasIndex(entry => entry.SupersedesEntryId);

        // A pulled task can be logged against more than once — an hour on Monday and two on Tuesday against the
        // same ticket is normal — so this is not unique.
        builder.HasIndex(entry => entry.ExternalRef);
    }
}

/// <summary>
/// Turns domain events into outbox rows just before EF writes, in the same transaction as the change.
/// </summary>
internal static class ActivityOutboxDispatcher
{
    public static void DispatchDomainEvents(ActivitiesDbContext context)
    {
        var entries = context.ChangeTracker
            .Entries<ActivityEntry>()
            .Select(entry => entry.Entity)
            .Where(entry => entry.DomainEvents.Count > 0)
            .ToArray();

        foreach (var entry in entries)
        {
            foreach (var domainEvent in entry.DomainEvents)
            {
                if (Translate(domainEvent) is { } integrationEvent)
                {
                    context.Enqueue(integrationEvent);
                }
            }

            entry.ClearDomainEvents();
        }
    }

    private static BuildingBlocks.Messaging.IIntegrationEvent? Translate(object domainEvent) => domainEvent switch
    {
        Domain.ActivityLogged logged => new Contracts.ActivityLogged(
            logged.EntryId,
            logged.PersonId,
            logged.ActivityTypeCode,
            logged.ProjectId,
            logged.Kind.ToString().ToLowerInvariant(),
            logged.Hours,
            logged.Week.Year,
            logged.Week.Week),

        Domain.ActivityReconciled reconciled => new Contracts.ActivityReconciled(
            reconciled.ActualEntryId,
            reconciled.PlannedEntryId,
            reconciled.PersonId,
            reconciled.PlannedHours,
            reconciled.ActualHours,
            reconciled.PlannedTypeCode,
            reconciled.ActualTypeCode),

        _ => null,
    };
}

internal sealed class ActivityRepository(ActivitiesDbContext context) : IActivityRepository
{
    public async Task<ActivityEntry> GetAsync(Guid entryId, CancellationToken ct) =>
        await context.Entries
            .AsTracking()
            .SingleOrDefaultAsync(entry => entry.Id == entryId, ct)
            // RLS filtered it or it does not exist; the caller cannot tell the two apart, by design.
            ?? throw new ResourceNotFoundException($"No activity entry {entryId}.");

    public async Task AddAsync(ActivityEntry entry, CancellationToken ct) => await context.Entries.AddAsync(entry, ct);

    public async Task DeleteAsync(ActivityEntry entry, CancellationToken ct)
    {
        // ExecuteDelete rather than the change tracker, so the row count comes back where it can be acted on. The
        // tracker route would surface the same refusal as a DbUpdateConcurrencyException, which is indistinguish-
        // able from a genuine concurrency conflict and would be reported as one.
        var deleted = await context.Entries
            .Where(candidate => candidate.Id == entry.Id)
            .ExecuteDeleteAsync(ct);

        if (deleted == 0)
        {
            throw new UnauthorizedAccessException("You may not delete that activity entry.");
        }

        // The tracked instance would otherwise be written again by the same SaveChanges.
        context.Entry(entry).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
    }

    public async Task<decimal> RecordedHoursAsync(
        Guid personId,
        IsoWeek week,
        Guid? excludingEntryId,
        CancellationToken ct)
    {
        var query = context.Entries
            .Where(entry => entry.PersonId == personId
                && entry.IsoYear == week.Year
                && entry.IsoWeekNumber == week.Week
                && entry.Kind == ActivityKind.Actual);

        if (excludingEntryId is { } excluded)
        {
            query = query.Where(entry => entry.Id != excluded);
        }

        // SumAsync over an empty set returns 0 for a non-nullable decimal, which is the answer we want for
        // somebody's first entry of the week.
        return await query.SumAsync(entry => entry.Hours, ct);
    }

    public async Task<ActivityEntry?> FindOpenPlanAsync(Guid personId, TimeSlot slot, CancellationToken ct) =>
        await context.Entries
            .AsTracking()
            .Where(entry => entry.PersonId == personId
                && entry.Kind == ActivityKind.Planned
                && !entry.Reconciled
                // Overlap, expressed the way an index can use it: the plan starts before this slot ends and ends
                // after it starts.
                && entry.SlotStart < slot.End
                && entry.SlotEnd > slot.Start)
            .OrderBy(entry => entry.SlotStart)
            .FirstOrDefaultAsync(ct);
}
