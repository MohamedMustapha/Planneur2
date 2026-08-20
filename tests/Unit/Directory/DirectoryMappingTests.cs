using Cracra.Modules.Directory.Domain;
using Cracra.Modules.Directory.Sync;

namespace Cracra.Tests.Unit.Directory;

/// <summary>
/// The LDAP-to-entity translation. Pure, so it is tested here rather than through a container — and worth testing
/// hard, because a mapping that places someone in the wrong unit does not fail, it just shows their work to the
/// wrong colleagues.
/// </summary>
public sealed class DirectoryMappingTests
{
    private static readonly Guid UnitId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid DepartmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherDepartmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Maps_a_complete_user()
    {
        var mapped = DirectoryMapping.Map(User());

        mapped.ShouldNotBeNull();
        mapped.PersonId.ShouldBe(Guid.Parse("c0000000-0000-0000-0000-000000000001"));
        mapped.LdapUid.ShouldBe("camille.villeneuve");
        mapped.DisplayName.ShouldBe("Camille Villeneuve");
        mapped.UnitId.ShouldBe(UnitId);
        mapped.DepartmentId.ShouldBe(DepartmentId);
        mapped.FunctionalRoleCode.ShouldBe("architecte");
        mapped.UiLanguage.ShouldBe("fr");
        mapped.Active.ShouldBeTrue();
    }

    [Fact]
    public void Falls_back_to_the_username_when_there_is_no_name()
    {
        var mapped = DirectoryMapping.Map(User(firstName: null, lastName: null));

        // A blank display name would render as an empty row on every board. The username is unlovely but true.
        mapped!.DisplayName.ShouldBe("camille.villeneuve");
    }

    [Fact]
    public void Refuses_a_user_with_no_unit()
    {
        DirectoryMapping.Map(User(includeUnit: false)).ShouldBeNull();
    }

    [Fact]
    public void Refuses_a_user_with_no_department()
    {
        DirectoryMapping.Map(User(departmentIds: [])).ShouldBeNull();
    }

    [Fact]
    public void Refuses_a_user_whose_unit_is_not_a_guid()
    {
        // Returning null rather than guessing: a person filed under an invented unit gets an RLS scope nobody
        // intended, which is strictly worse than a person missing from a list.
        DirectoryMapping.Map(User(rawUnitId: "not-a-guid")).ShouldBeNull();
    }

    [Fact]
    public void Takes_the_first_department_as_primary()
    {
        var mapped = DirectoryMapping.Map(User(departmentIds: [DepartmentId, OtherDepartmentId]));

        mapped!.DepartmentId.ShouldBe(DepartmentId);
    }

    [Fact]
    public void Accepts_departments_delivered_as_one_comma_separated_attribute()
    {
        var user = User(departmentIds: []) with
        {
            Attributes = new Dictionary<string, List<string>>
            {
                ["unit_id"] = [UnitId.ToString()],
                ["dept_ids"] = [$"{DepartmentId},{OtherDepartmentId}"],
            },
        };

        var mapped = DirectoryMapping.Map(user);

        mapped!.DepartmentId.ShouldBe(DepartmentId);
    }

    [Fact]
    public void Normalizes_an_unsupported_locale_to_French()
    {
        DirectoryMapping.Map(User(locale: "de-DE"))!.UiLanguage.ShouldBe("fr");
    }

    // --- Apply ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Apply_reports_no_change_when_nothing_moved()
    {
        var mapped = DirectoryMapping.Map(User())!;
        var person = DirectoryMapping.NewPerson(mapped, DateTimeOffset.UtcNow);

        var changed = DirectoryMapping.Apply(person, mapped, DateTimeOffset.UtcNow, out var movedFrom);

        // Idempotency lives or dies here: a second sync that reports every person as changed would republish an
        // integration event per person, every hour, forever.
        changed.ShouldBeFalse();
        movedFrom.ShouldBeNull();
    }

    [Fact]
    public void Apply_reports_the_previous_placement_when_someone_moves_unit()
    {
        var original = DirectoryMapping.Map(User())!;
        var person = DirectoryMapping.NewPerson(original, DateTimeOffset.UtcNow);

        var newUnit = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        var moved = original with { UnitId = newUnit };

        var changed = DirectoryMapping.Apply(person, moved, DateTimeOffset.UtcNow, out var movedFrom);

        changed.ShouldBeTrue();
        movedFrom.ShouldNotBeNull();

        // The previous unit cannot be recovered from the new state, and consumers need it to invalidate the boards
        // scoped to it — which is the whole reason PersonMoved carries both sides.
        movedFrom!.Value.UnitId.ShouldBe(UnitId);
        person.PrimaryUnitId.ShouldBe(newUnit);
    }

