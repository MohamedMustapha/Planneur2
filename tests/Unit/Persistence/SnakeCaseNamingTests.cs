using Cracra.BuildingBlocks.Persistence.Naming;

namespace Cracra.Tests.Unit.Persistence;

/// <summary>
/// Column names end up quoted verbatim inside RLS policies, which are hand-written SQL. A naming surprise here
/// shows up as a migration that fails at deploy time, so the edge cases are worth pinning.
/// </summary>
public sealed class SnakeCaseNamingTests
{
    [Theory]
    [InlineData("OccurredAt", "occurred_at")]
    [InlineData("Id", "id")]
    [InlineData("ProjectId", "project_id")]
    [InlineData("AttemptCount", "attempt_count")]
    [InlineData("outbox_message", "outbox_message")]
    [InlineData("ix_outbox_message_pending", "ix_outbox_message_pending")]
    [InlineData("HTTPStatus", "http_status")]
    [InlineData("Iso8601Code", "iso8601_code")]
    [InlineData("", "")]
    public void Converts_identifiers(string input, string expected)
    {
        SnakeCaseNaming.ToSnakeCase(input).ShouldBe(expected);
    }
}
