using System.Text.Json;
using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Directory.Services;

/// <summary>
/// A profile as an administrator edits it — nulls preserved, because null is the override mechanism.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="NodeProfileSnapshot"/>. That one is resolved and has no nulls left; this one is the
/// row. An admin screen that edited the resolved shape would silently write every inherited value down onto the
/// child the first time it saved, turning a one-field override into a full copy that stops tracking its parent.
/// </remarks>
public sealed record NodeProfileDetail(
    Guid Id,
    string Code,
    string LabelKey,
    string? ActivityTaxonomyJson,
    IReadOnlyList<string>? BoardArchetypes,
    IReadOnlyList<string>? ItemTypes,
    string? CapabilitiesJson,
    IReadOnlyList<string>? SolvesCategories,
    string? BudgetDefaultsJson,
    string? HeadlinePattern);

public sealed record SaveNodeProfileRequest(
    string Code,
    string LabelKey,
    string? ActivityTaxonomyJson = null,
    IReadOnlyList<string>? BoardArchetypes = null,
    IReadOnlyList<string>? ItemTypes = null,
    string? CapabilitiesJson = null,
    IReadOnlyList<string>? SolvesCategories = null,
    string? BudgetDefaultsJson = null,
    string? HeadlinePattern = null);

/// <summary>Attaches a profile to a node, or detaches it so the node inherits again.</summary>
/// <param name="ProfileId">Null detaches — which is a real edit, not a no-op, and the reason this is nullable.</param>
public sealed record AttachNodeProfileRequest(Guid? ProfileId);

public interface INodeProfileService
{
    Task<IReadOnlyList<NodeProfileDetail>> ListAsync(CancellationToken ct);

    Task<NodeProfileDetail> GetAsync(Guid profileId, CancellationToken ct);

    Task<NodeProfileDetail> CreateAsync(SaveNodeProfileRequest request, CancellationToken ct);

    /// <summary>Copies an existing profile under a new code. v2 §10.6 calls this the expected authoring path.</summary>
    Task<NodeProfileDetail> CloneAsync(Guid profileId, string newCode, string newLabelKey, CancellationToken ct);

    Task<NodeProfileDetail> UpdateAsync(Guid profileId, SaveNodeProfileRequest request, CancellationToken ct);

    Task AttachToDepartmentAsync(Guid departmentId, AttachNodeProfileRequest request, CancellationToken ct);

    Task AttachToUnitAsync(Guid unitId, AttachNodeProfileRequest request, CancellationToken ct);
}

