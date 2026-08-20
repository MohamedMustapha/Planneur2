using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cracra.BuildingBlocks.Web.Http;

/// <summary>
/// Gives every request a correlation id that survives BFF → API → database → integration hops, pushes it into the
/// log scope so SEQ can group a whole request, and echoes it back so a support ticket can quote one id.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Resolve(context);

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        Activity.Current?.SetTag("cracra.correlation_id", correlationId);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    private static string Resolve(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var incoming))
        {
            var candidate = incoming.ToString();

            // Bound it: the value ends up in logs and response headers, so an unbounded client string is a liability.
            if (candidate.Length is > 0 and <= 128)
            {
                return candidate;
            }
        }

        return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }
}
