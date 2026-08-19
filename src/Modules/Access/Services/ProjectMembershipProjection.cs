using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Access.Contracts;
using Cracra.Modules.Access.Data;
using Cracra.Modules.Access.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cracra.Modules.Access.Services;

/// <summary>
/// Maintains <c>access.project_membership</c> on behalf of the Projects module.
/// </summary>
/// <remarks>
/// <para>
/// Writes under the system context in its own scope. The projection's RLS policy is system-write-only precisely so
/// no human session can rewrite who can see which project — including the project lead making the change, whose
/// own session is what triggered this call.
/// </para>
/// <para>
/// Replace-whole rather than incremental. A team change is a small set, and computing the difference in two places
/// is how a projection drifts from the thing it projects — which here would mean someone quietly retaining access.
/// </para>
/// </remarks>
internal sealed class ProjectMembershipProjection(IServiceScopeFactory scopeFactory) : IProjectMembershipProjection
{
    public async Task ReplaceAsync(
        Guid projectId,
        IReadOnlyList<ProjectMembershipEntry> members,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var context = scope.ServiceProvider.GetRequiredService<AccessDbContext>();

        await context.ProjectMemberships
            .Where(membership => membership.ProjectId == projectId)
            .ExecuteDeleteAsync(ct);

        if (members.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;

            context.ProjectMemberships.AddRange(members.Select(member => new ProjectMembership
            {
                PersonId = member.PersonId,
                ProjectId = projectId,
                DepartmentId = member.DepartmentId,
                UpdatedAt = now,
            }));

            await context.SaveChangesAsync(ct);
        }
    }
}
