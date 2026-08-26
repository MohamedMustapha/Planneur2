using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Problems.Application;
using Cracra.Modules.Problems.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Problems.Api;

// =================================================================================================================
// Problems (v2 §05.3).
//
// Filing, proposing, voting and commenting are Authenticated: anybody can say something hurts, and a platform
// where reporting an irritant needs a role is a platform where irritants go unreported. Triage and conversion are
// AnyHead, because deciding what happens to somebody else's pain is a different act — and because conversion is
// what keeps the work inside a department instead of becoming shadow IT.
// =================================================================================================================

public sealed class FileProblemRequestModel
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Category { get; set; }

    public string? OriginScopeType { get; set; }

    public Guid? OriginScopeId { get; set; }

    public Guid? NodeId { get; set; }

    public decimal? ImpactTimeLoss { get; set; }

    public string? ImpactFrequency { get; set; }

    public int? AffectedPeopleEstimate { get; set; }
}

public sealed class FileProblemEndpoint(IProblemService problems)
    : Endpoint<FileProblemRequestModel, FiledProblem>
{
    public override void Configure()
    {
        Post("/problems");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Problems").WithSummary("Report an irritant."));
    }

    public override async Task HandleAsync(FileProblemRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            await problems.FileAsync(
                new FileProblemRequest(
                    request.Title,
                    request.Description,
                    request.Category,
                    request.OriginScopeType,
                    request.OriginScopeId,
                    request.NodeId,
                    request.ImpactTimeLoss,
                    request.ImpactFrequency,
                    request.AffectedPeopleEstimate),
                ct),
            ct);
}

public sealed class ListProblemsRequestModel
{
    [QueryParam]
    public string? Scope { get; set; }

    [QueryParam]
    public string? Category { get; set; }

    [QueryParam]
    public string? Status { get; set; }

    /// <summary>impact (default), votes or recent.</summary>
    [QueryParam]
    public string? Sort { get; set; }

    [QueryParam]
    public int? Limit { get; set; }
}

public sealed class ListProblemsEndpoint(IProblemService problems)
    : Endpoint<ListProblemsRequestModel, IReadOnlyList<ProblemCard>>
{
    public override void Configure()
    {
        Get("/problems");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Problems").WithSummary("The problems the caller may see."));
    }

    public override async Task HandleAsync(ListProblemsRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            await problems.ListAsync(
                new ProblemFilter(
                    request.Scope,
                    request.Category,
                    request.Status,
                    request.Sort,
                    Math.Clamp(request.Limit ?? 100, 1, 500)),
                ct),
            ct);
}

public sealed class SearchProblemsRequestModel
{
    [QueryParam]
    public string? Q { get; set; }

    [QueryParam]
    public int? Limit { get; set; }
}

public sealed class SearchProblemsEndpoint(IProblemService problems)
    : Endpoint<SearchProblemsRequestModel, IReadOnlyList<ProblemCard>>
{
    public override void Configure()
    {
        Get("/problems/search");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Problems").WithSummary("Has somebody already filed this?"));
    }

    public override async Task HandleAsync(SearchProblemsRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            await problems.SearchAsync(request.Q ?? string.Empty, Math.Clamp(request.Limit ?? 10, 1, 50), ct),
            ct);
}

public sealed class ProblemByIdRequest
{
    public Guid Id { get; set; }
}

public sealed class GetProblemEndpoint(IProblemService problems) : Endpoint<ProblemByIdRequest, ProblemDetail>
{
    public override void Configure()
    {
        Get("/problems/{id}");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Problems").WithSummary("A problem, with its proposals."));
    }

    public override async Task HandleAsync(ProblemByIdRequest request, CancellationToken ct) =>
        await Send.OkAsync(await problems.DetailAsync(request.Id, ct), ct);
}

public sealed class ProposeRequestModel
{
    public Guid Id { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal? EffortGuess { get; set; }
}

public sealed class ProposeEndpoint(IProblemService problems) : Endpoint<ProposeRequestModel, SavedProblemResponse>
{
    public override void Configure()
    {
        Post("/problems/{id}/proposals");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Problems").WithSummary("Suggest a fix."));
    }

    public override async Task HandleAsync(ProposeRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            new SavedProblemResponse(
                await problems.ProposeAsync(request.Id, request.Description, request.EffortGuess, ct)),
            ct);
}

public sealed record SavedProblemResponse(Guid Id);

public sealed record VoteResponse(bool Counted);

public sealed class VoteEndpoint(IProblemService problems) : Endpoint<ProblemByIdRequest, VoteResponse>
{
    public override void Configure()
    {
        Post("/problems/{id}/vote");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Problems").WithSummary("Me too."));
    }

    public override async Task HandleAsync(ProblemByIdRequest request, CancellationToken ct) =>
        await Send.OkAsync(new VoteResponse(await problems.VoteAsync(request.Id, ct)), ct);
}

public sealed class CommentRequestModel
{
    public Guid Id { get; set; }

    public string Body { get; set; } = string.Empty;
}

public sealed class CommentEndpoint(IProblemService problems) : Endpoint<CommentRequestModel, SavedProblemResponse>
{
    public override void Configure()
    {
        Post("/problems/{id}/comments");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Problems").WithSummary("Say something about it."));
    }

    public override async Task HandleAsync(CommentRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            new SavedProblemResponse(await problems.CommentAsync(request.Id, request.Body, ct)),
            ct);
}

public sealed class TriageRequestModel
{
    public Guid Id { get; set; }

    public string Decision { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public Guid? DuplicateOf { get; set; }
}

public sealed class TriageEndpoint(IProblemService problems) : Endpoint<TriageRequestModel>
{
    public override void Configure()
    {
        Post("/problems/{id}/triage");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Problems").WithSummary("Accept, decline or mark a duplicate."));
    }

    public override async Task HandleAsync(TriageRequestModel request, CancellationToken ct)
    {
        await problems.TriageAsync(
            request.Id,
            new TriageRequest(request.Decision, request.Reason, request.DuplicateOf),
            ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class ConvertRequestModel
{
    public Guid Id { get; set; }

    public string? Type { get; set; }

    public string? Category { get; set; }

    public Guid? OwnerNodeId { get; set; }
}

public sealed class ConvertEndpoint(IProblemService problems) : Endpoint<ConvertRequestModel, SavedProblemResponse>
{
    public override void Configure()
    {
        Post("/problems/{id}/convert");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Problems")
            .WithSummary("Turn an accepted problem into work somebody owns."));
    }

    public override async Task HandleAsync(ConvertRequestModel request, CancellationToken ct) =>
        await Send.OkAsync(
            new SavedProblemResponse(
                await problems.ConvertAsync(
                    request.Id,
                    new ConvertRequest(request.Type, request.Category, request.OwnerNodeId),
                    ct)),
            ct);
}

public sealed class ResolveRequestModel
{
    public Guid Id { get; set; }

    public string? Note { get; set; }
}

public sealed class ResolveEndpoint(IProblemService problems) : Endpoint<ResolveRequestModel>
{
    public override void Configure()
    {
        Post("/problems/{id}/resolve");
        Policies(CracraPolicies.AnyHead);
        Description(builder => builder.WithTags("Problems").WithSummary("The pain went away."));
    }

    public override async Task HandleAsync(ResolveRequestModel request, CancellationToken ct)
    {
        await problems.ResolveAsync(request.Id, request.Note, ct);

        await Send.NoContentAsync(ct);
    }
}
