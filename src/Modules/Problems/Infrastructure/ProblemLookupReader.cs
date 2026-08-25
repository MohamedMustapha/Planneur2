using Cracra.Modules.Problems.Contracts;
using Cracra.Modules.Problems.Data;
using Cracra.Modules.Problems.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Problems.Infrastructure;

/// <summary>
/// Problem references for other modules (v2 §06.1).
/// </summary>
/// <remarks>
/// Runs on the caller's connection like every cross-module reader, so an objective that names a problem the reader
/// may not see simply loses that line rather than leaking a title. The objective's weight still counts it: hiding
/// what somebody may not read is not the same as pretending the work is not happening.
/// </remarks>
internal sealed class ProblemLookupReader(ProblemsDbContext context) : IProblemLookupReader
{
    public async Task<IReadOnlyList<ProblemRef>> GetByIdsAsync(
        IReadOnlyList<Guid> problemIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(problemIds);

        if (problemIds.Count == 0)
        {
            return [];
        }

        return
        [
            .. (await context.Problems
                    .AsNoTracking()
                    .Where(problem => problemIds.Contains(problem.Id))
                    .ToListAsync(ct))
                .Select(problem => new ProblemRef(
                    problem.Id,
                    problem.Code,
                    problem.Title,
                    ProblemStatuses.Wire(problem.Status),
                    problem.NodeId)),
        ];
    }
}

internal sealed class ProblemTriageReader(ProblemsDbContext context) : IProblemTriageReader
{
    public async Task<int> CountAwaitingTriageAsync(CancellationToken ct) =>
        await context.Problems
            .AsNoTracking()
            .CountAsync(problem => problem.Status == ProblemStatus.New, ct);
}
