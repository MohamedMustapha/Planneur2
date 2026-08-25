using Cracra.Modules.Portfolio.Contracts;
using Cracra.Modules.Portfolio.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cracra.Modules.Portfolio.Infrastructure;

internal sealed class IterationDeadlineReader(PortfolioDbContext context) : IIterationDeadlineReader
{
    public async Task<IReadOnlyList<IterationDeadline>> GetClosingAsync(DateOnly by, CancellationToken ct) =>
        await context.Iterations
            .AsNoTracking()
            .Where(iteration => iteration.State == IterationState.Active && iteration.EndsOn <= by)
            .Join(
                context.Items.AsNoTracking(),
                iteration => iteration.PortfolioItemId,
                item => item.Id,
                (iteration, item) => new IterationDeadline(
                    item.Id,
                    item.Code,
                    item.Name,
                    iteration.Id,
                    iteration.Name,
                    iteration.EndsOn))
            .OrderBy(deadline => deadline.EndsOn)
            .Take(20)
            .ToListAsync(ct);
}
