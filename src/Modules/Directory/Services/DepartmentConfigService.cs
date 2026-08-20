using System.Text.Json;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Directory.Services;

public sealed record UpdateDepartmentConfigRequest(
    string ActivityTaxonomyJson,
    string RoleLabelsJson,
    string KudoRulesJson,
    string DefaultBoardLayout,
    string IterationPresetsJson,
    decimal WeeklyTargetHours,
    bool EnforceWeeklyTarget,
    string ShiftTemplatesJson = "{}");

public interface IDepartmentConfigService
{
    Task<DepartmentConfigSnapshot> GetAsync(Guid departmentId, CancellationToken ct);

    Task<DepartmentConfigSnapshot> UpdateAsync(
        Guid departmentId,
        UpdateDepartmentConfigRequest request,
        CancellationToken ct);
}

internal sealed class DepartmentConfigService(DirectoryDbContext context, IUserContext user)
    : IDepartmentConfigService
{
    public async Task<DepartmentConfigSnapshot> GetAsync(Guid departmentId, CancellationToken ct)
    {
        var config = await context.DepartmentConfigs
            .SingleOrDefaultAsync(candidate => candidate.DepartmentId == departmentId, ct);

        // RLS has already filtered this: a department the caller may not see returns no row, and they get the same
        // 404 as a department that does not exist. That indistinguishability is deliberate — otherwise the status
        // code tells a prober which departments are real.
        return config is null
            ? throw new ResourceNotFoundException($"No configuration for department {departmentId}.")
            : ToSnapshot(config);
    }

    public async Task<DepartmentConfigSnapshot> UpdateAsync(
        Guid departmentId,
        UpdateDepartmentConfigRequest request,
        CancellationToken ct)
    {
        DepartmentConfigValidator.Validate(request);

        var config = await context.DepartmentConfigs
            .AsTracking()
            .SingleOrDefaultAsync(candidate => candidate.DepartmentId == departmentId, ct)
            ?? throw new ResourceNotFoundException($"No configuration for department {departmentId}.");

        var now = DateTimeOffset.UtcNow;

        config.ActivityTaxonomyJson = request.ActivityTaxonomyJson;
        config.ShiftTemplatesJson = request.ShiftTemplatesJson;
        config.RoleLabelsJson = request.RoleLabelsJson;
        config.KudoRulesJson = request.KudoRulesJson;
        config.DefaultBoardLayout = request.DefaultBoardLayout;
        config.IterationPresetsJson = request.IterationPresetsJson;
        config.WeeklyTargetHours = request.WeeklyTargetHours;
        config.EnforceWeeklyTarget = request.EnforceWeeklyTarget;
        config.Version++;
        config.ModifiedAt = now;
        config.ModifiedBy = user.UserName;

        var snapshot = ToSnapshot(config);

        // Audit row and integration event in the same transaction as the change. If the write rolls back, so do
        // both — an audit trail that can disagree with the data it describes is worse than none.
        context.DepartmentConfigAudits.Add(new DepartmentConfigAudit
        {
            Id = Guid.CreateVersion7(),
            DepartmentId = departmentId,
            Version = config.Version,
            SnapshotJson = JsonSerializer.Serialize(snapshot),
            ChangedBy = user.UserId,
            ChangedAt = now,
        });

        context.Enqueue(new DepartmentConfigChanged(departmentId, config.Version));

        await context.SaveChangesAsync(ct);

        return snapshot;
    }

    private static DepartmentConfigSnapshot ToSnapshot(DepartmentConfig config) => new(
        config.DepartmentId,
        config.ActivityTaxonomyJson,
        config.RoleLabelsJson,
        config.KudoRulesJson,
        config.DefaultBoardLayout,
        config.IterationPresetsJson,
        config.WeeklyTargetHours,
        config.EnforceWeeklyTarget,
        config.Version);
}
