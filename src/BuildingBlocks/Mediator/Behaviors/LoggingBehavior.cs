using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Cracra.BuildingBlocks.Mediator.Behaviors;

/// <summary>
/// The one behavior every module gets, 2-layer and DDD alike. Emits a structured start/finish pair so SEQ can
/// correlate a request with the endpoint that raised it, and records the elapsed time for the slow-request view.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        logger.LogDebug("Mediator request {RequestName} starting", RequestName);

        try
        {
            var response = await next();

            logger.LogInformation(
                "Mediator request {RequestName} completed in {ElapsedMilliseconds:0.##} ms",
                RequestName,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Mediator request {RequestName} failed after {ElapsedMilliseconds:0.##} ms",
                RequestName,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            throw;
        }
    }
}
