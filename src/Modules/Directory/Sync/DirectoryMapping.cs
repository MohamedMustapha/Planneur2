using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Domain;

namespace Cracra.Modules.Directory.Sync;

/// <summary>A department and its units, as read from the Keycloak group tree.</summary>
public sealed record MappedOrg(
    IReadOnlyList<MappedDepartment> Departments,
    IReadOnlyList<MappedUnit> Units);

public sealed record MappedDepartment(Guid Id, string Code, string NameKey);

public sealed record MappedUnit(Guid Id, Guid DepartmentId, string Code, string Name, UnitKind Kind);

/// <summary>One Keycloak user, reduced to what the directory stores.</summary>
public sealed record MappedPerson(
    Guid PersonId,
    string LdapUid,
    string DisplayName,
    string? Email,
    Guid UnitId,
    Guid DepartmentId,
    string UnitCode,
    string? FunctionalRoleCode,
    string UiLanguage,
    bool Active,
    IReadOnlyList<string> ContextualRoles);

/// <summary>
/// The LDAP-to-entity translation, kept pure and static so it can be unit-tested without Keycloak, a database or
/// a host. This is the part of sync most likely to be wrong, and the part cheapest to test.
/// </summary>
public static class DirectoryMapping
{
    /// <summary>
    /// Returns null when the user cannot be placed in the org — no unit or no department. The caller warns and
    /// skips rather than inventing a placement, because a person filed under the wrong unit is worse than a person
    /// missing from a list: RLS would then show their activity to the wrong colleagues.
    /// </summary>
    public static MappedPerson? Map(KeycloakUser user)
    {
        if (!Guid.TryParse(user.Attribute("unit_id"), out var unitId))
        {
            return null;
        }

        var departmentIds = user.AttributeValues("dept_ids")
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(value => Guid.TryParse(value, out var parsed) ? parsed : (Guid?)null)
            .Where(parsed => parsed is not null)
            .Select(parsed => parsed!.Value)
            .ToArray();

        if (departmentIds.Length == 0)
        {
            return null;
        }

        return new MappedPerson(
            PersonId: user.Id,
            LdapUid: user.Username,
            DisplayName: BuildDisplayName(user),
            Email: user.Email,
            UnitId: unitId,
            // The first department is the primary one. A person in several is rare and, when it happens, their
            // home department is the one they are listed under first.
            DepartmentId: departmentIds[0],
            UnitCode: user.Attribute("unit_code") ?? unitId.ToString("N")[..8],
            FunctionalRoleCode: user.Attribute("functional_role"),
            UiLanguage: SupportedLanguages.Normalize(user.Attribute("locale")),
            Active: user.Enabled,
            // Straight through from the LDAP-derived attribute. Access materializes these into scoped assignments
            // and merges overrides on top; Directory deliberately does not interpret them.
            ContextualRoles: ReadContextualRoles(user));
    }

    /// <summary>
    /// Applies a mapped user onto an existing person. Returns whether anything changed, and reports the previous
    /// placement when the person moved, so the caller can raise <c>PersonMoved</c> — a move invalidates every board
    /// scoped to their old unit, and consumers cannot detect it from the new state alone.
    /// </summary>
    public static bool Apply(
        Person person,
        MappedPerson mapped,
        DateTimeOffset now,
        out (Guid? UnitId, Guid? DepartmentId)? movedFrom)
    {
        movedFrom = null;

        var moved = person.PrimaryUnitId != mapped.UnitId || person.PrimaryDepartmentId != mapped.DepartmentId;

        var changed = moved
                      || person.DisplayName != mapped.DisplayName
                      || person.Email != mapped.Email
                      || person.LdapUid != mapped.LdapUid
                      || person.UiLanguage != mapped.UiLanguage
                      || person.Active != mapped.Active;

        if (moved)
        {
            movedFrom = (person.PrimaryUnitId, person.PrimaryDepartmentId);
        }

        person.LdapUid = mapped.LdapUid;
        person.DisplayName = mapped.DisplayName;
        person.Email = mapped.Email;
        person.PrimaryUnitId = mapped.UnitId;
        person.PrimaryDepartmentId = mapped.DepartmentId;
        person.UiLanguage = mapped.UiLanguage;
        person.Active = mapped.Active;
        person.LastSyncedAt = now;

        if (changed)
        {
            person.ModifiedAt = now;
        }

        return changed;
    }

