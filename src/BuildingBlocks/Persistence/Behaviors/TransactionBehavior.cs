using System.Collections.Concurrent;
using System.Reflection;
using Cracra.BuildingBlocks.Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cracra.BuildingBlocks.Persistence.Behaviors;

/// <summary>
/// Marks a request as changing state. Only these open a transaction.
/// </summary>
/// <remarks>
/// Opt-in rather than opt-out. Wrapping every query in a transaction would cost nothing visible and hide which
/// operations actually mutate — and the outbox guarantee depends on knowing exactly that, because an integration
/// event has to commit with the change it describes or not at all.
/// </remarks>
public interface ITransactionalRequest;

/// <summary>
/// Maps a module's assembly to the DbContext that is its unit of work.
/// </summary>
/// <remarks>
/// The behavior has to know <em>which</em> module's context to commit, and it cannot be generic over it: .NET's
/// container will not accept a partially-closed generic as the implementation of an open generic service. Keying
/// on the declaring assembly works because a command belongs to exactly one module by construction — the mediator
/// only ever scans one assembly per module, so a request type cannot come from two.
/// </remarks>
public sealed class ModuleUnitOfWorkRegistry
{
    private readonly ConcurrentDictionary<Assembly, Type> _contexts = new();

    public void Register(Assembly moduleAssembly, Type contextType) => _contexts[moduleAssembly] = contextType;

    public Type? Resolve(Type requestType) =>
        _contexts.TryGetValue(requestType.Assembly, out var contextType) ? contextType : null;
}

/// <summary>
/// Opens one transaction per command, innermost in the pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Innermost on purpose: validation and authorization have already run, so a request that was never going to
/// succeed never opens a transaction and never takes a lock.
/// </para>
/// <para>
/// The RLS session GUCs are stamped when the connection opens, which happens inside this scope — so the policies
/// evaluating during the transaction see the caller's identity, exactly as they do for a read.
/// </para>
/// </remarks>
public sealed class TransactionBehavior<TRequest, TResponse>(
    IServiceProvider services,
    ModuleUnitOfWorkRegistry registry,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (request is not ITransactionalRequest)
        {
            return await next();
        }

        if (registry.Resolve(typeof(TRequest)) is not { } contextType)
        {
            // A command whose module never registered a unit of work would commit nothing and report success.
            // Loud, immediately, rather than a write that silently vanishes.
            throw new InvalidOperationException(
                $"{typeof(TRequest).Name} is transactional, but no module unit of work is registered for "
                + $"{typeof(TRequest).Assembly.GetName().Name}. Call AddModuleTransactions<TContext>() in the module.");
        }

        var context = (ModuleDbContext)services.GetRequiredService(contextType);

        // Already inside one — a command that sends another command on the mediator. The outer transaction is the
        // unit of work; nesting a second would commit half of it independently.
        if (context.Database.CurrentTransaction is not null)
        {
            return await next();
        }

        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async cancellationToken =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var response = await next();

                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return response;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Rolling back {RequestName} in {Module}",
                    typeof(TRequest).Name,
                    contextType.Name);

                // Explicit rather than relying on dispose: the intent should be readable, and a rollback that only
                // happens because a using block unwound is easy to misread as an oversight.
                await transaction.RollbackAsync(cancellationToken);

                throw;
            }
        }, ct);
    }
}
