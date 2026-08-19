using System.Security.Claims;
using System.Text.Encodings.Web;
using Cracra.BuildingBlocks.Web.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cracra.BuildingBlocks.Testing;

/// <summary>
/// The API, wired to a throwaway Postgres and to an authentication scheme a test can drive directly.
/// </summary>
/// <remarks>
/// Keycloak is deliberately not in the loop. What integration tests need to vary is <em>who the caller is</em>, and
/// minting real tokens to do that would test Keycloak's mappers rather than our access rules — the realm's mapping
/// is covered once, end to end, by the Playwright suite. Here, <see cref="AsUser"/> swaps the ambient context and
/// everything downstream, RLS included, behaves exactly as it would for a real session.
/// </remarks>
public sealed class CracraApplicationFactory(string adminConnectionString) : WebApplicationFactory<Program>
{
    public const string TestScheme = "CracraTest";

    /// <summary>The identity the next request will run as. Set it with <see cref="AsUser"/>.</summary>
    public IUserContext CurrentUser { get; private set; } = UserContext.Anonymous;

    /// <summary>
    /// Extra registrations for one test — a notification handler to observe, a clock to freeze. Applied after the
    /// application's own, so it can replace as well as add.
    /// </summary>
    public Action<IServiceCollection>? ConfigureAdditionalServices { get; init; }

    public CracraApplicationFactory AsUser(IUserContext user)
    {
        CurrentUser = user;

        return this;
    }

    public CracraApplicationFactory AsAnonymous() => AsUser(UserContext.Anonymous);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cracra:Database:AdminConnectionString"] = adminConnectionString,
                ["Cracra:Database:OwnerPassword"] = "test-owner-password",
                ["Cracra:Database:RuntimePassword"] = "test-runtime-password",
                ["Cracra:Database:MigrateOnStartup"] = "true",

                // Neither is reachable from a test run; both are validated at startup, so they need to be
                // well-formed rather than live.
                ["Cracra:Storage:ServiceUrl"] = "http://localhost:9010",
                ["Cracra:Storage:AccessKey"] = "test",
                ["Cracra:Storage:SecretKey"] = "test",
                ["Cracra:Ai:BaseUrl"] = "http://localhost:5200",
                ["Cracra:Keycloak:Authority"] = "http://localhost:8080/realms/cracra",

                // The drain loop is driven explicitly by the outbox tests; a background timer racing them would
                // make those tests flaky for no benefit.
                ["Outbox:PollingInterval"] = "00:04:00",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestScheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestScheme, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestScheme;
                options.DefaultChallengeScheme = TestScheme;
            });

            services.AddSingleton<ITestUserProvider>(new TestUserProvider(() => CurrentUser));

            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

            ConfigureAdditionalServices?.Invoke(services);
        });
    }
}

public interface ITestUserProvider
{
    IUserContext Current { get; }
}

internal sealed class TestUserProvider(Func<IUserContext> resolve) : ITestUserProvider
{
    public IUserContext Current => resolve();
}

/// <summary>
/// Authenticates as whatever <see cref="CracraApplicationFactory.CurrentUser"/> currently is, emitting the same
/// claims Keycloak would. Going through claims rather than setting the context directly means the middleware, the
/// claim parsing and the fail-closed rules are all still under test.
/// </summary>
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITestUserProvider users) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = users.Current;

        if (!user.IsAuthenticated)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(CracraClaims.Subject, user.UserId.ToString()),
            new(CracraClaims.PreferredUsername, user.UserName),
            new(CracraClaims.Locale, user.Language),
        };

        if (user.UnitId is { } unitId)
        {
            claims.Add(new Claim(CracraClaims.UnitId, unitId.ToString()));
        }

        claims.AddRange(user.DepartmentIds.Select(id => new Claim(CracraClaims.DepartmentIds, id.ToString())));
        claims.AddRange(user.Roles.Select(role => new Claim(CracraClaims.ContextualRoles, role)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CracraApplicationFactory.TestScheme));

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, CracraApplicationFactory.TestScheme)));
    }
}
