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
}