internal sealed class NodeProfileService(DirectoryDbContext context, IUserContext user)
    : INodeProfileService
{
    public async Task<IReadOnlyList<NodeProfileDetail>> ListAsync(CancellationToken ct) =>
        [.. (await context.NodeProfiles.AsNoTracking().OrderBy(profile => profile.Code).ToListAsync(ct))
            .Select(ToDetail)];

    public async Task<NodeProfileDetail> GetAsync(Guid profileId, CancellationToken ct) =>
        ToDetail(await FindAsync(profileId, ct));

    public async Task<NodeProfileDetail> CreateAsync(SaveNodeProfileRequest request, CancellationToken ct)
    {
        NodeProfileValidator.Validate(request);

        await EnsureCodeIsFreeAsync(request.Code, null, ct);

        var now = DateTimeOffset.UtcNow;

        var profile = new NodeProfile
        {
            Id = Guid.CreateVersion7(),
            Code = Normalize(request.Code),
            LabelKey = request.LabelKey.Trim(),
            CreatedAt = now,
            ModifiedAt = now,
            ModifiedBy = user.UserName,
        };

        Apply(profile, request);

        context.NodeProfiles.Add(profile);
        await context.SaveChangesAsync(ct);

        return ToDetail(profile);
    }

    public async Task<NodeProfileDetail> CloneAsync(
        Guid profileId,
        string newCode,
        string newLabelKey,
        CancellationToken ct)
    {
        var source = await FindAsync(profileId, ct);

        // Straight through Create so a clone is validated and de-duplicated exactly as a hand-authored profile is.
        // Nulls are copied as nulls: cloning a profile that inherits its parent's taxonomy must produce one that
        // also inherits, not one that has frozen today's answer.
        return await CreateAsync(
            new SaveNodeProfileRequest(
                newCode,
                newLabelKey,
                source.ActivityTaxonomyJson,
                source.BoardArchetypes,
                source.ItemTypes,
                source.CapabilitiesJson,
                source.SolvesCategories,
                source.BudgetDefaultsJson,
                source.HeadlinePattern),
            ct);
    }

    public async Task<NodeProfileDetail> UpdateAsync(
        Guid profileId,
        SaveNodeProfileRequest request,
        CancellationToken ct)
    {
        NodeProfileValidator.Validate(request);

        var profile = await context.NodeProfiles
            .AsTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == profileId, ct)
            ?? throw new ResourceNotFoundException($"No profile {profileId}.");

        await EnsureCodeIsFreeAsync(request.Code, profileId, ct);

        profile.Code = Normalize(request.Code);
        profile.LabelKey = request.LabelKey.Trim();
        profile.ModifiedAt = DateTimeOffset.UtcNow;
        profile.ModifiedBy = user.UserName;

        Apply(profile, request);

        await context.SaveChangesAsync(ct);

        return ToDetail(profile);
    }

    public async Task AttachToDepartmentAsync(
        Guid departmentId,
        AttachNodeProfileRequest request,
        CancellationToken ct)
    {
        var department = await context.Departments
            .AsTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == departmentId, ct)
            ?? throw new ResourceNotFoundException($"No department {departmentId}.");

        department.ProfileId = await EnsureAttachableAsync(request.ProfileId, ct);
        department.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
    }

    public async Task AttachToUnitAsync(Guid unitId, AttachNodeProfileRequest request, CancellationToken ct)
    {
        var unit = await context.Units
            .AsTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == unitId, ct)
            ?? throw new ResourceNotFoundException($"No unit {unitId}.");

        unit.ProfileId = await EnsureAttachableAsync(request.ProfileId, ct);
        unit.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Turns the requested id into one that exists, or into null for a detach.
    /// </summary>
    /// <remarks>
    /// Checked here rather than left to the foreign key so the caller gets a 404 naming the profile instead of a
    /// constraint violation surfacing as a 500. The FK stays as the backstop for races.
    /// </remarks>
    private async Task<Guid?> EnsureAttachableAsync(Guid? profileId, CancellationToken ct)
    {
        if (profileId is not { } id)
        {
            return null;
        }

        return await context.NodeProfiles.AnyAsync(profile => profile.Id == id, ct)
            ? id
            : throw new ResourceNotFoundException($"No profile {id}.");
    }

    private async Task<NodeProfile> FindAsync(Guid profileId, CancellationToken ct) =>
        await context.NodeProfiles.AsNoTracking().SingleOrDefaultAsync(profile => profile.Id == profileId, ct)
        ?? throw new ResourceNotFoundException($"No profile {profileId}.");

    private async Task EnsureCodeIsFreeAsync(string code, Guid? excluding, CancellationToken ct)
    {
        var normalized = Normalize(code);

        var taken = await context.NodeProfiles
            .AnyAsync(profile => profile.Code == normalized && (excluding == null || profile.Id != excluding), ct);

        if (taken)
        {
            throw new DomainRuleViolationException($"A profile with the code '{normalized}' already exists.");
        }
    }

    private static void Apply(NodeProfile profile, SaveNodeProfileRequest request)
    {
        profile.ActivityTaxonomyJson = Blank(request.ActivityTaxonomyJson);
        profile.BoardArchetypes = request.BoardArchetypes is null ? null : [.. request.BoardArchetypes];
        profile.ItemTypes = request.ItemTypes is null ? null : [.. request.ItemTypes];
        profile.CapabilitiesJson = Blank(request.CapabilitiesJson);
        profile.SolvesCategories = request.SolvesCategories is null ? null : [.. request.SolvesCategories];
        profile.BudgetDefaultsJson = Blank(request.BudgetDefaultsJson);
        profile.HeadlinePattern = Blank(request.HeadlinePattern);
    }

    /// <summary>Whitespace collapses to null, so "cleared the field" and "never set it" are one state, not two.</summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Normalize(string code) => code.Trim().ToUpperInvariant();

    private static NodeProfileDetail ToDetail(NodeProfile profile) =>
        new(
            profile.Id,
            profile.Code,
            profile.LabelKey,
            profile.ActivityTaxonomyJson,
            profile.BoardArchetypes,
            profile.ItemTypes,
            profile.CapabilitiesJson,
            profile.SolvesCategories,
            profile.BudgetDefaultsJson,
            profile.HeadlinePattern);
}

