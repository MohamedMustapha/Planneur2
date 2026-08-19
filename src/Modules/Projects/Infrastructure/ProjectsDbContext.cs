using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.Modules.Projects.Application;
using Cracra.Modules.Projects.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Contracts = Cracra.Modules.Projects.Contracts;

namespace Cracra.Modules.Projects.Infrastructure;

public sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "projects";

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectDepartment> ProjectDepartments => Set<ProjectDepartment>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProjectsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Drains the aggregates' domain events into the outbox immediately before writing.
    /// </summary>
    /// <remarks>
    /// Here rather than in each handler: a handler that forgot would produce a change nobody downstream ever hears
    /// about, and that failure stays invisible until a report is quietly wrong weeks later. Because it runs inside
    /// SaveChanges, the outbox rows land in the same transaction as the change they describe.
    /// </remarks>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        ProjectOutboxDispatcher.DispatchDomainEvents(this);

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }
}

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("project");
        builder.HasKey(project => project.Id);

        builder.Property(project => project.Id).ValueGeneratedNever();
        builder.Property(project => project.Code).HasMaxLength(64).IsRequired();
        builder.Property(project => project.Name).HasMaxLength(256).IsRequired();
        builder.Property(project => project.Description).HasMaxLength(4000);
        builder.Property(project => project.Classification).HasConversion<string>().HasMaxLength(16);
        builder.Property(project => project.CostAmount).HasPrecision(18, 2);
        builder.Property(project => project.CostCurrency).HasMaxLength(3).IsRequired();
        builder.Property(project => project.CostNotes).HasMaxLength(2000);

        builder.HasIndex(project => project.Code).IsUnique();

        // RLS compares both on every row read, so both are indexed.
        builder.HasIndex(project => project.LeadDepartmentId);
        builder.HasIndex(project => project.OwnerPersonId);

        // Domain events live in memory between the aggregate raising them and the repository draining them; they
        // are never a column.
        builder.Ignore(project => project.DomainEvents);
        builder.Ignore(project => project.Cost);

        builder.HasMany(project => project.Departments)
            .WithOne()
            .HasForeignKey(department => department.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(project => project.Members)
            .WithOne()
            .HasForeignKey(member => member.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // The aggregate owns its collections; EF reads the backing fields rather than going through the read-only
        // properties, which is what lets the invariants stay enforced by the methods.
        builder.Navigation(project => project.Departments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(project => project.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProjectDepartmentConfiguration : IEntityTypeConfiguration<ProjectDepartment>
{
    public void Configure(EntityTypeBuilder<ProjectDepartment> builder)
    {
        builder.ToTable("project_department");
        builder.HasKey(link => new { link.ProjectId, link.DepartmentId });

        // access.project_in_my_depts() looks up by department inside an RLS predicate — once per candidate row.
        builder.HasIndex(link => link.DepartmentId);
    }
}

internal sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_member");

        // Person plus start date, not person alone: someone can leave a project and rejoin it later, and both
        // memberships are real history.
        builder.HasKey(member => new { member.ProjectId, member.PersonId, member.From });

        builder.Ignore(member => member.Period);
        builder.Ignore(member => member.IsActive);

        builder.HasIndex(member => member.PersonId);
        builder.HasIndex(member => member.DepartmentId);

        builder.HasIndex(member => new { member.ProjectId, member.PersonId })
            .HasDatabaseName("ix_project_member_active")
            .HasFilter("\"to\" is null");
    }
}

/// <summary>
/// Loads and saves the aggregate, and converts its domain events into outbox rows on the way out.
/// </summary>
/// <remarks>
/// The conversion happens here rather than in the handlers so no handler can forget it. Domain events are internal
/// to the module; what crosses the boundary is the Contracts version, written to the outbox inside the same
/// transaction as the change — which is the whole point of the outbox.
/// </remarks>
internal sealed class ProjectRepository(ProjectsDbContext context) : IProjectRepository
{
    public async Task<Project> GetAsync(Guid projectId, CancellationToken ct)
    {
        var project = await context.Projects
            .Include(candidate => candidate.Departments)
            .Include(candidate => candidate.Members)
            .AsSplitQuery()
            .AsTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == projectId, ct)
            // RLS filtered it or it does not exist; the caller cannot tell, by design.
            ?? throw new ResourceNotFoundException($"No project {projectId}.");

        return project;
    }

    public async Task AddAsync(Project project, CancellationToken ct)
    {
        await context.Projects.AddAsync(project, ct);
    }

    public async Task<bool> CodeExistsAsync(string code, CancellationToken ct) =>
        await context.Projects.AnyAsync(project => project.Code == code, ct);
}

/// <summary>
/// Drains domain events into the outbox just before EF writes.
/// </summary>
/// <remarks>
/// A SaveChanges interceptor rather than something a handler calls: a handler that forgot would produce a change
/// nobody downstream ever hears about, and the failure is invisible until a report is quietly wrong weeks later.
/// </remarks>
internal static class ProjectOutboxDispatcher
{
    public static void DispatchDomainEvents(ProjectsDbContext context)
    {
        var aggregates = context.ChangeTracker
            .Entries<Project>()
            .Select(entry => entry.Entity)
            .Where(project => project.DomainEvents.Count > 0)
            .ToArray();

        foreach (var project in aggregates)
        {
            foreach (var domainEvent in project.DomainEvents)
            {
                if (Translate(domainEvent) is { } integrationEvent)
                {
                    context.Enqueue(integrationEvent);
                }
            }

            project.ClearDomainEvents();
        }
    }

    /// <summary>
    /// Not every domain event crosses the boundary. Ones that are only meaningful inside the module stay inside
    /// it, rather than becoming public contract nobody consumes but everybody must keep compatible.
    /// </summary>
    private static BuildingBlocks.Messaging.IIntegrationEvent? Translate(object domainEvent) => domainEvent switch
    {
        Domain.ProjectCreated created =>
            new Contracts.ProjectCreated(created.ProjectId, created.Code, created.Name, created.LeadDepartmentId),

        Domain.ProjectMemberAdded added =>
            new Contracts.ProjectMemberChanged(added.ProjectId, added.PersonId, added.DepartmentId, Added: true),

        Domain.ProjectMemberRemoved removed =>
            new Contracts.ProjectMemberChanged(removed.ProjectId, removed.PersonId, removed.DepartmentId, Added: false),

        Domain.ProjectDepartmentsChanged changed =>
            new Contracts.ProjectDepartmentsChanged(changed.ProjectId, changed.DepartmentIds),

        _ => null,
    };
}
