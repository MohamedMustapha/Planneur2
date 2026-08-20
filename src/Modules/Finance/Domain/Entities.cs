using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Finance.Domain;

// =================================================================================================================
// Two tables and a lot of arithmetic. Finance is a 2-layer query module (conventions.md §2): almost everything it
// shows is derived from rows other modules own, and the only things it stores are the two knobs that decide how
// the derivation runs — how each activity bucket is treated, and what an hour is worth.
//
// Nothing here is an aggregate and nothing has a lifecycle. A rate card is superseded rather than transitioned; a
// rule is edited. If either ever grows a state machine, that is the signal to revisit the archetype.
// =================================================================================================================

/// <summary>
/// What a bucket's cost and effort become in the capitalization view.
/// </summary>
/// <remarks>
/// Codes, never localized text, and deliberately only three. The spec's out-of-scope section is the reason: this
/// is a management view, not the books. A fourth treatment — "partially capitalizable at 60%" — is the shape of
/// an amortization rule, and the moment one appears the platform is pretending to be an accounting system.
/// </remarks>
public static class Treatments
{
    /// <summary>Capitalizable investment: BUILD, by default.</summary>
    public const string Capex = "capex";

    /// <summary>Operating expenditure: RUN and quality-of-life work, by default.</summary>
    public const string Opex = "opex";

    /// <summary>Counted in hours but left out of both columns. Recruitment and administration, by default.</summary>
    public const string Excluded = "excluded";

    public static readonly IReadOnlyList<string> All = [Capex, Opex, Excluded];

    public static bool IsKnown(string? treatment) =>
        treatment is not null && All.Contains(treatment, StringComparer.Ordinal);

    public static string Normalize(string? treatment)
    {
        var candidate = (treatment ?? string.Empty).Trim().ToLowerInvariant();

        return IsKnown(candidate)
            ? candidate
            : throw new DomainRuleViolationException($"'{treatment}' is not a capex/opex treatment.");
    }
}

/// <summary>
/// The four buckets the platform reports in, named as this module refers to them.
/// </summary>
/// <remarks>
/// <para>
/// These are S5's canonical activity types under different names — <c>build</c> is <c>project-build</c> — and the
/// mapping is spelled out in <see cref="ActivityBucket"/> rather than inferred. Two vocabularies for one idea is
/// a cost, and it is paid deliberately: the spec names the rule's columns <c>build_treatment</c> and
/// <c>run_treatment</c>, which is what a finance person calls them, and a column called
/// <c>project_build_treatment</c> would be this module speaking S5's dialect to an audience that does not use it.
/// </para>
/// <para>
/// A department's own subtypes resolve to their canonical parent before they get here. That resolution belongs to
/// Activities, which owns the merge; see <c>IActivityTaxonomyReader</c>.
/// </para>
/// </remarks>
public static class Buckets
{
    public const string Build = "build";
    public const string Run = "run";
    public const string QualityOfLife = "qol";
    public const string Admin = "admin";

    public static readonly IReadOnlyList<string> All = [Build, Run, QualityOfLife, Admin];
}

/// <summary>Maps S5's canonical activity codes onto this module's bucket names.</summary>
public static class ActivityBucket
{
    private static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["project-build"] = Buckets.Build,
        ["project-run"] = Buckets.Run,
        ["quality-of-life"] = Buckets.QualityOfLife,
        ["recruitment-admin"] = Buckets.Admin,
    };

    /// <summary>
    /// The bucket a canonical activity code belongs to, or null for a code this module does not recognize.
    /// </summary>
    /// <remarks>
    /// Null rather than a default bucket. A code that reaches here unrecognized means S5 grew a fifth canonical
    /// type and nobody told Finance — and silently filing those hours under quality-of-life would put real work
    /// in the opex column and leave nothing to notice. Unrecognized hours are reported as unclassified instead.
    /// </remarks>
    public static string? For(string? canonicalCode) =>
        canonicalCode is not null && ByCode.TryGetValue(canonicalCode, out var bucket) ? bucket : null;
}

/// <summary>
/// How one department treats each bucket.
/// </summary>
/// <remarks>
/// One row per department, and the absence of a row is not a missing configuration — it is the default, which is
/// the treatment every department starts with and most will keep. The service materializes a default rather than
/// requiring somebody to press save before the view works.
/// </remarks>
public sealed class CapexOpexRule
{
    public required Guid Id { get; init; }

    public required Guid DepartmentId { get; set; }

    public string BuildTreatment { get; set; } = Treatments.Capex;

    public string RunTreatment { get; set; } = Treatments.Opex;

    public string QolTreatment { get; set; } = Treatments.Opex;

    /// <summary>Recruitment and administration: counted in hours, in neither column. See <see cref="Treatments"/>.</summary>
    public string AdminTreatment { get; set; } = Treatments.Excluded;

    public Guid ModifiedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>The platform's defaults, for a department that has never configured one.</summary>
    public static CapexOpexRule Default(Guid departmentId) => new()
    {
        Id = Guid.Empty,
        DepartmentId = departmentId,
    };

    public string TreatmentFor(string bucket) => bucket switch
    {
        Buckets.Build => BuildTreatment,
        Buckets.Run => RunTreatment,
        Buckets.QualityOfLife => QolTreatment,
        Buckets.Admin => AdminTreatment,
        _ => Treatments.Excluded,
    };
}

/// <summary>
/// What an hour of one functional role costs, over a period.
/// </summary>
/// <remarks>
/// <para>
/// Optional throughout. A department that has entered no rates gets a view in hours only, which is the spec's
/// stated fallback and also the honest one: a valuation built on a rate somebody guessed is worse than no
/// valuation, because it looks like a number.
/// </para>
/// <para>
/// Effective-dated, because rates change and last quarter's report must not move when this year's rates are
/// entered. <see cref="EffectiveTo"/> is exclusive — a card ending on the day the next begins is the ordinary
/// case, and an inclusive end would make every handover either a gap or an overlap.
/// </para>
/// </remarks>
public sealed class RateCard
{
    public required Guid Id { get; init; }

    public required Guid DepartmentId { get; set; }

    /// <summary>The LDAP-derived job identity from S1 — dev, architecte, comptable.</summary>
    public required Guid FunctionalRoleId { get; set; }

    public decimal HourlyRate { get; set; }

    public string Currency { get; set; } = "EUR";

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Exclusive. Null means "still current".</summary>
    public DateOnly? EffectiveTo { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>True where this card governs the given day.</summary>
    public bool CoversDay(DateOnly day) =>
        day >= EffectiveFrom && (EffectiveTo is not { } end || day < end);

    /// <summary>True where two cards for the same role would both govern some day.</summary>
    /// <remarks>
    /// Checked before a write, because an overlap has no defensible resolution at read time: valuing an hour with
    /// whichever row the database happened to return first would make the same report produce different money on
    /// different days.
    /// </remarks>
    public bool Overlaps(RateCard other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var startsBeforeOtherEnds = other.EffectiveTo is not { } otherEnd || EffectiveFrom < otherEnd;
        var otherStartsBeforeThisEnds = EffectiveTo is not { } end || other.EffectiveFrom < end;

        return startsBeforeOtherEnds && otherStartsBeforeThisEnds;
    }
}
