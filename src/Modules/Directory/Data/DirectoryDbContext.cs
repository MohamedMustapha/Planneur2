using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Directory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cracra.Modules.Directory.Data;

public sealed class DirectoryDbContext(DbContextOptions<DirectoryDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "directory";

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<PersonUnit> PersonUnits => Set<PersonUnit>();

    public DbSet<FunctionalRole> FunctionalRoles => Set<FunctionalRole>();

    public DbSet<PersonFunctionalRole> PersonFunctionalRoles => Set<PersonFunctionalRole>();

    public DbSet<DepartmentConfig> DepartmentConfigs => Set<DepartmentConfig>();

    public DbSet<DepartmentConfigAudit> DepartmentConfigAudits => Set<DepartmentConfigAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DirectoryDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("department");
        builder.HasKey(department => department.Id);

        builder.Property(department => department.Code).HasMaxLength(64).IsRequired();
        builder.Property(department => department.NameKey).HasMaxLength(256).IsRequired();

        builder.HasIndex(department => department.Code).IsUnique();

        builder.HasMany(department => department.Units)
            .WithOne(unit => unit.Department)
            .HasForeignKey(unit => unit.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(department => department.Config)
            .WithOne(config => config.Department)
            .HasForeignKey<DepartmentConfig>(config => config.DepartmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("unit");
        builder.HasKey(unit => unit.Id);

        builder.Property(unit => unit.Code).HasMaxLength(64).IsRequired();
        builder.Property(unit => unit.Name).HasMaxLength(256).IsRequired();
        builder.Property(unit => unit.LdapFonction).HasMaxLength(256);
        builder.Property(unit => unit.Kind).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(unit => new { unit.DepartmentId, unit.Code }).IsUnique();

        // The reconciliation key. Unique so two departments cannot claim the same LDAP fonction — if that ever
        // happens the directory is ambiguous and sync should fail loudly rather than pick one.
        builder.HasIndex(unit => unit.LdapFonction)
            .IsUnique()
            .HasFilter("ldap_fonction is not null");
    }
}

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("person");

        // No ValueGeneratedOnAdd: the id is the Keycloak subject, supplied by sync, never generated here.
        builder.Property(person => person.Id).ValueGeneratedNever();
        builder.HasKey(person => person.Id);

        builder.Property(person => person.LdapUid).HasMaxLength(256).IsRequired();
        builder.Property(person => person.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(person => person.Email).HasMaxLength(320);
        builder.Property(person => person.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(person => person.UiLanguage).HasMaxLength(8).IsRequired();

        builder.HasIndex(person => person.LdapUid).IsUnique();
        builder.HasIndex(person => person.PrimaryUnitId);
        builder.HasIndex(person => person.PrimaryDepartmentId);
    }
}

internal sealed class PersonUnitConfiguration : IEntityTypeConfiguration<PersonUnit>
{
    public void Configure(EntityTypeBuilder<PersonUnit> builder)
    {
        builder.ToTable("person_unit");
        builder.HasKey(membership => new { membership.PersonId, membership.UnitId });

        builder.HasOne(membership => membership.Person)
            .WithMany(person => person.Units)
            .HasForeignKey(membership => membership.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(membership => membership.Unit)
            .WithMany()
            .HasForeignKey(membership => membership.UnitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(membership => membership.UnitId);
    }
}

internal sealed class FunctionalRoleConfiguration : IEntityTypeConfiguration<FunctionalRole>
{
    public void Configure(EntityTypeBuilder<FunctionalRole> builder)
    {
        builder.ToTable("functional_role");
        builder.HasKey(role => role.Id);

        builder.Property(role => role.Code).HasMaxLength(64).IsRequired();
        builder.Property(role => role.LabelKey).HasMaxLength(256).IsRequired();

        // A code is unique within a department, and the shared seeded roles (department_id null) form their own
        // namespace — so a department may define "dev" differently without colliding with the global one.
        builder.HasIndex(role => new { role.DepartmentId, role.Code }).IsUnique();
    }
}

internal sealed class PersonFunctionalRoleConfiguration : IEntityTypeConfiguration<PersonFunctionalRole>
{
    public void Configure(EntityTypeBuilder<PersonFunctionalRole> builder)
    {
        builder.ToTable("person_functional_role");
        builder.HasKey(assignment => new { assignment.PersonId, assignment.FunctionalRoleId, assignment.UnitId });

        builder.HasOne(assignment => assignment.Person)
            .WithMany(person => person.FunctionalRoles)
            .HasForeignKey(assignment => assignment.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(assignment => assignment.FunctionalRole)
            .WithMany()
            .HasForeignKey(assignment => assignment.FunctionalRoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DepartmentConfigConfiguration : IEntityTypeConfiguration<DepartmentConfig>
{
    public void Configure(EntityTypeBuilder<DepartmentConfig> builder)
    {
        builder.ToTable("department_config");
        builder.HasKey(config => config.Id);

        // jsonb rather than text: these are queried (S5 reads the taxonomy) and Postgres can index into jsonb.
        builder.Property(config => config.ActivityTaxonomyJson).HasColumnType("jsonb").IsRequired();
        builder.Property(config => config.RoleLabelsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(config => config.KudoRulesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(config => config.IterationPresetsJson).HasColumnType("jsonb").IsRequired();

        builder.Property(config => config.DefaultBoardLayout).HasMaxLength(64).IsRequired();
        builder.Property(config => config.WeeklyTargetHours).HasPrecision(5, 2);
        builder.Property(config => config.ModifiedBy).HasMaxLength(256);

        builder.HasIndex(config => config.DepartmentId).IsUnique();
    }
}

internal sealed class DepartmentConfigAuditConfiguration : IEntityTypeConfiguration<DepartmentConfigAudit>
{
    public void Configure(EntityTypeBuilder<DepartmentConfigAudit> builder)
    {
        builder.ToTable("department_config_audit");
        builder.HasKey(audit => audit.Id);

        builder.Property(audit => audit.SnapshotJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(audit => new { audit.DepartmentId, audit.Version });
    }
}
