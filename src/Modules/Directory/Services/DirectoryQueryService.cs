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
    IReadOnlyList<string> ContextualRoles,
    /// <summary>
    /// What this person chose for themselves, where they have chosen. Null means the synced values above still
    /// stand, and the client treats them as defaults rather than as decisions.
    /// </summary>
    string? PreferredLanguage = null,
    string? PreferredTimeZone = null,
    string? PreferredTheme = null,
    /// <summary>
    /// Whether the shell renders in Focus mode. Null means never chosen, and the client applies the default for
    /// the person's role rather than guessing at false.
    /// </summary>
    bool? FocusMode = null);

/// <summary>
/// A person's own display preferences.
/// </summary>
/// <remarks>
/// Every field is optional, and null clears rather than skips: "use whatever the directory says" is a choice
/// somebody may want to make again after having made a different one, and a partial-update shape would leave them
/// no way to express it.
/// </remarks>
public sealed record UpdatePreferencesRequest(
    string? Language,
    string? TimeZone,
    string? Theme,
    bool? FocusMode = null);

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

    /// <summary>Records the caller's own display preferences. Only ever their own row.</summary>
    Task<MeResponse> UpdateMyPreferencesAsync(UpdatePreferencesRequest request, CancellationToken ct);

    Task<IReadOnlyList<DepartmentSummary>> GetDepartmentsAsync(CancellationToken ct);

    Task<IReadOnlyList<UnitSummary>> GetUnitsAsync(Guid? departmentId, CancellationToken ct);

    Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(Guid? unitId, Guid? departmentId, CancellationToken ct);

    /// <summary>
    /// The job identities the platform knows, by id.
    /// </summary>
    /// <remarks>
    /// Added by S11, whose rate-card editor prices a role and therefore has to offer one by id. Everywhere else
    /// in the client a functional role travels as a code, because that is what people read; a card keys on the
    /// id, because a code is a label a department may relabel.
    /// </remarks>
    Task<IReadOnlyList<FunctionalRoleSummary>> GetFunctionalRolesAsync(CancellationToken ct);
}

/// <summary>One functional role, as a picker needs it: the id it is stored by and the key it renders through.</summary>
public sealed record FunctionalRoleSummary(Guid Id, string Code, string LabelKey, Guid? DepartmentId);

internal sealed class DirectoryQueryService(DirectoryDbContext context, IUserContext user) : IDirectoryQueryService
{
    /// <summary>
    /// Records the caller's preferences and hands back their refreshed record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scoped to <c>user.UserId</c> rather than taking a person id, so there is no parameter that could be pointed
    /// at somebody else's row. RLS would refuse the write anyway; not offering the shape is better than relying on
    /// it to say no.
    /// </para>
    /// <para>
    /// Returns the whole <see cref="MeResponse"/> because the client's context is what actually changed — the
    /// caller would otherwise have to follow every save with a re-read to get back in step.
    /// </para>
    /// </remarks>
    public async Task<MeResponse> UpdateMyPreferencesAsync(UpdatePreferencesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var person = await context.People
            .AsTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == user.UserId, ct)
            ?? throw new ResourceNotFoundException("You have no directory record to store preferences against.");

        person.PreferredLanguage = NormalizeLanguage(request.Language);
        person.PreferredTimeZone = NormalizeTimeZone(request.TimeZone);
        person.PreferredTheme = NormalizeTheme(request.Theme);
        // No normalization: a bool has no invalid value, and null already means what null means everywhere else
        // on this record — "handed back, decide for me".
        person.FocusMode = request.FocusMode;
        person.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);

        return await GetMeAsync(ct);
    }

    /// <summary>Null stays null — that is how somebody hands the choice back to the directory.</summary>
    private static string? NormalizeLanguage(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var twoLetter = candidate.Trim().Split('-', '_')[0].ToLowerInvariant();

        // Checked rather than run through SupportedLanguages.Normalize, which falls back to French for anything it
        // does not recognise. That is right for a synced LDAP attribute and wrong for a deliberate choice: storing
        // "fr" for a request that said "de" would look like the setting had simply been ignored.
        return SupportedLanguages.All.Contains(twoLetter, StringComparer.Ordinal)
            ? twoLetter
            : throw new DomainRuleViolationException(
                $"'{candidate}' is not a supported language. Choose one of: {string.Join(", ", SupportedLanguages.All)}.");
    }

    private static string? NormalizeTimeZone(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim();

        // Asked of the platform rather than checked against a list we maintain: the zone database changes, and a
        // hand-kept list would start refusing zones that are perfectly real.
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(trimmed, out _))
        {
            throw new DomainRuleViolationException($"'{candidate}' is not a time zone this system knows.");
        }

        return trimmed;
    }

    private static string? NormalizeTheme(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim().ToLowerInvariant();

        return trimmed is "light" or "dark"
            ? trimmed
            : throw new DomainRuleViolationException($"'{candidate}' is not a theme. Choose 'light' or 'dark'.");
    }

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
                candidate.PreferredLanguage,
                candidate.PreferredTimeZone,
                candidate.PreferredTheme,
                candidate.FocusMode,
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
            user.Roles,
            person.PreferredLanguage,
            person.PreferredTimeZone,
            person.PreferredTheme,
            person.FocusMode);
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

    public async Task<IReadOnlyList<FunctionalRoleSummary>> GetFunctionalRolesAsync(CancellationToken ct) =>
        await context.FunctionalRoles
            .Where(role => role.Active)
            // Platform-wide roles first, then a department's own. The picker reads as "the standard ones, and
            // ours" without the client having to sort one.
            .OrderBy(role => role.DepartmentId == null ? 0 : 1)
            .ThenBy(role => role.Code)
            .Select(role => new FunctionalRoleSummary(role.Id, role.Code, role.LabelKey, role.DepartmentId))
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
