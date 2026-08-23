using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Activities.Domain;
using Cracra.Modules.Activities.Infrastructure;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Projects.Domain;
using Cracra.Modules.Projects.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cracra.Host.DevSeed;

/// <summary>Kept behind an interface so a test can seed without waiting on a hosted service.</summary>
public interface IDevDataSeeder
{
    /// <summary>Seeds anything missing and returns how many projects it created. Zero means already seeded.</summary>
    Task<int> SeedAsync(CancellationToken ct);
}

/// <summary>
/// Writes two projects and a few weeks of activity onto a fresh dev box.
/// </summary>
/// <remarks>
/// <para>
/// Development only. Everything here goes through the aggregates rather than through raw SQL, so the rows it
/// produces are the rows the application itself would have produced: <c>Project.Create</c> enforces the lead
/// department, <c>AddMember</c> refuses a member from a department the project does not have, and
/// <c>ActivityEntry.Log</c> refuses project work with no project on it. Seed data that skipped the domain would be
/// seed data that can be invalid in ways the API can never produce — which is worse than no seed data, because it
/// sends people chasing bugs the code does not have.
/// </para>
/// <para>
/// Runs under the system context, like every other background writer: it writes across two departments and six
/// people, and no human session has the scope to do that in one pass. The one thing it must not leave to the
/// system context is <c>access.project_membership</c> — that projection is what decides who can subsequently read
/// each project, and it is maintained by Access rather than by a trigger, so it is called explicitly below.
/// </para>
/// </remarks>
internal sealed class DevDataSeeder(
    IServiceScopeFactory scopes,
    IProjectMembershipProjection membershipProjection,
    IOptions<DevSeedOptions> options,
    ILogger<DevDataSeeder> logger) : IDevDataSeeder
{
    /// <summary>The canonical buckets, which is all a fresh dev box has: no department has configured a taxonomy.</summary>
    private static readonly ActivityTaxonomy Taxonomy = ActivityTaxonomy.Resolve(null);

    private const string BuildWork = "project-build";
    private const string RunWork = "project-run";

    /// <summary>Everyone starts a working day at 09:00. Boards need a place on the clock, not just a duration.</summary>
    private static readonly TimeOnly DayStart = new(9, 0);

    public async Task<int> SeedAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var now = DateTimeOffset.UtcNow;
        var seeded = 0;

        await AttachNodeProfilesAsync(ct);

        foreach (var blueprint in DevSeedCatalogue.Projects)
        {
            if (await SeedProjectAsync(blueprint, settings, now, ct))
            {
                seeded++;
            }
        }

        if (seeded == 0)
        {
            logger.LogInformation("Dev seed: both projects are already present, nothing written.");
        }

        return seeded;
    }

    /// <summary>
    /// Points two sibling DSI units at different profiles, so the dev box shows what v2 §10 is for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The profile rows themselves are seeded by the Directory migration, because they are vocabulary the platform
    /// ships. The <em>attachment</em> is not: which branch does which kind of work is a deployment's own answer,
    /// and a migration that decided it for them would be exactly the hardcoded org semantics this slice removes.
    /// So it lives here, on the dev box, next to the rest of the make-believe.
    /// </para>
    /// <para>
    /// Delivery on one unit and dispatch on its sibling under the same department is the whole demonstration: the
    /// two land on different boards, offer different activity subtypes, and hide different controls, without
    /// either of them being a different <em>kind</em> of thing in the schema.
    /// </para>
    /// </remarks>
    private async Task AttachNodeProfilesAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var directory = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

        var attachments = new Dictionary<Guid, string>
        {
            [DevSeedCatalogue.DevelopmentUnitId] = "DELIVERY",
            [DevSeedCatalogue.HelpdeskUnitId] = "DISPATCH",
            [DevSeedCatalogue.TransformationUnitId] = "ADVISORY",
        };

        var profiles = await directory.NodeProfiles
            .Where(profile => attachments.Values.Contains(profile.Code))
            .ToDictionaryAsync(profile => profile.Code, profile => profile.Id, ct);

        var changed = 0;

        foreach (var (unitId, code) in attachments)
        {
            if (!profiles.TryGetValue(code, out var profileId))
            {
                continue;
            }

            var unit = await directory.Units.AsTracking().SingleOrDefaultAsync(u => u.Id == unitId, ct);

            // Absent rather than unprofiled: a dev box whose directory sync has not run yet has no units at all,
            // and re-running the seeder after it has is what fixes that. Nothing here should fail over it.
            if (unit is null || unit.ProfileId == profileId)
            {
                continue;
            }

            unit.ProfileId = profileId;
            unit.ModifiedAt = DateTimeOffset.UtcNow;
            changed++;
        }

        if (changed > 0)
        {
            await directory.SaveChangesAsync(ct);
            logger.LogInformation("Dev seed: attached node profiles to {UnitCount} units.", changed);
        }
    }

    private async Task<bool> SeedProjectAsync(
        SeedProject blueprint,
        DevSeedOptions settings,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();

        // Set before anything opens a connection: the RLS interceptor stamps whatever the context holds at that
        // moment, and a scope stamped after the first query runs the rest of the work as nobody.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var projects = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();

        // Idempotent on the code, which is unique platform-wide. A dev box is restarted far more often than it is
        // reset, and a seeder that appended a fresh copy of Portail RH on every F5 would be unusable within a day.
        if (await projects.Projects.AnyAsync(existing => existing.Code == blueprint.Code, ct))
        {
            return false;
        }

        var project = Project.Create(
            Guid.CreateVersion7(),
            blueprint.Code,
            blueprint.Name,
            blueprint.Description,
            blueprint.Classification,
            blueprint.Cost,
            blueprint.OwnerPersonId,
            blueprint.LeadDepartmentId,
            blueprint.ContributingDepartmentIds,
            createdBy: blueprint.OwnerPersonId,
            now);

        // Backdated to the start of the seeded history, so nobody appears to have logged time before they joined.
        var joined = FirstWorkingDay(settings);

        foreach (var member in blueprint.Team)
        {
            project.AddMember(
                member.Id,
                member.DepartmentId,
                member.FunctionalRoleId,
                member.AllocationPercent,
                joined,
                modifiedBy: blueprint.OwnerPersonId,
                now);
        }

        await projects.Projects.AddAsync(project, ct);
        await projects.SaveChangesAsync(ct);

        // Its own scope and its own transaction, exactly as the Projects adapter does it. Without this the rows
        // exist but nobody except the owner and the PMO can read them, which looks like a broken permission model.
        await membershipProjection.ReplaceAsync(
            project.Id,
            [.. blueprint.Team.Select(member => new ProjectMembershipEntry(member.Id, member.DepartmentId))],
            ct);

        var entries = await SeedActivityAsync(scope, blueprint, project.Id, settings, now, ct);

        logger.LogInformation(
            "Dev seed: created {Code} ({Name}) with {Members} team members and {Entries} activity entries.",
            project.Code,
            project.Name,
            blueprint.Team.Count,
            entries);

        return true;
    }

    /// <summary>
    /// Writes the working weeks around today.
    /// </summary>
    /// <remarks>
    /// Past and today are actuals; anything ahead is planned. That split is what makes the seed "ongoing" rather
    /// than a static block of history: the weekly boards open on a week that is half done, which is the state a
    /// reviewer actually needs to look at, and the planned-versus-actual gap S5 exists to show has something in it
    /// on the first run.
    /// </remarks>
    private static async Task<int> SeedActivityAsync(
        AsyncServiceScope scope,
        SeedProject blueprint,
        Guid projectId,
        DevSeedOptions settings,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var activities = scope.ServiceProvider.GetRequiredService<ActivitiesDbContext>();

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var day = FirstWorkingDay(settings);
        var lastDay = MondayOf(today).AddDays((7 * Math.Max(0, settings.PlannedWeeks)) + 4);
        var written = 0;

        for (; day <= lastDay; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var kind = day <= today ? ActivityKind.Actual : ActivityKind.Planned;

            foreach (var member in blueprint.Team)
            {
                var start = new DateTimeOffset(day.ToDateTime(DayStart), TimeSpan.Zero);
                var slot = new TimeSlot(start, start.AddHours((double)member.HoursPerDay));

                var entry = ActivityEntry.Log(
                    member.Id,
                    member.UnitId,
                    member.DepartmentId,
                    Taxonomy,
                    TypeCodeFor(member.Nature, day),
                    projectId,
                    iterationId: null,
                    kind,
                    ActivitySource.Manual,
                    externalRef: null,
                    slot,

                    // Null so the entry takes the slot's own length. Splitting the two apart is for the case where
                    // they genuinely differ, and here they do not.
                    hours: null,
                    TaskFor(member, day),
                    createdBy: member.Id,
                    now);

                if (kind is ActivityKind.Planned && ProgressFor(day, today) is { } percent)
                {
                    entry.SetProgress(percent, member.Id, now);
                }

                await activities.Entries.AddAsync(entry, ct);
                written++;
            }
        }

        await activities.SaveChangesAsync(ct);

        return written;
    }

    /// <summary>
    /// BUILD or RUN, as this person's own contribution rather than the project's headline.
    /// </summary>
    /// <remarks>
    /// Read from the member and not from <see cref="Classification"/>, because a project's classification is a
    /// budgeting statement about the whole and says nothing about the helpdesk absorbing tickets on the same code
    /// the developers are still writing. Alternating rather than randomising, so the seed is identical on every
    /// dev box — two people comparing the same screen have to be comparing the same data.
    /// </remarks>
    private static string TypeCodeFor(SeedWorkNature nature, DateOnly day) => nature switch
    {
        SeedWorkNature.Build => BuildWork,
        SeedWorkNature.Run => RunWork,
        _ => day.DayNumber % 2 == 0 ? BuildWork : RunWork,
    };

    /// <summary>
    /// Which of the member's tasks this day is against.
    /// </summary>
    /// <remarks>
    /// Rotating by day rather than by week: a week showing four different tasks reads as work, whereas the same
    /// title five times reads as a rendering bug — which is the wrong first impression for a board whose whole job
    /// is to show what people are doing.
    /// </remarks>
    private static string TaskFor(SeedTeamMember member, DateOnly day) =>
        member.Tasks.Count == 0 ? string.Empty : member.Tasks[day.DayNumber % member.Tasks.Count];

    /// <summary>
    /// How far along a planned slot is, or null for one nobody would have an opinion on yet.
    /// </summary>
    /// <remarks>
    /// Decaying with distance: this week's plan is largely done, next week's has been started, and anything beyond
    /// that carries no figure at all so the board still shows what "nobody has said" looks like. Derived from the
    /// date rather than randomised, for the same reproducibility reason as the type code above.
    /// </remarks>
    private static int? ProgressFor(DateOnly day, DateOnly today) => (day.DayNumber - today.DayNumber) switch
    {
        <= 7 => 65,
        <= 14 => 25,
        _ => null,
    };

    private static DateOnly FirstWorkingDay(DevSeedOptions settings) =>
        MondayOf(DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime))
            .AddDays(-7 * (Math.Max(1, settings.HistoryWeeks) - 1));

    /// <summary>ISO weeks start on Monday, and <see cref="DayOfWeek"/> starts on Sunday. Hence the shift.</summary>
    private static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
