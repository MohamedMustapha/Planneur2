using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Cracra.Bff;

/// <summary>
/// Keeps the tokens inside the session cookie alive.
/// </summary>
/// <remarks>
/// The session cookie lasts eight hours; Keycloak's access token lasts minutes. Without this the two drift apart
/// and the session enters a state that looks signed in and behaves signed out: <c>/bff/user</c> still answers from
/// the cookie's claims while every proxied API call carries a dead bearer token and comes back 401, so the UI
/// renders empty rather than sending the user back to log in. Renewing on the refresh token is what makes the
/// cookie's lifetime the real one. It also keeps <c>id_token</c> current, which sign-out needs — Keycloak rejects
/// an expired <c>id_token_hint</c> outright and strands the browser on its own error page.
/// </remarks>
public sealed class SessionTokenRefresher(
    IOptionsMonitor<OpenIdConnectOptions> optionsMonitor,
    TimeProvider timeProvider,
    ILogger<SessionTokenRefresher> logger)
{
    /// <summary>
    /// Renew slightly early. A token that is valid when we check but expires while the proxied call is in flight
    /// would fail for exactly the reason this class exists to prevent.
    /// </summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);

    /// <summary>
    /// One refresh per refresh token at a time. The Angular shell opens several API calls at once, so without this
    /// a single expiry fans out into a burst of identical token requests. That is merely wasteful while Keycloak
    /// keeps refresh tokens reusable, but becomes session loss the moment rotation is switched on: the first
    /// response invalidates the token the others are still presenting. Sharing the one call sidesteps both.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task<RefreshedTokens?>> inFlight = new(StringComparer.Ordinal);

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var properties = context.Properties;
        var expiresAt = properties.GetTokenValue("expires_at");

        // No stored expiry means no stored tokens — a cookie issued before SaveTokens, or a test principal.
        // Nothing to renew and nothing wrong, so leave the session alone.
        if (!DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry))
        {
            return;
        }

        if (expiry - ExpiryMargin > timeProvider.GetUtcNow())
        {
            return;
        }

        var refreshToken = properties.GetTokenValue("refresh_token");

        if (string.IsNullOrEmpty(refreshToken))
        {
            await SignOutAsync(context, "the access token expired and the session carries no refresh token");
            return;
        }

        var refreshed = await RefreshAsync(refreshToken, context.HttpContext.RequestAborted);

        if (refreshed is null)
        {
            // The refresh token is spent too — the identity provider has ended this session (idle timeout, an
            // admin revocation, a sign-out elsewhere). Dropping the principal is the honest answer: the client
            // then sees an anonymous /bff/user and the route guard starts a real login.
            await SignOutAsync(context, "Keycloak refused the refresh token");
            return;
        }

        Store(properties, refreshed, refreshToken);

        // Re-issue the cookie, or the renewed tokens live only for this one request.
        context.ShouldRenew = true;

        logger.LogDebug("Renewed the session tokens; the access token now expires at {ExpiresAt:o}.", refreshed.ExpiresAt);
    }

    private Task<RefreshedTokens?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        // GetOrAdd may run the factory more than once under contention, so the work is wrapped in a Lazy: whichever
        // task wins the dictionary is the only one that ever starts.
        var lazy = new Lazy<Task<RefreshedTokens?>>(() => RequestAsync(refreshToken, ct));
        var task = this.inFlight.GetOrAdd(refreshToken, _ => lazy.Value);

        // Clear the slot once the call settles. Keyed on the old refresh token, which is spent from here on, so
        // there is no window in which a later request could adopt a stale result.
        _ = task.ContinueWith(
            _ => this.inFlight.TryRemove(refreshToken, out Task<RefreshedTokens?>? _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task;
    }

    private async Task<RefreshedTokens?> RequestAsync(string refreshToken, CancellationToken ct)
    {
        var options = optionsMonitor.Get(OpenIdConnectDefaults.AuthenticationScheme);

        try
        {
            // The handler's own configuration manager and backchannel, so the token endpoint comes from discovery
            // and the call inherits whatever the handler was configured with rather than a second opinion.
            var configuration = await options.ConfigurationManager!.GetConfigurationAsync(ct);

            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = options.ClientId!,
                    ["client_secret"] = options.ClientSecret ?? string.Empty,
                }),
            };

            using var response = await options.Backchannel.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Keycloak refused a token refresh with {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    await response.Content.ReadAsStringAsync(ct));

                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);

            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
            {
                logger.LogWarning("Keycloak accepted the refresh but returned no access token.");
                return null;
            }

            return new RefreshedTokens(
                payload.AccessToken,
                payload.RefreshToken,
                payload.IdToken,
                payload.TokenType,
                timeProvider.GetUtcNow().AddSeconds(payload.ExpiresIn));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Keycloak being unreachable is not evidence that the session is invalid. Report failure so the caller
            // signs out rather than proxying a dead token — but say plainly in the log that this is an outage.
            logger.LogError(exception, "Could not reach Keycloak to refresh the session tokens.");
            return null;
        }
    }

    /// <summary>
    /// Writes the renewed tokens back, keeping every value the refresh response did not replace.
    /// <see cref="AuthenticationProperties.StoreTokens"/> replaces the whole set, so anything omitted here is
    /// dropped — including the refresh token itself when Keycloak reuses rather than rotates it.
    /// </summary>
    private static void Store(AuthenticationProperties properties, RefreshedTokens refreshed, string previousRefreshToken)
    {
        var tokens = new List<AuthenticationToken>
        {
            new() { Name = "access_token", Value = refreshed.AccessToken },
            new() { Name = "refresh_token", Value = refreshed.RefreshToken ?? previousRefreshToken },
            new() { Name = "expires_at", Value = refreshed.ExpiresAt.ToString("o", CultureInfo.InvariantCulture) },
        };

        var idToken = refreshed.IdToken ?? properties.GetTokenValue("id_token");

        if (!string.IsNullOrEmpty(idToken))
        {
            tokens.Add(new() { Name = "id_token", Value = idToken });
        }

        var tokenType = refreshed.TokenType ?? properties.GetTokenValue("token_type");

        if (!string.IsNullOrEmpty(tokenType))
        {
            tokens.Add(new() { Name = "token_type", Value = tokenType });
        }

        properties.StoreTokens(tokens);
    }

    private async Task SignOutAsync(CookieValidatePrincipalContext context, string reason)
    {
        logger.LogInformation("Ending the session because {Reason}.", reason);

        context.RejectPrincipal();

        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private sealed record RefreshedTokens(
        string AccessToken,
        string? RefreshToken,
        string? IdToken,
        string? TokenType,
        DateTimeOffset ExpiresAt);

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
