using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Meetings.Endpoints;

// =================================================================================================================
// One endpoint per file is the convention; these are grouped because each is a two-line delegation to a service
// and splitting them would spread one readable surface over ten files of boilerplate.
//
// Every endpoint declares a policy, and none of them filters rows. The policy decides whether you may call; RLS
// decides what comes back, and — for the writes — whether it lands (conventions.md §3).
//
// Note what the write policies are and are not. DeliveryLead admits any head, a project lead and a PO, which is
// the set S7 names as scope owners. It cannot know *which* unit or project, so it is the door, not the lock: the
// access.can_write_meeting predicate is what stops a Finance head scheduling into IT.
// =================================================================================================================

// --- Series --------------------------------------------------------------------------------------------------

public sealed class ListSeriesRequest
{
    [QueryParam]
    public string? ScopeType { get; set; }

    [QueryParam]
    public Guid? ScopeId { get; set; }

    /// <summary>Deactivated series are hidden by default: the manager is a list of what is running.</summary>
    [QueryParam]
    public bool IncludeInactive { get; set; }
}

public sealed class ListSeriesEndpoint(IMeetingSeriesService series)
    : Endpoint<ListSeriesRequest, IReadOnlyList<MeetingSeriesView>>
{
    public override void Configure()
    {
        Get("/meetings/series");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("Recurring meetings visible to the caller."));
    }

    public override async Task HandleAsync(ListSeriesRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await series.ListAsync(request.ScopeType, request.ScopeId, request.IncludeInactive, ct),
            ct);
}

public sealed class SeriesByIdRequest
{
    public Guid Id { get; set; }
}

public sealed class GetSeriesEndpoint(IMeetingSeriesService series) : Endpoint<SeriesByIdRequest, MeetingSeriesView>
{
    public override void Configure()
    {
        Get("/meetings/series/{id}");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("One recurring meeting."));
    }

    public override async Task HandleAsync(SeriesByIdRequest request, CancellationToken ct) =>
        await Send.OkAsync(await series.GetAsync(request.Id, ct), ct);
}

/// <summary>The body of a create or an update. Identical both ways — a series has nothing worth patching.</summary>
public class MeetingSeriesBody
{
    public string Kind { get; set; } = MeetingKinds.Weekly;

    /// <summary>A Transloco key or free text; the client renders whichever resolves.</summary>
    public string NameKey { get; set; } = string.Empty;

    public string ScopeType { get; set; } = MeetingScopeTypes.Unit;

    public Guid? ScopeId { get; set; }

    /// <summary>iCal RRULE, e.g. <c>FREQ=WEEKLY;BYDAY=MO</c>.</summary>
    public string RecurrenceRule { get; set; } = "FREQ=WEEKLY;BYDAY=MO";

    public DateOnly StartsOn { get; set; }

    public TimeOnly StartTime { get; set; }

    /// <summary>IANA zone. Omitted means the org default — see <see cref="MeetingsOptions.DefaultTimeZoneId"/>.</summary>
    public string? TimeZoneId { get; set; }

    public int DurationMinutes { get; set; } = 30;

    /// <summary>Whoever runs it. Omitted means the caller.</summary>
    public Guid? OwnerPersonId { get; set; }

    public string? Location { get; set; }

    public string? VideoLink { get; set; }

    public bool Active { get; set; } = true;

    internal MeetingSeriesRequest ToRequest() => new(
        Kind,
        NameKey,
        ScopeType,
        ScopeId,
        RecurrenceRule,
        StartsOn,
        StartTime,
        TimeZoneId,
        DurationMinutes,
        OwnerPersonId,
        Location,
        VideoLink,
        Active);
}

public sealed class CreateSeriesCommand : MeetingSeriesBody;

public sealed class CreateSeriesEndpoint(IMeetingSeriesService series)
    : Endpoint<CreateSeriesCommand, MeetingSeriesView>
{
    public override void Configure()
    {
        Post("/meetings/series");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Meetings").WithSummary("Create a recurring meeting."));
    }

    public override async Task HandleAsync(CreateSeriesCommand request, CancellationToken ct)
    {
        var created = await series.CreateAsync(request.ToRequest(), ct);

        await Send.ResponseAsync(created, StatusCodes.Status201Created, ct);
    }
}

