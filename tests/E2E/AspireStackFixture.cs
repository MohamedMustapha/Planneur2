using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Cracra.Tests.E2E;

/// <summary>
/// Boots the whole dev box — Postgres, Keycloak with the real realm import, RustFS, SEQ, the LLM stub, the API,
/// the BFF and the Angular dev server — and hands the tests a browser pointed at it.
/// </summary>
/// <remarks>
/// This is the one suite that exercises the pieces the other three deliberately stub. The integration tests swap
/// the authentication scheme so they can test access rules without Keycloak; that leaves the realm's protocol
/// mappers — the thing that decides whether a real login produces a real scope — untested. Here a browser
/// actually logs in, so a broken mapper fails a test instead of failing in production.
/// </remarks>
public sealed class AspireStackFixture : IAsyncLifetime
{
    private DistributedApplication? _app;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    /// <summary>Root of the Angular dev server, which proxies /api and /bff to the BFF (same origin).</summary>
    public string WebBaseUrl { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Cracra_AppHost>();

        builder.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        _app = await builder.BuildAsync();

        await _app.StartAsync();

        // Containers pull, Keycloak imports a realm, the Angular dev server compiles. Five minutes is generous
        // for a warm machine and barely enough for a cold one; failing here should read as "the stack did not
        // come up", not as a test failure.
        var readiness = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        await _app.ResourceNotifications.WaitForResourceHealthyAsync("bff", readiness.Token);
        await _app.ResourceNotifications.WaitForResourceAsync("web", KnownResourceStates.Running, readiness.Token);

        WebBaseUrl = _app.GetEndpoint("web", "http").ToString().TrimEnd('/');

        // "Running" for a dev server means the npm process started, not that Angular finished its first compile
        // and bound the port. Tests that started in that window got ERR_CONNECTION_REFUSED and looked like
        // application failures. Poll the endpoint itself until it actually answers.
        await WaitForWebServerAsync(readiness.Token);

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    private async Task WaitForWebServerAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await http.GetAsync(WebBaseUrl, ct);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // Not up yet.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        throw new TimeoutException($"The Angular dev server at {WebBaseUrl} never started serving.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// Signs in through the real Keycloak login form as one of the seeded people and returns the landed page.
    /// </summary>
    public async Task<IPage> SignInAsync(string username, string password = "cracra")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = WebBaseUrl,
            IgnoreHTTPSErrors = true,
            Locale = "fr-FR",
        });

        var page = await context.NewPageAsync();

        // Surface browser-side failures. Without this an exception inside a lazy route shows up only as "element
        // not found", which reads like a selector problem and sends you looking in the wrong place entirely.
        page.PageError += (_, error) => Console.WriteLine($"[browser pageerror] {error}");
        page.Console += (_, message) =>
        {
            if (message.Type is "error" or "warning")
            {
                Console.WriteLine($"[browser {message.Type}] {message.Text}");
            }
        };

        await page.GotoAsync("/board");

        // The guard bounces an anonymous visitor to /bff/login, which redirects to Keycloak.
        await page.WaitForURLAsync(url => url.Contains("/realms/cracra", StringComparison.Ordinal),
            new PageWaitForURLOptions { Timeout = 60_000 });

        await page.FillAsync("#username", username);
        await page.FillAsync("#password", password);
        await page.ClickAsync("#kc-login");

        await page.WaitForURLAsync(url => url.Contains("/board", StringComparison.Ordinal),
            new PageWaitForURLOptions { Timeout = 60_000 });

        return page;
    }
}

[CollectionDefinition(Name)]
public sealed class AspireStackCollection : ICollectionFixture<AspireStackFixture>
{
    public const string Name = "aspire-stack";
}
