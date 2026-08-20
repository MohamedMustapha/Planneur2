using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Access.Services;

public interface IEffectiveRoleResolver
{
    Task<EffectiveRoleSet> ResolveAsync(Guid personId, CancellationToken ct);

    /// <summary>Drops a cached answer. Called whenever an override is written or revoked.</summary>
    void Invalidate(Guid personId);
}

/// <summary>
/// Resolves effective roles, with a short cache.
/// </summary>
/// <remarks>
/// <para>
/// This runs on the authenticated path of every request, before the RLS session is stamped, so it cannot afford a
/// database round trip each time. The cache is deliberately short-lived and explicitly invalidated on every
/// override write: a revocation that took a minute to bite would make "deny wins" a promise the system does not
/// actually keep.
/// </para>
/// <para>
/// It reads under the system context in its own scope. The alternative is circular — resolving your roles would
/// require a database session scoped by the roles being resolved.
/// </para>
/// </remarks>
internal sealed class EffectiveRoleResolver(IServiceScopeFactory scopeFactory, IMemoryCache cache)
    : IEffectiveRoleResolver
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    public async Task<EffectiveRoleSet> ResolveAsync(Guid personId, CancellationToken ct)
    {
        if (personId == Guid.Empty)
        {
            return EffectiveRoleSet.Empty(personId);
        }

        if (cache.TryGetValue(Key(personId), out EffectiveRoleSet? cached) && cached is not null)
        {
            return cached;
        }

        using var scope = scopeFactory.CreateScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var context = scope.ServiceProvider.GetRequiredService<AccessDbContext>();

        var assignments = await context.RoleAssignments
            .Where(assignment => assignment.PersonId == personId)
            .ToListAsync(ct);

        var overrides = await context.Overrides
            .Where(item => item.PersonId == personId && item.RevokedAt == null)
            .ToListAsync(ct);

        var resolved = EffectiveRoles.Resolve(personId, assignments, overrides, DateTimeOffset.UtcNow);

        cache.Set(Key(personId), resolved, CacheLifetime);

        return resolved;
    }

    public void Invalidate(Guid personId) => cache.Remove(Key(personId));

    private static string Key(Guid personId) => $"access:roles:{personId}";
}
