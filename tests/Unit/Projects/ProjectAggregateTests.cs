using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Projects.Domain;

namespace Cracra.Tests.Unit.Projects;

/// <summary>
/// The Project aggregate's invariants.
/// </summary>
/// <remarks>
/// This is why S3 is a DDD module rather than 2-layer: the rules span several entities and none of them can be
/// expressed as a column constraint. Tested here, with no database, because that is what makes them cheap enough
/// to test exhaustively.
/// </remarks>
public sealed class ProjectAggregateTests
{
    // Local constants rather than the shared seed fixture: that lives in the Testing library, which pulls in
    // Testcontainers and the Host. A pure aggregate test should not need either.
    private static readonly Guid Owner = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Colleague = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid Outsider = Guid.Parse("c0000000-0000-0000-0000-000000000007");
    private static readonly Guid LeadDept = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherDept = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DevRole = Guid.Parse("f0000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 19);

    [Fact]
    public void A_new_project_has_exactly_one_lead_department()
    {
        var project = Build();

        project.Departments.Count(department => department.IsLeadDepartment).ShouldBe(1);
        project.LeadDepartmentId.ShouldBe(LeadDept);
    }

    [Fact]
    public void Creating_with_the_lead_repeated_in_the_contributors_does_not_duplicate_it()
    {
        var project = Build(contributing: [LeadDept, OtherDept]);

        project.Departments.Count.ShouldBe(2);
    }

    [Fact]
    public void A_project_needs_a_code_and_a_name()
    {
        Should.Throw<DomainRuleViolationException>(() => Build(code: "  "));
        Should.Throw<DomainRuleViolationException>(() => Build(name: ""));
    }

    [Fact]
    public void Creating_raises_a_domain_event()
    {
        Build().DomainEvents.OfType<ProjectCreated>().Count().ShouldBe(1);
    }

    // --- The headline invariant --------------------------------------------------------------------------------

    [Fact]
    public void A_member_must_contribute_from_one_of_the_projects_departments()
    {
        var project = Build();

        var exception = Should.Throw<DomainRuleViolationException>(() =>
            project.AddMember(Outsider, OtherDept, DevRole, null, Today, Owner, Now));

        // Not merely tidiness. Heads read projects touching their department, so a member tagged with a department
        // the project has no relationship to would be visible to a head with no legitimate interest in it.
        exception.Message.ShouldContain("department");
    }

    [Fact]
    public void A_member_can_be_added_once_their_department_contributes()
    {
        var project = Build();

        project.AddDepartment(OtherDept, Owner, Now);
        project.AddMember(Outsider, OtherDept, DevRole, 50, Today, Owner, Now);

        project.Members.Count.ShouldBe(1);
        project.Members.Single().AllocationPercent.ShouldBe(50);
    }

    [Fact]
    public void Adding_someone_already_on_the_team_corrects_them_rather_than_duplicating()
    {
        var project = Build(contributing: [OtherDept]);

        project.AddMember(Outsider, OtherDept, DevRole, 50, Today, Owner, Now);
        project.AddMember(Outsider, OtherDept, DevRole, 80, Today, Owner, Now);

        // Two active rows for one person would double them in every by-department count on the project view.
        project.Members.Count.ShouldBe(1);
        project.Members.Single().AllocationPercent.ShouldBe(80);
    }

    [Fact]
    public void An_allocation_outside_one_to_a_hundred_is_refused()
    {
        var project = Build();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            project.AddMember(Owner, LeadDept, DevRole, 0, Today, Owner, Now));

        Should.Throw<ArgumentOutOfRangeException>(() =>
            project.AddMember(Owner, LeadDept, DevRole, 101, Today, Owner, Now));
    }

    // --- Removal ------------------------------------------------------------------------------------------------

    [Fact]
    public void Removing_a_member_closes_their_period_rather_than_deleting_the_row()
    {
        var project = Build();

        project.AddMember(Owner, LeadDept, DevRole, null, Today, Owner, Now);
        project.RemoveMember(Owner, Today.AddDays(10), Owner, Now);

        // Their logged activity points at this membership; "who was on this in March" has to survive them
        // leaving in April.
        project.Members.Count.ShouldBe(1);
        project.Members.Single().IsActive.ShouldBeFalse();
        project.Members.Single().To.ShouldBe(Today.AddDays(10));
    }

