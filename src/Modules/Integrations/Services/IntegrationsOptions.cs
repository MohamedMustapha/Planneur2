using Cracra.Modules.Integrations.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cracra.Modules.Integrations.Services;

/// <summary>
/// The deployment's half of an integration: where it may talk to, and with what.
/// </summary>
/// <remarks>
/// Everything an administrator configures lives on the connection row; everything an operator configures lives
/// here. The split is the same one the platform draws everywhere else — a department head decides which DevOps
/// project their people work in, and has no business deciding which hosts the process may open a socket to.
/// </remarks>
public sealed class IntegrationsOptions
{
    public const string SectionName = "Cracra:Integrations";

    /// <summary>
    /// Hosts a connection may reach. Empty allows any, which is the dev box's case and nobody else's.
    /// </summary>
    /// <remarks>
    /// S10 asks for allow-listed endpoints, and this is it. Enforced twice: when a connection is written, so an
    /// administrator finds out immediately, and on every request, so a redirect cannot walk out of the list.
    /// </remarks>
    public IList<string> AllowedHosts { get; } = [];

    /// <summary>
    /// Secrets by <c>auth_ref</c>, in the form <c>kind:value</c> — <c>pat:…</c>, <c>basic:user:password</c>,
    /// <c>bearer:…</c>.
    /// </summary>
    /// <remarks>
    /// This is the vault seam. In a deployment the values arrive as environment variables injected from the
    /// secret store — <c>Cracra__Integrations__Credentials__is-devops</c> — and never touch source control or a
    /// database row. Keyed by name so that rotating a token is a deployment change and not an edit to a
    /// department's configuration.
    /// </remarks>
    public IDictionary<string, string> Credentials { get; } = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase);

    /// <summary>How often the scheduler looks for connections whose own poll interval has come due.</summary>
    public TimeSpan SchedulerInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Pull every active connection shortly after start, so a fresh dev box mirrors without being asked.</summary>
    public bool SyncOnStartup { get; set; }

    /// <summary>Ceiling on one pull, so a hung provider cannot hold the scheduler open indefinitely.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Resolves a connection's <c>auth_ref</c> to a usable credential.</summary>
public interface IIntegrationCredentials
{
    IntegrationCredential Resolve(string authRef);
}

/// <summary>
/// Reads the configured secret store.
/// </summary>
/// <remarks>
/// An unresolvable reference returns <see cref="IntegrationCredential.None"/> rather than throwing. A connection
/// whose secret has not been provisioned yet is a normal state on the way to a working one, and the sync reports
/// it as a failed pull with a message an administrator can act on — which is far more useful than a startup
/// crash in a process that runs nine other modules.
/// </remarks>
internal sealed class IntegrationCredentials(
    IOptions<IntegrationsOptions> options,
    ILogger<IntegrationCredentials> logger) : IIntegrationCredentials
{
    public IntegrationCredential Resolve(string authRef)
    {
        if (!options.Value.Credentials.TryGetValue(authRef, out var configured)
            || string.IsNullOrWhiteSpace(configured))
        {
            // No secret in the message, obviously — but no near-miss either. Logging "looked for is-devops-pat,
            // found is-devops" would be helpful and would also print half the secret store into SEQ.
            logger.LogWarning("No credential is configured for auth_ref '{AuthRef}'", authRef);

            return IntegrationCredential.None;
        }

        var separator = configured.IndexOf(':', StringComparison.Ordinal);

        if (separator <= 0)
        {
            logger.LogWarning(
                "The credential for '{AuthRef}' has no kind prefix; expected pat:, basic: or bearer:",
                authRef);

            return IntegrationCredential.None;
        }

        var kind = configured[..separator].ToLowerInvariant();
        var value = configured[(separator + 1)..];

        return kind switch
        {
            IntegrationCredential.PersonalAccessToken or IntegrationCredential.Basic or IntegrationCredential.Bearer
                => new IntegrationCredential(kind, value),
            _ => Unknown(authRef, kind),
        };
    }

    private IntegrationCredential Unknown(string authRef, string kind)
    {
        logger.LogWarning("The credential for '{AuthRef}' declares an unknown kind '{Kind}'", authRef, kind);

        return IntegrationCredential.None;
    }
}
