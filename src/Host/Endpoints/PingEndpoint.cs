using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Web.Authorization;
using Cracra.BuildingBlocks.Web.Users;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cracra.Host.Endpoints;

public sealed record PingResponse(
    string Service,
    DateTimeOffset UtcNow,
    PingIdentity Identity,
    PingDatabaseSession DatabaseSession);

/// <summary>What the application believes about the caller, resolved from the token.</summary>
public sealed record PingIdentity(
    Guid UserId,
    string UserName,
    Guid? UnitId,
    IReadOnlyList<Guid> DepartmentIds,
    IReadOnlyList<string> Roles,
    string Language);

/// <summary>What Postgres actually sees. If these two disagree, RLS is not protecting anything.</summary>
public sealed record PingDatabaseSession(
    Guid? UserId,
    Guid? UnitId,
    IReadOnlyList<Guid> DepartmentIds,
    IReadOnlyList<string> Roles,
    bool IsScoped);

/// <summary>
/// The S0 acceptance proof, and the only endpoint whose job is to describe itself. It walks the entire chain —
/// session cookie at the BFF, bearer token here, claims to <see cref="IUserContext"/>, context to Postgres session
/// GUCs — and reports both ends so a mismatch is visible rather than merely theoretical.
/// </summary>
public sealed class PingEndpoint(PlatformDbContext database, IUserContext user)
    : EndpointWithoutRequest<PingResponse>
{
    public override void Configure()
    {
        Get("/ping");
        Policies(CracraPolicies.Authenticated);
        Description(builder => builder
            .WithTags("Platform")
            .WithSummary("Proves the authenticated request chain and the RLS session context."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(
            new PingResponse(
                "cracra-api",
                DateTimeOffset.UtcNow,
                new PingIdentity(
                    user.UserId,
                    user.UserName,
                    user.UnitId,
                    user.DepartmentIds,
                    user.Roles,
                    user.Language),
                await ReadDatabaseSessionAsync(ct)),
            ct);
    }

    /// <summary>
    /// Reads the GUCs back through the <c>access</c> helper functions rather than through <c>current_setting</c>
    /// directly, so this exercises the same accessors the RLS policies use — testing the plumbing, not a copy of it.
    /// </summary>
    private async Task<PingDatabaseSession> ReadDatabaseSessionAsync(CancellationToken ct)
    {
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();

        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await database.Database.OpenConnectionAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            select access.uid()       as user_id,
                   access.unit()      as unit_id,
                   access.depts()     as dept_ids,
                   access.roles()     as roles,
                   access.is_scoped() as is_scoped;
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            return new PingDatabaseSession(null, null, [], [], false);
        }

        return new PingDatabaseSession(
            await reader.IsDBNullAsync(0, ct) ? null : reader.GetGuid(0),
            await reader.IsDBNullAsync(1, ct) ? null : reader.GetGuid(1),
            await reader.IsDBNullAsync(2, ct) ? [] : reader.GetFieldValue<Guid[]>(2),
            await reader.IsDBNullAsync(3, ct) ? [] : reader.GetFieldValue<string[]>(3),
            !await reader.IsDBNullAsync(4, ct) && reader.GetBoolean(4));
    }
}