    [Fact]
    public void A_membership_cannot_end_before_it_starts()
    {
        var project = Build();

        project.AddMember(Owner, LeadDept, DevRole, null, Today, Owner, Now);

        Should.Throw<DomainRuleViolationException>(() =>
            project.RemoveMember(Owner, Today.AddDays(-1), Owner, Now));
    }

    [Fact]
    public void Removing_someone_who_is_not_on_the_team_is_a_not_found()
    {
        var project = Build();

        Should.Throw<ResourceNotFoundException>(() =>
            project.RemoveMember(Colleague, Today, Owner, Now));
    }

    // --- Departments --------------------------------------------------------------------------------------------

    [Fact]
    public void The_lead_department_cannot_be_removed()
    {
        var project = Build();

        var exception = Should.Throw<DomainRuleViolationException>(() =>
            project.RemoveDepartment(LeadDept, Owner, Now));

        exception.Message.ShouldContain("lead");
    }

    [Fact]
    public void A_department_with_active_members_cannot_be_removed()
    {
        var project = Build(contributing: [OtherDept]);

        project.AddMember(Outsider, OtherDept, DevRole, null, Today, Owner, Now);

        // Removing it would orphan Sofia in every grouping on the project view, and quietly change who can see the
        // project through the cross-department rule.
        Should.Throw<DomainRuleViolationException>(() => project.RemoveDepartment(OtherDept, Owner, Now));
    }

    [Fact]
    public void A_department_can_be_removed_once_its_members_have_left()
    {
        var project = Build(contributing: [OtherDept]);

        project.AddMember(Outsider, OtherDept, DevRole, null, Today, Owner, Now);
        project.RemoveMember(Outsider, Today, Owner, Now);

        Should.NotThrow(() => project.RemoveDepartment(OtherDept, Owner, Now));
        project.Departments.Count.ShouldBe(1);
    }

    [Fact]
    public void Transferring_the_lead_leaves_exactly_one_lead()
    {
        var project = Build(contributing: [OtherDept]);

        project.TransferLead(OtherDept, Owner, Now);

        project.Departments.Count(department => department.IsLeadDepartment).ShouldBe(1);
        project.LeadDepartmentId.ShouldBe(OtherDept);
    }

    [Fact]
    public void The_lead_must_already_be_a_contributing_department()
    {
        var project = Build();

        Should.Throw<DomainRuleViolationException>(() => project.TransferLead(OtherDept, Owner, Now));
    }

    // --- Cost and classification ---------------------------------------------------------------------------------

    [Fact]
    public void A_cost_change_raises_an_event_and_an_unchanged_cost_does_not()
    {
        var project = Build();
        project.ClearDomainEvents();

        project.UpdateDetails("Same", null, Classification.Build, new Money(1000m, "EUR"), null, Owner, Now);
        project.DomainEvents.OfType<ProjectCostChanged>().Count().ShouldBe(1);

        project.ClearDomainEvents();

        // Republishing an unchanged cost every time somebody fixes a typo would make the event stream meaningless
        // to anyone consuming it.
        project.UpdateDetails("Renamed", "new note", Classification.Build, new Money(1000m, "EUR"), null, Owner, Now);
        project.DomainEvents.OfType<ProjectCostChanged>().ShouldBeEmpty();
    }

    [Fact]
    public void A_classification_change_raises_an_event()
    {
        var project = Build();
        project.ClearDomainEvents();

        project.UpdateDetails("Same", null, Classification.Run, Money.Zero, null, Owner, Now);

        project.DomainEvents.OfType<ProjectClassificationChanged>().Single().Classification.ShouldBe(Classification.Run);
    }

    [Fact]
    public void A_negative_cost_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Money(-1m, "EUR"));
    }

    [Fact]
    public void An_unsupported_currency_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Money(10m, "XYZ"));
    }

    [Fact]
    public void Currency_is_normalized_to_upper_case()
    {
        new Money(10m, "eur").Currency.ShouldBe("EUR");
    }

    private static Project Build(
        string code = "PRJ-1",
        string name = "Migration M365",
        IReadOnlyList<Guid>? contributing = null) =>
        Project.Create(
            Guid.CreateVersion7(),
            code,
            name,
            "description",
            Classification.Build,
            Money.Zero,
            Owner,
            LeadDept,
            contributing ?? [],
            Owner,
            Now);
}
