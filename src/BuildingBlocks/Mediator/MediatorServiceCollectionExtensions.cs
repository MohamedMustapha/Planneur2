using System.Reflection;
using Cracra.BuildingBlocks.Mediator.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracra.BuildingBlocks.Mediator;

public static class MediatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mediator itself plus the behaviors every module shares. Call once from the composition root,
    /// before any <c>AddXxxModule()</c>, because pipeline behaviors execute in registration order.
    /// </summary>
    public static IServiceCollection AddMediator(this IServiceCollection services)
    {
        services.TryAddScoped<IMediator, Mediator>();
        services.TryAddScoped<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.TryAddScoped<IPublisher>(sp => sp.GetRequiredService<IMediator>());

        return services.AddMediatorBehavior(typeof(LoggingBehavior<,>));
    }

    /// <summary>
    /// Appends an open-generic pipeline behavior (for example <c>typeof(ValidationBehavior&lt;,&gt;)</c>).
    /// Order matters: the first behavior registered is the outermost.
    /// </summary>
    public static IServiceCollection AddMediatorBehavior(this IServiceCollection services, Type openGenericBehavior)
    {
        ArgumentNullException.ThrowIfNull(openGenericBehavior);

        if (!openGenericBehavior.IsGenericTypeDefinition || openGenericBehavior.GetGenericArguments().Length != 2)
        {
            throw new ArgumentException(
                $"'{openGenericBehavior}' must be an open generic type with two type parameters, "
                + "e.g. typeof(ValidationBehavior<,>).",
                nameof(openGenericBehavior));
        }

        return services.AddScoped(typeof(IPipelineBehavior<,>), openGenericBehavior);
    }

    /// <summary>
    /// Registers every request and notification handler declared in one module's assembly. Modules call this from
    /// their own <c>AddXxxModule()</c>; we never scan the whole solution, so a module can never silently pick up
    /// another module's handlers.
    /// </summary>
    public static IServiceCollection AddMediatorHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var implementation in assembly.GetTypes())
        {
            if (implementation is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            {
                RegisterClosedInterfaces(services, implementation, typeof(IRequestHandler<,>));
                RegisterClosedInterfaces(services, implementation, typeof(INotificationHandler<>));
            }
        }

        return services;
    }

    private static void RegisterClosedInterfaces(
        IServiceCollection services,
        Type implementation,
        Type openGenericInterface)
    {
        foreach (var contract in implementation.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == openGenericInterface)
            {
                services.AddScoped(contract, implementation);
            }
        }
    }
}
