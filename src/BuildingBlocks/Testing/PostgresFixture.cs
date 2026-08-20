using Testcontainers.PostgreSql;
using Xunit;

namespace Cracra.BuildingBlocks.Testing;

/// <summary>
/// A real Postgres for the duration of the test collection.
/// </summary>
/// <remarks>
/// Not an in-memory provider and not SQLite: the thing under test <em>is</em> row-level security, plus schemas,
/// GUCs and role ownership. None of that exists outside Postgres, so a fake would test nothing that matters.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("cracra")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public string AdminConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
