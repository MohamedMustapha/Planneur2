namespace Cracra.Modules.Projects.Domain;

/// <summary>
/// BUILD delivers new capability, RUN keeps things running, Mixed does both (glossary).
/// </summary>
/// <remarks>
/// Stored as a stable code, never as localized text. The boards colour-code by this and S11 capitalizes on it, so
/// the value is a contract rather than a label.
/// </remarks>
public enum Classification
{
    Build = 0,
    Run = 1,
    Mixed = 2,
}

/// <summary>
/// A manually entered project cost.
/// </summary>
/// <remarks>
/// Manual on purpose — S3 is explicit that the number is typed in by a human, and S11 derives capex/opex on top of
/// it rather than replacing it. A value object because "amount" and "currency" are one fact: an amount that can
/// drift from its currency is how a 50 000 € project quietly becomes a $50 000 one.
/// </remarks>
public readonly record struct Money
{
    /// <summary>The currencies the platform accepts. Deliberately short — this is an internal tool, not a bureau de change.</summary>
    public static readonly IReadOnlyList<string> AllowedCurrencies = ["EUR", "USD", "GBP", "CHF"];

    public static readonly Money Zero = new(0m, "EUR");

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A project cost cannot be negative.");
        }

        var normalized = (currency ?? string.Empty).Trim().ToUpperInvariant();

        if (!AllowedCurrencies.Contains(normalized, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(currency),
                currency,
                $"'{currency}' is not an accepted currency. Accepted: {string.Join(", ", AllowedCurrencies)}.");
        }

        Amount = amount;
        Currency = normalized;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public override string ToString() => $"{Amount:0.##} {Currency}";
}

/// <summary>
/// How much of someone's time a project has, as a percentage.
/// </summary>
/// <remarks>
/// Not validated against the sum across a person's projects. Over-allocation is real, visible on the boards, and a
/// conversation for a lead to have — refusing to record it would just mean the truth stops being written down.
/// </remarks>
public readonly record struct Allocation
{
    public static readonly Allocation Full = new(100);

    public Allocation(int percent)
    {
        if (percent is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percent),
                percent,
                "An allocation must be between 1 and 100 percent.");
        }

        Percent = percent;
    }

    public int Percent { get; }

    public override string ToString() => $"{Percent}%";
}

/// <summary>
/// The window a person is on a project for. Open-ended until they leave.
/// </summary>
/// <remarks>
/// A member is removed by closing the period, not by deleting the row. Their logged activity points at that
/// membership, and the question "who was on this in March" has to stay answerable after they have left.
/// </remarks>
public readonly record struct MembershipPeriod
{
    public MembershipPeriod(DateOnly from, DateOnly? to = null)
    {
        if (to is { } end && end < from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "A membership cannot end before it starts.");
        }

        From = from;
        To = to;
    }

    public DateOnly From { get; }

    public DateOnly? To { get; }

    public bool IsOpen => To is null;

    public bool Covers(DateOnly date) => date >= From && (To is null || date <= To);
}
