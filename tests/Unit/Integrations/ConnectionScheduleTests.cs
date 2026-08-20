using Cracra.Modules.Integrations.Contracts;
using Cracra.Modules.Integrations.Domain;
using Cracra.Modules.Integrations.Providers;
using Cracra.Modules.Integrations.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cracra.Tests.Unit.Integrations;

/// <summary>When a connection is due, and what its <c>auth_ref</c> resolves to.</summary>
public sealed class ConnectionScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_connection_that_has_never_synced_is_due_immediately()
    {
        Connection().IsDue(Now).ShouldBeTrue();
    }

    [Fact]
    public void A_connection_is_due_again_once_its_own_interval_has_passed()
    {
        var connection = Connection();

        connection.LastSyncedAt = Now.AddMinutes(-10);
        connection.IsDue(Now).ShouldBeFalse();

        connection.LastSyncedAt = Now.AddMinutes(-15);
        connection.IsDue(Now).ShouldBeTrue();
    }

    [Fact]
    public void A_zero_interval_means_on_demand_only()
    {
        var connection = Connection();

        connection.PollInterval = TimeSpan.Zero;

        // Distinct from inactive: the connection works, and a head pressing "sync now" still pulls it. It simply
        // has no schedule, which is the right setting for an instance somebody is billed per API call for.
        connection.IsDue(Now).ShouldBeFalse();
    }

    [Fact]
    public void An_inactive_connection_is_never_due()
    {
        var connection = Connection();

        connection.Active = false;
        connection.LastSyncedAt = null;

        connection.IsDue(Now).ShouldBeFalse();
    }

    [Theory]
    [InlineData("pat:abc", IntegrationCredential.PersonalAccessToken, "abc")]
    [InlineData("basic:user:password", IntegrationCredential.Basic, "user:password")]
    [InlineData("bearer:jwt-value", IntegrationCredential.Bearer, "jwt-value")]
    public void A_configured_secret_resolves_to_its_kind_and_value(string configured, string kind, string value)
    {
        var credential = Credentials(("is-devops", configured)).Resolve("is-devops");

        credential.Kind.ShouldBe(kind);

        // Basic keeps the colon: the value is the whole user:password pair, and splitting on the first colon
        // twice would leave the password behind.
        credential.Value.ShouldBe(value);
        credential.IsUsable.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-prefix")]
    [InlineData("oauth1:whatever")]
    public void Anything_the_platform_cannot_use_resolves_to_none(string configured)
    {
        var credential = Credentials(("is-devops", configured)).Resolve("is-devops");

        // None rather than a throw. A secret that has not been provisioned yet is a normal state on the way to a
        // working connection, and the pull reports it as a failure an administrator can act on — which is more
        // use than a startup crash in a process running nine other modules.
        credential.IsUsable.ShouldBeFalse();
    }

    [Fact]
    public void An_auth_ref_nothing_is_configured_for_resolves_to_none()
    {
        Credentials().Resolve("is-devops").IsUsable.ShouldBeFalse();
    }

    [Fact]
    public void The_secret_store_is_matched_case_insensitively()
    {
        // These arrive as environment variables, and the casing of an environment variable is not something a
        // deployment should have to get exactly right to be authenticated.
        Credentials(("IS-DevOps", "pat:abc")).Resolve("is-devops").IsUsable.ShouldBeTrue();
    }

    private static IIntegrationCredentials Credentials(params (string Ref, string Value)[] configured)
    {
        var options = new IntegrationsOptions();

        foreach (var (name, value) in configured)
        {
            options.Credentials[name] = value;
        }

        return new IntegrationCredentials(Options.Create(options), NullLogger<IntegrationCredentials>.Instance);
    }

    private static ExternalConnection Connection() => new()
    {
        Id = Guid.CreateVersion7(),
        DepartmentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Provider = ExternalProviders.AzureDevOps,
        Name = "IS — DevOps",
        BaseUrl = "https://devops.intranet",
        AuthRef = "is-devops",
        ProjectOrQueue = "CRACRA",
        PollInterval = TimeSpan.FromMinutes(15),
    };
}
