namespace Cracra.Modules.Directory.Domain;

/// <summary>
/// A department — the top of the org tree and the unit of configuration. "Department-agnostic" in the positioning
/// means every behavioural knob hangs off <see cref="DepartmentConfig"/> rather than off code branches.
/// </summary>
public sealed class Department
{
    public required Guid Id { get; init; }

    /// <summary>Stable short code (`dsi`, `daf`). Used in keys and URLs; never localized.</summary>
    public required string Code { get; set; }

    /// <summary>
    /// Transloco key, not display text. Department names appear in three languages, and the DB stores stable codes
    /// rather than localized strings (conventions.md §5).
    /// </summary>
    public required string NameKey { get; set; }

    /// <summary>Set when a department sits under another. Null for a top-level one.</summary>
    public Guid? ParentDepartmentId { get; set; }

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public ICollection<Unit> Units { get; } = [];

    public DepartmentConfig? Config { get; set; }
}

/// <summary>What a unit mostly does. A hint for default board layouts, not an access-control input.</summary>
public enum UnitKind
{
    Delivery = 0,
    Run = 1,
    Support = 2,
    Admin = 3,
}

/// <summary>
/// A unit — the LDAP <c>fonction</c> grouping inside a department (glossary). This is the grain the visibility
/// matrix works at for members: "my unit" is what a member can see beyond their own rows.
/// </summary>
public sealed class Unit
{
    public required Guid Id { get; init; }

    public required Guid DepartmentId { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The LDAP <c>fonction</c> value this unit was created from. The reconciliation key — sync matches on this,
    /// not on the name, because names get edited and fonctions do not.
    /// </summary>
    public string? LdapFonction { get; set; }

    public UnitKind Kind { get; set; } = UnitKind.Delivery;

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public Department? Department { get; set; }
}

/// <summary>
/// A person.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is the Keycloak subject (<c>sub</c>), not a surrogate key. Every RLS predicate compares row
/// ownership against <c>access.uid()</c>, which is that same <c>sub</c> — so making them one value is what keeps
/// the policies to a single comparison instead of a join through an identity mapping table on every read.
/// </remarks>
public sealed class Person
{
    public required Guid Id { get; init; }

    /// <summary>LDAP <c>uid</c>, which Keycloak surfaces as the username. The sync reconciliation key.</summary>
    public required string LdapUid { get; set; }

    public required string DisplayName { get; set; }

    public string? Email { get; set; }

    /// <summary>
    /// The unit whose board this person lands on. Denormalized from <see cref="PersonUnit"/> because RLS reads it
    /// on nearly every query and a subquery per row is not worth the normalization.
    /// </summary>
    public Guid? PrimaryUnitId { get; set; }

    /// <summary>Denormalized for the same reason — it feeds <c>app.dept_ids</c> and the department predicates.</summary>
    public Guid? PrimaryDepartmentId { get; set; }

    public string TimeZone { get; set; } = "Europe/Paris";

    /// <summary>fr / en / es. Seeds the client's language on first load; the user may override per browser.</summary>
    public string UiLanguage { get; set; } = "fr";

    /// <summary>
    /// Set false when a person disappears from the directory. Never deleted: their logged activity, kudos and
    /// project history stay meaningful, and a hard delete would orphan every one of them.
    /// </summary>
    public bool Active { get; set; } = true;

    public DateTimeOffset? LastSyncedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public ICollection<PersonUnit> Units { get; } = [];

    public ICollection<PersonFunctionalRole> FunctionalRoles { get; } = [];
}

/// <summary>A person's membership of a unit. Several are possible; exactly one is primary.</summary>
public sealed class PersonUnit
{
    public required Guid PersonId { get; init; }

    public required Guid UnitId { get; init; }

    public required Guid DepartmentId { get; set; }

    public bool IsPrimary { get; set; }

    public Person? Person { get; set; }

    public Unit? Unit { get; set; }
}

/// <summary>
/// Job identity from LDAP — <c>dev</c>, <c>comptable</c>, <c>architecte</c>, <c>chef-de-pole</c> (glossary).
/// </summary>
/// <remarks>
/// Distinct from a contextual role and deliberately so: a functional role says what someone <em>does</em>, a
/// contextual role says what they may <em>see</em>. A tech-lead is not automatically a unit-head, and conflating
/// the two is how an org chart quietly becomes an access-control system.
/// </remarks>
public sealed class FunctionalRole
{
    public required Guid Id { get; init; }

    public required string Code { get; set; }

    /// <summary>Transloco key. Departments relabel these — see <see cref="DepartmentConfig.RoleLabelsJson"/>.</summary>
    public required string LabelKey { get; set; }

    /// <summary>Null for the seeded roles every department shares; set for one a department added.</summary>
    public Guid? DepartmentId { get; set; }

    public bool Active { get; set; } = true;
}

/// <summary>A functional role held within a specific unit — the same person can be a dev here and a lead there.</summary>
public sealed class PersonFunctionalRole
{
    public required Guid PersonId { get; init; }

    public required Guid FunctionalRoleId { get; init; }

    public required Guid UnitId { get; init; }

    public Person? Person { get; set; }

    public FunctionalRole? FunctionalRole { get; set; }
}

/// <summary>
/// The per-department knobs. This is the entire mechanism behind "department-agnostic": HR and Finance adopt the
/// platform by editing rows here, not by anyone writing an <c>if (department == …)</c>.
/// </summary>
public sealed class DepartmentConfig
{
    public required Guid Id { get; init; }

    public required Guid DepartmentId { get; init; }

    /// <summary>Activity types this department offers (S5). Validated against a schema before it is stored.</summary>
    public string ActivityTaxonomyJson { get; set; } = "{}";

    /// <summary>Overrides for functional-role labels, per language.</summary>
    public string RoleLabelsJson { get; set; } = "{}";

    /// <summary>Kudo mode and rules (S9): counter vs points/badges/leaderboard, categories, monthly cap.</summary>
    public string KudoRulesJson { get; set; } = "{}";

    /// <summary>Which board a member of this department lands on (S6).</summary>
    public string DefaultBoardLayout { get; set; } = "week";

    /// <summary>Iteration length presets offered in the quick selector (S4): 1w / 2w / 1m / custom.</summary>
    public string IterationPresetsJson { get; set; } = """["1w","2w","1m"]""";

    /// <summary>Weekly target hours. 35 by default; a department may differ, and S5 reads it.</summary>
    public decimal WeeklyTargetHours { get; set; } = 35m;

    /// <summary>When true, S5 blocks a week over target instead of soft-warning.</summary>
    public bool EnforceWeeklyTarget { get; set; }

    /// <summary>Bumped on every write. The audit trail keys off it, and the client uses it to invalidate caches.</summary>
    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public string? ModifiedBy { get; set; }

    public Department? Department { get; set; }
}

/// <summary>
/// Append-only record of every config change. S1's acceptance criteria call for versioned config; a head changing
/// their department's weekly-hours rule is exactly the kind of thing someone asks about three months later.
/// </summary>
public sealed class DepartmentConfigAudit
{
    public required Guid Id { get; init; }

    public required Guid DepartmentId { get; init; }

    public required int Version { get; init; }

    /// <summary>The full config as it was after this change. Whole snapshots, so reading history needs no replay.</summary>
    public required string SnapshotJson { get; init; }

    public required Guid ChangedBy { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }
}
