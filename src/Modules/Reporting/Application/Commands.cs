using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Storage;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Reporting.Contracts;
using Cracra.Modules.Reporting.Domain;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Reporting.Application;

/// <summary>
/// Generate — or reuse — the narrative for a report.
/// </summary>
/// <param name="Force">
/// Skips the cache. What the "regenerate" button sends, because somebody pressing it has already decided the text
/// they can see is not the text they want.
/// </param>
public sealed record GenerateSummaryCommand(
    string? Scope,
    Guid? ScopeId,
    string? Period,
    DateOnly? From,
    DateOnly? To,
    string? Language,
    bool Force) : IRequest<ReportSummaryView>;

/// <summary>
/// The same narrative, streamed rather than awaited.
/// </summary>
/// <remarks>
/// A request that returns an <c>IAsyncEnumerable</c> rather than an endpoint reaching past the mediator into the
/// composer. Slightly unusual, and worth it: the layering rule ("endpoints map a request to a command and send
/// it") is what keeps this module's one role check on the path every caller takes, streaming or not.
/// </remarks>
public sealed record StreamSummaryQuery(
    string? Scope,
    Guid? ScopeId,
    string? Period,
    DateOnly? From,
    DateOnly? To,
    string? Language) : IRequest<IAsyncEnumerable<string>>;

/// <summary>Renders a report and stores it, returning a short-lived link.</summary>
public sealed record ExportReportCommand(string ReportId, string? Format) : IRequest<ReportExportView>;

internal sealed class StreamSummaryHandler(
    ReportRequestService reports,
    IAiSummarizer summarizer,
    IUserContext user) : IRequestHandler<StreamSummaryQuery, IAsyncEnumerable<string>>
{
    public async Task<IAsyncEnumerable<string>> Handle(StreamSummaryQuery request, CancellationToken ct)
    {
        var descriptor = reports.Describe(
            request.Scope, request.ScopeId, request.Period, request.From, request.To, request.Language);

        // Composed here, before a single byte is written. Authorization and every figure are settled while the
        // response can still become a 403; once the stream is open the status code is already sent.
        var report = await reports.ComposeAsync(descriptor, ct);
        var prompt = SummaryPrompt.Build(report, GenerateSummaryHandler.Audience(user), descriptor.Language);

        return summarizer.StreamAsync(prompt, descriptor.Language, ct);
    }
}

internal sealed class GenerateSummaryHandler(
    ReportRequestService reports,
    ISummaryStore summaries,
    IAiSummarizer summarizer,
    ReportingTelemetry telemetry,
    IUserContext user) : IRequestHandler<GenerateSummaryCommand, ReportSummaryView>
{
    public async Task<ReportSummaryView> Handle(GenerateSummaryCommand request, CancellationToken ct)
    {
        var descriptor = reports.Describe(
            request.Scope, request.ScopeId, request.Period, request.From, request.To, request.Language);

        var report = await reports.ComposeAsync(descriptor, ct);
        var prompt = SummaryPrompt.Build(report, Audience(user), descriptor.Language);
        var hash = ReportIdentity.PromptHash(descriptor, summarizer.Model, prompt.Projection);

        if (!request.Force && await summaries.FindAsync(descriptor, hash, ct) is { } cached)
        {
            // A cache hit is the common case on a Monday morning, when a whole unit opens last week's report. On
            // an on-prem box sized for the building rather than for a data centre, that difference is felt.
            telemetry.SummaryReused();

            return View(cached, stale: false);
        }

        using var duration = telemetry.MeasureGeneration(descriptor.Scope);

        var text = await summarizer.WriteAsync(prompt, descriptor.Language, ct);

        if (string.IsNullOrWhiteSpace(text))
        {
            // An empty completion is a failure that would otherwise be stored and served as a summary forever.
            throw new DomainRuleViolationException("The model returned nothing. Try again in a moment.");
        }

        var stored = await summaries.SaveAsync(
            descriptor, hash, summarizer.Model, text.Trim(), prompt.User.Length, ct);

        return View(stored, stale: false);
    }

    /// <summary>
    /// The role the narrative is pitched at.
    /// </summary>
    /// <remarks>
    /// The viewer's widest role rather than all of them. "member,unit-head,dept-head" in a prompt asks the model
    /// to write for three audiences at once, and it obliges by writing for none of them.
    /// </remarks>
    internal static string Audience(IUserContext user) => user switch
    {
        _ when user.Has(ContextualRole.Pmo) => ContextualRole.Pmo,
        _ when user.Has(ContextualRole.DepartmentHead) => ContextualRole.DepartmentHead,
        _ when user.Has(ContextualRole.UnitHead) => ContextualRole.UnitHead,
        _ when user.HasAnyRole(ContextualRole.ProjectLead, ContextualRole.ProductOwner) => ContextualRole.ProjectLead,
        _ => ContextualRole.Member,
    };

    private static ReportSummaryView View(StoredSummary summary, bool stale) => new(
        summary.Id,
        summary.Text,
        summary.Model,
        summary.Language,
        summary.CreatedAt,
        stale);
}

internal sealed class ExportReportHandler(
    ReportRequestService reports,
    IEnumerable<IReportRenderer> renderers,
    IObjectStorage storage,
    IOptions<ObjectStorageOptions> storageOptions,
    ReportingTelemetry telemetry) : IRequestHandler<ExportReportCommand, ReportExportView>
{
    public async Task<ReportExportView> Handle(ExportReportCommand request, CancellationToken ct)
    {
        var format = (request.Format ?? "pdf").Trim().ToLowerInvariant();

        var renderer = renderers.FirstOrDefault(candidate =>
            string.Equals(candidate.Format, format, StringComparison.Ordinal))
            ?? throw new DomainRuleViolationException($"'{format}' is not an export format.");

        // Re-composed rather than read back from a snapshot: the export runs the caller's own authorization and
        // their own RLS session, so a link to somebody else's report id cannot produce somebody else's numbers.
        var descriptor = reports.Reopen(request.ReportId);
        var report = await reports.ComposeAsync(descriptor, ct);

        var bytes = renderer.Render(report);

        // Keyed by the report id and the day. Re-exporting the same report overwrites rather than accumulating,
        // and a report re-run tomorrow gets its own object because its figures will have moved.
        var key = $"reports/{DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime):yyyy/MM/dd}/{request.ReportId}.{format}";

        using (var content = new MemoryStream(bytes, writable: false))
        {
            await storage.PutAsync(key, content, renderer.ContentType, ct);
        }

        var lifetime = storageOptions.Value.DefaultPresignLifetime;
        var url = await storage.PresignGetAsync(key, lifetime, ct);

        telemetry.Exported(format, bytes.Length);

        return new ReportExportView(
            request.ReportId,
            format,
            url,
            DateTimeOffset.UtcNow.Add(lifetime),
            bytes.Length);
    }
}
