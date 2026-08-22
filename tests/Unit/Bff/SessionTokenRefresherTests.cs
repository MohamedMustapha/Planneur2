using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Cracra.Bff;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NSubstitute;

namespace Cracra.Tests.Unit.Bff;

/// <summary>
/// Keeping the tokens inside the session cookie alive.
/// </summary>
/// <remarks>
/// The failure this class exists to prevent is invisible from either end alone: the cookie still says signed in
/// while every proxied call carries a dead bearer token, so the UI renders empty instead of sending the user back
/// to log in. Nothing in an end-to-end test would catch it inside eight hours of wall clock, which is exactly why
/// the clock is injected and the decisions are exercised here.
/// </remarks>
public sealed class SessionTokenRefresherTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_token_with_time_left_on_it_is_left_alone()
    {
        var keycloak = new RecordingKeycloak();
        var context = Session(expiresAt: Now.AddMinutes(5));

        await RefresherFor(keycloak).ValidateAsync(context);

        // Not merely "still signed in": no call at all. Renewing a live token on every request would turn one
        // page load into a burst of token requests.
        keycloak.Calls.ShouldBe(0);
        context.ShouldRenew.ShouldBeFalse();
        context.Principal.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_token_expiring_within_the_margin_is_renewed_early()
    {
        var keycloak = new RecordingKeycloak();

        // Valid now, gone in half a minute. Waiting for it to actually expire would fail the very request that
        // noticed, which is the case this margin exists for.
        var context = Session(expiresAt: Now.AddSeconds(30));

        await RefresherFor(keycloak).ValidateAsync(context);

        keycloak.Calls.ShouldBe(1);
        context.ShouldRenew.ShouldBeTrue();
        context.Properties.GetTokenValue("access_token").ShouldBe("fresh-access");
    }

    [Fact]
    public async Task A_renewal_moves_the_stored_expiry_forward()
    {
        var keycloak = new RecordingKeycloak();
        var context = Session(expiresAt: Now.AddSeconds(-1));

        await RefresherFor(keycloak).ValidateAsync(context);

        var stored = DateTimeOffset.Parse(
            context.Properties.GetTokenValue("expires_at")!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        // Measured from the injected clock, not from the old expiry: a session renewed at ten o'clock is good
        // for the token's lifetime from ten o'clock.
        stored.ShouldBe(Now.AddSeconds(300));
    }

    [Fact]
    public async Task A_renewal_that_returns_no_new_refresh_token_keeps_the_old_one()
    {
        // Keycloak reuses rather than rotates by default. StoreTokens replaces the whole set, so anything not
        // restated here is dropped — and dropping the refresh token ends the session at the next expiry.
        var keycloak = new RecordingKeycloak { IncludeRefreshToken = false };
        var context = Session(expiresAt: Now.AddSeconds(-1));

        await RefresherFor(keycloak).ValidateAsync(context);

        context.Properties.GetTokenValue("refresh_token").ShouldBe("old-refresh");
    }

    [Fact]
    public async Task A_renewal_that_returns_no_id_token_keeps_the_one_the_session_had()
    {
        // Sign-out sends the id_token as a hint. Losing it here would strand the browser on Keycloak's own error
        // page at logout, long after the request that caused it.
        var keycloak = new RecordingKeycloak { IncludeIdToken = false };
        var context = Session(expiresAt: Now.AddSeconds(-1));

        await RefresherFor(keycloak).ValidateAsync(context);

        context.Properties.GetTokenValue("id_token").ShouldBe("old-id");
    }

    [Fact]
    public async Task A_refused_refresh_ends_the_session()
    {
        var keycloak = new RecordingKeycloak { Status = HttpStatusCode.BadRequest };
        var context = Session(expiresAt: Now.AddSeconds(-1));

        await RefresherFor(keycloak).ValidateAsync(context);

        // The identity provider has ended this session — an idle timeout, a revocation, a sign-out elsewhere.
        // Dropping the principal is what lets the client see an anonymous /bff/user and start a real login.
        context.Principal.ShouldBeNull();
        SignedOut(context).ShouldBeTrue();
    }

    [Fact]
    public async Task An_unreachable_keycloak_ends_the_session_rather_than_proxying_a_dead_token()
    {
        var keycloak = new RecordingKeycloak { Throw = new HttpRequestException("no route to host") };
        var context = Session(expiresAt: Now.AddSeconds(-1));

        await Should.NotThrowAsync(() => RefresherFor(keycloak).ValidateAsync(context));

        context.Principal.ShouldBeNull();
        SignedOut(context).ShouldBeTrue();
    }

    [Fact]
    public async Task An_expired_session_with_no_refresh_token_ends_without_asking()
    {
        var keycloak = new RecordingKeycloak();
        var context = Session(expiresAt: Now.AddSeconds(-1), refreshToken: null);

        await RefresherFor(keycloak).ValidateAsync(context);

        keycloak.Calls.ShouldBe(0);
        context.Principal.ShouldBeNull();
    }

    [Fact]
    public async Task A_cookie_carrying_no_tokens_is_left_to_itself()
    {
        var keycloak = new RecordingKeycloak();

        // A cookie issued before SaveTokens, or a principal a test signed in by hand. Nothing to renew and
        // nothing wrong — ending the session here would sign people out for the crime of an upgrade.
        var context = Session(expiresAt: null);

        await RefresherFor(keycloak).ValidateAsync(context);

        keycloak.Calls.ShouldBe(0);
        context.Principal.ShouldNotBeNull();
        context.ShouldRenew.ShouldBeFalse();
    }

    [Fact]
    public async Task Simultaneous_requests_share_one_token_request()
    {
        // The Angular shell opens several API calls at once. Merely wasteful while Keycloak keeps refresh tokens
        // reusable; session loss the moment rotation is switched on, because the first response invalidates the
        // token the others are still presenting.
        var keycloak = new RecordingKeycloak { Gate = new TaskCompletionSource() };
        var refresher = RefresherFor(keycloak);

        var contexts = Enumerable.Range(0, 4).Select(_ => Session(expiresAt: Now.AddSeconds(-1))).ToList();
        var validations = contexts.Select(context => refresher.ValidateAsync(context)).ToList();

        keycloak.Gate!.SetResult();

        await Task.WhenAll(validations);

        keycloak.Calls.ShouldBe(1);
        contexts.ShouldAllBe(context => context.Properties.GetTokenValue("access_token") == "fresh-access");
    }

    [Fact]
    public async Task A_session_renewed_once_renews_again_when_the_new_token_expires()
    {
        // The in-flight slot is keyed on the spent refresh token and must be released when the call settles;
        // a slot left behind would pin the first answer for the life of the process.
        var keycloak = new RecordingKeycloak();
        var refresher = RefresherFor(keycloak);

        await refresher.ValidateAsync(Session(expiresAt: Now.AddSeconds(-1)));
        await refresher.ValidateAsync(Session(expiresAt: Now.AddSeconds(-1)));

        keycloak.Calls.ShouldBe(2);
    }

    // --- Fixture ---------------------------------------------------------------------------------------------------

    private static SessionTokenRefresher RefresherFor(RecordingKeycloak keycloak)
    {
        var options = new OpenIdConnectOptions
        {
            ClientId = "cracra-bff",
            ClientSecret = "secret",
            Backchannel = new HttpClient(keycloak),
            ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { TokenEndpoint = "https://keycloak.test/token" }),
        };

        var monitor = Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        monitor.Get(OpenIdConnectDefaults.AuthenticationScheme).Returns(options);

        return new SessionTokenRefresher(
            monitor,
            new FixedClock(Now),
            NullLogger<SessionTokenRefresher>.Instance);
    }

    private static CookieValidatePrincipalContext Session(
        DateTimeOffset? expiresAt,
        string? refreshToken = "old-refresh")
    {
        var properties = new AuthenticationProperties();
        var tokens = new List<AuthenticationToken>
        {
            new() { Name = "access_token", Value = "old-access" },
            new() { Name = "id_token", Value = "old-id" },
        };

        if (refreshToken is not null)
        {
            tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = refreshToken });
        }

        if (expiresAt is { } expiry)
        {
            tokens.Add(new AuthenticationToken
            {
                Name = "expires_at",
                Value = expiry.ToString("o", CultureInfo.InvariantCulture),
            });
        }

        properties.StoreTokens(tokens);

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "camille")], "Cookies"));

        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new RecordingSignOut());

        return new CookieValidatePrincipalContext(
            new DefaultHttpContext { RequestServices = services.BuildServiceProvider() },
            new AuthenticationScheme(
                CookieAuthenticationDefaults.AuthenticationScheme,
                displayName: null,
                typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private static bool SignedOut(CookieValidatePrincipalContext context) =>
        ((RecordingSignOut)context.HttpContext.RequestServices.GetRequiredService<IAuthenticationService>())
        .SignedOut;

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Keycloak's token endpoint, as far as this class can tell.</summary>
    private sealed class RecordingKeycloak : HttpMessageHandler
    {
        private int calls;

        public int Calls => Volatile.Read(ref this.calls);

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public bool IncludeRefreshToken { get; init; } = true;

        public bool IncludeIdToken { get; init; } = true;

        public Exception? Throw { get; init; }

        /// <summary>Held open to keep several callers in flight at once.</summary>
        public TaskCompletionSource? Gate { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.calls);

            if (Gate is not null)
            {
                await Gate.Task;
            }

            if (Throw is not null)
            {
                throw Throw;
            }

            if (Status is not HttpStatusCode.OK)
            {
                return new HttpResponseMessage(Status) { Content = new StringContent("invalid_grant") };
            }

            var fields = new List<string>
            {
                "\"access_token\":\"fresh-access\"",
                "\"token_type\":\"Bearer\"",
                "\"expires_in\":300",
            };

            if (IncludeRefreshToken)
            {
                fields.Add("\"refresh_token\":\"fresh-refresh\"");
            }

            if (IncludeIdToken)
            {
                fields.Add("\"id_token\":\"fresh-id\"");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{{string.Join(',', fields)}}}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    /// <summary>Enough of the authentication stack for <c>SignOutAsync</c> to have somewhere to land.</summary>
    private sealed class RecordingSignOut : IAuthenticationService
    {
        public bool SignedOut { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut = true;

            return Task.CompletedTask;
        }
    }
}