/// <summary>Split from the route parameter so the body binds cleanly without the id appearing twice.</summary>
public sealed class UpdateSeriesCommand : MeetingSeriesBody
{
    public Guid Id { get; set; }
}

public sealed class UpdateSeriesEndpoint(IMeetingSeriesService series)
    : Endpoint<UpdateSeriesCommand, MeetingSeriesView>
{
    public override void Configure()
    {
        Put("/meetings/series/{id}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Meetings").WithSummary("Update a recurring meeting."));
    }

    public override async Task HandleAsync(UpdateSeriesCommand request, CancellationToken ct) =>
        await Send.OkAsync(await series.UpdateAsync(request.Id, request.ToRequest(), ct), ct);
}

public sealed class DeleteSeriesEndpoint(IMeetingSeriesService series) : Endpoint<SeriesByIdRequest>
{
    public override void Configure()
    {
        Delete("/meetings/series/{id}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Meetings").WithSummary("Delete a recurring meeting."));
    }

    public override async Task HandleAsync(SeriesByIdRequest request, CancellationToken ct)
    {
        await series.DeleteAsync(request.Id, ct);

        await Send.NoContentAsync(ct);
    }
}

// --- Occurrences ---------------------------------------------------------------------------------------------

public sealed class ListOccurrencesRequest
{
    [QueryParam]
    public DateOnly? From { get; set; }

    [QueryParam]
    public DateOnly? To { get; set; }

    [QueryParam]
    public string? ScopeType { get; set; }

    [QueryParam]
    public Guid? ScopeId { get; set; }
}

/// <summary>The materialized calendar for a window — what boards and reports read.</summary>
public sealed class ListOccurrencesEndpoint(IMeetingCalendarService calendar)
    : Endpoint<ListOccurrencesRequest, IReadOnlyList<MeetingOccurrenceView>>
{
    /// <summary>A window nobody bounded is a fortnight, matching what the "coming up" strip shows.</summary>
    private const int DefaultWindowDays = 13;

    public override void Configure()
    {
        Get("/meetings/occurrences");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("Meeting occurrences in a window."));
    }

    public override async Task HandleAsync(ListOccurrencesRequest request, CancellationToken ct)
    {
        var from = request.From ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var to = request.To ?? from.AddDays(DefaultWindowDays);

        await Send.OkAsync(
            await calendar.GetOccurrencesAsync(from, to, request.ScopeType, request.ScopeId, ct),
            ct);
    }
}

public sealed class RespondCommand
{
    public Guid Id { get; set; }

    /// <summary>accepted / declined / tentative.</summary>
    public string Response { get; set; } = AttendanceResponses.Accepted;
}

/// <summary>
/// The caller's own RSVP.
/// </summary>
/// <remarks>
/// Authenticated, not a lead policy: answering for yourself is the one write in this module that everybody may
/// do. Whose answer it is comes from the session, never from the body, so there is nothing here to escalate.
/// </remarks>
public sealed class RespondEndpoint(IMeetingCalendarService calendar) : Endpoint<RespondCommand>
{
    public override void Configure()
    {
        Post("/meetings/occurrences/{id}/respond");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("Record the caller's attendance."));
    }

    public override async Task HandleAsync(RespondCommand request, CancellationToken ct)
    {
        await calendar.RespondAsync(request.Id, request.Response, ct);

        await Send.NoContentAsync(ct);
    }
}

// --- Special days --------------------------------------------------------------------------------------------

public sealed class ListSpecialDaysRequest
{
    [QueryParam]
    public DateOnly? From { get; set; }

    [QueryParam]
    public DateOnly? To { get; set; }

    [QueryParam]
    public string? ScopeType { get; set; }

    [QueryParam]
    public Guid? ScopeId { get; set; }
}

