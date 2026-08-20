using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Scheduling.Contracts;
using Cracra.Modules.Scheduling.Domain;
using FluentValidation;

namespace Cracra.Modules.Scheduling.Application;

// =================================================================================================================
// The assignment side. Small, but it is why this slice is "mixed" rather than a pure projection: assigning is not
// a read, it brings a planned activity into existence, and taking the assignment away has to take that activity
// with it or the person is left with work on their week that nobody asked them to do.
// =================================================================================================================

public sealed record CreateWorkOrderCommand(
    string Reference,
    string Title,
    string? Description,
    Guid UnitId,
    Guid? ProjectId,
    string? ActivityTypeCode,
    decimal EstimatedHours) : IRequest<Guid>, ITransactionalRequest;

public sealed class CreateWorkOrderValidator : AbstractValidator<CreateWorkOrderCommand>
{
    public CreateWorkOrderValidator()
    {
        RuleFor(command => command.Title).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Reference).MaximumLength(64);
        RuleFor(command => command.Description).MaximumLength(2000);
        RuleFor(command => command.UnitId).NotEmpty();
        RuleFor(command => command.EstimatedHours).InclusiveBetween(0.25m, 24m);
        RuleFor(command => command.ActivityTypeCode).MaximumLength(64);
    }
}

public sealed record AssignWorkOrderCommand(
    Guid WorkOrderId,
    Guid PersonId,
    DateTimeOffset Start,
    DateTimeOffset? End) : IRequest<AssignmentResult>, ITransactionalRequest;

/// <summary>The assignment, and the planned activity it produced — which is the half the board has to draw.</summary>
public sealed record AssignmentResult(Guid WorkOrderId, Guid PersonId, Guid ActivityEntryId);

public sealed record UnassignWorkOrderCommand(Guid WorkOrderId) : IRequest<Unit>, ITransactionalRequest;

public sealed record PlanShiftCommand(Guid PersonId, string TemplateCode, DateOnly Day)
    : IRequest<Guid>, ITransactionalRequest;

public sealed class PlanShiftValidator : AbstractValidator<PlanShiftCommand>
{
    public PlanShiftValidator()
    {
        RuleFor(command => command.PersonId).NotEmpty();
        RuleFor(command => command.TemplateCode).NotEmpty().MaximumLength(32);
    }
}

public sealed record MoveShiftCommand(Guid ShiftId, string TemplateCode, DateOnly Day)
    : IRequest<Unit>, ITransactionalRequest;

public sealed record DeleteShiftCommand(Guid ShiftId) : IRequest<Unit>, ITransactionalRequest;

/// <summary>6c: dragging a planned block to a new time.</summary>
public sealed record RescheduleTaskCommand(Guid ActivityEntryId, DateTimeOffset Start, DateTimeOffset End)
    : IRequest<Unit>, ITransactionalRequest;

/// <summary>Pulls whatever the external system has waiting, and shadows anything not already known.</summary>
public sealed record RefreshPoolCommand(Guid UnitId, string? Source) : IRequest<int>, ITransactionalRequest;

// --- Handlers ----------------------------------------------------------------------------------------------------

internal sealed class CreateWorkOrderHandler(
    IWorkOrderRepository repository,
    IDirectoryPort directory,
    IUserContext user) : IRequestHandler<CreateWorkOrderCommand, Guid>
{
    public async Task<Guid> Handle(CreateWorkOrderCommand request, CancellationToken ct)
    {
        var departmentId = await DepartmentOfUnitAsync(directory, request.UnitId, ct);

        var order = WorkOrder.Create(
            request.Reference,
            request.Title,
            request.Description,
            source: "manual",
            externalRef: null,
            request.UnitId,
            departmentId,
            request.ProjectId,
            request.ActivityTypeCode,
            request.EstimatedHours,
            user.UserId,
            DateTimeOffset.UtcNow);

        await repository.AddAsync(order, ct);

        return order.Id;
    }

    /// <summary>
    /// The department a unit belongs to.
    /// </summary>
    /// <remarks>
    /// Copied onto the row so the RLS policy can scope by department without joining into Directory — the same
    /// reasoning as an activity entry, and for the same reason: the predicate runs per candidate row.
    /// </remarks>
    internal static async Task<Guid> DepartmentOfUnitAsync(
        IDirectoryPort directory,
        Guid unitId,
        CancellationToken ct)
    {
        var units = await directory.GetUnitsAsync(null, ct);

        return units.FirstOrDefault(unit => unit.Id == unitId)?.DepartmentId
               ?? throw new DomainRuleViolationException("That unit does not exist, or you cannot see it.");
    }
}

