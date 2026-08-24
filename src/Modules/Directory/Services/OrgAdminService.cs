using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Directory.Services;

public sealed record OrgLevelView(
    int LevelNo,
    string Code,
    string LabelKey,
    string LabelPluralKey,
    string HeadLabelKey,
    bool PeopleAllowed,
    bool IsOptional);

public sealed record OrgNodeAdminView(
    Guid Id,
    Guid? ParentId,
    int LevelNo,
    string Code,
    string Name,
    Guid? HeadPersonId,
    string? HeadName,
    Guid? ProfileId,
    bool Active,
    int Depth);

public sealed record SaveLevelRequest(
    int LevelNo,
    string Code,
    string LabelKey,
    string LabelPluralKey,
    string HeadLabelKey,
    bool PeopleAllowed,
    bool IsOptional);

public sealed record CreateNodeRequest(Guid? ParentId, int LevelNo, string Code, string Name);

/// <summary>
/// The org structure, as an administrator reshapes it (v2 §08.1).
/// </summary>
/// <remarks>
/// <para>
/// Nothing here checks a role. Who may write which node is <c>access.can_write_org_node</c>, which admits the PMO
/// and a global admin everywhere and a head only strictly beneath their own node — a node's own ancestor list does
/// not contain itself, so "in my subtree" cannot mean "at or above me". That is §08.2's rule expressed once, in
/// the place that is authoritative, rather than restated here where it could drift.
/// </para>
/// <para>
/// Re-parenting is the one act with reach beyond its row. Moving a node moves everything under it, and every
/// scoped row in every module carries a copy of its node's ancestry for the array-overlap the predicates use — so
/// the move recomputes the subtree's paths and then re-stamps the tables that copied them. Both are one call each
/// because the plumbing already exists; what matters is that they happen in the same unit of work as the move,
/// since a tree that saved and an ancestry that did not is a permission error nobody can reproduce.
/// </para>
/// </remarks>
public interface IOrgAdminService
{
    Task<IReadOnlyList<OrgLevelView>> LevelsAsync(CancellationToken ct);

    Task<OrgLevelView> SaveLevelAsync(SaveLevelRequest request, CancellationToken ct);

    Task<IReadOnlyList<OrgNodeAdminView>> TreeAsync(CancellationToken ct);

    Task<Guid> CreateNodeAsync(CreateNodeRequest request, CancellationToken ct);

    Task RenameAsync(Guid nodeId, string name, CancellationToken ct);

    Task ReparentAsync(Guid nodeId, Guid? parentId, CancellationToken ct);

    Task SetHeadAsync(Guid nodeId, Guid? personId, CancellationToken ct);

    Task SetActiveAsync(Guid nodeId, bool active, CancellationToken ct);
}

