using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Meetings.Endpoints;

// The CR surface (v2 §07.4). Every write is Authenticated rather than DeliveryLead: whoever ran the meeting
// writes its minutes, and access.can_write_minutes — not a role on the door — is what decides whose meeting it
// was. The one thing a policy can usefully say here is "be somebody".

public sealed class OccurrenceMinutesRequest
{
    public Guid Id { get; set; }
}

public sealed class GetMinutesEndpoint(IMinutesService minutes) : Endpoint<OccurrenceMinutesRequest, MinutesView>
{
    public override void Configure()
    {
        Get("/meetings/occurrences/{id}/minutes");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("The minutes of one meeting."));
    }

    public override async Task HandleAsync(OccurrenceMinutesRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await minutes.FindAsync(request.Id, ct)
            ?? throw new ResourceNotFoundException("Nobody has written minutes for that meeting yet."),
            ct);
}

public sealed class OpenMinutesEndpoint(IMinutesService minutes) : Endpoint<OccurrenceMinutesRequest, MinutesView>
{
    public override void Configure()
    {
        Post("/meetings/occurrences/{id}/minutes");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("Start (or reopen) the minutes of a meeting."));
    }

    public override async Task HandleAsync(OccurrenceMinutesRequest request, CancellationToken ct) =>
        await Send.OkAsync(await minutes.OpenAsync(request.Id, ct), ct);
}

public sealed class AmendMinutesCommand
{
    public Guid Id { get; set; }

    public string? Agenda { get; set; }

    public List<Guid>? Attendees { get; set; }

    public List<Guid>? Absentees { get; set; }

    public string? Summary { get; set; }
}

public sealed class AmendMinutesEndpoint(IMinutesService minutes) : Endpoint<AmendMinutesCommand, MinutesView>
{
    public override void Configure()
    {
        Patch("/meetings/minutes/{id}");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("Amend a draft's agenda, attendance or summary."));
    }

    public override async Task HandleAsync(AmendMinutesCommand request, CancellationToken ct) =>
        await Send.OkAsync(
            await minutes.AmendAsync(
                request.Id,
                new MinutesRequest(request.Agenda, request.Attendees, request.Absentees, request.Summary),
                ct),
            ct);
}

public sealed class MinutesByIdRequest
{
    public Guid Id { get; set; }
}

public sealed class PublishMinutesEndpoint(IMinutesService minutes) : Endpoint<MinutesByIdRequest, MinutesView>
{
    public override void Configure()
    {
        Post("/meetings/minutes/{id}/publish");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("Distribute the minutes to their scope."));
    }

    public override async Task HandleAsync(MinutesByIdRequest request, CancellationToken ct) =>
        await Send.OkAsync(await minutes.PublishAsync(request.Id, ct), ct);
}

public sealed class RecordDecisionCommand
{
    public Guid Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public string? Rationale { get; set; }

    /// <summary>Free text: "le COPIL", a name. Plenty of decisions belong to a room rather than a person.</summary>
    public string? DecidedBy { get; set; }
}

public sealed class RecordDecisionEndpoint(IMinutesService minutes) : Endpoint<RecordDecisionCommand, MinutesView>
{
    public override void Configure()
    {
        Post("/meetings/minutes/{id}/decisions");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("Record what the room decided."));
    }

    public override async Task HandleAsync(RecordDecisionCommand request, CancellationToken ct) =>
        await Send.ResponseAsync(
            await minutes.DecideAsync(
                request.Id,
                new DecisionRequest(request.Text, request.Rationale, request.DecidedBy),
                ct),
            StatusCodes.Status201Created,
            ct);
}

public sealed class AssignActionCommand
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Omitted means the caller owes it themselves.</summary>
    public Guid? OwnerPersonId { get; set; }

    public DateOnly? Due { get; set; }

    /// <summary>problem / item / objective / none — <see cref="ActionLinkTypes"/>.</summary>
    public string? LinkType { get; set; }

    public Guid? LinkId { get; set; }
}

public sealed class AssignActionEndpoint(IMinutesService minutes) : Endpoint<AssignActionCommand, MinutesView>
{
    public override void Configure()
    {
        Post("/meetings/minutes/{id}/actions");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("Assign an action, optionally linked to real work."));
    }

    public override async Task HandleAsync(AssignActionCommand request, CancellationToken ct) =>
        await Send.ResponseAsync(
            await minutes.AssignAsync(
                request.Id,
                new ActionRequest(request.Title, request.OwnerPersonId, request.Due, request.LinkType, request.LinkId),
                ct),
            StatusCodes.Status201Created,
            ct);
}

public sealed class AmendActionCommand
{
    public Guid Id { get; set; }

    public Guid ActionId { get; set; }

    public string? Title { get; set; }

    public Guid? OwnerPersonId { get; set; }

    public DateOnly? Due { get; set; }

    /// <summary>open / done / dropped — <see cref="ActionStatuses"/>.</summary>
    public string? Status { get; set; }
}

public sealed class AmendActionEndpoint(IMinutesService minutes) : Endpoint<AmendActionCommand, MinutesView>
{
    public override void Configure()
    {
        Patch("/meetings/minutes/{id}/actions/{actionId}");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("Reassign, re-date or settle an action."));
    }

    public override async Task HandleAsync(AmendActionCommand request, CancellationToken ct) =>
        await Send.OkAsync(
            await minutes.AmendActionAsync(
                request.Id,
                request.ActionId,
                new ActionPatch(request.Title, request.OwnerPersonId, request.Due, request.Status),
                ct),
            ct);
}

public sealed class LatestMinutesRequest
{
    [QueryParam]
    public string? ScopeType { get; set; }

    [QueryParam]
    public Guid? ScopeId { get; set; }

    [QueryParam]
    public int Take { get; set; }
}

public sealed class LatestMinutesEndpoint(IMinutesService minutes)
    : Endpoint<LatestMinutesRequest, IReadOnlyList<MinutesDigest>>
{
    public override void Configure()
    {
        Get("/meetings/minutes");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("The published minutes a dashboard strips."));
    }

    public override async Task HandleAsync(LatestMinutesRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await minutes.LatestAsync(request.ScopeType, request.ScopeId, request.Take, ct),
            ct);
}

public sealed class TrackActionsRequest
{
    /// <summary>me (default) or scope — whether the tracker is a personal debt list or a head's.</summary>
    [QueryParam]
    public string? Owner { get; set; }

    /// <summary>open (default), overdue or all.</summary>
    [QueryParam]
    public string? Status { get; set; }
}

public sealed class TrackActionsEndpoint(IMinutesService minutes)
    : Endpoint<TrackActionsRequest, IReadOnlyList<ActionItemView>>
{
    public override void Configure()
    {
        Get("/meetings/actions");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("The action tracker."));
    }

    public override async Task HandleAsync(TrackActionsRequest request, CancellationToken ct) =>
        await Send.OkAsync(await minutes.TrackAsync(request.Owner, request.Status, ct), ct);
}
