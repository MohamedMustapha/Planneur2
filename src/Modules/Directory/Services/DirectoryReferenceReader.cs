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

    public async Task<IReadOnlyDictionary<string, Guid>> ResolvePeopleByLdapUidAsync(
        IReadOnlyList<string> ldapUids,
        CancellationToken ct)
    {
        if (ldapUids.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        }

        await using var scope = SystemScope();

        // Normalized on both sides. Keycloak lowercases usernames and the external systems do not agree with
        // each other about case, so a comparison that respected it would resolve "C.Villeneuve" to nobody and
        // leave the item looking unassigned — which is worse than an error, because it looks like an answer.
        var normalized = ldapUids
            .Select(uid => uid.Trim().ToLowerInvariant())
            .Where(uid => uid.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var matches = await Context(scope).People
            .Where(person => person.Active && normalized.Contains(person.LdapUid.ToLower()))
            .Select(person => new { person.LdapUid, person.Id })
            .ToListAsync(ct);

        var resolved = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in matches)
        {
            // Last write wins rather than a throw. Two active people whose uids differ only in case is a
            // directory-level fault, and a background job is the wrong place to discover it: the pull would
            // stop, and the visible symptom would be a mirror that stopped updating for no stated reason.
            resolved[match.LdapUid] = match.Id;
        }

        return resolved;
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
