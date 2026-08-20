using Cracra.BuildingBlocks.Persistence;
using Cracra.Modules.Meetings.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cracra.Modules.Meetings.Data;

public sealed class MeetingsDbContext(DbContextOptions<MeetingsDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "meetings";

    public DbSet<MeetingSeries> Series => Set<MeetingSeries>();

    public DbSet<MeetingOccurrence> Occurrences => Set<MeetingOccurrence>();

    public DbSet<MeetingAttendance> Attendance => Set<MeetingAttendance>();

    public DbSet<SpecialDay> SpecialDays => Set<SpecialDay>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MeetingsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class MeetingSeriesConfiguration : IEntityTypeConfiguration<MeetingSeries>
{
    public void Configure(EntityTypeBuilder<MeetingSeries> builder)
    {
        builder.ToTable("meeting_series");
        builder.HasKey(series => series.Id);

        // Codes rather than enums, because S7 says the kinds are extensible per department. A converted enum
        // would make "we also run a bilan mensuel" a code change, which is the opposite of the promise.
        builder.Property(series => series.Kind).HasMaxLength(64).IsRequired();
        builder.Property(series => series.NameKey).HasMaxLength(256).IsRequired();
        builder.Property(series => series.ScopeType).HasMaxLength(32).IsRequired();
        builder.Property(series => series.RecurrenceRule).HasMaxLength(512).IsRequired();
        builder.Property(series => series.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(series => series.Location).HasMaxLength(256);
        builder.Property(series => series.VideoLink).HasMaxLength(1024);

        // The board query's shape: "what targets this scope, and is it still running".
        builder.HasIndex(series => new { series.ScopeType, series.ScopeId, series.Active });
        builder.HasIndex(series => series.DepartmentId);
        builder.HasIndex(series => series.OwnerPersonId);

        builder.HasMany(series => series.Occurrences)
            .WithOne(occurrence => occurrence.Series)
            .HasForeignKey(occurrence => occurrence.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MeetingOccurrenceConfiguration : IEntityTypeConfiguration<MeetingOccurrence>
{
    public void Configure(EntityTypeBuilder<MeetingOccurrence> builder)
    {
        builder.ToTable("meeting_occurrence");
        builder.HasKey(occurrence => occurrence.Id);

        builder.Property(occurrence => occurrence.ScopeType).HasMaxLength(32).IsRequired();
        builder.Property(occurrence => occurrence.Status).HasMaxLength(32).IsRequired();
        builder.Property(occurrence => occurrence.NotesRef).HasMaxLength(1024);

        // The identity the materializer upserts against: re-running an expansion must find the instance it
        // already wrote, or a cancellation somebody recorded would come back as scheduled.
        builder.HasIndex(occurrence => new { occurrence.SeriesId, occurrence.StartsAt }).IsUnique();

        // Every board and the "coming up" strip ask the same question — occurrences inside a window — so the
        // range is the leading column.
        builder.HasIndex(occurrence => new { occurrence.StartsAt, occurrence.ScopeType, occurrence.ScopeId });

        builder.HasMany(occurrence => occurrence.Attendance)
            .WithOne(attendance => attendance.Occurrence)
            .HasForeignKey(attendance => attendance.OccurrenceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MeetingAttendanceConfiguration : IEntityTypeConfiguration<MeetingAttendance>
{
    public void Configure(EntityTypeBuilder<MeetingAttendance> builder)
    {
        builder.ToTable("meeting_attendance");
        builder.HasKey(attendance => new { attendance.OccurrenceId, attendance.PersonId });

        builder.Property(attendance => attendance.Response).HasMaxLength(32).IsRequired();

        builder.HasIndex(attendance => attendance.PersonId);
    }
}

internal sealed class SpecialDayConfiguration : IEntityTypeConfiguration<SpecialDay>
{
    public void Configure(EntityTypeBuilder<SpecialDay> builder)
    {
        builder.ToTable("special_day");
        builder.HasKey(day => day.Id);

        builder.Property(day => day.Kind).HasMaxLength(64).IsRequired();
        builder.Property(day => day.NameKey).HasMaxLength(256).IsRequired();
        builder.Property(day => day.ScopeType).HasMaxLength(32).IsRequired();
        builder.Property(day => day.Severity).HasMaxLength(32).IsRequired();
        builder.Property(day => day.Description).HasMaxLength(2048);

        builder.HasIndex(day => new { day.Date, day.ScopeType, day.ScopeId });
        builder.HasIndex(day => day.DepartmentId);
    }
}
