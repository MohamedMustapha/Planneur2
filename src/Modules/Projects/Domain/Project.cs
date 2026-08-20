using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Projects.Domain;

/// <summary>
/// A department contributing to a project. Exactly one of them leads it.
/// </summary>
public sealed class ProjectDepartment
{
    public required Guid ProjectId { get; init; }

    public required Guid DepartmentId { get; init; }

    public bool IsLeadDepartment { get; internal set; }
}

/// <summary>
/// Someone on the project team, tagged with the department they contribute from and the function they do it in.
/// </summary>
/// <remarks>
/// Both tags are the point. The project view groups members by department, then by function, and that grouping is
/// the answer to "who from which department is doing what" — which is the question the whole screen exists for.
/// Deriving the department from the person's directory record instead would lose it the moment they transferred.
/// </remarks>
public sealed class ProjectMember
{
    public required Guid ProjectId { get; init; }

    public required Guid PersonId { get; init; }

    public required Guid DepartmentId { get; set; }

    public required Guid FunctionalRoleId { get; set; }

    public int? AllocationPercent { get; set; }

    public required DateOnly From { get; set; }

    public DateOnly? To { get; set; }

    public bool IsActive => To is null;

    internal MembershipPeriod Period => new(From, To);
}

/// <summary>
/// The Project aggregate.
/// </summary>
/// <remarks>
/// <para>
/// A DDD module rather than 2-layer because there are invariants spanning several entities — a member must belong
/// to a contributing department, exactly one department leads, a lead cannot be dropped while it is still needed —
/// and because other modules react to what happens here. conventions.md's test is "does it have a lifecycle
/// diagram"; this does.
/// </para>
/// <para>
/// Every mutation goes through a method on the aggregate. There are no public setters on the collections, so it is
/// not possible to add a member without the department check running.
/// </para>
/// </remarks>
public sealed class Project
{
    private readonly List<ProjectDepartment> _departments = [];
    private readonly List<ProjectMember> _members = [];
    private readonly List<object> _domainEvents = [];

    private Project()
    {
        // EF Core.
    }

    public Guid Id { get; private init; }

    /// <summary>Short stable code, unique across the platform. Appears in exports and conversations.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Classification Classification { get; private set; }

    public decimal CostAmount { get; private set; }

    public string CostCurrency { get; private set; } = "EUR";

    public string? CostNotes { get; private set; }

    /// <summary>The project lead or PO. They can always read and write their own project.</summary>
    public Guid OwnerPersonId { get; private set; }

    /// <summary>Denormalized from the lead department, because RLS compares it on every row read.</summary>
    public Guid LeadDepartmentId { get; private set; }

    public bool Archived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public Guid ModifiedBy { get; private set; }

    public IReadOnlyCollection<ProjectDepartment> Departments => _departments;

    public IReadOnlyCollection<ProjectMember> Members => _members;

    public IReadOnlyCollection<object> DomainEvents => _domainEvents;

    public Money Cost => new(CostAmount, CostCurrency);

    public static Project Create(
        Guid id,
        string code,
        string name,
        string? description,
        Classification classification,
        Money cost,
        Guid ownerPersonId,
        Guid leadDepartmentId,
        IReadOnlyList<Guid> contributingDepartmentIds,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainRuleViolationException("A project needs a code.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A project needs a name.");
        }

        var project = new Project
        {
            Id = id,
            Code = code.Trim(),
            Name = name.Trim(),
            Description = description?.Trim(),
            Classification = classification,
            CostAmount = cost.Amount,
            CostCurrency = cost.Currency,
            OwnerPersonId = ownerPersonId,
            LeadDepartmentId = leadDepartmentId,
            CreatedAt = now,
            CreatedBy = createdBy,
            ModifiedAt = now,
            ModifiedBy = createdBy,
        };

        project._departments.Add(new ProjectDepartment
        {
            ProjectId = id,
            DepartmentId = leadDepartmentId,
            IsLeadDepartment = true,
        });

        foreach (var departmentId in contributingDepartmentIds.Distinct().Where(d => d != leadDepartmentId))
        {
            project._departments.Add(new ProjectDepartment
            {
                ProjectId = id,
                DepartmentId = departmentId,
                IsLeadDepartment = false,
            });
        }

        project._domainEvents.Add(new ProjectCreated(id, project.Code, project.Name, leadDepartmentId));

