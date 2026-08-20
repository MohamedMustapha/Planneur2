using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Kudos.Domain;

/// <summary>
/// Why one person is allowed to recognise another.
/// </summary>
/// <remarks>
/// Resolved by the application layer, which is the only layer that can see the org, and carried into the aggregate
/// as a reason rather than as a boolean. The reason survives into the refusal message — "you can only thank people
/// you work with" is actionable, "not eligible" is not — and it is what the eligible-peers list is built from, so
/// the rule that decides the list and the rule that guards the write are one rule.
/// </remarks>
public enum KudoRelation
{
    /// <summary>No shared ground. The write is refused.</summary>
    None = 0,

    /// <summary>Same unit. The ordinary case, and the reason the matrix lets a member see their unit at all.</summary>
    UnitPeer = 1,

    /// <summary>On the same project team, whatever unit either of them sits in.</summary>
    ProjectPeer = 2,

    /// <summary>A head recognising somebody inside the scope they run.</summary>
    HeadScope = 3,
}

/// <summary>
/// One act of recognition.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate root of the module, and immutable once written. There is no <c>Amend</c> and no <c>Withdraw</c>:
/// a kudo is a thing somebody said about somebody else at a moment, and a platform that let it be edited
/// afterwards would be storing a claim about the past that its own author can rewrite. The database agrees — the
/// table carries an insert policy and no update or delete policy at all.
/// </para>
/// <para>
/// The receiver's unit and department are copied onto the row rather than joined, exactly as S5 copies them onto
/// an activity entry: <c>access.can_read_kudo(from, to, unit, dept)</c> is evaluated per candidate row, and a
/// policy that had to reach into the Directory schema to find them would both cross a module boundary and turn
/// every read into a nested loop. The copy is also the honest answer — recognition earned in a unit somebody has
/// since left was still earned there.
/// </para>
/// </remarks>
public sealed class Kudo
{
    private readonly List<object> _domainEvents = [];

    private Kudo()
    {
        // EF Core.
    }

    public Guid Id { get; private init; }

    public Guid FromPersonId { get; private init; }

    public Guid ToPersonId { get; private init; }

    /// <summary>The receiver's unit at the moment of the kudo. RLS reads it; nothing recomputes it later.</summary>
    public Guid UnitId { get; private init; }

    /// <summary>The receiver's department. Its configuration is what priced this kudo.</summary>
    public Guid DepartmentId { get; private init; }

    public string Category { get; private init; } = string.Empty;

    public string Message { get; private init; } = string.Empty;

    /// <summary>
    /// What the category was worth when this was given.
    /// </summary>
    /// <remarks>
    /// Recorded whatever the department's mode, and deliberately so — see <see cref="KudoRules"/>. Nothing outside
    /// the module ever sees it while the mode is a bare counter.
    /// </remarks>
    public int Points { get; private init; }

    /// <summary>Denormalised from <see cref="CreatedAt"/> so the monthly cap can index rather than compute.</summary>
    public int Year { get; private init; }

    public int MonthNumber { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public KudoMonth Month => new(Year, MonthNumber);

    public IReadOnlyCollection<object> DomainEvents => _domainEvents;

    /// <summary>The longest message the form accepts. Long enough for a paragraph, short enough not to be a report.</summary>
    public const int MaximumMessageLength = 1000;

    /// <summary>
    /// Gives a kudo.
    /// </summary>
    /// <param name="rules">
    /// The <em>receiver's</em> department's rules: they decide the category vocabulary, the price and the ladder,
    /// because those are what the kudo will be displayed and counted under.
    /// </param>
    /// <param name="relation">Why the giver is allowed to, resolved against the org by the application layer.</param>
    /// <param name="givenThisMonth">How many the giver has already given this month, under their own cap.</param>
    /// <param name="monthlyCapPerGiver">
    /// The <em>giver's</em> department's cap. It constrains the giver's behaviour, so it is theirs and not the
    /// receiver's — otherwise somebody could spend a neighbouring department's allowance.
    /// </param>
    /// <param name="receiverTallyBefore">
    /// What the receiver had accumulated before this kudo, so the badge it crosses can be named at the moment it
    /// is crossed.
    /// </param>
    public static Kudo Give(
        Guid fromPersonId,
        Guid toPersonId,
        Guid receiverUnitId,
        Guid receiverDepartmentId,
        KudoRules rules,
        string categoryCode,
        string? message,
        KudoRelation relation,
        int givenThisMonth,
        int monthlyCapPerGiver,
        KudoTally receiverTallyBefore,
        DateTimeOffset now)
    {
        if (fromPersonId == Guid.Empty || toPersonId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A kudo needs a giver and a receiver.");
        }

        if (fromPersonId == toPersonId && !KudoRules.SelfKudoAllowed)
        {
            throw new DomainRuleViolationException("You cannot give yourself a kudo.");
        }

        if (relation is KudoRelation.None)
        {
            throw new DomainRuleViolationException(
                "You can only recognise someone you share a unit or a project with.");
        }

        if (givenThisMonth >= monthlyCapPerGiver)
        {
            // The cap is the only anti-abuse rule that has to refuse rather than flag. An over-target week is
            // still a true fact about somebody's hours and worth recording; an eleventh kudo in a month where ten
            // is the agreed limit is not a fact about anything, it is the limit not existing.
            throw new DomainRuleViolationException(
                $"You have given all {monthlyCapPerGiver} of this month's kudos. The count resets on the first.");
        }

        var category = rules.Get(categoryCode);

        var trimmed = (message ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            // Required, not optional. A category with nothing said about it is a click, and the annual claim view
            // — the whole reason kudos are worth keeping for a year — is made of the sentences.
            throw new DomainRuleViolationException("Say what they did. A kudo without a reason is just a tally.");
        }

        if (trimmed.Length > MaximumMessageLength)
        {
            throw new DomainRuleViolationException(
                $"Keep it under {MaximumMessageLength} characters.");
        }

        var month = KudoMonth.Of(now);

        var kudo = new Kudo
        {
            Id = Guid.CreateVersion7(),
            FromPersonId = fromPersonId,
            ToPersonId = toPersonId,
            UnitId = receiverUnitId,
            DepartmentId = receiverDepartmentId,
            Category = category.Code,
            Message = trimmed,
            Points = category.Points,
            Year = month.Year,
            MonthNumber = month.Month,
            CreatedAt = now,
        };

        kudo._domainEvents.Add(new KudoGiven(
            kudo.Id,
            fromPersonId,
            toPersonId,
            receiverUnitId,
            receiverDepartmentId,
            kudo.Category,
            kudo.Points));

        // Named at the instant they become true. The badges themselves are derived on read, so nothing here is
        // the only record of them — this is the announcement, not the award.
        var after = receiverTallyBefore.Plus(kudo.Category, kudo.Points);

        foreach (var badge in BadgeLadder.NewlyEarned(rules, receiverTallyBefore, after))
        {
            kudo._domainEvents.Add(new BadgeAwarded(toPersonId, receiverDepartmentId, badge.Code, after.Points));
        }

        return kudo;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}

// --- Domain events -----------------------------------------------------------------------------------------------

public sealed record KudoGiven(
    Guid KudoId,
    Guid FromPersonId,
    Guid ToPersonId,
    Guid UnitId,
    Guid DepartmentId,
    string Category,
    int Points);

public sealed record BadgeAwarded(Guid PersonId, Guid DepartmentId, string BadgeCode, int PointsAtAward);