public sealed class ListSpecialDaysEndpoint(ISpecialDayService days)
    : Endpoint<ListSpecialDaysRequest, IReadOnlyList<SpecialDayView>>
{
    public override void Configure()
    {
        Get("/meetings/special-days");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("Special days visible to the caller."));
    }

    public override async Task HandleAsync(ListSpecialDaysRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await days.ListAsync(request.From, request.To, request.ScopeType, request.ScopeId, ct),
            ct);
}

public class SpecialDayBody
{
    public string Kind { get; set; } = SpecialDayKinds.Deadline;

    public string NameKey { get; set; } = string.Empty;

    public string ScopeType { get; set; } = MeetingScopeTypes.Department;

    public Guid? ScopeId { get; set; }

    public DateOnly Date { get; set; }

    public bool AllDay { get; set; } = true;

    /// <summary>info / warning / critical. Drives the board colour and nothing else.</summary>
    public string Severity { get; set; } = SpecialDaySeverities.Info;

    public string? Description { get; set; }

    internal SpecialDayRequest ToRequest() =>
        new(Kind, NameKey, ScopeType, ScopeId, Date, AllDay, Severity, Description);
}

public sealed class CreateSpecialDayCommand : SpecialDayBody;

public sealed class CreateSpecialDayEndpoint(ISpecialDayService days)
    : Endpoint<CreateSpecialDayCommand, SpecialDayView>
{
    public override void Configure()
    {
        Post("/meetings/special-days");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Meetings").WithSummary("Add a patch party, audit or deadline."));
    }

    public override async Task HandleAsync(CreateSpecialDayCommand request, CancellationToken ct)
    {
        var created = await days.CreateAsync(request.ToRequest(), ct);

        await Send.ResponseAsync(created, StatusCodes.Status201Created, ct);
    }
}

public sealed class UpdateSpecialDayCommand : SpecialDayBody
{
    public Guid Id { get; set; }
}

public sealed class UpdateSpecialDayEndpoint(ISpecialDayService days)
    : Endpoint<UpdateSpecialDayCommand, SpecialDayView>
{
    public override void Configure()
    {
        Put("/meetings/special-days/{id}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Meetings").WithSummary("Update a special day."));
    }

    public override async Task HandleAsync(UpdateSpecialDayCommand request, CancellationToken ct) =>
        await Send.OkAsync(await days.UpdateAsync(request.Id, request.ToRequest(), ct), ct);
}

public sealed class SpecialDayByIdRequest
{
    public Guid Id { get; set; }
}

public sealed class DeleteSpecialDayEndpoint(ISpecialDayService days) : Endpoint<SpecialDayByIdRequest>
{
    public override void Configure()
    {
        Delete("/meetings/special-days/{id}");
        Policies(CracraPolicies.DeliveryLead);
        Description(builder => builder.WithTags("Meetings").WithSummary("Remove a special day."));
    }

    public override async Task HandleAsync(SpecialDayByIdRequest request, CancellationToken ct)
    {
        await days.DeleteAsync(request.Id, ct);

        await Send.NoContentAsync(ct);
    }
}

// --- Coming up -----------------------------------------------------------------------------------------------

public sealed class UpcomingRequest
{
    [QueryParam]
    public int? Days { get; set; }
}

/// <summary>
/// Meetings and special days merged into one ordered list.
/// </summary>
/// <remarks>
/// Merged server-side rather than by the strip. Two calls, two orderings and a client-side merge is the sort of
/// assembly that goes subtly wrong — and the strip is on the dashboard, where a second round trip is felt.
/// </remarks>
public sealed class UpcomingEndpoint(IMeetingCalendarService calendar)
    : Endpoint<UpcomingRequest, IReadOnlyList<UpcomingEntry>>
{
    public override void Configure()
    {
        Get("/meetings/upcoming");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Meetings").WithSummary("The caller's next meetings and special days."));
    }

    public override async Task HandleAsync(UpcomingRequest request, CancellationToken ct) =>
        await Send.OkAsync(await calendar.GetUpcomingAsync(request.Days, ct), ct);
}
