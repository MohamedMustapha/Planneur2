namespace Cracra.Modules.Finance.Domain;

/// <summary>
/// The split, as arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// Every figure the capitalization view shows is produced here, from hours and amounts the caller has already
/// been allowed to read. No database, no clock, no ports — which is what makes the interesting cases (a mixed
/// project, an unvalued hour, a bucket somebody excluded) testable exhaustively rather than by inspection.
/// </para>
/// <para>
/// The accumulator is mutable and the result is not. Folding a few thousand activity rows into six numbers is the
/// one place in this module where allocation per row would be felt, and a caller cannot observe the mutation:
/// <see cref="Build"/> hands back a record and the accumulator is discarded.
/// </para>
/// </remarks>
public sealed class CapexOpexSplit(CapexOpexRule rule)
{
    private readonly Dictionary<string, BucketAccumulator> _buckets =
        Buckets.All.ToDictionary(bucket => bucket, _ => new BucketAccumulator(), StringComparer.Ordinal);

    private decimal _unclassifiedHours;
    private decimal _manualCapex;
    private decimal _manualOpex;
    private decimal _manualUnallocated;
    private bool _anyHourValued;

    /// <summary>
    /// Adds logged effort.
    /// </summary>
    /// <param name="bucket">The canonical bucket, or null where the code did not resolve to one.</param>
    /// <param name="hours">Hours logged.</param>
    /// <param name="cost">What those hours are worth, or null where no rate card covers them.</param>
    /// <remarks>
    /// Unvalued hours still count as hours. A department with no rate card gets a complete effort view and an
    /// empty money view, which is the spec's fallback; a department with a partial rate card gets both, plus the
    /// <see cref="SplitTotals.EffortValued"/> flag so the screen can say the money is incomplete rather than
    /// letting somebody read a half-valued total as a whole one.
    /// </remarks>
    public CapexOpexSplit AddEffort(string? bucket, decimal hours, decimal? cost)
    {
        if (hours == 0m)
        {
            return this;
        }

        if (bucket is null || !_buckets.TryGetValue(bucket, out var accumulator))
        {
            // A code Activities knows and this module does not. Reported, never filed: putting it in a bucket by
            // guesswork is how real work ends up in the wrong column with nothing to notice.
            _unclassifiedHours += hours;

            return this;
        }

        accumulator.Hours += hours;

        if (cost is { } amount)
        {
            accumulator.Cost += amount;
            _anyHourValued = true;
        }
        else
        {
            accumulator.UnvaluedHours += hours;
        }

        return this;
    }

