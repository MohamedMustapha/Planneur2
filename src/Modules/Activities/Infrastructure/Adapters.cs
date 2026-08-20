using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Application;
using Cracra.Modules.Activities.Contracts;
using Cracra.Modules.Activities.Domain;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Projects.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cracra.Modules.Activities.Infrastructure;

/// <summary>
/// The Directory port, implemented against Directory's contracts.
/// </summary>
/// <remarks>
/// Placement uses the reference reader: a lead logging an hour on behalf of someone in a unit they can see, but
/// whose directory row is department-scoped away from them, still needs that person's unit to stamp on the entry.
/// The authority comes from RLS refusing the row afterwards if they had no business writing it.
/// </remarks>
internal sealed class DirectoryAdapter(
    IDirectoryReferenceReader reference,
    IDepartmentConfigReader configs) : IDirectoryPort
{
    public async Task<(Guid UnitId, Guid DepartmentId)?> GetPlacementAsync(Guid personId, CancellationToken ct)
    {
        var person = await reference.GetPersonAsync(personId, ct);

        return person is { UnitId: { } unitId, DepartmentId: { } departmentId }
            ? (unitId, departmentId)
            : null;
    }

    public async Task<DepartmentPolicy> GetPolicyAsync(Guid departmentId, CancellationToken ct)
    {
        var config = await configs.TryGetAsync(departmentId, ct);

        // No readable config means the platform defaults apply: the canonical buckets and the statutory 35 hours,
        // soft. Failing here instead would mean one unreadable row stops someone recording their week.
        return config is null
            ? new DepartmentPolicy(ActivityTaxonomy.Resolve(null), DefaultWeeklyTargetHours, false)
            : new DepartmentPolicy(
                ActivityTaxonomy.Resolve(config.ActivityTaxonomyJson),
                config.WeeklyTargetHours,
                config.EnforceWeeklyTarget);
    }

    /// <summary>The statutory French working week, and the platform's default where a department has said nothing.</summary>
    private const decimal DefaultWeeklyTargetHours = 35m;

    public async Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct) =>
        await reference.GetPersonNamesAsync(personIds, ct);
}

/// <summary>
/// The Projects port.
/// </summary>
/// <remarks>
/// Membership is asked of Projects rather than derived from the access projection: the projection exists to answer
/// RLS's question quickly, and reading it here would couple Activities to Access's internals for a fact Projects
/// already publishes.
/// </remarks>
internal sealed class ProjectsAdapter(IProjectProvisioner projects, IProjectMembershipReader members)
    : IProjectsPort
{
    public async Task<bool> IsMemberAsync(Guid projectId, Guid personId, CancellationToken ct) =>
        await members.IsActiveMemberAsync(projectId, personId, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetProjectCodesAsync(
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct)
    {
        var summaries = await projects.GetSummariesAsync(projectIds, ct);

        return summaries.ToDictionary(entry => entry.Key, entry => entry.Value.Code);
    }
}

/// <summary>
/// The department's resolved taxonomy, for anything outside this module that has to interpret a type code.
/// </summary>
/// <remarks>
/// <para>
/// One consumer so far — S11, which needs to know whether an hour booked to a department's own subtype belongs
/// to BUILD or to RUN. It answers with the same merge S5's own picker is built from, which is the point: two
/// implementations of "which bucket is this" would disagree the first time a department added a subtype, and the
/// disagreement would show up as money in the wrong column.
/// </para>
/// <para>
/// Falls back to the canonical buckets when the configuration is unreadable, exactly as the picker does. A
/// department's knobs refine platform behaviour; not being able to read them should cost the refinement, not the
/// answer.
/// </para>
/// </remarks>
internal sealed class ActivityTaxonomyReader(IDirectoryPort directory, IUserContext user)
    : IActivityTaxonomyReader
{
    public async Task<IReadOnlyList<ActivityTypeOption>> GetTypesAsync(Guid? departmentId, CancellationToken ct)
    {
        var scope = departmentId ?? user.DepartmentIds.FirstOrDefault();

        var taxonomy = scope == Guid.Empty
            ? ActivityTaxonomy.Resolve(null)
            : (await directory.GetPolicyAsync(scope, ct)).Taxonomy;

        return
        [
            .. taxonomy.Types.Select(type => new ActivityTypeOption(
                type.Code,
                type.ParentCode,
                type.LabelKey,
                type.RequiresProject)),
        ];
    }
}

/// <summary>Design-time only. Scaffolding needs a provider, not a server.</summary>
public sealed class ActivitiesDbContextFactory : IDesignTimeDbContextFactory<ActivitiesDbContext>
{
    public ActivitiesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRACRA_DESIGN_TIME_CONNECTION")
                               ?? "Host=localhost;Port=5432;Database=cracra;Username=app_owner;Password=design-time";

        var options = new DbContextOptionsBuilder<ActivitiesDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ActivitiesDbContext.SchemaName))
            .Options;

        return new ActivitiesDbContext(options);
    }
}
