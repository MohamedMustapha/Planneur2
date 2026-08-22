using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Directory.Services;

/// <summary>
/// Directory's side of the cross-module config contract.
/// </summary>
/// <remarks>
/// Returns null rather than throwing where <see cref="IDepartmentConfigService"/> would 404. The difference is the
/// caller: an endpoint asking for a config the user opened deserves a 404, whereas S5 resolving a taxonomy while
/// someone logs an hour deserves a fallback. Same rows, different failure mode, so they are different methods
/// rather than one method with a flag.
/// </remarks>
internal sealed class DepartmentConfigReader(DirectoryDbContext context) : IDepartmentConfigReader
{
    public async Task<DepartmentConfigSnapshot?> TryGetAsync(Guid departmentId, CancellationToken ct)
    {
        var config = await context.DepartmentConfigs
            .SingleOrDefaultAsync(candidate => candidate.DepartmentId == departmentId, ct);

        return config is null
            ? null
            : new DepartmentConfigSnapshot(
                config.DepartmentId,
                config.ActivityTaxonomyJson,
                config.RoleLabelsJson,
                config.KudoRulesJson,
                config.DefaultBoardLayout,
                config.IterationPresetsJson,
                config.WeeklyTargetHours,
                config.EnforceWeeklyTarget,
                config.Version,
                config.ShiftTemplatesJson,
                config.WorkingDayJson);
    }
}