    public static Person NewPerson(MappedPerson mapped, DateTimeOffset now) => new()
    {
        Id = mapped.PersonId,
        LdapUid = mapped.LdapUid,
        DisplayName = mapped.DisplayName,
        Email = mapped.Email,
        PrimaryUnitId = mapped.UnitId,
        PrimaryDepartmentId = mapped.DepartmentId,
        UiLanguage = mapped.UiLanguage,
        Active = mapped.Active,
        LastSyncedAt = now,
        CreatedAt = now,
        ModifiedAt = now,
    };

    /// <summary>
    /// Reads the org tree out of the Keycloak group hierarchy: a top-level group is a department, its subgroups
    /// are units, and each carries the id the directory keys on as an attribute.
    /// </summary>
    /// <remarks>
    /// Groups are the source rather than user attributes because a group knows its own name and code, whereas a
    /// user only knows which ids they belong to. Deriving departments from users would produce rows named after a
    /// GUID that nobody would then think to rename — the acceptance criterion is that sync reproduces the fixture
    /// org exactly, and a placeholder name is not exactly.
    /// </remarks>
    public static MappedOrg MapOrg(IReadOnlyList<KeycloakGroup> groups)
    {
        var departments = new List<MappedDepartment>();
        var units = new List<MappedUnit>();

        foreach (var group in groups)
        {
            if (!Guid.TryParse(group.Attribute("department_id"), out var departmentId))
            {
                continue;
            }

            departments.Add(new MappedDepartment(
                departmentId,
                group.Name,
                $"directory.department.{group.Name}"));

            foreach (var subGroup in group.SubGroups ?? [])
            {
                if (Guid.TryParse(subGroup.Attribute("unit_id"), out var unitId))
                {
                    units.Add(new MappedUnit(
                        unitId,
                        departmentId,
                        subGroup.Name,
                        subGroup.Attribute("unit_name") ?? subGroup.Name,
                        ParseKind(subGroup.Attribute("unit_kind"))));
                }
            }
        }

        return new MappedOrg(departments, units);
    }

    public static Department NewDepartment(MappedDepartment mapped, DateTimeOffset now) => new()
    {
        Id = mapped.Id,
        Code = mapped.Code,
        NameKey = mapped.NameKey,
        CreatedAt = now,
        ModifiedAt = now,
    };

    public static Unit NewUnit(MappedUnit mapped, DateTimeOffset now) => new()
    {
        Id = mapped.Id,
        DepartmentId = mapped.DepartmentId,
        Code = mapped.Code,
        Name = mapped.Name,
        LdapFonction = mapped.Code,
        Kind = mapped.Kind,
        CreatedAt = now,
        ModifiedAt = now,
    };

    /// <summary>
    /// The unit's kind, defaulting to delivery.
    /// </summary>
    /// <remarks>
    /// An unparseable or absent value is the old behaviour rather than a skipped unit: kind is a hint for the
    /// default board layout, and losing a whole unit because a group attribute was misspelt would be a wildly
    /// disproportionate response to a cosmetic field.
    /// </remarks>
    private static UnitKind ParseKind(string? value) =>
        Enum.TryParse<UnitKind>(value, ignoreCase: true, out var kind) ? kind : UnitKind.Delivery;

    public static DepartmentConfig NewConfig(Guid departmentId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        DepartmentId = departmentId,
        CreatedAt = now,
        ModifiedAt = now,
    };

    private static IReadOnlyList<string> ReadContextualRoles(KeycloakUser user) =>
        [.. user.AttributeValues("contextual_roles")
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(role => role.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)];

    private static string BuildDisplayName(KeycloakUser user)
    {
        var full = $"{user.FirstName} {user.LastName}".Trim();

        return full.Length > 0 ? full : user.Username;
    }
}
