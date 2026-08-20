using Cracra.BuildingBlocks.Web.Users;
using Npgsql;

namespace Cracra.BuildingBlocks.Testing;

/// <summary>
/// Evaluates an <c>access.*</c> predicate under a chosen identity, against a real Postgres.
/// </summary>
/// <remarks>
/// <para>
/// This is the reusable half of S2's keystone fixture. Every later slice attaches its policies to these
/// predicates, so every later slice needs to assert what they return for each contextual role — and doing that by
/// creating a table, inserting rows and selecting them back would test the slice's schema as much as the rule.
/// Calling the predicate directly asks exactly one question: given this session, does the matrix say yes?
/// </para>
/// <para>
/// The session is stamped the same way the request path stamps it, through <c>set_config</c> on the same GUCs the
/// RLS interceptor writes. If that ever diverges from what the interceptor does, these tests stop meaning
/// anything — which is why <c>PingChainTests</c> separately asserts the interceptor writes what it claims.
/// </para>
/// </remarks>
public sealed class RlsMatrixProbe(string connectionString) : IAsyncDisposable
{
    private NpgsqlConnection? _connection;

    /// <summary>Opens a session stamped as <paramref name="user"/>, reusing the connection across calls.</summary>
    public async Task<NpgsqlConnection> ConnectAsync(IUserContext user, CancellationToken ct = default)
    {
        if (_connection is null)
        {
            _connection = new NpgsqlConnection(connectionString);
            await _connection.OpenAsync(ct);
        }

        await using var command = _connection.CreateCommand();

        command.CommandText = """
            select set_config('app.user_id',  @userId,  false),
                   set_config('app.unit_id',  @unitId,  false),
                   set_config('app.dept_ids', @deptIds, false),
                   set_config('app.roles',    @roles,   false);
            """;

        command.Parameters.AddWithValue("userId", user.IsAuthenticated && user.UserId != Guid.Empty
            ? user.UserId.ToString()
            : string.Empty);
        command.Parameters.AddWithValue("unitId", user.UnitId?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue("deptIds", string.Join(',', user.DepartmentIds));
        command.Parameters.AddWithValue("roles", string.Join(',', user.Roles));

        await command.ExecuteNonQueryAsync(ct);

        return _connection;
    }

    /// <summary>
    /// Evaluates a boolean predicate as <paramref name="user"/>.
    /// </summary>
    /// <param name="sql">
    /// The predicate call, with positional parameters as <c>@p0</c>, <c>@p1</c>… e.g.
    /// <c>access.can_read_activity(@p0, @p1, @p2, @p3)</c>.
    /// </param>
    public async Task<bool> EvaluateAsync(
        IUserContext user,
        string sql,
        object?[] parameters,
        CancellationToken ct = default)
    {
        var connection = await ConnectAsync(user, ct);

        await using var command = connection.CreateCommand();
        command.CommandText = $"select {sql}";

        for (var i = 0; i < parameters.Length; i++)
        {
            command.Parameters.AddWithValue($"p{i}", parameters[i] ?? DBNull.Value);
        }

        var result = await command.ExecuteScalarAsync(ct);

        // A predicate returning NULL is not "false with extra steps": it means a comparison hit an unset GUC, and
        // silently reading it as a deny would hide the fact that the session was never stamped.
        return result is bool value
            ? value
            : throw new InvalidOperationException(
                $"Predicate '{sql}' returned {result ?? "NULL"} rather than a boolean. "
                + "That usually means the session GUCs were not set.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
