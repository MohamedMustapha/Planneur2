using Cracra.Modules.Strategy.Contracts;

namespace Cracra.Modules.Strategy.Domain;

/// <summary>
/// The rollup arithmetic (v2 §06.2), as pure functions.
/// </summary>
/// <remarks>
/// <para>
/// Pulled out of the entities rather than left as methods on them, for one reason: these are the numbers a COPIL
/// argues about, and a rule nobody can unit-test without a database is a rule nobody checks. Everything here takes
/// its inputs explicitly, including the date — "am I on track" is a question about a moment, and reading the clock
/// inside the calculation would make the answer untestable and the tests flaky at midnight.
/// </para>
/// <para>
/// AI writes narrative only; every number is computed here (S8's rule, unchanged).
/// </para>
/// </remarks>
public static class ObjectiveMath
{
    /// <summary>
    /// How far behind the even line an objective may drift before it is called at risk.
    /// </summary>
    /// <remarks>
    /// A tenth, and then a quarter. Not a knob: a deployment tuning its own thresholds is a deployment where
    /// "at risk" means something different in each branch, and the point of the word is that it means one thing
    /// at the review where four branches report together.
    /// </remarks>
    private const decimal AtRiskSlack = 0.10m;

    private const decimal OffTrackSlack = 0.25m;

    public static decimal Bounded(decimal value) => value < 0m ? 0m : value > 1m ? 1m : value;

    /// <summary>
    /// An objective's progress, 0..1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured from the baseline, not from zero, and that is the whole difference between a useful number and a
    /// flattering one. "Cut ticket handling from 40 minutes to 32" is 0% done at 40 minutes and 100% at 32; read
    /// as current/target it would start at 125% and fall as the work succeeded.
    /// </para>
    /// <para>
    /// A missing baseline is treated as zero, which is what somebody who left the field empty meant. A target
    /// equal to the baseline is a no-op objective: it reports done rather than dividing by nothing.
    /// </para>
    /// </remarks>
    public static decimal Progress(Objective objective)
    {
        ArgumentNullException.ThrowIfNull(objective);

        if (!MetricKinds.IsMeasured(objective.MetricKind))
        {
            // A milestone is done or it is not, and a qualitative objective is whatever its owner last said. Both
            // still contribute to a weighted rollup, which is why they answer in the same 0..1 currency.
            return objective.Status == ObjectiveStatuses.Done ? 1m : 0m;
        }

        if (objective.Target is not { } target)
        {
            return 0m;
        }

        var baseline = objective.Baseline ?? 0m;
        var current = objective.Current ?? baseline;

        if (target == baseline)
        {
            return 1m;
        }

        return Bounded((current - baseline) / (target - baseline));
    }

    /// <summary>
    /// Where an objective should be by now if it advanced evenly.
    /// </summary>
    /// <param name="asOf">The day being asked about.</param>
    /// <remarks>
    /// An objective with no due date has no expectation to fall short of, so it returns 0 and can never be at
    /// risk on time alone — a commitment with no date is a commitment nobody agreed when to keep.
    /// </remarks>
    public static decimal Expected(DateOnly from, DateOnly? due, DateOnly asOf)
    {
        if (due is not { } deadline)
        {
            return 0m;
        }

        if (asOf >= deadline)
        {
            return 1m;
        }

        var span = deadline.DayNumber - from.DayNumber;

        if (span <= 0)
        {
            return 1m;
        }

        var elapsed = asOf.DayNumber - from.DayNumber;

        return Bounded((decimal)elapsed / span);
    }

    /// <summary>
    /// The status the numbers imply (v2 §06.1).
    /// </summary>
    /// <remarks>
    /// Done first, because an objective that reached its target is done whatever the calendar says. Then the
    /// comparison against the even line, which is the only honest reading of "on track": 20% of the way through a
    /// year at 5% progress is behind, and the same 5% in the first week is not.
    /// </remarks>
    public static string Derive(Objective objective, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(objective);

        if (objective.StatusOverridden)
        {
            return objective.Status;
        }

        var progress = Progress(objective);

        if (progress >= 1m)
        {
            return ObjectiveStatuses.Done;
        }

        if (!MetricKinds.IsMeasured(objective.MetricKind))
        {
            // Nothing to derive from: an unfinished milestone is on track until its due date passes, and then it
            // is not. Saying "at risk" about a milestone with no reading behind it would be inventing a signal.
            return objective.Due is { } due && asOf > due
                ? ObjectiveStatuses.OffTrack
                : ObjectiveStatuses.OnTrack;
        }

        var expected = Expected(objective.CreatedOn, objective.Due, asOf);
        var shortfall = expected - progress;

        return shortfall <= AtRiskSlack
            ? ObjectiveStatuses.OnTrack
            : shortfall <= OffTrackSlack
                ? ObjectiveStatuses.AtRisk
                : ObjectiveStatuses.OffTrack;
    }
}
