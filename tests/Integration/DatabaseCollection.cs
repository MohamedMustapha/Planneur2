using Cracra.BuildingBlocks.Testing;

namespace Cracra.Tests.Integration;

/// <summary>
/// One Postgres container shared by every integration test in this assembly.
/// </summary>
/// <remarks>
/// xUnit requires a collection definition to live in the same assembly as the tests that join it, which is why
/// this sits here rather than next to <see cref="PostgresFixture"/>. Sharing one container is deliberate: starting
/// Postgres costs a couple of seconds and paying that per class would dominate the run, while the tests are
/// written not to depend on each other's rows.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
