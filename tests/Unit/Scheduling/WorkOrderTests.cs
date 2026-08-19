using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Scheduling.Domain;

namespace Cracra.Tests.Unit.Scheduling;

/// <summary>
/// The 6a work order: the pool, the drag onto a row, and the drag back.
/// </summary>
/// <remarks>
/// The rule that matters most here is that unassigning gives back the planned activity to remove. A card returned
/// to the pool while its hours stay on someone's week is the failure mode this aggregate is shaped to prevent.
/// </remarks>
public sealed class WorkOrderTests
{
    private static readonly Guid Agent = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherAgent = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid Lead = Guid.Parse("c0000000-0000-0000-0000-000000000004");
    private static readonly Guid Unit = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Department = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Slot = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_work_order_sits_unassigned_in_the_pool()
    {
        var order = Create();

        order.State.ShouldBe(WorkOrderState.Unassigned);
        order.AssignedToPersonId.ShouldBeNull();
        order.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_work_order_needs_a_title_and_a_unit()
    {
        Should.Throw<DomainRuleViolationException>(() => Create(title: "  "));
        Should.Throw<DomainRuleViolationException>(() => Create(unitId: Guid.Empty));
    }

    [Fact]
    public void An_estimate_must_be_a_plausible_amount_of_work()
    {
        Should.Throw<DomainRuleViolationException>(() => Create(estimate: 0m));
        Should.Throw<DomainRuleViolationException>(() => Create(estimate: 40m));
    }

    [Fact]
    public void Assigning_records_the_person_and_the_slot()
    {
        var order = Create();

        order.AssignTo(Agent, Slot, Slot.AddHours(2), Lead, Now);

        order.State.ShouldBe(WorkOrderState.Assigned);
        order.AssignedToPersonId.ShouldBe(Agent);
        order.ScheduledStart.ShouldBe(Slot);
        order.ScheduledEnd.ShouldBe(Slot.AddHours(2));
    }

    [Fact]
    public void An_assignment_must_end_after_it_starts()
    {
        var order = Create();

        Should.Throw<DomainRuleViolationException>(() => order.AssignTo(Agent, Slot, Slot, Lead, Now));
    }

    [Fact]
    public void Handing_an_assigned_order_straight_to_someone_else_is_refused()
    {
        var order = Create();
        order.AssignTo(Agent, Slot, Slot.AddHours(2), Lead, Now);

        // Reassignment is legitimate, but it has to go through the pool so the first person's planned hours are
        // actually removed rather than orphaned on their week.
        Should.Throw<DomainRuleViolationException>(
            () => order.AssignTo(OtherAgent, Slot, Slot.AddHours(2), Lead, Now));
    }

    [Fact]
    public void Reassigning_to_the_same_person_just_moves_the_slot()
    {
        var order = Create();
        order.AssignTo(Agent, Slot, Slot.AddHours(2), Lead, Now);

        Should.NotThrow(() => order.AssignTo(Agent, Slot.AddHours(3), Slot.AddHours(5), Lead, Now));

        order.ScheduledStart.ShouldBe(Slot.AddHours(3));
    }

    [Fact]
    public void Unassigning_returns_the_planned_activity_to_remove()
    {
        var order = Create();
        var entryId = Guid.CreateVersion7();

        order.AssignTo(Agent, Slot, Slot.AddHours(2), Lead, Now);
        order.LinkActivity(entryId);

        var removed = order.Unassign(Lead, Now);

        // The whole point: the caller now knows exactly which entry to cancel.
        removed.ShouldBe(entryId);
        order.State.ShouldBe(WorkOrderState.Unassigned);
        order.AssignedToPersonId.ShouldBeNull();
        order.ActivityEntryId.ShouldBeNull();
        order.ScheduledStart.ShouldBeNull();
    }

    [Fact]
    public void An_unassigned_order_cannot_be_unassigned_again()
    {
        var order = Create();

        Should.Throw<DomainRuleViolationException>(() => order.Unassign(Lead, Now));
    }

    [Fact]
    public void Only_an_assigned_order_has_a_schedule_to_change()
    {
        var order = Create();

        Should.Throw<DomainRuleViolationException>(
            () => order.Reschedule(Slot, Slot.AddHours(1), Lead, Now));
    }

    [Fact]
    public void A_closed_order_cannot_be_assigned()
    {
        var order = Create();
        order.Close(Lead, Now);

        Should.Throw<DomainRuleViolationException>(() => order.AssignTo(Agent, Slot, Slot.AddHours(1), Lead, Now));
        Should.Throw<DomainRuleViolationException>(() => order.Close(Lead, Now));
    }

    [Fact]
    public void Assigning_and_unassigning_each_raise_an_event()
    {
        var order = Create();

        order.AssignTo(Agent, Slot, Slot.AddHours(2), Lead, Now);
        order.LinkActivity(Guid.CreateVersion7());

        order.DomainEvents.OfType<WorkOrderAssigned>().ShouldHaveSingleItem();

        order.Unassign(Lead, Now);

        order.DomainEvents.OfType<WorkOrderUnassigned>()
            .ShouldHaveSingleItem()
            .PreviousPersonId.ShouldBe(Agent);
    }

    [Fact]
    public void An_order_defaults_to_project_run()
    {
        // What the spec names. It requires a project under S5's taxonomy, which is why a helpdesk queue with
        // unattached tickets has to say otherwise — and can.
        Create().ActivityTypeCode.ShouldBe("project-run");

        WorkOrder.Create("WO-2", "Incident", null, "manual", null, Unit, Department, null, "support-run", 1m, Lead, Now)
            .ActivityTypeCode.ShouldBe("support-run");
    }

    [Fact]
    public void A_pulled_order_keeps_its_external_reference()
    {
        var order = WorkOrder.Create(
            "INC-4821",
            "Poste bloqué au démarrage",
            null,
            "servicenow",
            "INC-4821",
            Unit,
            Department,
            projectId: null,
            activityTypeCode: null,
            1m,
            Lead,
            Now);

        // A local shadow of the ticket, never the ticket. Nothing is ever written back the other way.
        order.Source.ShouldBe("servicenow");
        order.ExternalRef.ShouldBe("INC-4821");
    }

    private static WorkOrder Create(
        string title = "Imprimante hors service",
        Guid? unitId = null,
        decimal estimate = 1m) =>
        WorkOrder.Create(
            "WO-1",
            title,
            "Bourrage papier récurrent.",
            "manual",
            externalRef: null,
            unitId ?? Unit,
            Department,
            projectId: null,
            activityTypeCode: null,
            estimate,
            Lead,
            Now);
}
