using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace Cracra.BuildingBlocks.Persistence;

/// <summary>
/// How the platform reaches Postgres. Three identities, one server (<c>architecture.md §3</c>).
/// </summary>
/// <remarks>
/// The orchestrator hands us one administrative connection string and two passwords; the owner and runtime
/// strings are derived from it. That is deliberate — it keeps the AppHost from having to assemble three nearly
/// identical strings by hand, and it makes it structurally impossible for them to drift onto different servers or
/// databases, which is the failure mode where migrations quietly apply somewhere the app never reads.
/// </remarks>
public sealed class CracraDatabaseOptions
{
    public const string SectionName = "Cracra:Database";

    /// <summary>Superuser. Used once at startup to create roles, and never by request-path code.</summary>
    [Required]
    public string AdminConnectionString { get; set; } = string.Empty;

    /// <summary>Password for <c>app_owner</c>, the role that owns the schemas and runs migrations.</summary>
    [Required]
    public string OwnerPassword { get; set; } = string.Empty;

    /// <summary>Password for <c>app_rw</c>, the non-owner runtime role that FORCE ROW LEVEL SECURITY applies to.</summary>
    [Required]
    public string RuntimePassword { get; set; } = string.Empty;

    /// <summary>Run role creation, the access schema and EF migrations at startup. Off in production deploys.</summary>
    public bool MigrateOnStartup { get; set; } = true;

    public const string OwnerRole = "app_owner";
    public const string RuntimeRole = "app_rw";

    public string OwnerConnectionString => WithIdentity(OwnerRole, OwnerPassword);

    public string RuntimeConnectionString => WithIdentity(RuntimeRole, RuntimePassword);

    private string WithIdentity(string username, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = username,
            Password = password,
        };

        return builder.ConnectionString;
    }
}
