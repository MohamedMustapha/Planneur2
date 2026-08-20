using Cracra.BuildingBlocks.Web.Users;
using Microsoft.AspNetCore.Http;

namespace Cracra.Modules.Access.Services;

/// <summary>
/// Replaces the token's roles with the effective ones before anything opens a database connection.
/// </summary>
/// <remarks>
/// <para>
/// This is step 3 of architecture.md §4 — the cross-check against the RBAC fallback view — and its position in the
/// pipeline is load-bearing. It must run after authentication, so there is an identity to resolve, and before the
/// RLS session interceptor, because that interceptor stamps <c>app.roles</c> onto the connection and every policy
/// reads what it wrote. Resolve after that point and the request runs on the token's roles, silently ignoring
/// every override.
/// </para>
/// <para>
/// The token is not trusted as the final word on roles. It reflects LDAP group membership at issue time, and a
/// revocation has to bite before the access token expires — otherwise "deny wins" holds only in theory.
/// </para>
/// </remarks>
public sealed class EffectiveRoleEnricher(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IUserContextAccessor accessor,
        IEffectiveRoleResolver resolver)
    {
        var current = accessor.Current;

        if (current is { IsAuthenticated: true } && current.UserId != Guid.Empty)
        {
            var effective = await resolver.ResolveAsync(current.UserId, context.RequestAborted);

            accessor.Current = new UserContext
            {
                IsAuthenticated = true,
                UserId = current.UserId,
                UserName = current.UserName,
                UnitId = current.UnitId,
                DepartmentIds = current.DepartmentIds,
                // The token is the fallback only while Access has nothing on file for this person — before the
                // first sync materializes their assignments. Once it does, its answer wins even when that answer
                // is "no roles", because otherwise revoking someone's last role would hand it straight back.
                Roles = effective.IsAuthoritative ? effective.RoleNames : current.Roles,
                Language = current.Language,
            };
        }

        await next(context);
    }
}
