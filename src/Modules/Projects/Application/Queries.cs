using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Mediator;
using Cracra.Modules.Projects.Contracts;
using Cracra.Modules.Projects.Domain;
using Cracra.Modules.Projects.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Projects.Application;

// =================================================================================================================
// Queries read projections straight off the DbContext rather than going through the repository (conventions.md §2).
// Rehydrating an aggregate to render a list is wasted work, and none of these mutate anything.
//
// Not one of them filters by role or department. RLS removed those rows before they reached us, and a second
// filter here would either duplicate the policy or quietly disagree with it.
// =================================================================================================================

public sealed record ListProjectsQuery(Guid? DepartmentId, string? Classification) : IRequest<IReadOnlyList<ProjectSummary>>;

public sealed record GetProjectQuery(Guid ProjectId) : IRequest<ProjectDetail>;

public sealed record GetProjectTeamQuery(Guid ProjectId) : IRequest<ProjectTeam>;

internal sealed class ListProjectsHandler(ProjectsDbContext context)
    : IRequestHandler<ListProjectsQuery, IReadOnlyList<ProjectSummary>>
{
    public async Task<IReadOnlyList<ProjectSummary>> Handle(ListProjectsQuery request, CancellationToken ct)
    {
        var query = context.Projects.Where(project => !project.Archived);

        if (request.DepartmentId is { } departmentId)
        {
            query = query.Where(project =>
                context.ProjectDepartments.Any(link =>
                    link.ProjectId == project.Id && link.DepartmentId == departmentId));
        }

        if (request.Classification is { Length: > 0 } classification
            && Enum.TryParse<Classification>(classification, ignoreCase: true, out var parsed))
        {
            query = query.Where(project => project.Classification == parsed);
        }

        return await query
            .OrderBy(project => project.Code)
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
            .ToListAsync(ct);
    }
}

internal sealed class GetProjectHandler(ProjectsDbContext context, IDirectoryPort directory)
    : IRequestHandler<GetProjectQuery, ProjectDetail>
{
    public async Task<ProjectDetail> Handle(GetProjectQuery request, CancellationToken ct)
    {
        var project = await context.Projects
            .Where(candidate => candidate.Id == request.ProjectId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Code,
                candidate.Name,
                candidate.Description,
                candidate.Classification,
                candidate.CostAmount,
                candidate.CostCurrency,
                candidate.CostNotes,
                candidate.LeadDepartmentId,
                candidate.OwnerPersonId,
            })
            .SingleOrDefaultAsync(ct)
            // RLS filtered it, or it does not exist. The caller cannot tell the two apart, and neither can a
            // prober enumerating ids.
            ?? throw new ResourceNotFoundException($"No project {request.ProjectId}.");

        var departmentIds = await context.ProjectDepartments
            .Where(link => link.ProjectId == request.ProjectId)
            .Select(link => link.DepartmentId)
            .ToListAsync(ct);

        var names = await directory.GetDepartmentNameKeysAsync(departmentIds, ct);

        return new ProjectDetail(
            project.Id,
            project.Code,
            project.Name,
            project.Description,
            project.Classification.ToString().ToLower(),
            project.CostAmount,
            project.CostCurrency,
            project.CostNotes,
            project.LeadDepartmentId,
            project.OwnerPersonId,
            [
                .. departmentIds.Select(id => new ProjectDepartmentSummary(
                    id,
                    names.TryGetValue(id, out var key) ? key : $"directory.department.{id}",
                    id == project.LeadDepartmentId)),
            ]);
    }
}

