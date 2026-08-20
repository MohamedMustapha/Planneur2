using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Kudos.Domain;
using FluentValidation;

namespace Cracra.Modules.Kudos.Application;

// =================================================================================================================
// One command. The module writes exactly one kind of row and never rewrites it, so this is the whole write side.
//
// What the aggregate cannot see for itself is supplied here: where the receiver sits, which department's rules
// price the kudo, why the giver is allowed to give it, and what both parties' months already hold.
// =================================================================================================================

public sealed record GiveKudoCommand(
    Guid ToPersonId,
    string Category,
    string? Message) : IRequest<GiveKudoResult>, ITransactionalRequest;

/// <summary>
/// The kudo, and what giving it changed.
/// </summary>
/// <remarks>
/// The remaining allowance comes back with the write rather than being fetched afterwards. The cap only works as a
/// visible budget — somebody who discovers it by being refused their eleventh has been told about it too late.
/// </remarks>
public sealed record GiveKudoResult(
    Guid Id,
    string Category,
    int Points,
    bool ShowsPoints,
    int RemainingThisMonth,
    IReadOnlyList<string> EarnedBadgeCodes);

public sealed class GiveKudoValidator : AbstractValidator<GiveKudoCommand>
{
    public GiveKudoValidator()
    {
        RuleFor(command => command.ToPersonId).NotEmpty();
        RuleFor(command => command.Category).NotEmpty().MaximumLength(64);

        // Length only. Whether a message is required, and what "empty" means once trimmed, is the aggregate's
        // call — it is the same rule wherever a kudo comes from, and a validator is only reached over HTTP.
        RuleFor(command => command.Message).MaximumLength(Kudo.MaximumMessageLength);
    }
}

internal sealed class GiveKudoHandler(
    IKudoRepository repository,
    IDirectoryPort directory,
    KudoEligibility eligibility,
    KudosTelemetry telemetry,
    IUserContext user) : IRequestHandler<GiveKudoCommand, GiveKudoResult>
{
    public async Task<GiveKudoResult> Handle(GiveKudoCommand request, CancellationToken ct)
    {
        var placement = await directory.GetPlacementAsync(request.ToPersonId, ct)
            ?? throw new DomainRuleViolationException("That person is not in the directory.");

        // The receiver's department prices the kudo, names the categories and owns the ladder, because that is
        // what it will be counted and displayed under — on their unit's board, in their unit's report.
        var rules = await directory.GetRulesAsync(placement.DepartmentId, ct);

        // The giver's department owns the cap. It constrains the giver's behaviour, so spending a neighbouring
        // department's allowance by reaching across a shared project is not something the rule should permit.
        var cap = (await GiverRulesAsync(ct)).MonthlyCapPerGiver;

        var month = KudoMonth.Of(DateTimeOffset.UtcNow);

        var relation = await eligibility.ResolveAsync(
            request.ToPersonId, placement.UnitId, placement.DepartmentId, ct);

        var givenThisMonth = await repository.GivenInMonthAsync(user.UserId, month, ct);
        var tally = await repository.TallyForAsync(request.ToPersonId, ct);

        Kudo kudo;

        try
        {
            kudo = Kudo.Give(
                user.UserId,
                request.ToPersonId,
                placement.UnitId,
                placement.DepartmentId,
                rules,
                request.Category,
                request.Message,
                relation,
                givenThisMonth,
                cap,
                tally,
                DateTimeOffset.UtcNow);
        }
        catch (DomainRuleViolationException)
        {
            // Counted before it is rethrown. A refusal rate that climbs is the signal that a department's cap is
            // set below how much its people actually thank each other, and nothing else would show it.
            telemetry.Refused(RefusalReason(request, relation, givenThisMonth, cap));
            throw;
        }

        await repository.AddAsync(kudo, ct);

        telemetry.Given(placement.DepartmentId, kudo.Category, KudoRules.ToCode(rules.Mode));

        var badges = kudo.DomainEvents.OfType<BadgeAwarded>().ToList();

        foreach (var badge in badges)
        {
            telemetry.Badge(placement.DepartmentId, badge.BadgeCode);
        }

        return new GiveKudoResult(
            kudo.Id,
            kudo.Category,
            // Zero unless the department shows points, whatever the row holds. The mode is a promise about what
            // people see, and this is the response the form reads to decide whether to say anything about a score.
            rules.ShowsPoints ? kudo.Points : 0,
            rules.ShowsPoints,
            Math.Max(0, cap - givenThisMonth - 1),
            [.. badges.Select(badge => badge.BadgeCode)]);
    }

    /// <summary>
    /// The giver's own department's rules, for the cap.
    /// </summary>
    /// <remarks>
    /// Somebody in no department — which the directory allows, briefly, between a sync and a placement — gets the
    /// platform default rather than an error. They are still allowed to thank a project teammate.
    /// </remarks>
    private async Task<KudoRules> GiverRulesAsync(CancellationToken ct) =>
        user.DepartmentIds.Count > 0
            ? await directory.GetRulesAsync(user.DepartmentIds[0], ct)
            : KudoRules.Default;

    private string RefusalReason(GiveKudoCommand request, KudoRelation relation, int given, int cap) =>
        request.ToPersonId == user.UserId ? "self"
        : given >= cap ? "cap"
        : relation is KudoRelation.None ? "eligibility"
        : "category";
}