internal sealed class OrgAdminService(
    DirectoryDbContext context,
    IDirectoryReader directory,
    IAdminAudit audit) : IOrgAdminService
{
    public async Task<IReadOnlyList<OrgLevelView>> LevelsAsync(CancellationToken ct)
    {
        var levels = await context.OrgLevels.OrderBy(level => level.LevelNo).ToListAsync(ct);

        return [.. levels.Select(View)];
    }

    public async Task<OrgLevelView> SaveLevelAsync(SaveLevelRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!int.IsPositive(request.LevelNo))
        {
            throw new DomainRuleViolationException("A level number starts at the top of the tree.");
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new DomainRuleViolationException("A level needs a code.");
        }

        var level = await context.OrgLevels
            .AsTracking()
            .SingleOrDefaultAsync(candidate => candidate.LevelNo == request.LevelNo, ct);

        if (level is null)
        {
            level = new OrgLevel
            {
                LevelNo = request.LevelNo,
                Code = request.Code.Trim().ToLowerInvariant(),
                LabelKey = request.LabelKey.Trim(),
                LabelPluralKey = request.LabelPluralKey.Trim(),
                HeadLabelKey = request.HeadLabelKey.Trim(),
            };

            context.OrgLevels.Add(level);
        }
        else
        {
            level.Code = request.Code.Trim().ToLowerInvariant();
            level.LabelKey = request.LabelKey.Trim();
            level.LabelPluralKey = request.LabelPluralKey.Trim();
            level.HeadLabelKey = request.HeadLabelKey.Trim();
        }

        level.PeopleAllowed = request.PeopleAllowed;
        level.IsOptional = request.IsOptional;

        await SaveAsync(ct, "Only a global administrator defines the levels themselves.");

        await audit.RecordAsync(
            AuditActions.LevelSaved,
            AuditTargets.Level,
            Guid.Empty,
            null,
            $"Level {level.LevelNo} is now '{level.Code}'.",
            ct);

        return View(level);
    }

    public async Task<IReadOnlyList<OrgNodeAdminView>> TreeAsync(CancellationToken ct)
    {
        var nodes = await context.OrgNodes.ToListAsync(ct);

        var names = await directory.GetPersonNamesAsync(
            [.. nodes.Where(node => node.HeadPersonId is not null).Select(node => node.HeadPersonId!.Value).Distinct()],
            ct);

        var byParent = nodes
            .Where(node => node.ParentId is not null)
            .GroupBy(node => node.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(node => node.Code, StringComparer.Ordinal).ToArray());

        // Depth-first from every root, so the client renders one flat list and indents by the depth it is given.
        // The alternative — a nested payload — would make the client recurse over a shape whose depth is the
        // deployment's, which is the fixed ladder v2 exists to remove.
        var ordered = new List<OrgNodeAdminView>(nodes.Count);

        foreach (var root in nodes
            .Where(node => node.ParentId is null)
            .OrderBy(node => node.Code, StringComparer.Ordinal))
        {
            Walk(root, 0);
        }

        // Anything whose parent is not visible to this caller would otherwise vanish from the list entirely.
        foreach (var orphan in nodes.Where(node => ordered.All(row => row.Id != node.Id)))
        {
            ordered.Add(ToView(orphan, 0));
        }

        return ordered;

        void Walk(OrgNode node, int depth)
        {
            ordered.Add(ToView(node, depth));

            if (!byParent.TryGetValue(node.Id, out var children))
            {
                return;
            }

            foreach (var child in children)
            {
                Walk(child, depth + 1);
            }
        }

        OrgNodeAdminView ToView(OrgNode node, int depth) => new(
            node.Id,
            node.ParentId,
            node.LevelNo,
            node.Code,
            node.Name,
            node.HeadPersonId,
            node.HeadPersonId is { } head ? names.GetValueOrDefault(head) : null,
            node.ProfileId,
            node.Active,
            depth);
    }

    public async Task<Guid> CreateNodeAsync(CreateNodeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainRuleViolationException("A branch needs a code and a name.");
        }

        var parent = request.ParentId is { } parentId ? await FindAsync(parentId, ct) : null;

        if (parent is not null && request.LevelNo <= parent.LevelNo)
        {
            throw new DomainRuleViolationException(
                $"A branch under {parent.Code} sits at a level below {parent.LevelNo}, not at or above it.");
        }

        var node = new OrgNode
        {
            Id = Guid.CreateVersion7(),
            ParentId = request.ParentId,
            LevelNo = request.LevelNo,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        };

        context.OrgNodes.Add(node);

        await SaveAsync(ct, "You can see this branch but not create one under it.");

        await audit.RecordAsync(
            AuditActions.NodeCreated,
            AuditTargets.Node,
            node.Id,
            node.Id,
            $"'{node.Name}' created at level {node.LevelNo}.",
            ct);

        return node.Id;
    }

    public async Task RenameAsync(Guid nodeId, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A branch needs a name.");
        }

        var node = await FindAsync(nodeId, ct, tracked: true);
        var was = node.Name;

        node.Name = name.Trim();
        node.ModifiedAt = DateTimeOffset.UtcNow;

        await SaveAsync(ct, "You can see this branch but not rename it.");

        await audit.RecordAsync(
            AuditActions.NodeRenamed,
            AuditTargets.Node,
            node.Id,
            node.Id,
            $"'{was}' renamed to '{node.Name}'.",
            ct);
    }

    public async Task ReparentAsync(Guid nodeId, Guid? parentId, CancellationToken ct)
    {
        var node = await FindAsync(nodeId, ct, tracked: true);

        if (parentId == nodeId)
        {
            throw new DomainRuleViolationException("A branch cannot hang off itself.");
        }

        var parent = parentId is { } wanted ? await FindAsync(wanted, ct) : null;

        if (parent is not null && parent.AncestorIds.Contains(nodeId))
        {
            throw new DomainRuleViolationException(
                $"'{parent.Name}' already sits under '{node.Name}'; moving it there would make a loop.");
        }

        if (parent is not null && node.LevelNo <= parent.LevelNo)
        {
            throw new DomainRuleViolationException(
                $"'{node.Name}' sits at level {node.LevelNo} and cannot hang off a level {parent.LevelNo} branch.");
        }

        var was = node.ParentId;

        node.ParentId = parentId;
        node.ModifiedAt = DateTimeOffset.UtcNow;

        await SaveAsync(ct, "You can see this branch but not move it.");

        // The row moved; every descendant's ancestry and every scoped row that copied it has not. Both catch up
        // here, in the same request, because a tree that saved and an ancestry that did not is a permission bug
        // that reproduces for one person and nobody else.
        await context.Database.ExecuteSqlAsync($"select access.refresh_node_paths({nodeId})", ct);
        await context.Database.ExecuteSqlRawAsync("select access.refresh_stale_node_paths()", ct);

        await audit.RecordAsync(
            AuditActions.NodeReparented,
            AuditTargets.Node,
            node.Id,
            node.Id,
            was is null
                ? $"'{node.Name}' moved to the top."
                : $"'{node.Name}' moved from {was} to {parentId?.ToString() ?? "the top"}.",
            ct);
    }

    public async Task SetHeadAsync(Guid nodeId, Guid? personId, CancellationToken ct)
    {
        var node = await FindAsync(nodeId, ct, tracked: true);

        node.HeadPersonId = personId;
        node.ModifiedAt = DateTimeOffset.UtcNow;

        await SaveAsync(ct, "You can see this branch but not choose who heads it.");

        await audit.RecordAsync(
            AuditActions.NodeHeadSet,
            AuditTargets.Node,
            node.Id,
            node.Id,
            personId is null ? $"'{node.Name}' has no head." : $"'{node.Name}' is now headed by {personId}.",
            ct);
    }

    public async Task SetActiveAsync(Guid nodeId, bool active, CancellationToken ct)
    {
        var node = await FindAsync(nodeId, ct, tracked: true);

        node.Active = active;
        node.ModifiedAt = DateTimeOffset.UtcNow;

        await SaveAsync(ct, "You can see this branch but not deactivate it.");

        await audit.RecordAsync(
            active ? AuditActions.NodeReactivated : AuditActions.NodeDeactivated,
            AuditTargets.Node,
            node.Id,
            node.Id,
            active ? $"'{node.Name}' reactivated." : $"'{node.Name}' deactivated.",
            ct);
    }

    private async Task<OrgNode> FindAsync(Guid nodeId, CancellationToken ct, bool tracked = false)
    {
        var query = tracked ? context.OrgNodes.AsTracking() : context.OrgNodes;

        return await query.SingleOrDefaultAsync(node => node.Id == nodeId, ct)
            ?? throw new ResourceNotFoundException("That branch does not exist.");
    }

    /// <summary>
    /// Saves, and turns a write the policy refused into a refusal the caller can read.
    /// </summary>
    /// <remarks>
    /// An UPDATE the policy refuses affects no rows and raises nothing, so without this a head who moved a branch
    /// they do not run would be told it worked. An INSERT it refuses does raise, and arrives here as the same
    /// sentence, which is the answer either way: you may look, you may not change.
    /// </remarks>
    private async Task SaveAsync(CancellationToken ct, string refusal)
    {
        try
        {
            if (await context.SaveChangesAsync(ct) == 0)
            {
                throw new UnauthorizedAccessException(refusal);
            }
        }
        catch (DbUpdateException)
        {
            throw new UnauthorizedAccessException(refusal);
        }
    }

    private static OrgLevelView View(OrgLevel level) => new(
        level.LevelNo,
        level.Code,
        level.LabelKey,
        level.LabelPluralKey,
        level.HeadLabelKey,
        level.PeopleAllowed,
        level.IsOptional);
}
