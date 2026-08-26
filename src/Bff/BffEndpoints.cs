using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Cracra.Bff;

/// <summary>
/// The session surface the Angular client talks to. Three endpoints and no more: who am I, log me in, log me out.
/// Everything else goes through the proxy to the API.
/// </summary>
public static class BffEndpoints
{
    public const string AntiForgeryHeader = "X-Cracra-Csrf";

    public static WebApplication MapBffEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/bff");

        group.MapGet("/user", (HttpContext context) =>
        {
            var user = context.User;

            if (user.Identity is not { IsAuthenticated: true })
            {
                return Results.Ok(BffUser.Anonymous);
            }

            return Results.Ok(new BffUser(
                true,
                user.FindFirstValue("sub") ?? string.Empty,
                user.FindFirstValue("preferred_username") ?? user.Identity.Name ?? string.Empty,
                user.FindFirstValue("name"),
                user.FindFirstValue("unit_id"),
                ReadMulti(user, "dept_ids"),
                CollapseLegacyHeads(ReadMulti(user, "contextual_roles")),
                user.FindFirstValue("functional_role"),
                NormalizeLanguage(user.FindFirstValue("locale"))));
        }).AllowAnonymous();

        group.MapGet("/login", (string? returnUrl) => Results.Challenge(
                new AuthenticationProperties
                {
                    // Only ever a local path — an open redirect here would hand an attacker the login flow.
                    RedirectUri = IsLocalPath(returnUrl) ? returnUrl! : "/",
                },
                [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous();

        group.MapPost("/logout", (HttpContext context) =>
            {
                // Anonymous rather than RequireAuthorization, because signing out of a session that has already
                // ended is not an error — and a challenge here would answer "log me out" with a login page. That
                // is reachable now that an unrenewable session drops its principal on the way in.
                if (context.User.Identity is not { IsAuthenticated: true })
                {
                    return Results.Redirect("/");
                }

                return Results.SignOut(
                    new AuthenticationProperties { RedirectUri = "/" },
                    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
            })
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Speaks the API's vocabulary rather than the realm's: v2 §01 collapsed the level-specific head roles into
    /// one <c>node-head</c>, and the realm still issues the v1 names.
    /// </summary>
    /// <remarks>
    /// The API does this in its effective-role middleware, and these roles decide only what the client draws — but
    /// a client that hides every head control from a head is broken all the same, and it fails silently: the
    /// button is simply absent, with no error anywhere to explain it. Duplicated rather than shared because the
    /// BFF deliberately references nothing of the API's; the claim names beside it are literals for the same
    /// reason.
    /// </remarks>
    private static string[] CollapseLegacyHeads(string[] roles) =>
        [.. roles
            .Select(role => role is "unit-head" or "dept-head" ? "node-head" : role)
            .Distinct(StringComparer.Ordinal)];

    private static string[] ReadMulti(ClaimsPrincipal user, string claimType) =>
        [.. user.FindAll(claimType)
            .SelectMany(claim => claim.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)];

    private static string NormalizeLanguage(string? locale)
    {
        var twoLetter = (locale ?? "fr").Split('-', '_')[0].ToLowerInvariant();

        return twoLetter is "fr" or "en" or "es" ? twoLetter : "fr";
    }

    private static bool IsLocalPath(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal);
}

/// <summary>
/// The shape the client's auth signal store holds. Note it carries roles for <em>rendering</em> only — the UI never
/// trusts them for data, because RLS is what actually decides which rows come back.
/// </summary>
public sealed record BffUser(
    bool IsAuthenticated,
    string Id,
    string UserName,
    string? DisplayName,
    string? UnitId,
    IReadOnlyList<string> DepartmentIds,
    IReadOnlyList<string> Roles,
    string? FunctionalRole,
    string Language)
{
    public static readonly BffUser Anonymous = new(false, string.Empty, string.Empty, null, null, [], [], null, "fr");
}

/// <summary>
/// Rejects a proxied API call that does not carry the anti-forgery header. A cross-site request can ride along on
/// the session cookie but cannot add a custom header, so requiring one is enough — and it costs the client a single
/// interceptor rather than a token round trip.
/// </summary>
/// <remarks>
/// Middleware rather than an endpoint filter, because YARP's proxy pipeline is not a route-handler pipeline and
/// therefore has nowhere to hang a filter.
/// </remarks>
public sealed class AntiForgeryHeaderMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey(BffEndpoints.AntiForgeryHeader))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            await context.Response.WriteAsJsonAsync(new
            {
                title = "Missing anti-forgery header.",
                detail = $"Requests proxied to the API must send the '{BffEndpoints.AntiForgeryHeader}' header.",
                status = StatusCodes.Status403Forbidden,
            });

            return;
        }

        await next(context);
    }
}