    [Fact]
    public void Apply_notices_a_rename()
    {
        var original = DirectoryMapping.Map(User())!;
        var person = DirectoryMapping.NewPerson(original, DateTimeOffset.UtcNow);

        var changed = DirectoryMapping.Apply(
            person,
            original with { DisplayName = "Camille Villeneuve-Roux" },
            DateTimeOffset.UtcNow,
            out var movedFrom);

        changed.ShouldBeTrue();
        movedFrom.ShouldBeNull();
        person.DisplayName.ShouldBe("Camille Villeneuve-Roux");
    }

    [Fact]
    public void Apply_stamps_the_sync_time_even_when_nothing_changed()
    {
        var mapped = DirectoryMapping.Map(User())!;
        var person = DirectoryMapping.NewPerson(mapped, DateTimeOffset.UtcNow.AddDays(-1));

        var now = DateTimeOffset.UtcNow;

        DirectoryMapping.Apply(person, mapped, now, out _);

        // Last-synced is evidence the person was still in the directory on this run, which is exactly what the
        // deactivation pass and the staleness gauge depend on.
        person.LastSyncedAt.ShouldBe(now);
    }

    // --- Org tree ------------------------------------------------------------------------------------------------

    [Fact]
    public void Maps_departments_and_their_units_from_the_group_tree()
    {
        var org = DirectoryMapping.MapOrg([
            Group("dsi", DepartmentId, [("infra", UnitId, "Infrastructure & Réseaux")]),
        ]);

        org.Departments.Count.ShouldBe(1);
        org.Departments[0].Code.ShouldBe("dsi");
        org.Departments[0].NameKey.ShouldBe("directory.department.dsi");

        org.Units.Count.ShouldBe(1);
        org.Units[0].Id.ShouldBe(UnitId);
        org.Units[0].DepartmentId.ShouldBe(DepartmentId);
        org.Units[0].Name.ShouldBe("Infrastructure & Réseaux");
    }

    [Fact]
    public void Ignores_a_group_that_declares_no_department_id()
    {
        // Keycloak realms accumulate groups for all sorts of reasons. Only the ones carrying our attributes are
        // part of the org; the rest are somebody else's concern.
        var org = DirectoryMapping.MapOrg([
            new KeycloakGroup(Guid.CreateVersion7(), "unrelated", "/unrelated", null, null),
        ]);

        org.Departments.ShouldBeEmpty();
        org.Units.ShouldBeEmpty();
    }

    // --- Builders ------------------------------------------------------------------------------------------------

    private static KeycloakUser User(
        string? firstName = "Camille",
        string? lastName = "Villeneuve",
        bool includeUnit = true,
        string? rawUnitId = null,
        Guid[]? departmentIds = null,
        string locale = "fr",
        bool enabled = true)
    {
        var attributes = new Dictionary<string, List<string>>
        {
            ["locale"] = [locale],
            ["functional_role"] = ["architecte"],
        };

        // rawUnitId exists to feed the mapper a malformed value; includeUnit omits the attribute entirely. They
        // are different failures and the mapper must reject both.
        if (rawUnitId is not null)
        {
            attributes["unit_id"] = [rawUnitId];
        }
        else if (includeUnit)
        {
            attributes["unit_id"] = [UnitId.ToString()];
        }

        var departments = departmentIds ?? [DepartmentId];

        if (departments.Length > 0)
        {
            attributes["dept_ids"] = [.. departments.Select(id => id.ToString())];
        }

        return new KeycloakUser(
            Guid.Parse("c0000000-0000-0000-0000-000000000001"),
            "camille.villeneuve",
            firstName,
            lastName,
            "camille.villeneuve@cracra.local",
            enabled,
            attributes);
    }

    private static KeycloakGroup Group(
        string name,
        Guid departmentId,
        (string Name, Guid Id, string Label)[] units) =>
        new(
            Guid.CreateVersion7(),
            name,
            $"/{name}",
            new Dictionary<string, List<string>> { ["department_id"] = [departmentId.ToString()] },
            [
                .. units.Select(unit => new KeycloakGroup(
                    Guid.CreateVersion7(),
                    unit.Name,
                    $"/{name}/{unit.Name}",
                    new Dictionary<string, List<string>>
                    {
                        ["unit_id"] = [unit.Id.ToString()],
                        ["unit_name"] = [unit.Label],
                    },
                    null)),
            ]);
}