internal sealed class AssignWorkOrderHandler(
    IWorkOrderRepository repository,
    IActivitiesPort activities,
    IUserContext user) : IRequestHandler<AssignWorkOrderCommand, AssignmentResult>
{
    public async Task<AssignmentResult> Handle(AssignWorkOrderCommand request, CancellationToken ct)
    {
        var order = await repository.GetAsync(request.WorkOrderId, ct);

        // The estimate becomes the slot length when the drop point does not supply one. A card dropped on a row
        // lands at a time but has no width, and inventing an hour for a four-hour job would be worse than using
        // the number the lead already wrote down.
        var end = request.End ?? request.Start.AddHours((double)order.EstimatedHours);

        order.AssignTo(request.PersonId, request.Start, end, user.UserId, DateTimeOffset.UtcNow);

        // Through the Activities contract, so the entry passes that module's taxonomy check, slot rules and the
        // department's weekly guardrail. A blocked week refuses the assignment, which is the correct outcome:
        // scheduling someone past a cap their department enforces is exactly what the cap is for.
        var entryId = await activities.PlanAsync(
            request.PersonId,
            order.ActivityTypeCode,
            order.ProjectId,
            request.Start,
            end,
            $"{order.Reference} — {order.Title}",
            order.Source,
            order.ExternalRef,
            ct);

        order.LinkActivity(entryId);

        return new AssignmentResult(order.Id, request.PersonId, entryId);
    }
}

internal sealed class UnassignWorkOrderHandler(
    IWorkOrderRepository repository,
    IActivitiesPort activities,
    IUserContext user) : IRequestHandler<UnassignWorkOrderCommand, Unit>
{
    public async Task<Unit> Handle(UnassignWorkOrderCommand request, CancellationToken ct)
    {
        var order = await repository.GetAsync(request.WorkOrderId, ct);

        var entryId = order.Unassign(user.UserId, DateTimeOffset.UtcNow);

        if (entryId is { } id)
        {
            // In the same transaction as the unassignment. Dragging a card back to the pool and leaving the
            // planned hours on somebody's week is the failure this ordering exists to prevent.
            await activities.CancelAsync(id, ct);
        }

        return Unit.Value;
    }
}

internal sealed class PlanShiftHandler(
    IShiftRepository repository,
    IDirectoryPort directory,
    IUserContext user) : IRequestHandler<PlanShiftCommand, Guid>
{
    public async Task<Guid> Handle(PlanShiftCommand request, CancellationToken ct)
    {
        var person = await directory.GetPersonAsync(request.PersonId, ct)
            ?? throw new DomainRuleViolationException("That person is not in the directory.");

        if (person.UnitId is not { } unitId || person.DepartmentId is not { } departmentId)
        {
            throw new DomainRuleViolationException("That person is not placed in a unit yet.");
        }

        var templates = await directory.GetShiftTemplatesAsync(departmentId, ct);

        var template = templates.FirstOrDefault(candidate =>
                           string.Equals(candidate.Code, request.TemplateCode, StringComparison.OrdinalIgnoreCase))
                       ?? throw new DomainRuleViolationException(
                           $"'{request.TemplateCode}' is not a shift this department offers.");

        // The person's existing shifts around that day, so the aggregate can refuse a double-booking. A week
        // either side rather than the day alone: an on-call slot can run past midnight into the next one.
        var existing = await repository.GetForPersonAsync(
            request.PersonId,
            request.Day.AddDays(-1),
            request.Day.AddDays(1),
            ct);

        var shift = Shift.Plan(
            request.PersonId,
            unitId,
            departmentId,
            template,
            request.Day,
            existing,
            user.UserId,
            DateTimeOffset.UtcNow);

        await repository.AddAsync(shift, ct);

        return shift.Id;
    }
}

