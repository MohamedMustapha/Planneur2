using System.Data.Common;
using Cracra.BuildingBlocks.Web.Users;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Cracra.BuildingBlocks.Persistence.Rls;

/// <summary>The Postgres session GUCs the <c>access</c> schema predicates read (<c>visibility-matrix.md §2</c>).</summary>
public static class RlsGucs
{
    public const string UserId = "app.user_id";
    public const string UnitId = "app.unit_id";
    public const string DepartmentIds = "app.dept_ids";
    public const string Roles = "app.roles";

    public static readonly IReadOnlyList<string> All = [UserId, UnitId, DepartmentIds, Roles];
}

/// <summary>
/// Stamps the current <see cref="IUserContext"/> onto every database connection the moment EF opens it, so the RLS
/// policies have a scope to evaluate against. This is the single point where an application identity becomes a
/// database identity; if it does not run, the policies see NULL and return nothing.
/// </summary>
/// <remarks>
/// <para>
/// The GUCs are set at <em>session</em> scope (<c>set_config(..., false)</c>) rather than transaction scope. The
/// architecture note describes transaction-local values, but EF issues plenty of reads outside an explicit
/// transaction, and a transaction-local GUC is discarded the moment that implicit transaction ends — which would
/// leave ordinary queries running with an empty scope. Session scope is safe here precisely because this
/// interceptor runs on <em>every</em> open: a pooled connection is always re-stamped before it is reused, so one
/// request can never observe another request's scope.
/// </para>
/// <para>
/// An unauthenticated context deliberately writes empty strings rather than skipping the statement. Empty means
/// <c>access.uid()</c> is NULL, every predicate is false, and the query returns zero rows — the system fails closed.
/// </para>
/// </remarks>
public sealed class RlsSessionInterceptor(
    IUserContext userContext,
    ILogger<RlsSessionInterceptor> logger) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyAsync(connection, cancellationToken);

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyAsync(connection, CancellationToken.None).GetAwaiter().GetResult();

        base.ConnectionOpened(connection, eventData);
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken ct)
    {
        var userId = userContext.IsAuthenticated && userContext.UserId != Guid.Empty
            ? userContext.UserId.ToString()
            : string.Empty;

        var unitId = userContext.UnitId?.ToString() ?? string.Empty;
        var departmentIds = string.Join(',', userContext.DepartmentIds);
        var roles = string.Join(',', userContext.Roles);

        await using var command = connection.CreateCommand();

        // One round trip for all four. Parameterised so an identifier can never be concatenated into SQL.
        command.CommandText = """
            select set_config('app.user_id',  @userId,  false),
                   set_config('app.unit_id',  @unitId,  false),
                   set_config('app.dept_ids', @deptIds, false),
                   set_config('app.roles',    @roles,   false);
            """;

        AddParameter(command, "userId", userId);
        AddParameter(command, "unitId", unitId);
        AddParameter(command, "deptIds", departmentIds);
        AddParameter(command, "roles", roles);

        await command.ExecuteNonQueryAsync(ct);

        logger.LogTrace(
            "RLS session set: user={UserId} unit={UnitId} depts={DepartmentIds} roles={Roles}",
            userId,
            unitId,
            departmentIds,
            roles);
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
