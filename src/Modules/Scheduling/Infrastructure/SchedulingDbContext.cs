using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.Modules.Scheduling.Application;
using Cracra.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Contracts = Cracra.Modules.Scheduling.Contracts;

namespace Cracra.Modules.Scheduling.Infrastructure;

/// <summary>
/// The only state this module owns.
/// </summary>
/// <remarks>
/// Two small tables against four modules' worth of rendered data — which is the shape the spec predicts. Boards
/// are projections over other people's rows; work orders and shifts are the two things that genuinely belong to
/// scheduling and to nothing else.
/// </remarks>
public sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "scheduling";

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    public DbSet<Shift> Shifts => Set<Shift>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulingDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        SchedulingOutboxDispatcher.DispatchDomainEvents(this);

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }
}

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_order");
        builder.HasKey(order => order.Id);

        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.Reference).HasMaxLength(64).IsRequired();
        builder.Property(order => order.Title).HasMaxLength(256).IsRequired();
        builder.Property(order => order.Description).HasMaxLength(2000);
        builder.Property(order => order.Source).HasMaxLength(32).IsRequired();
        builder.Property(order => order.ActivityTypeCode).HasMaxLength(64).IsRequired();
        builder.Property(order => order.ExternalRef).HasMaxLength(256);
        builder.Property(order => order.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(order => order.EstimatedHours).HasPrecision(6, 2);

        builder.Ignore(order => order.DomainEvents);
        builder.Ignore(order => order.IsOpen);

        // RLS scopes the pool by unit and department; the board queries by unit on every render.
        builder.HasIndex(order => order.UnitId);
        builder.HasIndex(order => order.DepartmentId);
        builder.HasIndex(order => order.AssignedToPersonId);

        // One local shadow per external item, so a repeated pull cannot duplicate the pool. Unique rather than
        // merely indexed: the handler checks first, but under two leads refreshing at once only the constraint
        // actually decides.
        builder.HasIndex(order => new { order.Source, order.ExternalRef })
            .IsUnique()
            .HasFilter("external_ref is not null");
    }
}

internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("shift");
        builder.HasKey(shift => shift.Id);

        builder.Property(shift => shift.Id).ValueGeneratedNever();
        builder.Property(shift => shift.TemplateCode).HasMaxLength(32).IsRequired();
        builder.Property(shift => shift.Hours).HasPrecision(5, 2);

        builder.Ignore(shift => shift.DomainEvents);

        builder.HasIndex(shift => shift.PersonId);
        builder.HasIndex(shift => shift.DepartmentId);

        // The scheduler's own query: one unit's roster across a week.
        builder.HasIndex(shift => new { shift.UnitId, shift.Day });

        // One person, one slot, one day. The aggregate refuses an overlap on the evidence it was handed; this is
        // what holds when two leads roster the same person at the same moment.
        builder.HasIndex(shift => new { shift.PersonId, shift.Day, shift.TemplateCode }).IsUnique();
    }
}

internal static class SchedulingOutboxDispatcher
{
    public static void DispatchDomainEvents(SchedulingDbContext context)
    {
        foreach (var order in context.ChangeTracker.Entries<WorkOrder>()
                     .Select(entry => entry.Entity)
                     .Where(order => order.DomainEvents.Count > 0)
                     .ToArray())
        {
            foreach (var domainEvent in order.DomainEvents)
            {
                if (Translate(domainEvent) is { } integrationEvent)
                {
                    context.Enqueue(integrationEvent);
                }
            }

            order.ClearDomainEvents();
        }

        foreach (var shift in context.ChangeTracker.Entries<Shift>()
                     .Select(entry => entry.Entity)
                     .Where(shift => shift.DomainEvents.Count > 0)
                     .ToArray())
        {
            foreach (var domainEvent in shift.DomainEvents)
            {
                if (Translate(domainEvent) is { } integrationEvent)
                {
                    context.Enqueue(integrationEvent);
                }
            }

            shift.ClearDomainEvents();
        }
    }

    private static BuildingBlocks.Messaging.IIntegrationEvent? Translate(object domainEvent) => domainEvent switch
    {
        Domain.WorkOrderAssigned assigned =>
            new Contracts.WorkOrderAssigned(assigned.WorkOrderId, assigned.PersonId, assigned.ActivityEntryId),

        Domain.WorkOrderUnassigned unassigned =>
            new Contracts.WorkOrderUnassigned(unassigned.WorkOrderId, unassigned.PreviousPersonId),

        Domain.ShiftPlanned planned =>
            new Contracts.ShiftPlanned(planned.ShiftId, planned.PersonId, planned.Day, planned.TemplateCode),

        _ => null,
    };
}

internal sealed class WorkOrderRepository(SchedulingDbContext context) : IWorkOrderRepository
{
    public async Task<WorkOrder> GetAsync(Guid workOrderId, CancellationToken ct) =>
        await context.WorkOrders
            .AsTracking()
            .SingleOrDefaultAsync(order => order.Id == workOrderId, ct)
            // RLS filtered it or it does not exist; indistinguishable by design.
            ?? throw new ResourceNotFoundException($"No work order {workOrderId}.");

    public async Task AddAsync(WorkOrder workOrder, CancellationToken ct) =>
        await context.WorkOrders.AddAsync(workOrder, ct);

    public async Task<IReadOnlyList<WorkOrder>> GetForUnitAsync(Guid unitId, CancellationToken ct) =>
        await context.WorkOrders
            .Where(order => order.UnitId == unitId
                && (order.State == WorkOrderState.Unassigned || order.State == WorkOrderState.Assigned))
            .OrderBy(order => order.Reference)
            .ToListAsync(ct);

    public async Task<bool> ExistsForExternalRefAsync(string source, string externalRef, CancellationToken ct) =>
        await context.WorkOrders.AnyAsync(
            order => order.Source == source && order.ExternalRef == externalRef,
            ct);
}

internal sealed class ShiftRepository(SchedulingDbContext context) : IShiftRepository
{
    public async Task<Shift> GetAsync(Guid shiftId, CancellationToken ct) =>
        await context.Shifts
            .AsTracking()
            .SingleOrDefaultAsync(shift => shift.Id == shiftId, ct)
            ?? throw new ResourceNotFoundException($"No shift {shiftId}.");

    public async Task AddAsync(Shift shift, CancellationToken ct) => await context.Shifts.AddAsync(shift, ct);

    public async Task DeleteAsync(Shift shift, CancellationToken ct)
    {
        // ExecuteDelete for the row count, as in Activities: Postgres filters a policy-refused DELETE rather than
        // raising, so a refusal is only visible as nothing having happened.
        var deleted = await context.Shifts
            .Where(candidate => candidate.Id == shift.Id)
            .ExecuteDeleteAsync(ct);

        if (deleted == 0)
        {
            throw new UnauthorizedAccessException("You may not remove that shift.");
        }

        context.Entry(shift).State = EntityState.Detached;
    }

    public async Task<IReadOnlyList<Shift>> GetForUnitAsync(
        Guid unitId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        await context.Shifts
            .Where(shift => shift.UnitId == unitId && shift.Day >= from && shift.Day <= to)
            .OrderBy(shift => shift.Day)
            .ThenBy(shift => shift.Start)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Shift>> GetForPersonAsync(
        Guid personId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct) =>
        await context.Shifts
            .AsTracking()
            .Where(shift => shift.PersonId == personId && shift.Day >= from && shift.Day <= to)
            .ToListAsync(ct);
}
