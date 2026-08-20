using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Kudos.Application;
using Cracra.Modules.Kudos.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Kudos.Api;

// =================================================================================================================
// Thin by design. Every endpoint here is Authenticated rather than role-gated: giving and receiving recognition is
// something every single person on the platform does, and who may see whose kudo is RLS's answer.
//
// The one exception is the leaderboard, and it is not a role check — it is a check on what the department asked
// for. A head who set their department to "counter" is refused it exactly like everybody else.
// =================================================================================================================

public sealed class ListKudosRequest
{
    /// <summary>me | unit | department. Narrows within what RLS already allowed.</summary>
    [QueryParam]
    public string? Scope { get; set; }

    /// <summary>received | given | all. Only meaningful for <c>me</c>.</summary>
    [QueryParam]
    public string? Direction { get; set; }

    /// <summary>month | year. Defaults to the current month.</summary>
    [QueryParam]
    public string? Period { get; set; }

    [QueryParam]
    public int? Year { get; set; }

    [QueryParam]
    public int? Month { get; set; }
}

public sealed class ListKudosEndpoint(ISender sender) : Endpoint<ListKudosRequest, IReadOnlyList<KudoView>>
{
    public override void Configure()
    {
        Get("/kudos");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Kudos")
            .WithSummary("The kudos wall: recognition in a scope over a period."));
    }

    public override async Task HandleAsync(ListKudosRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await sender.Send(
                new ListKudosQuery(request.Scope, request.Direction, request.Period, request.Year, request.Month),
                ct),
            ct);
}

public sealed class GiveKudoRequest
{
    public Guid ToPersonId { get; set; }

    /// <summary>One of the categories the receiver's department offers.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Required. What they actually did — the annual claim view is made of these.</summary>
    public string? Message { get; set; }
}

public sealed class GiveKudoEndpoint(ISender sender) : Endpoint<GiveKudoRequest, GiveKudoResult>
{
    public override void Configure()
    {
        Post("/kudos");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Kudos")
            .WithSummary("Recognise a unit peer or a project teammate."));
    }

    public override async Task HandleAsync(GiveKudoRequest request, CancellationToken ct)
    {
        var result = await sender.Send(
            new GiveKudoCommand(request.ToPersonId, request.Category, request.Message), ct);

        // 201 with what it cost and what it earned. The remaining allowance travels on the success rather than
        // being fetched afterwards, so the form can show the budget going down as it is spent.
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }
}

public sealed class KudoRulesRequest
{
    /// <summary>The prospective receiver. Their department decides the categories and the price.</summary>
    [QueryParam]
    public Guid? PersonId { get; set; }
}

/// <summary>Everything the give-kudo modal needs to draw itself, in one call.</summary>
public sealed class GetKudoRulesEndpoint(ISender sender) : Endpoint<KudoRulesRequest, KudoRulesView>
{
    public override void Configure()
    {
        Get("/kudos/rules");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Kudos")
            .WithSummary("Categories on offer, the department's mode, and this month's remaining allowance."));
    }

    public override async Task HandleAsync(KudoRulesRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetKudoRulesQuery(request.PersonId), ct), ct);
}

/// <summary>
/// Who the caller may recognise.
/// </summary>
/// <remarks>
/// Takes no scope parameter. The list <em>is</em> the eligibility rule, resolved for the caller and nobody else —
/// a parameter here would be an endpoint that tells one person who somebody else works with.
/// </remarks>
public sealed class GetEligiblePeersEndpoint(ISender sender) : EndpointWithoutRequest<IReadOnlyList<EligiblePeer>>
{
    public override void Configure()
    {
        Get("/kudos/eligible");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Kudos")
            .WithSummary("Unit peers and project teammates the caller may recognise."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetEligiblePeersQuery(), ct), ct);
}

public sealed class KudosScopeRequest
{
    /// <summary>unit | department. Defaults to the caller's own unit.</summary>
    [QueryParam]
    public string? Scope { get; set; }

    [QueryParam]
    public Guid? ScopeId { get; set; }

    /// <summary>month | year. Defaults to the current month.</summary>
    [QueryParam]
    public string? Period { get; set; }

    [QueryParam]
    public int? Year { get; set; }

    [QueryParam]
    public int? Month { get; set; }
}

/// <summary>The monthly totals the team board shows. Works in every mode, ranks nobody.</summary>
public sealed class GetKudosSummaryEndpoint(ISender sender) : Endpoint<KudosScopeRequest, KudosSummary>
{
    public override void Configure()
    {
        Get("/kudos/summary");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Kudos")
            .WithSummary("Per-person recognition counts for a unit or department over a period."));
    }

    public override async Task HandleAsync(KudosScopeRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await sender.Send(
                new GetKudosSummaryQuery(request.Scope, request.ScopeId, request.Period, request.Year, request.Month),
                ct),
            ct);
}

/// <summary>
/// The ranked board, where a department enabled one.
/// </summary>
/// <remarks>
/// 403 where it did not, rather than an empty list: "nobody has been recognised here" and "we do not rank people
/// here" are different statements, and only one of them is true.
/// </remarks>
public sealed class GetLeaderboardEndpoint(ISender sender) : Endpoint<KudosScopeRequest, LeaderboardView>
{
    public override void Configure()
    {
        Get("/kudos/leaderboard");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Kudos")
            .WithSummary("Ranked recognition, only where the department's mode enables it."));
    }

    public override async Task HandleAsync(KudosScopeRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await sender.Send(
                new GetLeaderboardQuery(request.Scope, request.ScopeId, request.Period, request.Year, request.Month),
                ct),
            ct);
}

public sealed class AnnualKudosRequest
{
    /// <summary>Defaults to the current year.</summary>
    [QueryParam]
    public int? Year { get; set; }
}

/// <summary>
/// The review claim.
/// </summary>
/// <remarks>
/// Always the caller's own. A head who wants somebody else's year has the unit wall and the leaderboard; this
/// screen exists to be pasted into a review form, and it is the subject who does the pasting.
/// </remarks>
public sealed class GetAnnualKudosEndpoint(ISender sender) : Endpoint<AnnualKudosRequest, AnnualKudosView>
{
    public override void Configure()
    {
        Get("/kudos/me/annual");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Kudos")
            .WithSummary("Every kudo received in a year, grouped by category, with the messages."));
    }

    public override async Task HandleAsync(AnnualKudosRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetAnnualKudosQuery(request.Year, null), ct), ct);
}
