using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Guidance.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Guidance.Endpoints;

public sealed class GetNavigationEndpoint(IShellNavigationService navigation)
    : EndpointWithoutRequest<ShellNavigation>
{
    public override void Configure()
    {
        Get("/guidance/navigation");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Guidance")
            .WithSummary("Where this viewer lands, and which sections they see."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await navigation.ResolveAsync(ct), ct);
}

public sealed class GetNextActionEndpoint(INextActionService actions) : EndpointWithoutRequest<NextAction>
{
    public override void Configure()
    {
        Get("/guidance/next-action");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Guidance")
            .WithSummary("The one suggested action, computed from real state."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await actions.ResolveAsync(ct), ct);
}

public sealed class GetObligationsEndpoint(IObligationService obligations)
    : EndpointWithoutRequest<IReadOnlyList<Obligation>>
{
    public override void Configure()
    {
        Get("/guidance/obligations");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Guidance")
            .WithSummary("What the viewer owes: the only thing allowed to pierce Focus mode."));
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await obligations.ListAsync(ct), ct);
}