        return project;
    }

    public void UpdateDetails(
        string name,
        string? description,
        Classification classification,
        Money cost,
        string? costNotes,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A project needs a name.");
        }

        var classificationChanged = Classification != classification;
        var costChanged = CostAmount != cost.Amount || CostCurrency != cost.Currency;

        Name = name.Trim();
        Description = description?.Trim();
        Classification = classification;
        CostAmount = cost.Amount;
        CostCurrency = cost.Currency;
        CostNotes = costNotes?.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAt = now;

        // Raised only on an actual change. Republishing an unchanged classification every time somebody fixes a
        // typo in the description would make the event stream meaningless to anyone consuming it.
        if (classificationChanged)
        {
            _domainEvents.Add(new ProjectClassificationChanged(Id, classification));
        }

        if (costChanged)
        {
            _domainEvents.Add(new ProjectCostChanged(Id, cost.Amount, cost.Currency));
        }
    }

    /// <summary>
    /// Adds someone to the team.
    /// </summary>
    /// <remarks>
    /// The department must already be contributing. That is the invariant S3 names first, and it is what keeps the
    /// cross-department read rule honest: heads see projects touching their department, so a member tagged with a
    /// department the project has no relationship to would be visible to a head with no legitimate interest.
    /// </remarks>
    public void AddMember(
        Guid personId,
        Guid departmentId,
        Guid functionalRoleId,
        int? allocationPercent,
        DateOnly from,
        Guid modifiedBy,
        DateTimeOffset now)
    {
        if (_departments.All(department => department.DepartmentId != departmentId))
        {
            throw new DomainRuleViolationException(
                "A team member must contribute from one of the project's departments. "
                + "Add the department to the project first.");
        }

        if (allocationPercent is { } percent)
        {
            _ = new Allocation(percent);
        }

        var existing = _members.SingleOrDefault(member => member.PersonId == personId && member.IsActive);

        if (existing is not null)
        {
            // Already on the team: treat this as a correction rather than a duplicate. Two active rows for one
            // person would double them in every by-department count on the project view.
            existing.DepartmentId = departmentId;
            existing.FunctionalRoleId = functionalRoleId;
            existing.AllocationPercent = allocationPercent;

            Touch(modifiedBy, now);

            return;
        }

        _members.Add(new ProjectMember
        {
            ProjectId = Id,
            PersonId = personId,
            DepartmentId = departmentId,
            FunctionalRoleId = functionalRoleId,
            AllocationPercent = allocationPercent,
            From = from,
        });

        _domainEvents.Add(new ProjectMemberAdded(Id, personId, departmentId));

        Touch(modifiedBy, now);
    }

    /// <summary>
    /// Takes someone off the team by closing their period.
    /// </summary>
    /// <remarks>
    /// Closed, never deleted. Their logged activity references this membership, and "who was on this in March" has
    /// to survive them leaving in April.
    /// </remarks>
    public void RemoveMember(Guid personId, DateOnly to, Guid modifiedBy, DateTimeOffset now)
    {
        var member = _members.SingleOrDefault(candidate => candidate.PersonId == personId && candidate.IsActive)
                     ?? throw new ResourceNotFoundException($"{personId} is not an active member of this project.");

        if (to < member.From)
        {
            throw new DomainRuleViolationException("A membership cannot end before it starts.");
        }

        member.To = to;

        _domainEvents.Add(new ProjectMemberRemoved(Id, personId, member.DepartmentId));

        Touch(modifiedBy, now);
    }

    public void AddDepartment(Guid departmentId, Guid modifiedBy, DateTimeOffset now)
    {
        if (_departments.Any(department => department.DepartmentId == departmentId))
        {
            return;
        }

        _departments.Add(new ProjectDepartment
        {
            ProjectId = Id,
            DepartmentId = departmentId,
            IsLeadDepartment = false,
        });

        _domainEvents.Add(new ProjectDepartmentsChanged(Id, [.. _departments.Select(d => d.DepartmentId)]));

        Touch(modifiedBy, now);
    }

    /// <summary>
    /// Removes a contributing department.
    /// </summary>
    /// <remarks>
    /// Refused while the lead, and refused while anyone is still contributing from it. Allowing either would leave
    /// members tagged with a department the project no longer has — orphaning them in every grouping on the project
    /// view, and quietly changing who can see the project through the cross-department rule.
    /// </remarks>
    public void RemoveDepartment(Guid departmentId, Guid modifiedBy, DateTimeOffset now)
    {
        var department = _departments.SingleOrDefault(candidate => candidate.DepartmentId == departmentId)
                         ?? throw new ResourceNotFoundException($"{departmentId} does not contribute to this project.");

        if (department.IsLeadDepartment)
        {
            throw new DomainRuleViolationException(
                "The lead department cannot be removed. Transfer the lead to another department first.");
        }

        if (_members.Any(member => member.IsActive && member.DepartmentId == departmentId))
        {
            throw new DomainRuleViolationException(
                "This department still has active team members. Remove them before removing the department.");
        }

        _departments.Remove(department);

        _domainEvents.Add(new ProjectDepartmentsChanged(Id, [.. _departments.Select(d => d.DepartmentId)]));

        Touch(modifiedBy, now);
    }

    /// <summary>Moves the lead to another contributing department. Exactly one lead, always.</summary>
    public void TransferLead(Guid departmentId, Guid modifiedBy, DateTimeOffset now)
    {
        var target = _departments.SingleOrDefault(candidate => candidate.DepartmentId == departmentId)
                     ?? throw new DomainRuleViolationException(
                         "The lead must be a department already contributing to the project.");

        foreach (var department in _departments)
        {
            department.IsLeadDepartment = false;
        }

        target.IsLeadDepartment = true;
        LeadDepartmentId = departmentId;

        Touch(modifiedBy, now);
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void Touch(Guid modifiedBy, DateTimeOffset now)
    {
        ModifiedBy = modifiedBy;
        ModifiedAt = now;
    }
}

// --- Domain events -------------------------------------------------------------------------------------------
// Raised inside the aggregate and converted to integration events after save (conventions.md §2). They stay
// internal to the module: what leaves is the Contracts version, which other modules deserialize.

public sealed record ProjectCreated(Guid ProjectId, string Code, string Name, Guid LeadDepartmentId);

public sealed record ProjectMemberAdded(Guid ProjectId, Guid PersonId, Guid DepartmentId);

public sealed record ProjectMemberRemoved(Guid ProjectId, Guid PersonId, Guid DepartmentId);

public sealed record ProjectClassificationChanged(Guid ProjectId, Classification Classification);

public sealed record ProjectCostChanged(Guid ProjectId, decimal Amount, string Currency);

public sealed record ProjectDepartmentsChanged(Guid ProjectId, IReadOnlyList<Guid> DepartmentIds);
