using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Projects.Contracts;
using Cracra.Modules.Projects.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Projects.Infrastructure;

/// <summary>
/// Projects' side of the provisioning contract Portfolio calls on commitment.
/// </summary>
/// <remarks>
/// Runs on the caller's own session, so the created project belongs to whoever committed the item and RLS governs
/// every read of it exactly as it would for one created through the API. Deliberately not a system-context write:
/// a project nobody owns is one nobody can subsequently edit.
/// </remarks>
internal sealed class ProjectProvisioner(ProjectsDbContext context, IUserContext user) : IProjectProvisioner
{
    public async Task<Guid> ProvisionAsync(
        string code,
        string name,
        string? description,
        Guid leadDepartmentId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var project = Project.Create(
            Guid.CreateVersion7(),
            code,
            name,
            description,
            // BUILD by default: a newly committed candidate is delivery work until somebody says otherwise, and
            // the classification is editable on the project screen straight afterwards.
            Classification.Build,
            Money.Zero,
            user.UserId,
            leadDepartmentId,
            [],
            user.UserId,
            now);

        await context.Projects.AddAsync(project, ct);
        await context.SaveChangesAsync(ct);

        return project.Id;
    }

    public async Task<bool> ExistsAsync(Guid projectId, CancellationToken ct) =>
        await context.Projects.AnyAsync(project => project.Id == projectId, ct);

    public async Task<bool> HasTeamAsync(Guid projectId, CancellationToken ct) =>
        await context.ProjectMembers.AnyAsync(member => member.ProjectId == projectId && member.To == null, ct);

    public async Task<IReadOnlyList<Guid>> GetVisibleProjectIdsAsync(CancellationToken ct) =>
        await context.Projects
            .Where(project => !project.Archived)
            .Select(project => project.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, ProjectSummary>> GetSummariesAsync(
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<Guid, ProjectSummary>();
        }

        // Anything RLS hid simply does not come back, and the board renders that card without a cost rather than
        // hiding the item — the item's own visibility is a separate question from the project's.
        return await context.Projects
            .Where(project => projectIds.Contains(project.Id))
            .Select(project => new ProjectSummary(
                project.Id,
                project.Code,
                project.Name,
                project.Classification.ToString().ToLower(),
                project.CostAmount,
                project.CostCurrency,
                project.LeadDepartmentId,
                project.OwnerPersonId,
                context.ProjectMembers.Count(member => member.ProjectId == project.Id && member.To == null)))
            .ToDictionaryAsync(summary => summary.Id, ct);
    }
}

/// <summary>
/// Projects answering "is this person on it" for S5.
/// </summary>
/// <remarks>
/// Runs on the caller's own connection, so a project RLS hides answers false — which is the right answer for the
/// question being asked. Someone who cannot see a project has no business booking time to it.
/// </remarks>
internal sealed class ProjectMembershipReader(ProjectsDbContext context) : IProjectMembershipReader
{
    public async Task<bool> IsActiveMemberAsync(Guid projectId, Guid personId, CancellationToken ct) =>
        await context.ProjectMembers.AnyAsync(member =>
            member.ProjectId == projectId && member.PersonId == personId && member.To == null, ct);

    /// <summary>
    /// A self-join over current memberships.
    /// </summary>
    /// <remarks>
    /// The person themselves is excluded here rather than by the caller: every consumer of this wants "who else",
    /// and the one that forgot would offer somebody the chance to thank themselves.
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> GetProjectPeersAsync(Guid personId, CancellationToken ct) =>
        await context.ProjectMembers
            .Where(mine => mine.PersonId == personId && mine.To == null)
            .Join(
                context.ProjectMembers.Where(theirs => theirs.To == null),
                mine => mine.ProjectId,
                theirs => theirs.ProjectId,
                (mine, theirs) => theirs.PersonId)
            .Where(peer => peer != personId)
            .Distinct()
            .ToListAsync(ct);
}

/// <summary>
/// Projects' side of the team-reading contract S6 draws rows from.
/// </summary>
/// <remarks>
/// Flat, and on the caller's own connection. Anyone Directory hid comes back unnamed rather than dropped: they
/// are on the project and their hours fill the board, so removing the row would understate the load on it.
/// </remarks>
internal sealed class ProjectTeamReader(
    ProjectsDbContext context,
    Application.IDirectoryPort directory) : IProjectTeamReader
{
    public async Task<IReadOnlyList<ProjectTeamMemberView>> GetTeamAsync(Guid projectId, CancellationToken ct)
    {
        var members = await context.ProjectMembers
            .Where(member => member.ProjectId == projectId && member.To == null)
            .Select(member => new
            {
                member.PersonId,
                member.DepartmentId,
                member.FunctionalRoleId,
            })
            .ToListAsync(ct);

        if (members.Count == 0)
        {
            return [];
        }

        var names = await directory.GetPersonNamesAsync([.. members.Select(member => member.PersonId)], ct);

        var departments = await directory.GetDepartmentNameKeysAsync(
            [.. members.Select(member => member.DepartmentId).Distinct()],
            ct);

        var functions = await directory.GetFunctionalRoleCodesAsync(
            [.. members.Select(member => member.FunctionalRoleId).Distinct()],
            ct);

        return
        [
            .. members.Select(member => new ProjectTeamMemberView(
                member.PersonId,
                names.GetValueOrDefault(member.PersonId),
                member.DepartmentId,
                departments.GetValueOrDefault(member.DepartmentId, "department.unknown"),
                functions.GetValueOrDefault(member.FunctionalRoleId, "unknown"))),
        ];
    }
}
