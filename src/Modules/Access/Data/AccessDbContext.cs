using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Access.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cracra.Modules.Access.Data;

/// <summary>
/// The Access module's schema — the same <c>access</c> schema the platform bootstrap creates the predicate
/// functions in. Tables and the functions that read them belong together: a predicate is only as trustworthy as
/// the rows it consults, and splitting them would put a schema boundary through the middle of one guarantee.
/// </summary>
public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "access";

    public DbSet<ContextualRoleAssignment> RoleAssignments => Set<ContextualRoleAssignment>();

    public DbSet<RbacOverride> Overrides => Set<RbacOverride>();

    public DbSet<RbacOverrideAudit> OverrideAudits => Set<RbacOverrideAudit>();

    public DbSet<ProjectMembership> ProjectMemberships => Set<ProjectMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccessDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class ContextualRoleAssignmentConfiguration : IEntityTypeConfiguration<ContextualRoleAssignment>
{
    public void Configure(EntityTypeBuilder<ContextualRoleAssignment> builder)
    {
        builder.ToTable("contextual_role_assignment");
        builder.HasKey(assignment => assignment.Id);

        builder.Property(assignment => assignment.Role).HasMaxLength(64).IsRequired();
        builder.Property(assignment => assignment.ScopeType).HasConversion<string>().HasMaxLength(32);
        builder.Property(assignment => assignment.Source).HasConversion<string>().HasMaxLength(32);

        // Resolution reads every assignment for one person on every request that misses the cache, so this index
        // is on the hottest path in the system.
        builder.HasIndex(assignment => assignment.PersonId);

        // The same role, scope and source twice is meaningless. Unique so a sync bug shows up as a failed write
        // rather than as quietly duplicated rows that make an audit read wrong.
        builder.HasIndex(assignment => new
        {
            assignment.PersonId,
            assignment.Role,
            assignment.ScopeType,
            assignment.ScopeId,
            assignment.Source,
        }).IsUnique();
    }
}

internal sealed class RbacOverrideConfiguration : IEntityTypeConfiguration<RbacOverride>
{
    public void Configure(EntityTypeBuilder<RbacOverride> builder)
    {
        builder.ToTable("rbac_override");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Role).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ScopeType).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.Reason).HasMaxLength(1000).IsRequired();

        builder.HasIndex(item => item.PersonId);

        // Resolution only ever wants the live ones; a partial index keeps that lookup proportional to the
        // overrides in force rather than to every override ever written.
        builder.HasIndex(item => new { item.PersonId, item.ExpiresAt })
            .HasDatabaseName("ix_rbac_override_active")
            .HasFilter("revoked_at is null");
    }
}

internal sealed class RbacOverrideAuditConfiguration : IEntityTypeConfiguration<RbacOverrideAudit>
{
    public void Configure(EntityTypeBuilder<RbacOverrideAudit> builder)
    {
        builder.ToTable("rbac_override_audit");
        builder.HasKey(audit => audit.Id);

        builder.Property(audit => audit.Action).HasMaxLength(32).IsRequired();
        builder.Property(audit => audit.SnapshotJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(audit => audit.PersonId);
        builder.HasIndex(audit => audit.OverrideId);
    }
}

internal sealed class ProjectMembershipConfiguration : IEntityTypeConfiguration<ProjectMembership>
{
    public void Configure(EntityTypeBuilder<ProjectMembership> builder)
    {
        builder.ToTable("project_membership");
        builder.HasKey(membership => new { membership.PersonId, membership.ProjectId });

        // access.on_project() looks up by person; access.project_in_my_depts() by project. Both are called from
        // inside RLS predicates, which means once per row scanned — they need to be index lookups, not scans.
        builder.HasIndex(membership => membership.ProjectId);
        builder.HasIndex(membership => membership.DepartmentId);
    }
}