internal sealed class MoveShiftHandler(
    IShiftRepository repository,
    IDirectoryPort directory,
    IUserContext user) : IRequestHandler<MoveShiftCommand, Unit>
{
    public async Task<Unit> Handle(MoveShiftCommand request, CancellationToken ct)
    {
        var shift = await repository.GetAsync(request.ShiftId, ct);

        var templates = await directory.GetShiftTemplatesAsync(shift.DepartmentId, ct);

        var template = templates.FirstOrDefault(candidate =>
                           string.Equals(candidate.Code, request.TemplateCode, StringComparison.OrdinalIgnoreCase))
                       ?? throw new DomainRuleViolationException(
                           $"'{request.TemplateCode}' is not a shift this department offers.");

        var existing = await repository.GetForPersonAsync(
            shift.PersonId,
            request.Day.AddDays(-1),
            request.Day.AddDays(1),
            ct);

        shift.MoveTo(template, request.Day, existing, user.UserId, DateTimeOffset.UtcNow);

        return Unit.Value;
    }
}

internal sealed class DeleteShiftHandler(IShiftRepository repository)
    : IRequestHandler<DeleteShiftCommand, Unit>
{
    public async Task<Unit> Handle(DeleteShiftCommand request, CancellationToken ct)
    {
        var shift = await repository.GetAsync(request.ShiftId, ct);

        await repository.DeleteAsync(shift, ct);

        return Unit.Value;
    }
}

internal sealed class RescheduleTaskHandler(IActivitiesPort activities)
    : IRequestHandler<RescheduleTaskCommand, Unit>
{
    public async Task<Unit> Handle(RescheduleTaskCommand request, CancellationToken ct)
    {
        // Straight through to Activities, which refuses to move anything that is not a planned slot. There is no
        // rule for this module to add: it owns where a block sits on a canvas, not what a block means.
        await activities.RescheduleAsync(request.ActivityEntryId, request.Start, request.End, ct);

        return Unit.Value;
    }
}

internal sealed class RefreshPoolHandler(
    IWorkOrderRepository repository,
    IEnumerable<IWorkOrderPoolSource> sources,
    IDirectoryPort directory,
    IUserContext user) : IRequestHandler<RefreshPoolCommand, int>
{
    public async Task<int> Handle(RefreshPoolCommand request, CancellationToken ct)
    {
        var departmentId = await CreateWorkOrderHandler.DepartmentOfUnitAsync(directory, request.UnitId, ct);

        var wanted = request.Source?.Trim().ToLowerInvariant();

        var selected = string.IsNullOrEmpty(wanted)
            ? sources
            : sources.Where(source => source.Source == wanted);

        var created = 0;

        foreach (var source in selected)
        {
            foreach (var pooled in await source.GetUnassignedAsync(request.UnitId, ct))
            {
                // Idempotent by external reference. Refreshing is something a lead does repeatedly, and a pull
                // that duplicated the pool every time would make the board useless within an afternoon.
                if (await repository.ExistsForExternalRefAsync(source.Source, pooled.ExternalRef, ct))
                {
                    continue;
                }

                await repository.AddAsync(
                    WorkOrder.Create(
                        pooled.ExternalRef,
                        pooled.Title,
                        pooled.Description,
                        source.Source,
                        pooled.ExternalRef,
                        request.UnitId,
                        departmentId,
                        projectId: null,
                        // Pulled tickets carry no project, so they cannot default to project-run. The department
                        // names the type its unattached RUN work belongs to; until it does, this is quality-of-life,
                        // which is the only canonical bucket that both means maintenance and needs no project.
                        activityTypeCode: "quality-of-life",
                        pooled.EstimatedHours,
                        user.UserId,
                        DateTimeOffset.UtcNow),
                    ct);

                created++;
            }
        }

        return created;
    }
}
