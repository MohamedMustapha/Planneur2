using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Directory.Services;

/// <summary>The caller's own record — what the client bootstraps its context from.</summary>
public sealed record MeResponse(
    Guid PersonId,
    string DisplayName,
    string? Email,
    string LdapUid,
    Guid? PrimaryUnitId,
    Guid? PrimaryDepartmentId,
    string TimeZone,
    string UiLanguage,
    IReadOnlyList<UnitSummary> Units,
    IReadOnlyList<DepartmentSummary> Departments,
    IReadOnlyList<string> FunctionalRoleCodes,
    IReadOnlyList<string> ContextualRoles);

/// <summary>
/// Reads for the directory.
/// </summary>
/// <remarks>
/// Not one of these methods filters by department or role. That is the point: RLS has already removed every row
/// the caller may not see, and a second filter in C# would be either redundant or — worse — subtly different from
/// the policy, at which point the two disagree and nobody can say which one is authoritative.
/// </remarks>
public interface IDirectoryQueryService
{
    Task<MeResponse> GetMeAsync(CancellationToken ct);

    Task<IReadOnlyList<DepartmentSummary>> GetDepartmentsAsync(CancellationToken ct);

    Task<IReadOnlyList<UnitSummary>> GetUnitsAsync(Guid? departmentId, CancellationToken ct);

    Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(Guid? unitId, Guid? departmentId, CancellationToken ct);
}

internal sealed class DirectoryQueryService(DirectoryDbContext context, IUserContext user) : IDirectoryQueryService
{
    public async Task<MeResponse> GetMeAsync(CancellationToken ct)
    {
        var person = await context.People
            .Where(candidate => candidate.Id == user.UserId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.DisplayName,
                candidate.Email,
                candidate.LdapUid,
                candidate.PrimaryUnitId,
                candidate.PrimaryDepartmentId,
                candidate.TimeZone,
                candidate.UiLanguage,
            })
            .SingleOrDefaultAsync(ct);

        if (person is null)
        {
            // Authenticated against Keycloak but absent from the directory: sync has not run yet, or ran and
            // skipped them for want of a unit. Either way this is a real, actionable state rather than a 500.
            throw new ResourceNotFoundException(
                "The signed-in user has no directory record yet. Run a directory sync.");
        }

        var units = await context.PersonUnits
            .Where(membership => membership.PersonId == user.UserId)
            .Join(
                context.Units,
                membership => membership.UnitId,
                unit => unit.Id,
                (_, unit) => new UnitSummary(unit.Id, unit.DepartmentId, unit.Code, unit.Name, unit.Kind.ToString()))
            .ToListAsync(ct);

        var departmentIds = units.Select(unit => unit.DepartmentId).Distinct().ToList();

        var departments = await context.Departments
            .Where(department => departmentIds.Contains(department.Id))
            .Select(department => new DepartmentSummary(
                department.Id,
                department.Code,
                department.NameKey,
                department.ParentDepartmentId))
            .ToListAsync(ct);

        var functionalRoles = await context.PersonFunctionalRoles
            .Where(assignment => assignment.PersonId == user.UserId)
            .Join(context.FunctionalRoles, a => a.FunctionalRoleId, role => role.Id, (_, role) => role.Code)
            .Distinct()
            .ToListAsync(ct);

        return new MeResponse(
            person.Id,
            person.DisplayName,
            person.Email,
            person.LdapUid,
            person.PrimaryUnitId,
            person.PrimaryDepartmentId,
            person.TimeZone,
            person.UiLanguage,
            units,
            departments,
            functionalRoles,
            // Straight from the token. Contextual roles are an access concern, not a directory record — S2 owns
            // resolving them, and echoing them here only saves the client a second call.
            user.Roles);
    }

    public async Task<IReadOnlyList<DepartmentSummary>> GetDepartmentsAsync(CancellationToken ct) =>
        await context.Departments
            .Where(department => department.Active)
            .OrderBy(department => department.Code)
            .Select(department => new DepartmentSummary(
                department.Id,
                department.Code,
                department.NameKey,
                department.ParentDepartmentId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UnitSummary>> GetUnitsAsync(Guid? departmentId, CancellationToken ct) =>
        await context.Units
            .Where(unit => unit.Active)
            .Where(unit => departmentId == null || unit.DepartmentId == departmentId)
            .OrderBy(unit => unit.Name)
            .Select(unit => new UnitSummary(unit.Id, unit.DepartmentId, unit.Code, unit.Name, unit.Kind.ToString()))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(
        Guid? unitId,
        Guid? departmentId,
        CancellationToken ct)
    {
        var people = await context.People
            .Where(person => person.Active)
            .Where(person => unitId == null || person.PrimaryUnitId == unitId)
            .Where(person => departmentId == null || person.PrimaryDepartmentId == departmentId)
            .OrderBy(person => person.DisplayName)
            .Select(person => new
            {
                person.Id,
                person.DisplayName,
                person.PrimaryUnitId,
                person.PrimaryDepartmentId,
                person.Active,
            })
            .ToListAsync(ct);

        var ids = people.Select(person => person.Id).ToList();

        // One extra round trip rather than a correlated subquery per row — the N+1 this avoids is the difference
        // between one query and one-per-person on a department board.
        var rolesByPerson = await context.PersonFunctionalRoles
            .Where(assignment => ids.Contains(assignment.PersonId))
            .Join(
                context.FunctionalRoles,
                assignment => assignment.FunctionalRoleId,
                role => role.Id,
                (assignment, role) => new { assignment.PersonId, role.Code })
            .ToListAsync(ct);

        var lookup = rolesByPerson
            .GroupBy(entry => entry.PersonId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)[.. group.Select(e => e.Code).Distinct()]);

        return
        [
            .. people.Select(person => new PersonSummary(
                person.Id,
                person.DisplayName,
                person.PrimaryUnitId,
                person.PrimaryDepartmentId,
                lookup.TryGetValue(person.Id, out var codes) ? codes : [],
                person.Active)),
        ];
    }
}
