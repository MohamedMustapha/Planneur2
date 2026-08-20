using System.Data.Common;
using System.Security;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FluentValidation;
using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Http;

namespace Cracra.BuildingBlocks.Web.Errors;

/// <summary>
/// Turns anything that escapes a handler into RFC 7807 <see cref="ProblemDetails"/>. Nothing else in the system is
/// allowed to write an error body, so this is the only place an exception can leak detail — and it does not:
/// outside Development the client gets a category and a correlation id, never a message or a stack.
/// </summary>
public sealed class CracraExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<CracraExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title) = Classify(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "Request rejected for {Method} {Path}: {Title}", httpContext.Request.Method, httpContext.Request.Path, title);
        }

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.io/{status}",
            Instance = httpContext.Request.Path,
        };

        problem.Extensions["correlationId"] = httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();

        if (exception is ValidationException validation)
        {
            // The one case where detail is safe outside Development: these are the client's own field errors, and
            // withholding them turns a fixable form into a guessing game.
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());
        }

        if (environment.IsDevelopment())
        {
            problem.Detail = exception.Message;
            problem.Extensions["exception"] = exception.GetType().FullName;
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    /// <summary>Postgres <c>insufficient_privilege</c>, which is what a WITH CHECK violation raises.</summary>
    private const string PostgresInsufficientPrivilege = "42501";

    private static (int Status, string Title) Classify(Exception exception) => exception switch
    {
        ValidationException => (StatusCodes.Status400BadRequest, "The request is not valid."),
        DomainRuleViolationException => (StatusCodes.Status422UnprocessableEntity, "The request violates a domain rule."),
        ResourceNotFoundException => (StatusCodes.Status404NotFound, "The requested resource does not exist."),
        ConcurrencyConflictException => (StatusCodes.Status409Conflict, "The resource changed since it was read."),
        UnauthorizedAccessException or SecurityException => (StatusCodes.Status403Forbidden, "Access denied."),

        OperationCanceledException => (StatusCodes.Status499ClientClosedRequest, "The request was cancelled."),

        // An RLS policy refusing a write. Postgres raises insufficient_privilege when a WITH CHECK clause rejects
        // an INSERT or UPDATE, and without this it would surface as a 500 — an access decision reported as a bug.
        //
        // 403 rather than 404: the caller reached this by naming a row or a person they could already see, so
        // there is nothing left to conceal by pretending it does not exist. Reads are the opposite case, and RLS
        // handles those by returning no row at all.
        _ when IsWriteRefusedByPolicy(exception) => (StatusCodes.Status403Forbidden, "Access denied."),

        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
    };

    /// <summary>
    /// Walks the chain looking for Postgres refusing the write.
    /// </summary>
    /// <remarks>
    /// EF wraps the provider's exception in a DbUpdateException, so the SqlState is never on the exception that
    /// actually reaches here. Matching only the outer type would silently stop working the first time anything
    /// wrapped it — which is exactly what a 500 on a permission check looks like.
    /// </remarks>
    private static bool IsWriteRefusedByPolicy(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: PostgresInsufficientPrivilege })
            {
                return true;
            }
        }

        return false;
    }
}
