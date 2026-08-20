using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.Modules.Reporting.Application;
using Cracra.Modules.Reporting.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Reporting.Api;

// =================================================================================================================
// Every one of these is Authenticated rather than role-gated, and that is not a relaxation.
//
// A report's scope is what a role decides, and the scope resolver already refuses one the viewer does not hold —
// with a 403 that says so. Gating the endpoint as well would mean the same rule stated in two places, and the
// coarser of the two would be the one that answered first: a member has a perfectly legitimate "my work" report,
// so any policy strong enough to protect the department scope would take theirs away.
// =================================================================================================================

public sealed class GetReportRequest
{
    [QueryParam]
    public string? Scope { get; set; }

    [QueryParam]
    public Guid? ScopeId { get; set; }

    [QueryParam]
    public string? Period { get; set; }

    [QueryParam]
    public DateOnly? From { get; set; }

    [QueryParam]
    public DateOnly? To { get; set; }

    /// <summary>Defaults to the viewer's own UI language.</summary>
    [QueryParam]
    public string? Lang { get; set; }
}

/// <summary>The structured report: numbers and section keys, with whatever narrative is already cached.</summary>
public sealed class GetReportEndpoint(ISender sender) : Endpoint<GetReportRequest, ReportView>
{
    public override void Configure()
    {
        Get("/reports");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Reports")
            .WithSummary("The contextual status report for the caller's scope."));
    }

    public override async Task HandleAsync(GetReportRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await sender.Send(
                new GetReportQuery(
                    request.Scope,
                    request.ScopeId,
                    request.Period,
                    request.From,
                    request.To,
                    request.Lang),
                ct),
            ct);
}

public sealed class GenerateSummaryRequest
{
    public string? Scope { get; set; }

    public Guid? ScopeId { get; set; }

    public string? Period { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public string? Lang { get; set; }

    /// <summary>What the "regenerate" button sends. Skips the cache.</summary>
    public bool Force { get; set; }
}

/// <summary>
/// Writes — or reuses — the AI narrative for a report.
/// </summary>
/// <remarks>
/// A POST although it often only reads: generation is slow, expensive on an on-prem box, and not idempotent when
/// forced. Making it a GET would invite every proxy and prefetcher in the path to run the model.
/// </remarks>
public sealed class GenerateSummaryEndpoint(ISender sender) : Endpoint<GenerateSummaryRequest, ReportSummaryView>
{
    public override void Configure()
    {
        Post("/reports/summary");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Reports")
            .WithSummary("Generate or refresh the machine-written narrative for a report."));
    }

    public override async Task HandleAsync(GenerateSummaryRequest request, CancellationToken ct) =>
        await Send.OkAsync(
            await sender.Send(
                new GenerateSummaryCommand(
                    request.Scope,
                    request.ScopeId,
                    request.Period,
                    request.From,
                    request.To,
                    request.Lang,
                    request.Force),
                ct),
            ct);
}

/// <summary>
/// The narrative, streamed as it is written.
/// </summary>
/// <remarks>
/// <para>
/// Server-sent events rather than a websocket: this is one-way, short-lived text and SSE survives the BFF's
/// reverse proxy without any configuration of its own.
/// </para>
/// <para>
/// Deliberately does not write to the cache. A stream that the reader closed halfway through would otherwise
/// store half a summary, and half a summary served as a whole one is worse than none — so the panel streams for
/// the wait and then POSTs to <c>/reports/summary</c> to persist the finished text.
/// </para>
/// </remarks>
public sealed class StreamSummaryEndpoint(ISender sender) : Endpoint<GenerateSummaryRequest>
{
    public override void Configure()
    {
        Post("/reports/summary/stream");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Reports").WithSummary("Stream the narrative as it is written."));
    }

    public override async Task HandleAsync(GenerateSummaryRequest request, CancellationToken ct)
    {
        var chunks = await sender.Send(
            new StreamSummaryQuery(
                request.Scope, request.ScopeId, request.Period, request.From, request.To, request.Lang),
            ct);

        HttpContext.Response.ContentType = "text/event-stream";
        HttpContext.Response.Headers.CacheControl = "no-cache";

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            // One SSE frame per chunk, newlines escaped: a raw newline inside a data field would terminate the
            // frame early and the client would see the paragraph break as the end of the message.
            await HttpContext.Response.WriteAsync(
                $"data: {chunk.Replace("\n", "\\n", StringComparison.Ordinal)}\n\n",
                ct);

            await HttpContext.Response.Body.FlushAsync(ct);
        }

        await HttpContext.Response.WriteAsync("data: [DONE]\n\n", ct);
    }
}

public sealed class ExportReportRequest
{
    public string Id { get; set; } = string.Empty;

    [QueryParam]
    public string? Format { get; set; }
}

/// <summary>
/// Renders a report, stores it in RustFS and hands back a short-lived link.
/// </summary>
/// <remarks>
/// The id is a description of the request rather than a stored row, so this re-composes the report under the
/// caller's own session before rendering. Somebody who guessed an id gets their own answer to that question, or a
/// 403 — never somebody else's numbers.
/// </remarks>
public sealed class ExportReportEndpoint(ISender sender) : Endpoint<ExportReportRequest, ReportExportView>
{
    public override void Configure()
    {
        Get("/reports/{id}/export");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder.WithTags("Reports").WithSummary("Export a report and return a download link."));
    }

    public override async Task HandleAsync(ExportReportRequest request, CancellationToken ct) =>
        await Send.OkAsync(await sender.Send(new ExportReportCommand(request.Id, request.Format), ct), ct);
}
