using Cracra.BuildingBlocks.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Tests.Unit.Mediator;

public sealed class MediatorTests
{
    [Fact]
    public async Task Send_invokes_the_registered_handler()
    {
        var mediator = Build();

        var result = await mediator.Send(new Greet("Camille"), TestContext.Current.CancellationToken);

        result.ShouldBe("Bonjour Camille");
    }

    [Fact]
    public async Task Send_throws_a_descriptive_error_when_no_handler_is_registered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator();

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        var exception = await Should.ThrowAsync<MediatorHandlerNotFoundException>(
            async () => await mediator.Send(new Greet("Camille"), TestContext.Current.CancellationToken));

        exception.RequestType.ShouldBe(typeof(Greet));
    }

    /// <summary>
    /// conventions.md §1 fixes the order as Logging → Validation → Authorization → Transaction → Handler, and the
    /// order is not cosmetic: authorization must run inside logging so a denial is recorded, and the transaction
    /// must open innermost so a rejected request never opens one.
    /// </summary>
    [Fact]
    public async Task Behaviors_run_outermost_first_in_registration_order()
    {
        var trace = new List<string>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(trace);
        services.AddMediator();
        services.AddMediatorBehavior(typeof(RecordingBehaviorOne<,>));
        services.AddMediatorBehavior(typeof(RecordingBehaviorTwo<,>));
        services.AddMediatorHandlersFrom(typeof(MediatorTests).Assembly);

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        await mediator.Send(new Greet("Camille"), TestContext.Current.CancellationToken);

        // LoggingBehavior is registered first by AddMediator, so it wraps everything that follows.
        trace.ShouldBe(["one:before", "two:before", "handler", "two:after", "one:after"]);
    }

    [Fact]
    public async Task Repeated_sends_of_the_same_request_type_reuse_the_cached_executor()
    {
        var mediator = Build();

        // The first Send pays for reflection; every later one must not. Correctness is what we can assert here —
        // that a cached executor keeps producing right answers for different instances of the same closed type.
        var first = await mediator.Send(new Greet("Camille"), TestContext.Current.CancellationToken);
        var second = await mediator.Send(new Greet("Mehdi"), TestContext.Current.CancellationToken);

        first.ShouldBe("Bonjour Camille");
        second.ShouldBe("Bonjour Mehdi");
    }

    [Fact]
    public async Task Publish_reaches_every_handler_for_the_notification()
    {
        var trace = new List<string>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(trace);
        services.AddMediator();
        services.AddMediatorHandlersFrom(typeof(MediatorTests).Assembly);

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        await mediator.Publish(new WeekClosed(), TestContext.Current.CancellationToken);

        trace.ShouldBe(["archive", "notify"], ignoreOrder: true);
    }

    [Fact]
    public async Task Publish_stops_at_the_first_failing_handler_by_default()
    {
        var trace = new List<string>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(trace);
        services.AddMediator();
        services.AddScoped<INotificationHandler<PayrollClosed>, ThrowingHandler>();
        services.AddScoped<INotificationHandler<PayrollClosed>, ShouldNotRunHandler>();

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        await Should.ThrowAsync<InvalidOperationException>(async () => await mediator.Publish(new PayrollClosed(), TestContext.Current.CancellationToken));

        // Sequential-stop-on-first-exception is what makes domain events safe inside a transaction: a handler that
        // throws must prevent the ones after it from acting on a state change that is about to roll back.
        trace.ShouldBeEmpty();
    }

    [Fact]
    public async Task Publish_with_no_handlers_is_a_no_op()
    {
        var mediator = Build();

        // Not every integration event has a consumer today, and publishing one that nobody listens to must be
        // unremarkable — otherwise adding an event would mean hunting for a handler to satisfy it.
        await Should.NotThrowAsync(
            async () => await mediator.Publish(new NobodyListens(), TestContext.Current.CancellationToken));
    }

    private static IMediator Build()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(new List<string>());
        services.AddMediator();
        services.AddMediatorHandlersFrom(typeof(MediatorTests).Assembly);

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }
}

// --- Test doubles --------------------------------------------------------------------------------------------------

public sealed record Greet(string Name) : IRequest<string>;

public sealed class GreetHandler(List<string> trace) : IRequestHandler<Greet, string>
{
    public Task<string> Handle(Greet request, CancellationToken ct)
    {
        trace.Add("handler");

        return Task.FromResult($"Bonjour {request.Name}");
    }
}

public sealed record WeekClosed : INotification;

public sealed class ArchiveWeekHandler(List<string> trace) : INotificationHandler<WeekClosed>
{
    public Task Handle(WeekClosed notification, CancellationToken ct)
    {
        trace.Add("archive");

        return Task.CompletedTask;
    }
}

public sealed class NotifyLeadHandler(List<string> trace) : INotificationHandler<WeekClosed>
{
    public Task Handle(WeekClosed notification, CancellationToken ct)
    {
        trace.Add("notify");

        return Task.CompletedTask;
    }
}

public sealed record PayrollClosed : INotification;

/// <summary>Deliberately has no handler anywhere in this assembly.</summary>
public sealed record NobodyListens : INotification;

public sealed class ThrowingHandler : INotificationHandler<PayrollClosed>
{
    public Task Handle(PayrollClosed notification, CancellationToken ct) =>
        throw new InvalidOperationException("boom");
}

public sealed class ShouldNotRunHandler(List<string> trace) : INotificationHandler<PayrollClosed>
{
    public Task Handle(PayrollClosed notification, CancellationToken ct)
    {
        trace.Add("should-not-run");

        return Task.CompletedTask;
    }
}

public sealed class RecordingBehaviorOne<TRequest, TResponse>(List<string> trace)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        trace.Add("one:before");
        var response = await next();
        trace.Add("one:after");

        return response;
    }
}

public sealed class RecordingBehaviorTwo<TRequest, TResponse>(List<string> trace)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        trace.Add("two:before");
        var response = await next();
        trace.Add("two:after");

        return response;
    }
}