    /// <summary>
    /// Adds a project's manually entered cost, treated according to how the project is classified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A BUILD project's cost follows the build treatment and a RUN project's the run treatment, which is the
    /// only reading of the rule that makes the manual and the effort columns say the same thing about one
    /// project.
    /// </para>
    /// <para>
    /// A <c>mixed</c> project is apportioned by its own BUILD-to-RUN hours in the period, and reported as
    /// unallocated when it logged none. The alternatives were both worse: splitting it in half invents a ratio,
    /// and defaulting it to opex would quietly understate capex for every mixed project nobody logged against —
    /// which is a number somebody reports upward.
    /// </para>
    /// </remarks>
    public CapexOpexSplit AddManualCost(string? classification, decimal amount, decimal buildHours, decimal runHours)
    {
        if (amount == 0m)
        {
            return this;
        }

        switch ((classification ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "build":
                Apply(rule.BuildTreatment, amount);
                break;

            case "run":
                Apply(rule.RunTreatment, amount);
                break;

            case "mixed":
                Apportion(amount, buildHours, runHours);
                break;

            default:
                _manualUnallocated += amount;
                break;
        }

        return this;
    }

    public SplitTotals Build()
    {
        var byBucket = Buckets.All
            .Select(bucket => new BucketTotal(
                bucket,
                rule.TreatmentFor(bucket),
                _buckets[bucket].Hours,
                _buckets[bucket].Cost,
                _buckets[bucket].UnvaluedHours))
            .ToList();

        decimal EffortIn(string treatment) => byBucket
            .Where(total => string.Equals(total.Treatment, treatment, StringComparison.Ordinal))
            .Sum(total => total.EffortCost);

        decimal HoursIn(string treatment) => byBucket
            .Where(total => string.Equals(total.Treatment, treatment, StringComparison.Ordinal))
            .Sum(total => total.Hours);

        return new SplitTotals(
            CapexAmount: _manualCapex + EffortIn(Treatments.Capex),
            OpexAmount: _manualOpex + EffortIn(Treatments.Opex),
            ExcludedAmount: EffortIn(Treatments.Excluded),
            UnallocatedAmount: _manualUnallocated,
            ManualCapex: _manualCapex,
            ManualOpex: _manualOpex,
            EffortCapex: EffortIn(Treatments.Capex),
            EffortOpex: EffortIn(Treatments.Opex),
            CapexHours: HoursIn(Treatments.Capex),
            OpexHours: HoursIn(Treatments.Opex),
            ExcludedHours: HoursIn(Treatments.Excluded),
            UnclassifiedHours: _unclassifiedHours,

            // "Some hour got a rate" rather than "every hour did". A view that claimed to be valued only when
            // the rate card were complete would show nothing at all for the common case of a department that has
            // priced its developers and not yet its architects.
            EffortValued: _anyHourValued,
            ByBucket: byBucket);
    }

    private void Apportion(decimal amount, decimal buildHours, decimal runHours)
    {
        var total = buildHours + runHours;

        if (total <= 0m)
        {
            _manualUnallocated += amount;

            return;
        }

        // Rounded to the currency's minor unit, with the remainder going to the run side so the two halves add
        // back to the original to the cent. Losing a cent on every mixed project is the kind of drift that makes
        // a total disagree with the sum of its rows.
        var capexShare = Math.Round(amount * (buildHours / total), 2, MidpointRounding.ToEven);

        Apply(rule.BuildTreatment, capexShare);
        Apply(rule.RunTreatment, amount - capexShare);
    }

    private void Apply(string treatment, decimal amount)
    {
        switch (treatment)
        {
            case Treatments.Capex:
                _manualCapex += amount;
                break;

            case Treatments.Opex:
                _manualOpex += amount;
                break;

            default:
                // Excluded means excluded from both columns, including the manual cost of a project whose bucket
                // a department chose to leave out. It is still visible as unallocated, so the money does not
                // simply disappear from a view somebody is reconciling.
                _manualUnallocated += amount;
                break;
        }
    }

    private sealed class BucketAccumulator
    {
        public decimal Hours { get; set; }

        public decimal Cost { get; set; }

        public decimal UnvaluedHours { get; set; }
    }
}

/// <summary>One bucket's contribution, and how the department chose to treat it.</summary>
public sealed record BucketTotal(
    string Bucket,
    string Treatment,
    decimal Hours,
    decimal EffortCost,

    /// <summary>Hours no rate card covered. Non-zero means <see cref="EffortCost"/> understates the bucket.</summary>
    decimal UnvaluedHours);

/// <summary>
/// The split.
/// </summary>
/// <remarks>
/// Manual and effort-derived money are carried both separately and combined, because the screen shows them side
/// by side and the two answer different questions: what was budgeted, and what it cost in people. A single total
/// would let a reader double-count a project whose manual cost was itself derived from effort.
/// </remarks>
public sealed record SplitTotals(
    decimal CapexAmount,
    decimal OpexAmount,
    decimal ExcludedAmount,

    /// <summary>Money the rules left in neither column: excluded buckets, and mixed projects with no effort.</summary>
    decimal UnallocatedAmount,
    decimal ManualCapex,
    decimal ManualOpex,
    decimal EffortCapex,
    decimal EffortOpex,
    decimal CapexHours,
    decimal OpexHours,
    decimal ExcludedHours,

    /// <summary>Hours on a type this module could not place. Zero in a healthy deployment; visible when not.</summary>
    decimal UnclassifiedHours,
    bool EffortValued,
    IReadOnlyList<BucketTotal> ByBucket)
{
    public decimal TotalHours => CapexHours + OpexHours + ExcludedHours + UnclassifiedHours;

    /// <summary>Capex as a share of the two columns, or null when there is nothing to take a share of.</summary>
    public decimal? CapexRatio =>
        CapexAmount + OpexAmount == 0m ? null : CapexAmount / (CapexAmount + OpexAmount);
}
