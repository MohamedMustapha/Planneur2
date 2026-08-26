using Microsoft.Extensions.Options;

namespace Cracra.Host.DevSeed;

/// <summary>
/// Runs the dev seeder once, shortly after the host is up.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="BackgroundService"/> rather than a startup hook, for the same reason the directory sync is one: the
/// platform initializer has already migrated every schema by the time hosted services start, and holding the whole
/// host open while demo data is written would make a failed seed look like a failed boot.
/// </para>
/// <para>
/// It deliberately does not wait for the Keycloak directory sync. No schema here has a foreign key into Directory
/// — cross-module references store the id and are validated in the application layer — so a project seeded before
/// its people have synced is not broken, it is briefly unnamed on screen and correct the moment the sync lands.
/// Waiting would mean coupling the seeder to another module's schedule to fix a few seconds of cosmetics.
/// </para>
/// <para>
/// The one exception is the node-profile attachment (v2 §10), which writes onto <c>unit</c> rows and therefore
/// cannot run before they exist. That step waits for them itself rather than making the whole seed wait, so the
/// property above still holds for everything else.
/// </para>
/// <para>
/// Failures are logged and swallowed. Demo data is worth a warning in the log; it is not worth taking the API down
/// on a dev box, which is the one environment where the developer can see the log anyway.
/// </para>
/// </remarks>
internal sealed class DevSeedHostedService(
    IDevDataSeeder seeder,
    IOptions<DevSeedOptions> options,
    ILogger<DevSeedHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Dev seed is disabled; no demo data will be written.");
            return;
        }

        try
        {
            // A moment for the host to finish starting. Nothing depends on this being exact.
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

            await seeder.SeedAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Dev seed failed. The API is unaffected; the boards will simply be empty.");
        }
    }
}

public static class DevSeedExtensions
{
    /// <summary>
    /// Wires the development data seeder.
    /// </summary>
    /// <remarks>
    /// Called only from the Development branch in <c>Program.cs</c>. The environment check lives at the call site
    /// rather than in here so that "this never runs in production" is visible in the composition root, where
    /// somebody reviewing the startup path will actually see it.
    /// </remarks>
    public static IServiceCollection AddCracraDevSeed(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DevSeedOptions>(configuration.GetSection(DevSeedOptions.SectionName));

        services.AddSingleton<IDevDataSeeder, DevDataSeeder>();
        services.AddHostedService<DevSeedHostedService>();

        return services;
    }
}
