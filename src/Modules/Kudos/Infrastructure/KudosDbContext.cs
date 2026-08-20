using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.Modules.Kudos.Application;
using Cracra.Modules.Kudos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Contracts = Cracra.Modules.Kudos.Contracts;

namespace Cracra.Modules.Kudos.Infrastructure;

public sealed class KudosDbContext(DbContextOptions<KudosDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "kudos";

    public DbSet<Kudo> Kudos => Set<Kudo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KudosDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        KudoOutboxDispatcher.DispatchDomainEvents(this);

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }
}

internal sealed class KudoConfiguration : IEntityTypeConfiguration<Kudo>
{
    public void Configure(EntityTypeBuilder<Kudo> builder)
    {
        builder.ToTable("kudo");
        builder.HasKey(kudo => kudo.Id);

        builder.Property(kudo => kudo.Id).ValueGeneratedNever();
        builder.Property(kudo => kudo.Category).HasMaxLength(64).IsRequired();
        builder.Property(kudo => kudo.Message).HasMaxLength(Kudo.MaximumMessageLength).IsRequired();

        builder.Ignore(kudo => kudo.DomainEvents);
        builder.Ignore(kudo => kudo.Month);

        // RLS reads all four of these on every candidate row.
        builder.HasIndex(kudo => kudo.FromPersonId);
        builder.HasIndex(kudo => kudo.ToPersonId);
        builder.HasIndex(kudo => kudo.UnitId);
        builder.HasIndex(kudo => kudo.DepartmentId);

        // The cap's query, and the one that runs on every single write.
        builder.HasIndex(kudo => new { kudo.FromPersonId, kudo.Year, kudo.MonthNumber })
            .HasDatabaseName("ix_kudo_giver_month");

        // The wall and the widget: a scope over a window.
        builder.HasIndex(kudo => kudo.CreatedAt);
    }
}

/// <summary>
/// Turns domain events into outbox rows just before EF writes, in the same transaction as the change.
/// </summary>
internal static class KudoOutboxDispatcher
{
    public static void DispatchDomainEvents(KudosDbContext context)
    {
        var kudos = context.ChangeTracker
            .Entries<Kudo>()
            .Select(entry => entry.Entity)
            .Where(kudo => kudo.DomainEvents.Count > 0)
            .ToArray();

        foreach (var kudo in kudos)
        {
            foreach (var domainEvent in kudo.DomainEvents)
            {
                if (Translate(domainEvent) is { } integrationEvent)
                {
                    context.Enqueue(integrationEvent);
                }
            }

            kudo.ClearDomainEvents();
        }
    }

    private static BuildingBlocks.Messaging.IIntegrationEvent? Translate(object domainEvent) => domainEvent switch
    {
        Domain.KudoGiven given => new Contracts.KudoGiven(
            given.KudoId,
            given.FromPersonId,
            given.ToPersonId,
            given.UnitId,
            given.DepartmentId,
            given.Category,
            given.Points),

        Domain.BadgeAwarded awarded => new Contracts.BadgeAwarded(
            awarded.PersonId,
            awarded.DepartmentId,
            awarded.BadgeCode,
            awarded.PointsAtAward),

        _ => null,
    };
}

/// <summary>
/// The store.
/// </summary>
/// <remarks>
/// No <c>GetAsync</c> and no <c>DeleteAsync</c>, because there is nothing to fetch one for: a kudo is written once
/// and never touched again. The table agrees — it carries an insert policy and no update or delete policy at all,
/// so Postgres would refuse either whatever this class asked for.
/// </remarks>
internal sealed class KudoRepository(KudosDbContext context) : IKudoRepository
{
    public async Task AddAsync(Kudo kudo, CancellationToken ct) => await context.Kudos.AddAsync(kudo, ct);

    public async Task<int> GivenInMonthAsync(Guid giverId, KudoMonth month, CancellationToken ct) =>
        await context.Kudos.CountAsync(
            kudo => kudo.FromPersonId == giverId && kudo.Year == month.Year && kudo.MonthNumber == month.Month,
            ct);

    public async Task<KudoTally> TallyForAsync(Guid personId, CancellationToken ct)
    {
        var rows = await context.Kudos
            .Where(kudo => kudo.ToPersonId == personId)
            .GroupBy(kudo => kudo.Category)
            .Select(group => new
            {
                Category = group.Key,
                Count = group.Count(),
                Points = group.Sum(kudo => kudo.Points),
            })
            .ToListAsync(ct);

        return new KudoTally(
            rows.Sum(row => row.Count),
            rows.Sum(row => row.Points),
            rows.ToDictionary(row => row.Category, row => row.Count, StringComparer.OrdinalIgnoreCase),
            rows.ToDictionary(row => row.Category, row => row.Points, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<KudoRecord>>> RecordsForAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct)
    {
        if (personIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<KudoRecord>>();
        }

        var rows = await context.Kudos
            .Where(kudo => personIds.Contains(kudo.ToPersonId))
            .Select(kudo => new { kudo.ToPersonId, kudo.Category, kudo.Points, kudo.CreatedAt })
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.ToPersonId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<KudoRecord>)
                [
                    .. group.Select(row => new KudoRecord(row.Category, row.Points, row.CreatedAt)),
                ]);
    }
}
