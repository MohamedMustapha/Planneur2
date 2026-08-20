using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Activities.Domain;

/// <summary>What a department does when someone's week goes over target.</summary>
public enum GuardrailOutcome
{
    /// <summary>Within target. Nothing to say.</summary>
    Within = 0,

    /// <summary>Over target, recorded and flagged. The default.</summary>
    Warned = 1,

    /// <summary>Over target and refused, because the department configured a hard limit.</summary>
    Blocked = 2,
}

/// <summary>
/// The result of checking a week against its department's target.
/// </summary>
public sealed record GuardrailVerdict(
    GuardrailOutcome Outcome,
    decimal RecordedHours,
    decimal TargetHours,
    decimal Overtime)
{
    public bool IsOvertime => Outcome is not GuardrailOutcome.Within;
}

/// <summary>
/// The 35-hour week, as a rule rather than as a comment.
/// </summary>
/// <remarks>
/// <para>
/// Soft by default and hard only where a department says so. That asymmetry is the whole design: the legal limit
/// is real, but so is the week somebody genuinely worked 41 hours, and a platform that refuses to record the
/// second one does not enforce the first — it just stops knowing about it. Overtime is flagged, never silently
/// dropped.
/// </para>
/// <para>
/// Only actual hours count. A planned week is an intention, and refusing to let someone sketch out 40 hours they
/// then trim would make the planning half of the module useless.
/// </para>
/// </remarks>
public static class WeeklyGuardrail
{
    public static GuardrailVerdict Check(
        decimal alreadyRecordedHours,
        decimal incomingHours,
        decimal targetHours,
        bool enforce)
    {
        if (targetHours <= 0)
        {
            // A department with no target has opted out of the guardrail rather than opted into a zero-hour week.
            return new GuardrailVerdict(GuardrailOutcome.Within, alreadyRecordedHours + incomingHours, targetHours, 0m);
        }

        var total = alreadyRecordedHours + incomingHours;

        if (total <= targetHours)
        {
            return new GuardrailVerdict(GuardrailOutcome.Within, total, targetHours, 0m);
        }

        var overtime = total - targetHours;

        return new GuardrailVerdict(
            enforce ? GuardrailOutcome.Blocked : GuardrailOutcome.Warned,
            total,
            targetHours,
            overtime);
    }

    /// <summary>
    /// Throws when the department blocks. Kept separate from <see cref="Check"/> so a caller can ask what would
    /// happen — the weekly summary does exactly that — without triggering it.
    /// </summary>
    public static GuardrailVerdict Enforce(GuardrailVerdict verdict)
    {
        if (verdict.Outcome is GuardrailOutcome.Blocked)
        {
            throw new DomainRuleViolationException(
                $"This would put the week at {verdict.RecordedHours:0.##}h against a {verdict.TargetHours:0.##}h "
                + "limit your department enforces. Adjust the hours, or ask your manager to record the overtime.");
        }

        return verdict;
    }
}
