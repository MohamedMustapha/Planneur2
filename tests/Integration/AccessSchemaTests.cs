using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Testing;
using Cracra.BuildingBlocks.Web.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Cracra.Tests.Integration;

/// <summary>
/// The <c>access</c> schema is the executable half of visibility-matrix.md. Every future slice's policies are
/// one-liners that delegate to these functions, so if they are wrong, every policy is wrong in the same way.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AccessSchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task An_unstamped_session_has_no_scope()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);

        // Force the host (and therefore the initializer) to run, then connect outside the request pipeline.
        _ = factory.Services;

        await using var connection = new NpgsqlConnection(postgres.AdminConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "select access.uid() is null, access.is_scoped(), cardinality(access.roles())";

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        // No GUCs means no identity, which means every predicate is false and every table reads empty. This is
        // the behaviour that makes "forgot to run the interceptor" a visible outage rather than a silent leak.
        reader.GetBoolean(0).ShouldBeTrue();
        reader.GetBoolean(1).ShouldBeFalse();
        reader.GetInt32(2).ShouldBe(0);
    }

    [Fact]
    public async Task Role_predicates_read_the_stamped_session()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);
        factory.AsUser(SeedOrganisation.Olivier);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = SeedOrganisation.Olivier;

        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select access.has('dept-head'),
                   access.has('pmo'),
                   access.is_head(),
                   access.is_system(),
                   access.unit() = @unit,
                   @dept = any(access.depts())
            """;
        command.Parameters.AddWithValue("unit", SeedOrganisation.Units.Development);
        command.Parameters.AddWithValue("dept", SeedOrganisation.Departments.InformationSystems);

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        reader.GetBoolean(0).ShouldBeTrue();
        reader.GetBoolean(1).ShouldBeFalse();
        reader.GetBoolean(2).ShouldBeTrue();
        // A human session must never satisfy is_system(); that predicate exists only for background jobs.
        reader.GetBoolean(3).ShouldBeFalse();
        reader.GetBoolean(4).ShouldBeTrue();
        reader.GetBoolean(5).ShouldBeTrue();
    }

    [Fact]
    public async Task The_runtime_role_is_not_the_owner_so_FORCE_row_level_security_applies()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);
        factory.AsUser(SeedOrganisation.Camille);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = SeedOrganisation.Camille;

        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select current_user,
                   (select relrowsecurity from pg_class where oid = 'platform.outbox_message'::regclass),
                   (select relforcerowsecurity from pg_class where oid = 'platform.outbox_message'::regclass)
            """;

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        reader.GetString(0).ShouldBe(CracraDatabaseOptions.RuntimeRole);
        reader.GetBoolean(1).ShouldBeTrue();
        reader.GetBoolean(2).ShouldBeTrue();
    }

    [Fact]
    public async Task A_user_session_cannot_read_the_outbox()
    {
        await using var factory = new CracraApplicationFactory(postgres.AdminConnectionString);
        factory.AsUser(SeedOrganisation.Nadia);

        using var scope = factory.Services.CreateScope();

        // Nadia is PMO — the widest human role there is. Even she must not see the queue: the outbox carries
        // events from every module, so a policy that let any human read it would route around every other policy.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = SeedOrganisation.Nadia;

        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var visible = await context.OutboxMessages.CountAsync(TestContext.Current.CancellationToken);

        visible.ShouldBe(0);
    }
}
