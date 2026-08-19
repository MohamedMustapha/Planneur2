using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Cracra.BuildingBlocks.Persistence;

/// <summary>Implemented per module so the initializer can migrate each schema without knowing its context type.</summary>
public interface IModuleMigrator
{
    string ModuleName { get; }

    Task MigrateAsync(string ownerConnectionString, CancellationToken ct);
}

public sealed class ModuleMigrator<TContext>(IServiceScopeFactory scopeFactory) : IModuleMigrator
    where TContext : ModuleDbContext
{
    public string ModuleName => typeof(TContext).Name;

    public async Task MigrateAsync(string ownerConnectionString, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        // Migrations create and alter objects, so they run as app_owner rather than as the runtime role — which by
        // design has no DDL rights at all.
        await context.Database.GetDbConnection().CloseAsync();
        context.Database.SetConnectionString(ownerConnectionString);

        await context.Database.MigrateAsync(ct);
    }
}

/// <summary>
/// Brings a bare Postgres instance up to "the platform can run against it": roles, the access schema, then every
/// module's migrations. Runs once at startup, before the host starts serving.
/// </summary>
public sealed class PlatformDatabaseInitializer(
    IEnumerable<IModuleMigrator> migrators,
    IOptions<CracraDatabaseOptions> options,
    ILogger<PlatformDatabaseInitializer> logger) : IHostedLifecycleService
{
    private const string RolesScript = "Cracra.BuildingBlocks.Persistence.Access.DatabaseRoles.sql";
    private const string AccessScript = "Cracra.BuildingBlocks.Persistence.Access.AccessSchema.sql";

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!settings.MigrateOnStartup)
        {
            logger.LogInformation("MigrateOnStartup is off; assuming the database is already at the expected version");
            return;
        }

        var started = Stopwatch.GetTimestamp();

        await ApplyBootstrapScriptsAsync(settings, cancellationToken);

        foreach (var migrator in migrators)
        {
            logger.LogInformation("Migrating {ModuleName}", migrator.ModuleName);

            await migrator.MigrateAsync(settings.OwnerConnectionString, cancellationToken);
        }

        logger.LogInformation(
            "Database initialization completed in {ElapsedMilliseconds:0} ms",
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private async Task ApplyBootstrapScriptsAsync(CracraDatabaseOptions settings, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(settings.AdminConnectionString);

        await connection.OpenAsync(ct);

        // The roles script reads the passwords from session GUCs rather than taking them as interpolated text,
        // because CREATE ROLE ... PASSWORD cannot be parameterised and a password is exactly the kind of value
        // you never want to build a statement out of.
        await using (var configure = connection.CreateCommand())
        {
            configure.CommandText = """
                select set_config('cracra.app_owner_password', @ownerPassword, false),
                       set_config('cracra.app_rw_password',    @runtimePassword, false);
                """;

            configure.Parameters.AddWithValue("ownerPassword", settings.OwnerPassword);
            configure.Parameters.AddWithValue("runtimePassword", settings.RuntimePassword);

            await configure.ExecuteNonQueryAsync(ct);
        }

        await ExecuteScriptAsync(connection, RolesScript, ct);
        await ExecuteScriptAsync(connection, AccessScript, ct);

        logger.LogInformation("Roles and the access schema are in place");
    }

    private static async Task ExecuteScriptAsync(NpgsqlConnection connection, string resourceName, CancellationToken ct)
    {
        await using var stream = typeof(PlatformDatabaseInitializer).Assembly.GetManifestResourceStream(resourceName)
                                 ?? throw new InvalidOperationException(
                                     $"Embedded SQL script '{resourceName}' is missing from "
                                     + $"{Assembly.GetExecutingAssembly().GetName().Name}.");

        using var reader = new StreamReader(stream);

        await using var command = connection.CreateCommand();
        command.CommandText = await reader.ReadToEndAsync(ct);

        await command.ExecuteNonQueryAsync(ct);
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