/// <summary>
/// Structural validation of an authored profile.
/// </summary>
/// <remarks>
/// The same division as <see cref="DepartmentConfigValidator"/>: shape here, meaning elsewhere. What it will not
/// do is check that the taxonomy's subtypes are ones the platform has heard of — the entire point of v2 §10 is
/// that an administrator invents their own vocabulary, so a validator with a list of acceptable subtypes would be
/// the hardcoded semantics this slice exists to remove.
/// </remarks>
public static class NodeProfileValidator
{
    /// <summary>
    /// The archetypes the client can actually render.
    /// </summary>
    /// <remarks>
    /// This is a rendering constraint, not an organizational one: naming an archetype no component implements
    /// produces a blank board, and the administrator should be told at save time rather than discovering it on
    /// somebody's Monday morning. Adding an archetype is a code change by definition, so the list belongs in code.
    /// </remarks>
    public static readonly IReadOnlyList<string> KnownArchetypes =
        ["week-grid", "work-orders", "shifts", "task-progress"];

    public static void Validate(SaveNodeProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new DomainRuleViolationException("A profile code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.LabelKey))
        {
            throw new DomainRuleViolationException("A profile label key is required.");
        }

        EnsureJsonObject(request.ActivityTaxonomyJson, nameof(request.ActivityTaxonomyJson));
        EnsureJsonObject(request.CapabilitiesJson, nameof(request.CapabilitiesJson));
        EnsureJsonObject(request.BudgetDefaultsJson, nameof(request.BudgetDefaultsJson));

        if (request.BoardArchetypes is { } archetypes)
        {
            if (archetypes.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "A profile that sets board archetypes must name at least one; leave it unset to inherit.");
            }

            foreach (var archetype in archetypes)
            {
                if (!KnownArchetypes.Contains(archetype, StringComparer.OrdinalIgnoreCase))
                {
                    throw new DomainRuleViolationException($"'{archetype}' is not a board archetype this client renders.");
                }
            }
        }

        if (request.CapabilitiesJson is { } capabilities)
        {
            EnsureCapabilitiesAreKnown(capabilities);
        }
    }

    /// <summary>
    /// Every key in a capabilities blob must be a registered capability.
    /// </summary>
    /// <remarks>
    /// Strict on write and lenient on read, on purpose. A typo caught at save time is a message an administrator
    /// can act on; the same typo caught at read time would silently do nothing, and they would spend an afternoon
    /// wondering why switching the capability off changed nothing.
    /// </remarks>
    private static void EnsureCapabilitiesAreKnown(string capabilitiesJson)
    {
        using var document = JsonDocument.Parse(capabilitiesJson);

        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!NodeCapabilities.IsKnown(property.Name))
            {
                throw new DomainRuleViolationException(
                    $"'{property.Name}' is not a capability. Known capabilities: {string.Join(", ", NodeCapabilities.All)}.");
            }

            if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new DomainRuleViolationException($"Capability '{property.Name}' must be true or false.");
            }
        }
    }

    private static void EnsureJsonObject(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(value);

            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                throw new DomainRuleViolationException($"{field} must be a JSON object.");
            }
        }
        catch (JsonException exception)
        {
            throw new DomainRuleViolationException($"{field} is not valid JSON: {exception.Message}");
        }
    }
}
