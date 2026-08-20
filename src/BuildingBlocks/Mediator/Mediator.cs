using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.BuildingBlocks.Mediator;

/// <summary>
/// The hand-written in-process mediator (see <c>conventions.md §1</c>). We deliberately do not use MediatR;
/// an architecture test asserts no <c>MediatR.*</c> reference exists anywhere in the solution.
/// </summary>
/// <remarks>
/// Reflection happens exactly once per closed request/notification type, when the executor for that type is
/// created and cached. Every subsequent dispatch is a virtual call on a strongly-typed generic executor, so the
/// hot path allocates only the pipeline closures it actually needs.
/// </remarks>
internal sealed class Mediator(IServiceProvider services) : IMediator
{
    private static readonly ConcurrentDictionary<Type, object> RequestExecutors = new();
    private static readonly ConcurrentDictionary<Type, object> NotificationExecutors = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executor = (RequestExecutor<TResponse>)RequestExecutors.GetOrAdd(
            request.GetType(),
            static (requestType, responseType) =>
            {
                var executorType = typeof(RequestExecutor<,>).MakeGenericType(requestType, responseType);
                return Activator.CreateInstance(executorType)
                       ?? throw new InvalidOperationException($"Could not create a mediator executor for {requestType}.");
            },
            typeof(TResponse));

        return executor.Execute(services, request, ct);
    }

    public Task Publish(INotification notification, CancellationToken ct = default)
        => Publish(notification, PublishStrategy.StopOnFirstException, ct);

    public Task Publish(INotification notification, PublishStrategy strategy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var executor = (NotificationExecutor)NotificationExecutors.GetOrAdd(
            notification.GetType(),
            static notificationType =>
            {
                var executorType = typeof(NotificationExecutor<>).MakeGenericType(notificationType);
                return Activator.CreateInstance(executorType)
                       ?? throw new InvalidOperationException($"Could not create a mediator executor for {notificationType}.");
            });

        return executor.Execute(services, notification, strategy, ct);
    }

    private abstract class RequestExecutor<TResponse>
    {
        public abstract Task<TResponse> Execute(IServiceProvider services, object request, CancellationToken ct);
    }

    private sealed class RequestExecutor<TRequest, TResponse> : RequestExecutor<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> Execute(IServiceProvider services, object request, CancellationToken ct)
        {
            var typed = (TRequest)request;

            var handler = services.GetService<IRequestHandler<TRequest, TResponse>>()
                          ?? throw new MediatorHandlerNotFoundException(typeof(TRequest));

            // Behaviors are resolved in registration order (Logging -> Validation -> Authorization -> Transaction),
            // so we fold from the last to the first to end up with the first-registered behavior outermost.
            var behaviors = services.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();

            RequestHandlerDelegate<TResponse> next = () => handler.Handle(typed, ct);

            for (var i = behaviors.Length - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var inner = next;
                next = () => behavior.Handle(typed, inner, ct);
            }

            return next();
        }
    }

    private abstract class NotificationExecutor
    {
        public abstract Task Execute(
            IServiceProvider services,
            object notification,
            PublishStrategy strategy,
            CancellationToken ct);
    }

    private sealed class NotificationExecutor<TNotification> : NotificationExecutor
        where TNotification : INotification
    {
        public override Task Execute(
            IServiceProvider services,
            object notification,
            PublishStrategy strategy,
            CancellationToken ct)
        {
            var typed = (TNotification)notification;
            var handlers = services.GetServices<INotificationHandler<TNotification>>().ToArray();

            if (handlers.Length == 0)
            {
                return Task.CompletedTask;
            }

            return strategy switch
            {
                PublishStrategy.ParallelWhenAll => Task.WhenAll(handlers.Select(h => h.Handle(typed, ct))),
                _ => Sequential(handlers, typed, ct),
            };
        }

        private static async Task Sequential(
            INotificationHandler<TNotification>[] handlers,
            TNotification notification,
            CancellationToken ct)
        {
            foreach (var handler in handlers)
            {
                await handler.Handle(notification, ct);
            }
        }
    }
}

/// <summary>Thrown when a request reaches the mediator with no registered handler.</summary>
public sealed class MediatorHandlerNotFoundException(Type requestType)
    : InvalidOperationException($"No handler is registered for request type '{requestType.FullName}'.")
{
    public Type RequestType { get; } = requestType;
}
