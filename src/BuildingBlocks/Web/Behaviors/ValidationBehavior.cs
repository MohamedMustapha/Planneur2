using Cracra.BuildingBlocks.Mediator;
using FluentValidation;

namespace Cracra.BuildingBlocks.Web.Behaviors;

/// <summary>
/// Runs every FluentValidation validator registered for the request before the handler sees it.
/// </summary>
/// <remarks>
/// <para>
/// Second in the pipeline, inside logging and outside the transaction. Inside logging so a rejected request still
/// appears in SEQ with its correlation id — "the client says it failed and there is nothing in the logs" is a bad
/// afternoon. Outside the transaction because a request that was never going to be valid should not open one.
/// </para>
/// <para>
/// Every validator for the request runs, not just the first to fail. A form that reports one bad field at a time
/// is a form people fill in four times.
/// </para>
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var applicable = validators as IValidator<TRequest>[] ?? [.. validators];

        if (applicable.Length == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(applicable.Select(validator => validator.ValidateAsync(context, ct))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToArray();

        return failures.Length > 0
            ? throw new ValidationException(failures)
            : await next();
    }
}
