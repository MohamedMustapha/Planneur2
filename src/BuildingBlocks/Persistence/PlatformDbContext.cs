using Microsoft.EntityFrameworkCore;

namespace Cracra.BuildingBlocks.Persistence;

/// <summary>
/// The platform's own module. It owns no business data — its job in S0 is to be a real, migrated, RLS-governed
/// schema with a real outbox, so the plumbing every later slice depends on is exercised before any of those slices
/// exist. Modules from S1 onwards each get their own context and schema; none of them extend this one.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "platform";
}
