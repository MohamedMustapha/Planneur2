using System.Diagnostics;
using Cracra.BuildingBlocks.Observability;
using Cracra.BuildingBlocks.Persistence.Outbox;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Directory.Contracts;
using Cracra.Modules.Directory.Data;
using Cracra.Modules.Directory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cracra.Modules.Directory.Sync;

/// <summary>What one reconciliation did. Returned by the on-demand endpoint and logged to SEQ.</summary>
public sealed record DirectorySyncResult(
    int PeopleCreated,
    int PeopleUpdated,
    int PeopleDeactivated,
    int UnitsCreated,
    int DepartmentsCreated,
    TimeSpan Duration)
{
    public static readonly DirectorySyncResult Empty = new(0, 0, 0, 0, 0, TimeSpan.Zero);

    public int TotalChanges => PeopleCreated + PeopleUpdated + PeopleDeactivated + UnitsCreated + DepartmentsCreated;
}

public interface IDirectorySynchronizer
{
    Task<DirectorySyncResult> SynchronizeAsync(CancellationToken ct);
}

/// <summary>
/// Reconciles the directory against Keycloak.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent by construction: everything is an upsert keyed on an external identifier — the Keycloak subject for
/// a person, the LDAP <c>fonction</c> for a unit, the code for a department. Running it twice changes nothing the
/// first run did not, which is what makes it safe to run on startup, on a timer and on demand all at once.
/// </para>
/// <para>
/// People are never deleted, only deactivated. Their logged activity, kudos and project history stay meaningful
/// long after they leave, and a cascade delete would take all of it with them.
/// </para>
/// </remarks>
internal sealed class DirectorySynchronizer(
    IServiceScopeFactory scopeFactory,
    IKeycloakDirectoryClient keycloak,
    ILogger<DirectorySynchronizer> logger) : IDirectorySynchronizer
{
    public async Task<DirectorySyncResult> SynchronizeAsync(CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        using var scope = scopeFactory.CreateScope();

        // Sync is a background job: it must see and write every row regardless of department, which is exactly
        // what the system context grants and nothing a human session ever gets.
        scope.ServiceProvider.GetRequiredService<IUserContextAccessor>().Current = UserContext.SystemJob;

        var context = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();

        var users = await keycloak.GetUsersAsync(ct);
        var groups = await keycloak.GetGroupsAsync(ct);

        logger.LogInformation(
            "Directory sync read {UserCount} users and {GroupCount} top-level groups from Keycloak",
            users.Count,
            groups.Count);

        var strategy = context.Database.CreateExecutionStrategy();

        var result = await strategy.ExecuteAsync(async cancellationToken =>
            await ReconcileAsync(context, users, groups, cancellationToken), ct);

        var elapsed = Stopwatch.GetElapsedTime(started);

        DirectorySyncTelemetry.RecordSuccess(elapsed, result.TotalChanges);

        logger.LogInformation(
            "Directory sync completed in {ElapsedMilliseconds:0} ms: +{Created} people, ~{Updated}, -{Deactivated}, "
            + "+{Units} units, +{Departments} departments",
            elapsed.TotalMilliseconds,
            result.PeopleCreated,
            result.PeopleUpdated,
            result.PeopleDeactivated,
            result.UnitsCreated,
            result.DepartmentsCreated);

        return result with { Duration = elapsed };
    }

    private async Task<DirectorySyncResult> ReconcileAsync(
        DirectoryDbContext context,
        IReadOnlyList<KeycloakUser> users,
        IReadOnlyList<KeycloakGroup> groups,
        CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        var departments = await context.Departments.AsTracking().ToDictionaryAsync(d => d.Id, ct);
        var units = await context.Units.AsTracking().ToDictionaryAsync(u => u.Id, ct);
        // Both collections, or the dedupe checks below run against an empty list and re-insert on every run —
        // which is precisely how idempotency breaks. AsSplitQuery because two collection includes on one query
        // multiply the rows out.
        var people = await context.People
            .Include(person => person.Units)
            .Include(person => person.FunctionalRoles)
            .AsSplitQuery()
            .AsTracking()
            .ToDictionaryAsync(person => person.Id, ct);
        var roles = await context.FunctionalRoles.AsTracking().ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var created = 0;
        var updated = 0;
        var newUnits = 0;
        var newDepartments = 0;

        // The org tree first: a person can only be placed into a unit that exists, and taking the structure from
        // groups is what gives departments and units their real codes and names rather than id-derived stand-ins.
        var org = DirectoryMapping.MapOrg(groups);

        foreach (var mappedDepartment in org.Departments)
        {
            if (departments.TryGetValue(mappedDepartment.Id, out var existing))
            {
                existing.Code = mappedDepartment.Code;
                existing.NameKey = mappedDepartment.NameKey;
                existing.ModifiedAt = now;
            }
            else
            {
                var department = DirectoryMapping.NewDepartment(mappedDepartment, now);
                departments[department.Id] = department;
                context.Departments.Add(department);
                context.DepartmentConfigs.Add(DirectoryMapping.NewConfig(department.Id, now));
                newDepartments++;
            }
        }

        foreach (var mappedUnit in org.Units)
        {
            if (units.TryGetValue(mappedUnit.Id, out var existing))
            {
                existing.DepartmentId = mappedUnit.DepartmentId;
                existing.Code = mappedUnit.Code;
                existing.Name = mappedUnit.Name;
                existing.ModifiedAt = now;
            }
            else
            {
                var unit = DirectoryMapping.NewUnit(mappedUnit, now);
                units[unit.Id] = unit;
                context.Units.Add(unit);
                newUnits++;
            }
        }

        var seen = new HashSet<Guid>();

        foreach (var user in users)
        {
            var mapped = DirectoryMapping.Map(user);

            if (mapped is null)
            {
                // A user with no unit or department cannot be placed in the org. Skipping is correct and worth a
                // warning: it usually means an LDAP attribute mapping broke rather than that the person is odd.
                logger.LogWarning(
                    "Keycloak user {Username} has no mappable unit/department attributes; skipping",
                    user.Username);

                continue;
            }

            seen.Add(mapped.PersonId);

            if (!departments.ContainsKey(mapped.DepartmentId) || !units.ContainsKey(mapped.UnitId))
            {
                // Their attributes point at an org node no group declares. Creating one on the fly would produce
                // a unit nobody manages and, worse, an RLS scope nobody intended — so skip and say so loudly.
                logger.LogWarning(
                    "Keycloak user {Username} references unit {UnitId} / department {DepartmentId}, which the group "
                    + "tree does not declare; skipping",
                    user.Username,
                    mapped.UnitId,
                    mapped.DepartmentId);

                seen.Remove(mapped.PersonId);

                continue;
            }

            if (people.TryGetValue(mapped.PersonId, out var person))
            {
                if (DirectoryMapping.Apply(person, mapped, now, out var movedFrom))
                {
                    updated++;

                    if (movedFrom is not null)
                    {
                        context.Enqueue(new PersonMoved(
                            person.Id,
                            movedFrom.Value.UnitId,
                            person.PrimaryUnitId,
                            movedFrom.Value.DepartmentId,
                            person.PrimaryDepartmentId));
                    }
                }
            }
            else
            {
                person = DirectoryMapping.NewPerson(mapped, now);
                people[person.Id] = person;
                context.People.Add(person);
                created++;

                context.Enqueue(new PersonJoined(
                    person.Id,
                    person.DisplayName,
                    person.PrimaryUnitId,
                    person.PrimaryDepartmentId));
            }

            SyncMembership(context, person, mapped);
            SyncFunctionalRole(context, roles, person, mapped);
        }

        var deactivated = 0;

        foreach (var person in people.Values.Where(p => p.Active && !seen.Contains(p.Id)))
        {
            person.Active = false;
            person.ModifiedAt = now;
            deactivated++;

            context.Enqueue(new PersonDeactivated(person.Id));

            logger.LogInformation(
                "Person {PersonId} ({LdapUid}) is no longer in the directory and was deactivated",
                person.Id,
                person.LdapUid);
        }

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new DirectorySyncResult(created, updated, deactivated, newUnits, newDepartments, TimeSpan.Zero);
    }

    private static void SyncMembership(DirectoryDbContext context, Person person, MappedPerson mapped)
    {
        var existing = person.Units.FirstOrDefault(membership => membership.UnitId == mapped.UnitId);

        if (existing is null)
        {
            context.PersonUnits.Add(new PersonUnit
            {
                PersonId = person.Id,
                UnitId = mapped.UnitId,
                DepartmentId = mapped.DepartmentId,
                IsPrimary = true,
            });

            return;
        }

        existing.DepartmentId = mapped.DepartmentId;
        existing.IsPrimary = true;
    }

    private static void SyncFunctionalRole(
        DirectoryDbContext context,
        List<FunctionalRole> roles,
        Person person,
        MappedPerson mapped)
    {
        if (mapped.FunctionalRoleCode is not { Length: > 0 } code)
        {
            return;
        }

        var role = roles.FirstOrDefault(r => r.Code == code && r.DepartmentId is null);

        if (role is null)
        {
            // A department can invent a functional role at any time; the directory follows LDAP rather than
            // requiring someone to pre-register it here first.
            role = new FunctionalRole
            {
                Id = Guid.CreateVersion7(),
                Code = code,
                LabelKey = $"directory.functionalRole.{code}",
            };

            roles.Add(role);
            context.FunctionalRoles.Add(role);
        }

        var alreadyAssigned = person.FunctionalRoles.Any(assignment =>
            assignment.FunctionalRoleId == role.Id && assignment.UnitId == mapped.UnitId);

        if (!alreadyAssigned)
        {
            context.PersonFunctionalRoles.Add(new PersonFunctionalRole
            {
                PersonId = person.Id,
                FunctionalRoleId = role.Id,
                UnitId = mapped.UnitId,
            });
        }
    }
}
