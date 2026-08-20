using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Meetings.Contracts;
using Cracra.Modules.Meetings.Data;
using Cracra.Modules.Meetings.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Meetings.Services;

/// <summary>What the caller supplies for a patch party, an audit, a go-live or a deadline.</summary>
public sealed record SpecialDayRequest(
    string Kind,
    string NameKey,
    string ScopeType,
    Guid? ScopeId,
    DateOnly Date,
    bool AllDay,
    string Severity,
    string? Description);

/// <summary>The dated half of S7 — everything that colours a board column rather than occupying an hour.</summary>
public interface ISpecialDayService
{
    Task<IReadOnlyList<SpecialDayView>> ListAsync(
        DateOnly? from,
        DateOnly? to,
        string? scopeType,
        Guid? scopeId,
        CancellationToken ct);

    Task<SpecialDayView> CreateAsync(SpecialDayRequest request, CancellationToken ct);

    Task<SpecialDayView> UpdateAsync(Guid id, SpecialDayRequest request, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}

internal sealed class SpecialDayService(
    MeetingsDbContext context,
    IMeetingScopeResolver scopes,
    IUserContext user) : ISpecialDayService
{
    public async Task<IReadOnlyList<SpecialDayView>> ListAsync(
        DateOnly? from,
        DateOnly? to,
        string? scopeType,
        Guid? scopeId,
        CancellationToken ct)
    {
        var normalized = string.IsNullOrWhiteSpace(scopeType) ? null : scopeType.Trim().ToLowerInvariant();

        var days = await context.SpecialDays
            .Where(day => from == null || day.Date >= from)
            .Where(day => to == null || day.Date <= to)
            .Where(day => normalized == null || day.ScopeType == normalized)
            .Where(day => scopeId == null || day.ScopeId == scopeId)
            .OrderBy(day => day.Date)
            .ThenBy(day => day.NameKey)
            .ToListAsync(ct);

        return [.. days.Select(Project)];
    }

    public async Task<SpecialDayView> CreateAsync(SpecialDayRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = await scopes.ResolveAsync(request.ScopeType, request.ScopeId, ct);
        var now = DateTimeOffset.UtcNow;

        var day = new SpecialDay
        {
            Id = Guid.CreateVersion7(),
            Kind = Code(request.Kind, nameof(request.Kind)),
            NameKey = Name(request.NameKey),
            ScopeType = scope.ScopeType,
            ScopeId = scope.ScopeId,
            DepartmentId = scope.DepartmentId,
            Date = request.Date,
            AllDay = request.AllDay,
            Severity = Severity(request.Severity),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedBy = user.UserId,
            CreatedAt = now,
            ModifiedAt = now,
        };

        context.SpecialDays.Add(day);

        context.Enqueue(new SpecialDayUpserted(
            day.Id, day.Kind, day.ScopeType, day.ScopeId, day.Date, day.Severity));

        await context.SaveChangesAsync(ct);

        return Project(day);
    }

    public async Task<SpecialDayView> UpdateAsync(Guid id, SpecialDayRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var day = await FindAsync(id, ct);
        var scope = await scopes.ResolveAsync(request.ScopeType, request.ScopeId, ct);

        day.Kind = Code(request.Kind, nameof(request.Kind));
        day.NameKey = Name(request.NameKey);
        day.ScopeType = scope.ScopeType;
        day.ScopeId = scope.ScopeId;
        day.DepartmentId = scope.DepartmentId;
        day.Date = request.Date;
        day.AllDay = request.AllDay;
        day.Severity = Severity(request.Severity);
        day.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        day.ModifiedAt = DateTimeOffset.UtcNow;

        context.Enqueue(new SpecialDayUpserted(
            day.Id, day.Kind, day.ScopeType, day.ScopeId, day.Date, day.Severity));

        await context.SaveChangesAsync(ct);

        return Project(day);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var day = await FindAsync(id, ct);

        context.SpecialDays.Remove(day);

        context.Enqueue(new SpecialDayRemoved(day.Id, day.ScopeType, day.ScopeId));

        await context.SaveChangesAsync(ct);
    }

    private async Task<SpecialDay> FindAsync(Guid id, CancellationToken ct)
    {
        var day = await context.SpecialDays.AsTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return day ?? throw new ResourceNotFoundException("That special day does not exist.");
    }

    private static SpecialDayView Project(SpecialDay day) => new(
        day.Id,
        day.Kind,
        day.NameKey,
        day.ScopeType,
        day.ScopeId,
        day.Date,
        day.AllDay,
        day.Severity,
        day.Description);

    private static string Name(string value) => string.IsNullOrWhiteSpace(value)
        ? throw new DomainRuleViolationException("A special day needs a name.")
        : value.Trim();

    private static string Severity(string? value)
    {
        var severity = (value ?? SpecialDaySeverities.Info).Trim().ToLowerInvariant();

        // A closed set, unlike the kinds. Severity is not vocabulary a department extends — it maps onto three
        // colours the design fixes, and a fourth value would render as nothing at all.
        return SpecialDaySeverities.All.Contains(severity, StringComparer.Ordinal)
            ? severity
            : throw new DomainRuleViolationException(
                $"'{value}' is not a severity. Use one of: {string.Join(", ", SpecialDaySeverities.All)}.");
    }

    private static string Code(string value, string field)
    {
        var code = (value ?? string.Empty).Trim().ToLowerInvariant();

        if (code.Length == 0)
        {
            throw new DomainRuleViolationException($"{field} is required.");
        }

        if (!code.All(character => char.IsAsciiLetterOrDigit(character) || character is '-'))
        {
            throw new DomainRuleViolationException($"{field} may contain only letters, digits and hyphens.");
        }

        return code;
    }
}