/// <summary>
/// The team, grouped by department and then by function.
/// </summary>
/// <remarks>
/// This shape <em>is</em> the project view. The design draws one collapsible group per department with functional
/// sub-groups inside it, so that "who from which department is doing what" is answerable at a glance — and doing
/// the grouping here rather than in the client means the report (S8) and the timeline (S6) get the same answer
/// without reimplementing it.
/// </remarks>
internal sealed class GetProjectTeamHandler(ProjectsDbContext context, IDirectoryPort directory)
    : IRequestHandler<GetProjectTeamQuery, ProjectTeam>
{
    public async Task<ProjectTeam> Handle(GetProjectTeamQuery request, CancellationToken ct)
    {
        var exists = await context.Projects.AnyAsync(project => project.Id == request.ProjectId, ct);

        if (!exists)
        {
            throw new ResourceNotFoundException($"No project {request.ProjectId}.");
        }

        var members = await context.ProjectMembers
            .Where(member => member.ProjectId == request.ProjectId && member.To == null)
            .Select(member => new
            {
                member.PersonId,
                member.DepartmentId,
                member.FunctionalRoleId,
                member.AllocationPercent,
                member.From,
            })
            .ToListAsync(ct);

        // Three lookups rather than a join per member: the names live in another module, reachable only through
        // its contracts, and batching keeps this at a fixed number of calls however large the team gets.
        var personNames = await directory.GetPersonNamesAsync([.. members.Select(m => m.PersonId).Distinct()], ct);
        var departmentNames = await directory.GetDepartmentNameKeysAsync([.. members.Select(m => m.DepartmentId).Distinct()], ct);
        var roleCodes = await directory.GetFunctionalRoleCodesAsync([.. members.Select(m => m.FunctionalRoleId).Distinct()], ct);

        var groups = members
            .GroupBy(member => member.DepartmentId)
            .OrderBy(group => departmentNames.TryGetValue(group.Key, out var key) ? key : group.Key.ToString())
            .Select(departmentGroup => new ProjectTeamDepartment(
                departmentGroup.Key,
                departmentNames.TryGetValue(departmentGroup.Key, out var nameKey)
                    ? nameKey
                    : $"directory.department.{departmentGroup.Key}",
                [
                    .. departmentGroup
                        .GroupBy(member => member.FunctionalRoleId)
                        .OrderBy(functionGroup =>
                            roleCodes.TryGetValue(functionGroup.Key, out var code) ? code : string.Empty)
                        .Select(functionGroup => new ProjectTeamFunction(
                            functionGroup.Key,
                            roleCodes.TryGetValue(functionGroup.Key, out var code) ? code : "unknown",
                            [
                                .. functionGroup
                                    .OrderBy(member => personNames.TryGetValue(member.PersonId, out var name)
                                        ? name
                                        : string.Empty)
                                    .Select(member => new ProjectTeamMember(
                                        member.PersonId,
                                        // A person RLS hid from this caller still counts toward the headcount —
                                        // hiding the row entirely would misreport the team's size — but they are
                                        // not named.
                                        personNames.TryGetValue(member.PersonId, out var name) ? name : null,
                                        member.AllocationPercent,
                                        member.From)),
                            ])),
                ]))
            .ToList();

        return new ProjectTeam(request.ProjectId, groups, members.Count);
    }
}

/// <summary>
/// People who could join this project's team.
/// </summary>
/// <remarks>
/// Everyone in the project's contributing departments, including departments the caller cannot otherwise see. That
/// is the whole point: a head leading a cross-department project has to be able to pick from the contributing
/// department's staff, and the authority for it comes from being allowed to write this project — which the
/// repository load establishes through the project's own RLS policy before this query returns anything.
/// </remarks>
public sealed record GetTeamCandidatesQuery(Guid ProjectId) : IRequest<IReadOnlyList<TeamCandidate>>;

internal sealed class GetTeamCandidatesHandler(
    ProjectsDbContext context,
    Cracra.Modules.Directory.Contracts.IDirectoryReferenceReader reference)
    : IRequestHandler<GetTeamCandidatesQuery, IReadOnlyList<TeamCandidate>>
{
    public async Task<IReadOnlyList<TeamCandidate>> Handle(GetTeamCandidatesQuery request, CancellationToken ct)
    {
        // Reading the project through the normal DbContext means RLS decides whether it exists for this caller.
        // Everything below is scoped by a project they have already proved they can see.
        var visible = await context.Projects.AnyAsync(project => project.Id == request.ProjectId, ct);

        if (!visible)
        {
            throw new ResourceNotFoundException($"No project {request.ProjectId}.");
        }

        var departmentIds = await context.ProjectDepartments
            .Where(link => link.ProjectId == request.ProjectId)
            .Select(link => link.DepartmentId)
            .ToListAsync(ct);

        var existing = await context.ProjectMembers
            .Where(member => member.ProjectId == request.ProjectId && member.To == null)
            .Select(member => member.PersonId)
            .ToListAsync(ct);

        var people = await reference.GetPeopleInDepartmentsAsync(departmentIds, ct);

        return
        [
            .. people
                .Where(person => !existing.Contains(person.Id))
                .Select(person => new TeamCandidate(person.Id, person.DisplayName, person.DepartmentId)),
        ];
    }
}
