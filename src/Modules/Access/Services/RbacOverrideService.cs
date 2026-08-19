using System.Text.Json;
using Cracra.BuildingBlocks.Web.Errors;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Access.Data;
using Cracra.Modules.Access.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Access.Services;

public sealed record CreateOverrideRequest(
    Guid PersonId,
    string Role,
    string ScopeType,
    Guid? ScopeId,
    bool IsGrant,
    string Reason,
    DateTimeOffset? ExpiresAt);

public interface IRbacOverrideService
{
    Task<IReadOnlyList<RbacOverrideDto>> ListAsync(Guid? personId, CancellationToken ct);

    Task<RbacOverrideDto> CreateAsync(CreateOverrideRequest request, CancellationToken ct);

    Task RevokeAsync(Guid overrideId, CancellationToken ct);
}

internal sealed class RbacOverrideService(
    AccessDbContext context,
    IUserContext user,
    IEffectiveRoleResolver resolver) : IRbacOverrideService
{
    public async Task<IReadOnlyList<RbacOverrideDto>> ListAsync(Guid? personId, CancellationToken ct)
    {
        // No role filtering here: the RLS read policy already removed anything this caller may not see.
        var overrides = await context.Overrides
            .Where(item => personId == null || item.PersonId == personId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;

        return [.. overrides.Select(item => ToDto(item, now))];
    }

    public async Task<RbacOverrideDto> CreateAsync(CreateOverrideRequest request, CancellationToken ct)
    {
        Validate(request);

        var now = DateTimeOffset.UtcNow;

        var item = new RbacOverride
        {
            Id = Guid.CreateVersion7(),
            PersonId = request.PersonId,
            Role = request.Role,
            ScopeType = ParseScope(request.ScopeType),
            ScopeId = request.ScopeId,
            IsGrant = request.IsGrant,
            Reason = request.Reason,
            ExpiresAt = request.ExpiresAt,
            CreatedBy = user.UserId,
            CreatedAt = now,
        };

        context.Overrides.Add(item);

        Audit(item, request.IsGrant ? "granted" : "denied", now);

        await context.SaveChangesAsync(ct);

        // Immediately, not on the next cache expiry. An override that took thirty seconds to apply would make
        // "deny wins" a claim the system does not honour at the moment it matters most.
        resolver.Invalidate(item.PersonId);

        return ToDto(item, now);
    }

    public async Task RevokeAsync(Guid overrideId, CancellationToken ct)
    {
        var item = await context.Overrides
            .AsTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == overrideId, ct)
            ?? throw new ResourceNotFoundException($"No override {overrideId}.");

        if (item.RevokedAt is not null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        item.RevokedAt = now;
        item.RevokedBy = user.UserId;

        Audit(item, "revoked", now);

        await context.SaveChangesAsync(ct);

        resolver.Invalidate(item.PersonId);
    }

    private void Audit(RbacOverride item, string action, DateTimeOffset now) =>
        context.OverrideAudits.Add(new RbacOverrideAudit
        {
            Id = Guid.CreateVersion7(),
            OverrideId = item.Id,
            PersonId = item.PersonId,
            Action = action,
            // A whole snapshot, so reading the trail never depends on replaying it against current state — the
            // override may since have been deleted, and the question is always what was true at the time.
            SnapshotJson = JsonSerializer.Serialize(new
            {
                item.Role,
                ScopeType = item.ScopeType.ToString(),
                item.ScopeId,
                item.IsGrant,
                item.Reason,
                item.ExpiresAt,
            }),
            ChangedBy = user.UserId,
            ChangedAt = now,
        });

    private static void Validate(CreateOverrideRequest request)
    {
        if (!ContextualRole.All.Contains(request.Role, StringComparer.Ordinal)
            || string.Equals(request.Role, ContextualRole.System, StringComparison.Ordinal))
        {
            // 'system' is for background jobs. Granting it by hand would hand a person read-all across every
            // module in one row — the exact bypass the whole matrix exists to prevent.
            throw new DomainRuleViolationException($"'{request.Role}' is not a grantable contextual role.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainRuleViolationException(
                "An override needs a reason; otherwise nobody can safely remove it later.");
        }

        var scope = ParseScope(request.ScopeType);

        if (scope is not ScopeType.Global && request.ScopeId is null)
        {
            throw new DomainRuleViolationException($"A {request.ScopeType} override needs a scope id.");
        }

        if (request.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            throw new DomainRuleViolationException("An override cannot expire in the past.");
        }
    }

    private static ScopeType ParseScope(string scopeType) =>
        Enum.TryParse<ScopeType>(scopeType, ignoreCase: true, out var parsed)
            ? parsed
            : throw new DomainRuleViolationException($"'{scopeType}' is not a valid scope type.");

    private static RbacOverrideDto ToDto(RbacOverride item, DateTimeOffset now) => new(
        item.Id,
        item.PersonId,
        item.Role,
        item.ScopeType.ToString(),
        item.ScopeId,
        item.IsGrant,
        item.Reason,
        item.ExpiresAt,
        item.CreatedBy,
        item.CreatedAt,
        item.IsActiveAt(now));
}
