using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Directory.Services;

/// <summary>
/// Directory's side of the read port other modules consume.
/// </summary>
/// <remarks>
/// Runs on the caller's own RLS session, not a system one. That is what makes validation elsewhere inherit the
/// visibility rules for free: a project lead adding a team member can only resolve people they can already see, so
/// "you may not add someone you cannot see" needs no separate check anywhere.
/// </remarks>
internal sealed class DirectoryReader(DirectoryDbContext context) : IDirectoryReader
{
    public async Task<PersonSummary?> GetPersonAsync(Guid personId, CancellationToken ct)
    {
        var person = await context.People
            .Where(candidate => candidate.Id == personId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.DisplayName,
                candidate.PrimaryUnitId,
                candidate.PrimaryDepartmentId,
                candidate.Active,
            })
            .SingleOrDefaultAsync(ct);

        if (person is null)
        {
            return null;
        }

        var roles = await context.PersonFunctionalRoles
            .Where(assignment => assignment.PersonId == personId)
            .Join(context.FunctionalRoles, a => a.FunctionalRoleId, role => role.Id, (_, role) => role.Code)
            .Distinct()
            .ToListAsync(ct);

        return new PersonSummary(
            person.Id,
            person.DisplayName,
            person.PrimaryUnitId,
            person.PrimaryDepartmentId,
            roles,
            person.Active);
    }

    public async Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken ct) =>
        await context.Departments.AnyAsync(department => department.Id == departmentId && department.Active, ct);

    public async Task<bool> FunctionalRoleExistsAsync(Guid functionalRoleId, CancellationToken ct) =>
        await context.FunctionalRoles.AnyAsync(role => role.Id == functionalRoleId && role.Active, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct)
    {
        if (personIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // Anyone RLS filtered simply does not come back. The caller renders them as an unnamed seat rather than
        // dropping them, which keeps headcounts honest without naming people the viewer may not see.
        return await context.People
            .Where(person => personIds.Contains(person.Id))
            .ToDictionaryAsync(person => person.Id, person => person.DisplayName, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetDepartmentNameKeysAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct)
    {
        if (departmentIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await context.Departments
            .Where(department => departmentIds.Contains(department.Id))
            .ToDictionaryAsync(department => department.Id, department => department.NameKey, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetFunctionalRoleCodesAsync(
        IReadOnlyList<Guid> functionalRoleIds,
        CancellationToken ct)
    {
        if (functionalRoleIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await context.FunctionalRoles
            .Where(role => functionalRoleIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, role => role.Code, ct);
    }

    /// <summary>
    /// Units the caller can see, optionally narrowed to one department.
    /// </summary>
    /// <remarks>
    /// The department board's rows. No role check here: RLS on the unit table already decided which units this
    /// caller may see, and a member asking for a department they are not in simply gets nothing back.
    /// </remarks>
    public async Task<IReadOnlyList<UnitSummary>> GetUnitsAsync(Guid? departmentId, CancellationToken ct)
    {
        var query = context.Units.AsQueryable();

        if (departmentId is { } scoped)
        {
            query = query.Where(unit => unit.DepartmentId == scoped);
        }

        return await query
            .OrderBy(unit => unit.Name)
            .Select(unit => new UnitSummary(unit.Id, unit.DepartmentId, unit.Code, unit.Name, unit.Kind.ToString()))
            .ToListAsync(ct);
    }

    /// <summary>People in a unit or department — the team and unit boards' rows.</summary>
    public async Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(
        Guid? unitId,
        Guid? departmentId,
        CancellationToken ct)
    {
        var query = context.People.Where(person => person.Active);

        if (unitId is { } unit)
        {
            query = query.Where(person => person.PrimaryUnitId == unit);
        }

        if (departmentId is { } department)
        {
            query = query.Where(person => person.PrimaryDepartmentId == department);
        }

        return await query
            .OrderBy(person => person.DisplayName)
            .Select(person => new PersonSummary(
                person.Id,
                person.DisplayName,
                person.PrimaryUnitId,
                person.PrimaryDepartmentId,
                new List<string>(),
                person.Active))
            .ToListAsync(ct);
    }
}
