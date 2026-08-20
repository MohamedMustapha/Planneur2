using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Activities.Application;
using Cracra.Modules.Activities.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Activities.Api;

// =================================================================================================================
// Thin by design. Every endpoint here is Authenticated rather than role-gated: logging your own week is the one
// thing every single person on the platform does, and who may write whose row is RLS's answer, not a policy's.
// =================================================================================================================

public sealed class ListActivitiesRequest
{
    /// <summary>me | unit | department | project | all. Narrows within what RLS already allowed.</summary>
    [QueryParam]
    public string? Scope { get; set; }

    [QueryParam]
    public Guid? ProjectId { get; set; }

    [QueryParam]
    public DateOnly? From { get; set; }

    [QueryParam]
    public DateOnly? To { get; set; }
}

public sealed class ListActivitiesEndpoint(ISender sender)
    : Endpoint<ListActivitiesRequest, IReadOnlyList<ActivityEntryView>>
{
    public override void Configure()
    {
        Get("/activities");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Activities")
            .WithSummary("The activity feed the boards and reports read."));
    }

    public override async Task HandleAsync(ListActivitiesRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await sender.Send(
                new ListActivitiesQuery(request.Scope, request.ProjectId, request.From, request.To),
                ct),
            ct);
}

public sealed class LogActivityRequest
{
    /// <summary>Omitted for the usual case: yourself. A lead may name someone in scope, and RLS has the last word.</summary>
    public Guid? PersonId { get; set; }

    public string ActivityTypeCode { get; set; } = string.Empty;

    public Guid? ProjectId { get; set; }

    public Guid? IterationId { get; set; }

    public string Kind { get; set; } = "actual";

    public string Source { get; set; } = "manual";

    public string? ExternalRef { get; set; }

    public DateTimeOffset SlotStart { get; set; }

    public DateTimeOffset SlotEnd { get; set; }

    /// <summary>Defaults to the slot's length. Given separately when only part of the slot was this activity.</summary>
    public decimal? Hours { get; set; }

    public string? Note { get; set; }
}

public sealed class LogActivityEndpoint(ISender sender) : Endpoint<LogActivityRequest, LogActivityResult>
{
    public override void Configure()
    {
        Post("/activities");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Activities").WithSummary("Log a planned or actual slot."));
    }

    public override async Task HandleAsync(LogActivityRequest request, CancellationToken ct)
    {
        var result = await sender.Send(
            new LogActivityCommand(
                request.PersonId,
                request.ActivityTypeCode,
                request.ProjectId,
                request.IterationId,
                request.Kind,
                request.Source,
                request.ExternalRef,
                request.SlotStart,
                request.SlotEnd,
                request.Hours,
                request.Note),
            ct);

        // 201 with the guardrail verdict in the body. A soft warn is not an error — the hour was recorded — so it
        // travels as data on a success rather than as a status code the client would have to treat as a failure.
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }
}

public sealed class AmendActivityRequest
{
    public Guid Id { get; set; }

    public string ActivityTypeCode { get; set; } = string.Empty;

    public Guid? ProjectId { get; set; }

    public Guid? IterationId { get; set; }

    public DateTimeOffset SlotStart { get; set; }

    public DateTimeOffset SlotEnd { get; set; }

    public decimal? Hours { get; set; }

    public string? Note { get; set; }
}

public sealed class AmendActivityEndpoint(ISender sender) : Endpoint<AmendActivityRequest, LogActivityResult>
{
    public override void Configure()
    {
        Put("/activities/{id}");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Activities").WithSummary("Amend an entry."));
    }

    public override async Task HandleAsync(AmendActivityRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await sender.Send(
                new AmendActivityCommand(
                    request.Id,
                    request.ActivityTypeCode,
                    request.ProjectId,
                    request.IterationId,
                    request.SlotStart,
                    request.SlotEnd,
                    request.Hours,
                    request.Note),
                ct),
            ct);
}

public sealed class DeleteActivityEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/activities/{id}");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Activities").WithSummary("Delete an entry."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await sender.Send(new DeleteActivityCommand(Route<Guid>("id")), ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed class ActivityTypesRequest
{
    [QueryParam]
    public Guid? DepartmentId { get; set; }
}

/// <summary>The picker's options: canonical buckets merged with whatever the department added.</summary>
public sealed class GetActivityTypesEndpoint(ISender sender)
    : Endpoint<ActivityTypesRequest, IReadOnlyList<ActivityTypeOption>>
{
    public override void Configure()
    {
        Get("/activities/types");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Activities")
            .WithSummary("Activity types available to a department."));
    }

    public override async Task HandleAsync(ActivityTypesRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetActivityTypesQuery(request.DepartmentId), ct), ct);
}

public sealed class AssignableTasksRequest
{
    /// <summary>azure-devops | servicenow. Omitted asks every configured source.</summary>
    [QueryParam]
    public string? Source { get; set; }
}

/// <summary>
/// The dropdown.
/// </summary>
/// <remarks>
/// Takes no person parameter, deliberately. The tasks returned are always the caller's own, because handing one
/// person another person's assigned tickets is a disclosure the external system never agreed to and no role on
/// this platform implies.
/// </remarks>
public sealed class GetAssignableTasksEndpoint(ISender sender)
    : Endpoint<AssignableTasksRequest, IReadOnlyList<AssignableTask>>
{
    public override void Configure()
    {
        Get("/activities/assignable-tasks");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Activities")
            .WithSummary("Tasks assigned to the caller in Azure DevOps or ServiceNow, read-only."));
    }

    public override async Task HandleAsync(AssignableTasksRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new GetAssignableTasksQuery(request.Source), ct), ct);
}

public sealed class WeeklySummaryRequest
{
    /// <summary>ISO week, as <c>2026-W34</c>. Omitted means the current one.</summary>
    [QueryParam]
    public string? Week { get; set; }
}

public sealed class GetWeeklySummaryEndpoint(ISender sender) : Endpoint<WeeklySummaryRequest, WeeklySummary>
{
    public override void Configure()
    {
        Get("/activities/weekly-summary/me");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Activities")
            .WithSummary("Hours by type for a week, and where that leaves the 35h target."));
    }

    public override async Task HandleAsync(WeeklySummaryRequest request, CancellationToken ct)
    {
        var (year, week) = ParseWeek(request.Week);

        await Send.OkAsync(await sender.Send(new GetWeeklySummaryQuery(null, year, week), ct), ct);
    }

    /// <summary>
    /// Parses <c>2026-W34</c>, tolerating nonsense by falling back to the current week.
    /// </summary>
    /// <remarks>
    /// A malformed query string on a read is not worth a 400: the caller wanted a summary and the useful answer is
    /// this week's, not an error page.
    /// </remarks>
    private static (int? Year, int? Week) ParseWeek(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var parts = value.Split('W', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length == 2
               && int.TryParse(parts[0].TrimEnd('-'), out var year)
               && int.TryParse(parts[1], out var week)
               && week is >= 1 and <= 53
            ? (year, week)
            : (null, null);
    }
}
