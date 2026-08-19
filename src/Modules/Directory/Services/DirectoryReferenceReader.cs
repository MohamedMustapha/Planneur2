using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Directory.Services;

/// <summary>
/// Referential lookups that deliberately run outside the caller's visibility.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a real conflict in the matrix. A department head leading a cross-department project must
/// be able to put a colleague from the contributing department on the team — but the directory is
/// department-scoped, so before that person is on the project, the head cannot see them. The head can only see
/// them <em>because</em> they are on the project, and they cannot get on the project without being seen. That is a
/// deadlock, not a safeguard.
/// </para>
/// <para>
/// The resolution: the authority to read these rows derives from an authority RLS has already established. Every
/// caller reaches this through the Projects module, which loads the project first — and loading it proves, via the
/// project's own RLS policy, that the caller may write it. What leaks is confined to the departments that project
/// already contributes to.
/// </para>
/// <para>
/// It is not a general-purpose bypass, and the shape of the interface is what keeps it that way: no method here
/// takes a free-form query. Existence checks by id, and people filtered to an explicit department list the caller
/// has already been authorized for.
/// </para>
/// </remarks>
internal sealed class DirectoryReferenceReader(IServiceScopeFactory scopeFactory) : IDirectoryReferenceReader
{
    public async Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken ct)
    {
        await using var scope = SystemScope();

        return await Context(scope).Departments
            .AnyAsync(department => department.Id == departmentId && department.Active, ct);
    }

    public async Task<bool> FunctionalRoleExistsAsync(Guid functionalRoleId, CancellationToken ct)
    {
        await using var scope = SystemScope();

        return await Context(scope).FunctionalRoles
            .AnyAsync(role => role.Id == functionalRoleId && role.Active, ct);
    }

    public async Task<PersonSummary?> GetPersonAsync(Guid personId, CancellationToken ct)
    {
        await using var scope = SystemScope();

        return await Context(scope).People
            .Where(person => person.Id == personId)
            .Select(person => new PersonSummary(
                person.Id,
                person.DisplayName,
                person.PrimaryUnitId,
                person.PrimaryDepartmentId,
                new List<string>(),
                person.Active))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<PersonSummary>> GetPeopleInDepartmentsAsync(
        IReadOnlyList<Guid> departmentIds,
        CancellationToken ct)
    {
        if (departmentIds.Count == 0)
        {
            return [];
        }

        await using var scope = SystemScope();

        return await Context(scope).People
            .Where(person => person.Active && person.PrimaryDepartmentId != null
                                           && departmentIds.Contains(person.PrimaryDepartmentId.Value))
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

    public async Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct)
    {
        if (personIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        await using var scope = SystemScope();

        return await Context(scope).People
            .Where(person => personIds.Contains(person.Id))
            .ToDictionaryAsync(person => person.Id, person => person.DisplayName, ct);
    }

    private AsyncServiceScope SystemScope()
    {
        var scope = scopeFactory.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        return scope;
    }

    private static DirectoryDbContext Context(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
}
