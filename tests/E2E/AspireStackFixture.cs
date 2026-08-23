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

    /// <summary>
    /// Root of the Azure DevOps / ServiceNow stub, for a test that configures a connection against it.
    /// </summary>
    /// <remarks>
    /// Exposed rather than hard-coded because the stub's port is Aspire's to choose. A test uses it exactly as an
    /// administrator would use a real collection URL — it goes in the connection's base-URL field, and nothing
    /// about the platform's code path differs.
    /// </remarks>
    public string ProvidersBaseUrl { get; private set; } = string.Empty;

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
        ProvidersBaseUrl = _app.GetEndpoint("providers", "http").ToString().TrimEnd('/');

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
            // Bigger than Playwright's 1280x720 default, because this application is a nav rail beside a
            // five-day timeline and the default is narrow enough to clip the canvas. A clipped element is still
            // in the DOM, so the symptom is an assertion that waits its full minute on something that is
            // demonstrably there — which reads as a broken feature rather than as a small window.
            ViewportSize = new ViewportSize { Width = 1600, Height = 1200 },
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

        await ResetPreferencesAsync(page);

        return page;
    }

    /// <summary>
    /// Puts this person's display preferences back to "never chosen" before the journey starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v2 §02 made language, theme and Focus mode server-persisted, which is right for a product and quietly
    /// poisonous for a suite that shares one stack: a journey that switches to English leaves every later
    /// journey's French assertions matching nothing, and which journeys those are changes with the run order.
    /// The symptom is a handful of unrelated tests failing on locators that are obviously correct.
    /// </para>
    /// <para>
    /// Nulls rather than explicit values, so what each journey starts from is the shipped default for that
    /// person's role — the state a new employee meets — rather than a set of values this fixture has opinions
    /// about. It runs after the redirect has landed so the session cookie is in place, and it is deliberately
    /// part of signing in: there is no reading of this suite in which a journey wants the previous one's shell.
    /// </para>
    /// </remarks>
    private static async Task ResetPreferencesAsync(IPage page)
    {
        var response = await page.APIRequest.PutAsync("/api/directory/me/preferences", new APIRequestContextOptions
        {
            Headers = new Dictionary<string, string> { ["X-Cracra-Csrf"] = "1" },
            DataObject = new Dictionary<string, object?>
            {
                ["language"] = null,
                ["timeZone"] = null,
                ["theme"] = null,
                ["focusMode"] = null,
            },
        });

        // Not fatal. A person the directory sync has not created yet has no row to reset, and that is a state the
        // journey itself should report rather than one the fixture should throw on.
        if (response.Status is not (200 or 404))
        {
            throw new InvalidOperationException(
                $"Could not reset preferences before the journey started: {response.Status} {response.StatusText}.");
        }

        await page.ReloadAsync();
    }

    /// <summary>
    /// Takes the signed-in person out of Focus mode, if they are in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v2 §02 turns Focus mode on by default for members, and Focus mode removes the secondary panels: the "à
    /// venir" strip, the language switcher, the department switcher. A journey about any of those has to leave
    /// Focus mode first, exactly as the person would — the control is deliberately always drawn, in both states,
    /// so this is a real path and not a test-only escape hatch.
    /// </para>
    /// <para>
    /// Idempotent, because the toggle is a toggle: clicking it blindly would put a head who was already out of
    /// Focus mode into it. The preference is server-persisted, so it also survives to the next test in this
    /// shared stack — which is fine in the direction this method moves it, and the reason it only ever moves it
    /// one way.
    /// </para>
    /// </remarks>
    public static async Task LeaveFocusModeAsync(IPage page)
    {
        // Addressed by class, not by name. The button is always drawn but its label is the action rather than
        // the state — "Mode focus" to enter, "Quitter le focus" to leave — so matching on the entering label
        // finds it only when it is already off, which is precisely the case this method does not need to handle.
        var toggle = page.Locator("button.topbar__focus");

        await Assertions.Expect(toggle).ToBeVisibleAsync(new() { Timeout = 60_000 });

        if (await toggle.GetAttributeAsync("aria-pressed") == "true")
        {
            await toggle.ClickAsync();

            await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "false");
        }
    }
}

[CollectionDefinition(Name)]
public sealed class AspireStackCollection : ICollectionFixture<AspireStackFixture>
{
    public const string Name = "aspire-stack";
}
