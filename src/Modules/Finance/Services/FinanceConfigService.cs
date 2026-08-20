using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Finance.Data;
using Cracra.Modules.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Finance.Services;

/// <summary>How a department treats each bucket. Carries the department so a caller knows whose rule they got.</summary>
public sealed record CapexOpexRuleView(
    Guid DepartmentId,
    string BuildTreatment,
    string RunTreatment,
    string QolTreatment,
    string AdminTreatment,

    /// <summary>False where no row exists and these are the platform's defaults. The editor shows the difference.</summary>
    bool Configured)
{
    internal CapexOpexRule ToDomain() => new()
    {
        Id = Guid.Empty,
        DepartmentId = DepartmentId,
        BuildTreatment = BuildTreatment,
        RunTreatment = RunTreatment,
        QolTreatment = QolTreatment,
        AdminTreatment = AdminTreatment,
    };
}

public sealed record RuleRequest(
    Guid DepartmentId,
    string BuildTreatment,
    string RunTreatment,
    string QolTreatment,
    string AdminTreatment);

public sealed record RateCardView(
    Guid Id,
    Guid DepartmentId,
    Guid FunctionalRoleId,

    /// <summary>The role's own code — <c>dev</c>, <c>architecte</c>. Resolved for display; the id is the key.</summary>
    string? FunctionalRoleCode,
    decimal HourlyRate,
    string Currency,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo)
{
    public bool CoversDay(DateOnly day) =>
        day >= EffectiveFrom && (EffectiveTo is not { } end || day < end);
}

