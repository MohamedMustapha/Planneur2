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

    public DbSet<NodeProfile> NodeProfiles => Set<NodeProfile>();

    public DbSet<OrgLevel> OrgLevels => Set<OrgLevel>();

    public DbSet<OrgNode> OrgNodes => Set<OrgNode>();

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

        // Restrict, not cascade: deleting a profile that branches still point at must fail loudly. Silently
        // nulling the pointer would re-parent a whole branch's behaviour to whatever its ancestor happens to say,
        // which is the kind of change nobody notices until a board renders the wrong archetype.
        builder.HasOne<NodeProfile>()
            .WithMany()
            .HasForeignKey(department => department.ProfileId)
            .OnDelete(DeleteBehavior.Restrict);

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

        builder.HasOne<NodeProfile>()
            .WithMany()
            .HasForeignKey(unit => unit.ProfileId)
            .OnDelete(DeleteBehavior.Restrict);

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

        // Nullable on purpose: null is "never chosen", which is what lets the synced values above act as seeds.
        builder.Property(person => person.PreferredLanguage).HasMaxLength(8);
        builder.Property(person => person.PreferredTimeZone).HasMaxLength(64);
        builder.Property(person => person.PreferredTheme).HasMaxLength(16);

        builder.Property(person => person.NodeAncestorIds)
            .HasColumnType("uuid[]")
            .HasDefaultValueSql("'{}'::uuid[]")
            .ValueGeneratedOnAddOrUpdate()
            .IsRequired();

        builder.HasIndex(person => person.LdapUid).IsUnique();
        builder.HasIndex(person => person.HomeNodeId);
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
        builder.Property(config => config.ShiftTemplatesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(config => config.WorkingDayJson).HasColumnType("jsonb").IsRequired();
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

internal sealed class NodeProfileConfiguration : IEntityTypeConfiguration<NodeProfile>
{
    public void Configure(EntityTypeBuilder<NodeProfile> builder)
    {
        builder.ToTable("node_profile");
        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Code).HasMaxLength(64).IsRequired();
        builder.Property(profile => profile.LabelKey).HasMaxLength(256).IsRequired();
        builder.Property(profile => profile.HeadlinePattern).HasMaxLength(512);
        builder.Property(profile => profile.ModifiedBy).HasMaxLength(256);

        // jsonb for the same reason the department config uses it: these are read whole but Postgres can still
        // index into them, and a text column would make the taxonomy unqueryable the day a report needs it.
        builder.Property(profile => profile.ActivityTaxonomyJson).HasColumnType("jsonb");
        builder.Property(profile => profile.CapabilitiesJson).HasColumnType("jsonb");
        builder.Property(profile => profile.BudgetDefaultsJson).HasColumnType("jsonb");

        builder.HasIndex(profile => profile.Code).IsUnique();
    }
}

internal sealed class OrgLevelConfiguration : IEntityTypeConfiguration<OrgLevel>
{
    public void Configure(EntityTypeBuilder<OrgLevel> builder)
    {
        builder.ToTable("org_level", t => t.HasCheckConstraint("ck_org_level_level_no", "level_no between 1 and 8"));
        builder.HasKey(level => level.LevelNo);

        builder.Property(level => level.LevelNo).ValueGeneratedNever();
        builder.Property(level => level.Code).HasMaxLength(64).IsRequired();
        builder.Property(level => level.LabelKey).HasMaxLength(256).IsRequired();
        builder.Property(level => level.LabelPluralKey).HasMaxLength(256).IsRequired();
        builder.Property(level => level.HeadLabelKey).HasMaxLength(256).IsRequired();

        builder.HasIndex(level => level.Code).IsUnique();
    }
}

internal sealed class OrgNodeConfiguration : IEntityTypeConfiguration<OrgNode>
{
    public void Configure(EntityTypeBuilder<OrgNode> builder)
    {
        builder.ToTable("org_node");
        builder.HasKey(node => node.Id);

        builder.Property(node => node.Id).ValueGeneratedNever();
        builder.Property(node => node.Code).HasMaxLength(64).IsRequired();
        builder.Property(node => node.Name).HasMaxLength(256).IsRequired();

        builder.Property(node => node.AncestorIds)
            .HasColumnType("uuid[]")
            .HasDefaultValueSql("'{}'::uuid[]")
            .ValueGeneratedOnAddOrUpdate()
            .IsRequired();

        builder.HasOne<OrgNode>()
            .WithMany()
            .HasForeignKey(node => node.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OrgLevel>()
            .WithMany()
            .HasForeignKey(node => node.LevelNo)
            .HasPrincipalKey(level => level.LevelNo)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<NodeProfile>()
            .WithMany()
            .HasForeignKey(node => node.ProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(node => new { node.ParentId, node.Code }).IsUnique();
        builder.HasIndex(node => new { node.ParentId, node.LevelNo });
        builder.HasIndex(node => node.ProfileId);
        builder.HasIndex(node => node.HeadPersonId);
    }
}
