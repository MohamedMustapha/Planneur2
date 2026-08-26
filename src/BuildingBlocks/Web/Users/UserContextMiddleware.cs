using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cracra.BuildingBlocks.Web.Users;

/// <summary>
/// Claim names the Keycloak realm's protocol mappers emit. The realm export under <c>/deploy/keycloak</c> is the
/// other half of this contract — change one and you must change the other.
/// </summary>
public static class CracraClaims
{
    public const string Subject = "sub";
    public const string PreferredUsername = "preferred_username";
    public const string UnitId = "unit_id";
    public const string DepartmentIds = "dept_ids";
    public const string HeadedNodes = "headed_nodes";
    public const string ContextualRoles = "contextual_roles";
    public const string FunctionalRole = "functional_role";
    public const string Locale = "locale";
}

/// <summary>
/// Builds the ambient <see cref="IUserContext"/> from the access token Keycloak issued. Runs after authentication
/// and before anything that opens a database connection, because the RLS session interceptor reads what it writes.
/// </summary>
/// <remarks>
/// S2 will extend this to cross-check the resolved roles against the Access module's RBAC fallback view, for the
/// case where LDAP group membership is incomplete. Until then the token is the only source.
/// </remarks>
public sealed class UserContextMiddleware(RequestDelegate next, ILogger<UserContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IUserContextAccessor accessor)
    {
        var principal = context.User;

        if (principal?.Identity is { IsAuthenticated: true })
        {
            accessor.Current = Build(principal, logger);
        }

        await next(context);
    }

    internal static UserContext Build(ClaimsPrincipal principal, ILogger logger)
    {
        var subject = principal.FindFirstValue(CracraClaims.Subject)
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(subject, out var userId))
        {
            // Fail closed: an authenticated principal we cannot map to a person id gets no scope at all, which
            // means every RLS predicate evaluates false rather than accidentally matching NULL.
            logger.LogWarning("Authenticated principal has no parsable '{Claim}' claim; scope will be empty", CracraClaims.Subject);

            return UserContext.Anonymous with { IsAuthenticated = true };
        }

        Guid? unitId = Guid.TryParse(principal.FindFirstValue(CracraClaims.UnitId), out var parsedUnit)
            ? parsedUnit
            : null;

        return new UserContext
        {
            IsAuthenticated = true,
            UserId = userId,
            UserName = principal.FindFirstValue(CracraClaims.PreferredUsername)
                       ?? principal.Identity?.Name
                       ?? userId.ToString(),
            UnitId = unitId,
            DepartmentIds = ReadGuidList(principal, CracraClaims.DepartmentIds),
            HeadedNodes = ReadGuidList(principal, CracraClaims.HeadedNodes),
            Roles = ReadRoles(principal),
            Language = SupportedLanguages.Normalize(
                principal.FindFirstValue(CracraClaims.Locale) ?? CultureInfo.CurrentUICulture.Name),
        };
    }

    /// <summary>
    /// A multi-valued claim arrives either as several claims of the same type or as one comma-separated claim,
    /// depending on how the mapper is configured. Accept both so a realm tweak cannot silently empty a scope.
    /// </summary>
    private static IReadOnlyList<Guid> ReadGuidList(ClaimsPrincipal principal, string claimType)
    {
        var values = principal.FindAll(claimType)
            .SelectMany(claim => claim.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(value => Guid.TryParse(value, out var parsed) ? parsed : (Guid?)null)
            .Where(parsed => parsed is not null)
            .Select(parsed => parsed!.Value)
            .Distinct()
            .ToArray();

        return values;
    }

    private static IReadOnlyList<string> ReadRoles(ClaimsPrincipal principal)
    {
        var roles = principal.FindAll(CracraClaims.ContextualRoles)
            .SelectMany(claim => claim.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Concat(principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value))
            .Select(role => role.ToLowerInvariant())
            .Where(role => ContextualRole.All.Contains(role, StringComparer.Ordinal))
            // 'system' is reserved for background jobs; a token must never be able to claim it.
            .Where(role => !string.Equals(role, ContextualRole.System, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Everyone who authenticates is at least a member — that is what grants "see my own unit" in the matrix.
        return roles.Contains(ContextualRole.Member, StringComparer.Ordinal)
            ? roles
            : [ContextualRole.Member, .. roles];
    }
}
