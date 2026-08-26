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

    public DbSet<MeetingMinutes> Minutes => Set<MeetingMinutes>();

    public DbSet<MeetingDecision> Decisions => Set<MeetingDecision>();

    public DbSet<ActionItem> Actions => Set<ActionItem>();

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
        builder.Property(series => series.Level).HasMaxLength(32).IsRequired();
        builder.Property(series => series.ScopeIds).HasColumnType("uuid[]").IsRequired();
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

internal sealed class MeetingMinutesConfiguration : IEntityTypeConfiguration<MeetingMinutes>
{
    public void Configure(EntityTypeBuilder<MeetingMinutes> builder)
    {
        builder.ToTable("meeting_minutes");
        builder.HasKey(minutes => minutes.Id);

        builder.Property(minutes => minutes.Id).ValueGeneratedNever();
        builder.Property(minutes => minutes.Level).HasMaxLength(32).IsRequired();
        builder.Property(minutes => minutes.ScopeType).HasMaxLength(32).IsRequired();
        builder.Property(minutes => minutes.ScopeIds).HasColumnType("uuid[]").IsRequired();
        builder.Property(minutes => minutes.Attendees).HasColumnType("uuid[]").IsRequired();
        builder.Property(minutes => minutes.Absentees).HasColumnType("uuid[]").IsRequired();
        builder.Property(minutes => minutes.Agenda).HasMaxLength(8000);
        builder.Property(minutes => minutes.Summary).HasMaxLength(16000);

        // One CR per occurrence. Two people writing minutes for the same meeting is a merge nobody wants to do
        // afterwards, and the editor opens the existing draft instead.
        builder.HasIndex(minutes => minutes.OccurrenceId).IsUnique();

        // The "Derniers CR" strip's query: what was published to this scope, most recent first.
        builder.HasIndex(minutes => new { minutes.ScopeType, minutes.ScopeId, minutes.OccurredAt });
        builder.HasIndex(minutes => minutes.AuthorPersonId);

        builder.HasMany(minutes => minutes.Decisions)
            .WithOne()
            .HasForeignKey(decision => decision.MinutesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(minutes => minutes.Actions)
            .WithOne()
            .HasForeignKey(action => action.MinutesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(minutes => minutes.Decisions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(minutes => minutes.Actions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class MeetingDecisionConfiguration : IEntityTypeConfiguration<MeetingDecision>
{
    public void Configure(EntityTypeBuilder<MeetingDecision> builder)
    {
        builder.ToTable("meeting_decision");
        builder.HasKey(decision => decision.Id);

        builder.Property(decision => decision.Id).ValueGeneratedNever();
        builder.Property(decision => decision.Text).HasMaxLength(4000).IsRequired();
        builder.Property(decision => decision.Rationale).HasMaxLength(4000);
        builder.Property(decision => decision.DecidedBy).HasMaxLength(256);

        builder.HasIndex(decision => decision.MinutesId);
    }
}

internal sealed class ActionItemConfiguration : IEntityTypeConfiguration<ActionItem>
{
    public void Configure(EntityTypeBuilder<ActionItem> builder)
    {
        builder.ToTable("action_item");
        builder.HasKey(action => action.Id);

        builder.Property(action => action.Id).ValueGeneratedNever();
        builder.Property(action => action.Title).HasMaxLength(1000).IsRequired();
        builder.Property(action => action.Status).HasMaxLength(16).IsRequired();
        builder.Property(action => action.LinkType).HasMaxLength(16).IsRequired();

        builder.Ignore(action => action.IsOpen);

        builder.HasIndex(action => action.MinutesId);

        // The tracker's two questions: "what do I owe" and "what is still open here", both with the overdue ones
        // first. Status leads because every one of those queries filters on it.
        builder.HasIndex(action => new { action.OwnerPersonId, action.Status, action.Due });

        // And the reverse: when a problem or an item resolves, which actions were waiting on it.
        builder.HasIndex(action => new { action.LinkType, action.LinkId });
    }
}
