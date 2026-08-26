using Cracra.BuildingBlocks.Mediator;
using Cracra.Modules.Scheduling.Contracts;

namespace Cracra.Modules.Scheduling.Application;

// =================================================================================================================
// The read side. Every one of these delegates to the composer or to a repository — there is no state here, and a
// query that started applying rules would be a rule living somewhere the domain cannot see it.
// =================================================================================================================

public sealed record GetBoardQuery(string? Type, Guid? ScopeId, bool ExpandPeople, DateOnly? From, DateOnly? To)
    : IRequest<BoardPayload>;

public sealed record GetPoolQuery(Guid? UnitId, string? Source) : IRequest<IReadOnlyList<WorkOrderView>>;

public sealed record GetShiftTemplatesQuery(Guid? DepartmentId) : IRequest<IReadOnlyList<Contracts.ShiftTemplate>>;

internal sealed class GetBoardHandler(BoardComposer composer) : IRequestHandler<GetBoardQuery, BoardPayload>
{
    public async Task<BoardPayload> Handle(GetBoardQuery request, CancellationToken ct)
    {
        // A week by default, Monday to Sunday. The pager on every board steps in weeks, so defaulting to anything
        // else would make the first render disagree with the first click.
        var from = request.From ?? MondayOf(DateOnly.FromDateTime(DateTime.UtcNow));
        var to = request.To ?? from.AddDays(6);

        return await composer.ComposeAsync(
            request.Type ?? BoardTypes.My,
            request.ScopeId,
            request.ExpandPeople,
            from,
            to,
            ct);
    }

    private static DateOnly MondayOf(DateOnly day) =>
        day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}

internal sealed class GetPoolHandler(
    IWorkOrderRepository repository,
    IProjectsPort projects,
    Cracra.BuildingBlocks.Web.Users.IUserContext user)
    : IRequestHandler<GetPoolQuery, IReadOnlyList<WorkOrderView>>
{
    public async Task<IReadOnlyList<WorkOrderView>> Handle(GetPoolQuery request, CancellationToken ct)
    {
        var unitId = request.UnitId ?? user.UnitId;

        if (unitId is not { } unit)
        {
            return [];
        }

        var orders = await repository.GetForUnitAsync(unit, ct);

        var open = orders.Where(order => order.State == Domain.WorkOrderState.Unassigned).ToList();

        if (request.Source is { Length: > 0 } source)
        {
            open = [.. open.Where(order => string.Equals(order.Source, source, StringComparison.OrdinalIgnoreCase))];
        }

        var codes = await projects.GetProjectCodesAsync(
            [.. open.Where(order => order.ProjectId is not null).Select(order => order.ProjectId!.Value).Distinct()],
            ct);

        return
        [
            .. open.Select(order => new WorkOrderView(
                order.Id,
                order.Reference,
                order.Title,
                order.Description,
                order.Source,
                order.ExternalRef,
                order.ProjectId,
                order.ProjectId is { } id ? codes.GetValueOrDefault(id) : null,
                order.ActivityTypeCode,
                order.UnitId,
                order.AssignedToPersonId,
                AssignedToName: null,
                order.ScheduledStart,
                order.ScheduledEnd,
                order.EstimatedHours,
                order.State.ToString().ToLowerInvariant())),
        ];
    }
}

internal sealed class GetShiftTemplatesHandler(
    IDirectoryPort directory,
    Cracra.BuildingBlocks.Web.Users.IUserContext user)
    : IRequestHandler<GetShiftTemplatesQuery, IReadOnlyList<Contracts.ShiftTemplate>>
{
    public async Task<IReadOnlyList<Contracts.ShiftTemplate>> Handle(
        GetShiftTemplatesQuery request,
        CancellationToken ct)
    {
        var departmentId = request.DepartmentId ?? user.DepartmentIds.FirstOrDefault();

        var templates = departmentId == Guid.Empty
            ? Domain.ShiftTemplate.Defaults
            : await directory.GetShiftTemplatesAsync(departmentId, ct);

        return
        [
            .. templates.Select(template => new Contracts.ShiftTemplate(
                template.Code,
                template.LabelKey,
                template.Start,
                template.End,
                template.MinimumStaff,
                template.Color)),
        ];
    }
}
