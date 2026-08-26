using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Domain;
using Cracra.Modules.Directory.Contracts;
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
        IEffectiveRoleResolver resolver,
        IOrgNodeReader nodes)
    {
        var current = accessor.Current;

        if (current is { IsAuthenticated: true } && current.UserId != Guid.Empty)
        {
            var effective = await resolver.ResolveAsync(current.UserId, context.RequestAborted);

            // The token is the fallback only while Access has nothing on file for this person — before the first
            // sync materializes their assignments. Once it does, its answer wins even when that answer is "no
            // roles", because otherwise revoking someone's last role would hand it straight back.
            var roles = effective.IsAuthoritative ? effective.RoleNames : current.Roles;

            accessor.Current = new UserContext
            {
                IsAuthenticated = true,
                UserId = current.UserId,
                UserName = current.UserName,
                UnitId = current.UnitId,
                DepartmentIds = current.DepartmentIds,
                Roles = Collapse(roles),
                Language = current.Language,
            };

            var scope = await nodes.GetHomeScopeAsync(current.UserId, context.RequestAborted);

            accessor.Current = (UserContext)accessor.Current with
            {
                NodeId = scope?.NodeId ?? current.UnitId,
                NodePath = scope?.NodePath ?? [],
                HeadedNodes = HeadedNodes(effective, current),
            };
        }

        await next(context);
    }

    private static IReadOnlyList<string> Collapse(IReadOnlyList<string> roles) =>
        [.. roles
            .Select(role => ContextualRole.LegacyHeadRoles.Contains(role, StringComparer.Ordinal)
                ? ContextualRole.NodeHead
                : role)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private static IReadOnlyList<Guid> HeadedNodes(EffectiveRoleSet effective, IUserContext token)
    {
        if (effective.IsAuthoritative)
        {
            return
            [
                .. effective.Roles
                    .Where(role => role.Role is ContextualRole.NodeHead
                                       or ContextualRole.UnitHead
                                       or ContextualRole.DepartmentHead)
                    .Select(role => role.ScopeId)
                    .Where(scopeId => scopeId is not null)
                    .Select(scopeId => scopeId!.Value)
                    .Distinct(),
            ];
        }

        if (token.HeadedNodes.Count > 0)
        {
            return token.HeadedNodes;
        }

        List<Guid> headed = [];

        if (token.Has(ContextualRole.UnitHead) && token.UnitId is { } unitId)
        {
            headed.Add(unitId);
        }

        if (token.Has(ContextualRole.DepartmentHead))
        {
            headed.AddRange(token.DepartmentIds);
        }

        return headed;
    }
}
