using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Kudos.Domain;

namespace Cracra.Modules.Kudos.Application;

/// <summary>
/// The module's own store.
/// </summary>
/// <remarks>
/// Small, because the aggregate is: kudos are written once and never amended, so there is no <c>GetAsync</c> here
/// and nothing that tracks one. What the write path genuinely needs is two counts — the giver's month, for the
/// cap, and the receiver's running tally, for the badge a kudo might cross.
/// </remarks>
public interface IKudoRepository
{
    Task AddAsync(Kudo kudo, CancellationToken ct);

    /// <summary>How many kudos this person has given in a calendar month.</summary>
    Task<int> GivenInMonthAsync(Guid giverId, KudoMonth month, CancellationToken ct);

    /// <summary>
    /// What a person has received so far, in total and by category.
    /// </summary>
    /// <remarks>
    /// Runs inside the caller's RLS session like everything else, which has one consequence worth naming. A kudo
    /// carries the <em>receiver's</em> unit, so everybody who can see one of somebody's kudos can see all of them
    /// — the tally is the same number for every colleague in the unit. The exception is the giver from another
    /// unit reaching across a shared project: they see only what they gave, so the badge announcement they trigger
    /// may fire late. The badge itself is unaffected, because it is derived from the whole record whenever anyone
    /// reads it; only the "you just earned this" moment is theirs to miss.
    /// </remarks>
    Task<KudoTally> TallyForAsync(Guid personId, CancellationToken ct);

    /// <summary>
    /// Every kudo a set of people have received, reduced to what a badge ladder reads.
    /// </summary>
    /// <remarks>
    /// One query for the whole leaderboard rather than one per row. Ordering is left to
    /// <see cref="BadgeLadder.Earned"/>, which needs chronological and says so.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<KudoRecord>>> RecordsForAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct);
}

/// <summary>What Kudos needs from the org, as a port.</summary>
public interface IDirectoryPort
{
    /// <summary>Where a person sits right now. The kudo copies the receiver's placement onto the row.</summary>
    Task<(Guid UnitId, Guid DepartmentId)?> GetPlacementAsync(Guid personId, CancellationToken ct);

    /// <summary>The department's recognition rules, merged over the platform's.</summary>
    Task<KudoRules> GetRulesAsync(Guid departmentId, CancellationToken ct);

    /// <summary>Display names, as the caller can see them. Anybody the directory hides is simply absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetPersonNamesAsync(IReadOnlyList<Guid> personIds, CancellationToken ct);

    /// <summary>
    /// Display names for people the caller demonstrably works with.
    /// </summary>
    /// <remarks>
    /// The directory is department-scoped, so a project teammate from a contributing department has no name the
    /// caller can read — and a picker built from the scoped reader would offer a list the write path then accepts
    /// people who are not on it. The authority comes from elsewhere, exactly as it does for S3's team picker: the
    /// caller reached these ids by being on a project with them, which their own RLS session proved.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, string>> GetTeammateNamesAsync(
        IReadOnlyList<Guid> personIds,
        CancellationToken ct);

    /// <summary>People in a unit or department, as the caller can see them.</summary>
    Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(Guid? unitId, Guid? departmentId, CancellationToken ct);

    /// <summary>
    /// Which department a unit belongs to.
    /// </summary>
    /// <remarks>
    /// Needed because the rules that govern a unit's board are its own department's, not the reader's. A head
    /// looking at a neighbouring unit under their own department's mode would see numbers that mean something
    /// different from what they say.
    /// </remarks>
    Task<Guid?> GetUnitDepartmentAsync(Guid unitId, CancellationToken ct);
}

/// <summary>The half of eligibility that is not about units.</summary>
public interface IProjectsPort
{
    /// <summary>Everyone currently sharing an active project with this person.</summary>
    Task<IReadOnlyList<Guid>> GetPeersAsync(Guid personId, CancellationToken ct);
}
