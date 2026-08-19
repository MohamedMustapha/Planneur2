namespace Cracra.BuildingBlocks.Mediator;

/// <summary>Marker for a request that produces <typeparamref name="TResponse"/>.</summary>
public interface IRequest<out TResponse>;

/// <summary>A command with no meaningful payload result.</summary>
public interface IRequest : IRequest<Unit>;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

/// <summary>An in-process domain or integration event.</summary>
public interface INotification;

public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken ct);
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}

public interface IPublisher
{
    Task Publish(INotification notification, CancellationToken ct = default);

    Task Publish(INotification notification, PublishStrategy strategy, CancellationToken ct = default);
}

public interface IMediator : ISender, IPublisher;

/// <summary>
/// How <see cref="IPublisher.Publish(INotification, PublishStrategy, CancellationToken)"/> fans out.
/// </summary>
public enum PublishStrategy
{
    /// <summary>Sequential, stop on the first exception. The default for domain events inside a transaction.</summary>
    StopOnFirstException = 0,

    /// <summary>All handlers run concurrently; exceptions aggregate. For post-commit fire-and-forget.</summary>
    ParallelWhenAll = 1,
}

/// <summary>The unit type — mirrors MediatR's <c>Unit</c> so commands without a result stay expressible.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;

    public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(Value);

    public override string ToString() => "()";
}