public sealed record RateCardRequest(
    Guid DepartmentId,
    Guid FunctionalRoleId,
    decimal HourlyRate,
    string Currency,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public interface IFinanceConfigService
{
    /// <summary>
    /// The department's rule, or the platform defaults where it has never configured one.
    /// </summary>
    /// <remarks>
    /// A null department means the caller's own, resolved from the session here rather than at the endpoint: an
    /// endpoint in this archetype delegates, and "whose department did they mean" is a decision, not a mapping.
    /// </remarks>
    Task<CapexOpexRuleView> GetRuleAsync(Guid? departmentId, CancellationToken ct);

    Task<CapexOpexRuleView> SaveRuleAsync(RuleRequest request, CancellationToken ct);

    Task<IReadOnlyList<RateCardView>> GetRateCardsAsync(Guid? departmentId, CancellationToken ct);

    Task<RateCardView> SaveRateCardAsync(Guid? id, RateCardRequest request, CancellationToken ct);

    Task DeleteRateCardAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// The two knobs, read and written.
/// </summary>
/// <remarks>
/// <para>
/// Both tables carry a department and RLS decides which rows a caller reaches; there is no department filter in
/// this code beyond the ones a caller explicitly asked for. The endpoint policy says "a head may call me", the
/// <c>access.can_write_department_config</c> predicate says "of this department", and this class says neither.
/// </para>
/// <para>
/// A missing rule is not an error. Every department has a treatment — the platform's — from the moment it exists,
/// and requiring somebody to press save before the view works would make the first visit look broken.
/// </para>
/// </remarks>
internal sealed class FinanceConfigService(
    FinanceDbContext context,
    Cracra.Modules.Directory.Contracts.IDirectoryReader directory,
    IUserContext user) : IFinanceConfigService
{
    public async Task<CapexOpexRuleView> GetRuleAsync(Guid? departmentId, CancellationToken ct)
    {
        var scope = departmentId ?? user.DepartmentIds.FirstOrDefault();

        var stored = await context.Rules
            .FirstOrDefaultAsync(rule => rule.DepartmentId == scope, ct);

        return stored is null ? Defaults(scope) : Project(stored);
    }

    public async Task<CapexOpexRuleView> SaveRuleAsync(RuleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;

        var rule = await context.Rules
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.DepartmentId == request.DepartmentId, ct);

        if (rule is null)
        {
            rule = new CapexOpexRule
            {
                Id = Guid.CreateVersion7(),
                DepartmentId = request.DepartmentId,
                CreatedAt = now,
            };

            context.Rules.Add(rule);
        }

        rule.BuildTreatment = Treatments.Normalize(request.BuildTreatment);
        rule.RunTreatment = Treatments.Normalize(request.RunTreatment);
        rule.QolTreatment = Treatments.Normalize(request.QolTreatment);
        rule.AdminTreatment = Treatments.Normalize(request.AdminTreatment);
        rule.ModifiedBy = user.UserId;
        rule.ModifiedAt = now;

        await SaveAsync(ct);

        return Project(rule);
    }

    public async Task<IReadOnlyList<RateCardView>> GetRateCardsAsync(Guid? departmentId, CancellationToken ct)
    {
        var cards = await context.RateCards
            .Where(card => departmentId == null || card.DepartmentId == departmentId)
            // Newest first: the valuation takes the first card that covers a day, and a report is far more often
            // about a recent period than a historical one.
            .OrderByDescending(card => card.EffectiveFrom)
            .ThenBy(card => card.FunctionalRoleId)
            .ToListAsync(ct);

        if (cards.Count == 0)
        {
            return [];
        }

        var codes = await directory.GetFunctionalRoleCodesAsync(
            [.. cards.Select(card => card.FunctionalRoleId).Distinct()], ct);

        return [.. cards.Select(card => Project(card, codes.GetValueOrDefault(card.FunctionalRoleId)))];
    }

    public async Task<RateCardView> SaveRateCardAsync(Guid? id, RateCardRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.HourlyRate < 0m)
        {
            throw new DomainRuleViolationException("An hourly rate cannot be negative.");
        }

        if (request.EffectiveTo is { } end && end <= request.EffectiveFrom)
        {
            throw new DomainRuleViolationException("A rate card must end after it starts.");
        }

        var currency = Currency(request.Currency);
        var now = DateTimeOffset.UtcNow;

        var card = id is { } existing
            ? await context.RateCards.AsTracking().FirstOrDefaultAsync(row => row.Id == existing, ct)
                ?? throw new ResourceNotFoundException($"Rate card {existing} was not found.")
            : null;

        if (card is null)
        {
            card = new RateCard
            {
                Id = Guid.CreateVersion7(),
                DepartmentId = request.DepartmentId,
                FunctionalRoleId = request.FunctionalRoleId,
                CreatedBy = user.UserId,
                CreatedAt = now,
            };

            context.RateCards.Add(card);
        }

        card.DepartmentId = request.DepartmentId;
        card.FunctionalRoleId = request.FunctionalRoleId;
        card.HourlyRate = request.HourlyRate;
        card.Currency = currency;
        card.EffectiveFrom = request.EffectiveFrom;
        card.EffectiveTo = request.EffectiveTo;
        card.ModifiedAt = now;

        await EnsureNoOverlapAsync(card, ct);
        await SaveAsync(ct);

        var codes = await directory.GetFunctionalRoleCodesAsync([card.FunctionalRoleId], ct);

        return Project(card, codes.GetValueOrDefault(card.FunctionalRoleId));
    }

    public async Task DeleteRateCardAsync(Guid id, CancellationToken ct)
    {
        var card = await context.RateCards.AsTracking().FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw new ResourceNotFoundException($"Rate card {id} was not found.");

        context.RateCards.Remove(card);

        await SaveAsync(ct);
    }

    /// <summary>
    /// Refuses two cards that would both price the same role on the same day.
    /// </summary>
    /// <remarks>
    /// Checked here rather than left to a database constraint because Postgres would need an exclusion constraint
    /// over a range type to express it, and that would mean modelling the dates as a <c>daterange</c> — a shape
    /// nothing else in the platform uses and every query would have to unpack. The cost is that two administrators
    /// saving at the same instant could both pass; the window is small, the audience is a handful of heads, and
    /// the consequence is a view somebody re-saves rather than data loss.
    /// </remarks>
    private async Task EnsureNoOverlapAsync(RateCard card, CancellationToken ct)
    {
        var siblings = await context.RateCards
            .Where(other => other.DepartmentId == card.DepartmentId
                && other.FunctionalRoleId == card.FunctionalRoleId
                && other.Id != card.Id)
            .ToListAsync(ct);

        if (siblings.Any(card.Overlaps))
        {
            throw new DomainRuleViolationException(
                "Another rate card already covers part of that period for this role.");
        }
    }

    /// <summary>
    /// Saves, and reads a refusal by row-level security as one.
    /// </summary>
    /// <remarks>
    /// Postgres filters a write its policy rejects rather than raising, so an INSERT that violates the WITH CHECK
    /// does throw — but an UPDATE simply matches nothing. Treating a zero row count as a refusal is what turns
    /// "a head edited another department's rate card" into a 404 rather than a silent no-op that reports success.
    /// </remarks>
    private async Task SaveAsync(CancellationToken ct)
    {
        var written = await context.SaveChangesAsync(ct);

        if (written == 0)
        {
            throw new ResourceNotFoundException("The row was not written; it is outside your scope.");
        }
    }

    private static string Currency(string? currency)
    {
        var candidate = (currency ?? "EUR").Trim().ToUpperInvariant();

        // The same four S3 accepts for a project cost. A fifth here would produce a rate card that can price
        // hours in a currency no project can be costed in.
        return candidate is "EUR" or "USD" or "GBP" or "CHF"
            ? candidate
            : throw new DomainRuleViolationException($"'{currency}' is not a currency the platform accepts.");
    }

    private static CapexOpexRuleView Defaults(Guid departmentId)
    {
        var defaults = CapexOpexRule.Default(departmentId);

        return new CapexOpexRuleView(
            departmentId,
            defaults.BuildTreatment,
            defaults.RunTreatment,
            defaults.QolTreatment,
            defaults.AdminTreatment,
            Configured: false);
    }

    private static CapexOpexRuleView Project(CapexOpexRule rule) => new(
        rule.DepartmentId,
        rule.BuildTreatment,
        rule.RunTreatment,
        rule.QolTreatment,
        rule.AdminTreatment,
        Configured: true);

    private static RateCardView Project(RateCard card, string? roleCode) => new(
        card.Id,
        card.DepartmentId,
        card.FunctionalRoleId,
        roleCode,
        card.HourlyRate,
        card.Currency,
        card.EffectiveFrom,
        card.EffectiveTo);
}
