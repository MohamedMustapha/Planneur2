using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Portfolio.Domain;
using FluentValidation;

namespace Cracra.Modules.Portfolio.Application;

// =================================================================================================================
// Commands. The lifecycle rules live on the aggregate; these handlers gather the evidence it cannot gather for
// itself (does the project exist, is anyone on it) and append the audit trail afterwards.
// =================================================================================================================

public sealed record ConsiderItemCommand(string Name, Guid DepartmentId, int Priority, string? Notes)
    : IRequest<Guid>, ITransactionalRequest;

public sealed class ConsiderItemValidator : AbstractValidator<ConsiderItemCommand>
{
    public ConsiderItemValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.DepartmentId).NotEmpty();
        RuleFor(command => command.Priority).InclusiveBetween(1, 999);
        RuleFor(command => command.Notes).MaximumLength(4000);
    }
}

/// <summary>
/// Commits to a candidate, either linking an existing project or provisioning a new one.
/// </summary>
/// <remarks>
/// Both paths in one command because from the user's side it is one decision — "yes, we are doing this" — and
/// whether the project already exists is a detail of how the work was set up, not a different act.
/// </remarks>
public sealed record CommitItemCommand(
    Guid ItemId,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string DecisionNotes) : IRequest<Guid>, ITransactionalRequest;

public sealed class CommitItemValidator : AbstractValidator<CommitItemCommand>
{
    public CommitItemValidator()
    {
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.DecisionNotes).NotEmpty().MaximumLength(4000);

        // Either link or provision, never both and never neither — the alternative is silently ignoring one of the
        // two, which is how an item ends up committed to a project the caller did not mean.
        RuleFor(command => command)
            .Must(command => command.ProjectId is not null ^ !string.IsNullOrWhiteSpace(command.ProjectCode))
            .WithMessage("Give either an existing project id or a code for a new one, not both.");

        RuleFor(command => command.ProjectCode).MaximumLength(64);
        RuleFor(command => command.ProjectName).MaximumLength(256);
    }
}

public sealed record ActivateItemCommand(Guid ItemId) : IRequest<Unit>, ITransactionalRequest;

public sealed record ArchiveItemCommand(Guid ItemId, string Reason) : IRequest<Unit>, ITransactionalRequest;

public sealed class ArchiveItemValidator : AbstractValidator<ArchiveItemCommand>
{
    public ArchiveItemValidator()
    {
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(4000);
    }
}

/// <summary>Backwards move. The endpoint restricts it to the PMO; the handler assumes that has happened.</summary>
public sealed record RevertItemCommand(Guid ItemId, string TargetState, string Reason)
    : IRequest<Unit>, ITransactionalRequest;

public sealed class RevertItemValidator : AbstractValidator<RevertItemCommand>
{
    public RevertItemValidator()
    {
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(4000);

        RuleFor(command => command.TargetState)
            .Must(value => Enum.TryParse<PortfolioState>(value, ignoreCase: true, out _))
            .WithMessage("Target must be considered, committed, active or dephase.");
    }
}

public sealed record AddIterationCommand(
    Guid ItemId,
    string Name,
    string Length,
    DateOnly StartsOn,
    DateOnly? EndsOn) : IRequest<Guid>, ITransactionalRequest;

public sealed class AddIterationValidator : AbstractValidator<AddIterationCommand>
{
    public AddIterationValidator()
    {
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.Name).MaximumLength(128);

        RuleFor(command => command.Length)
            .Must(value => Enum.TryParse<IterationLength>(value, ignoreCase: true, out _))
            .WithMessage("Length must be oneweek, twoweeks, onemonth or custom.");
    }
}

public sealed record RescheduleIterationCommand(
    Guid ItemId,
    Guid IterationId,
    string Name,
    string Length,
    DateOnly StartsOn,
    DateOnly? EndsOn) : IRequest<Unit>, ITransactionalRequest;

public sealed record CloseIterationCommand(Guid ItemId, Guid IterationId) : IRequest<Unit>, ITransactionalRequest;

public sealed record CancelIterationCommand(Guid ItemId, Guid IterationId) : IRequest<Unit>, ITransactionalRequest;

// --- Handlers ----------------------------------------------------------------------------------------------------

internal sealed class ConsiderItemHandler(
    IPortfolioRepository repository,
    IDirectoryPort directory,
    ICatalogReader catalog,
    IUserContext user) : IRequestHandler<ConsiderItemCommand, Guid>
{
    public async Task<Guid> Handle(ConsiderItemCommand request, CancellationToken ct)
    {
        if (!await directory.DepartmentExistsAsync(request.DepartmentId, ct))
        {
            throw new DomainRuleViolationException($"Department {request.DepartmentId} does not exist.");
        }

        var now = DateTimeOffset.UtcNow;

        var item = PortfolioItem.Consider(
            await ItemCodes.AllocateAsync(catalog, requested: null, request.Name, ct),
            request.Name,
            request.Priority,
            request.DepartmentId,
            request.Notes,
            user.UserId,
            now);

        await repository.AddAsync(item, ct);

        await repository.RecordTransitionAsync(
            Transitions.For(
                item,
                from: null,
                PortfolioState.Considered,
                request.Notes ?? "Registered as a candidate.",
                user.UserId,
                now),
            ct);

        return item.Id;
    }
}

/// <summary>Builds the audit row every transition handler appends.</summary>
internal static class Transitions
{
    public static PortfolioTransition For(
        PortfolioItem item,
        PortfolioState? from,
        PortfolioState to,
        string reason,
        Guid decidedBy,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            PortfolioItemId = item.Id,
            DepartmentId = item.DepartmentId,
            FromState = from,
            ToState = to,
            IsReversal = from is { } previous && to < previous,
            Reason = reason,
            DecidedBy = decidedBy,
            DecidedAt = now,
        };
}

internal sealed class CommitItemHandler(
    IPortfolioRepository repository,
    IProjectsPort projects,
    IUserContext user) : IRequestHandler<CommitItemCommand, Guid>
{
    public async Task<Guid> Handle(CommitItemCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);
        var from = item.State;
        var now = DateTimeOffset.UtcNow;

        Guid projectId;

        if (request.ProjectId is { } linked)
        {
            // Exists, and the caller can see it — RLS answers both at once, and an id they cannot read is one they
            // have no business linking a commitment to.
            if (!await projects.ExistsAsync(linked, ct))
            {
                throw new DomainRuleViolationException("That project does not exist, or you cannot see it.");
            }

            if (await repository.IsLinkedToProjectAsync(linked, ct))
            {
                throw new DomainRuleViolationException(
                    "That project is already committed to another portfolio item.");
            }

            projectId = linked;
        }
        else
        {
            projectId = await projects.ProvisionAsync(
                request.ProjectCode!,
                string.IsNullOrWhiteSpace(request.ProjectName) ? item.Name : request.ProjectName!,
                request.DecisionNotes,
                item.DepartmentId,
                ct);
        }

        item.Commit(projectId, request.DecisionNotes, user.UserId, now);

        await repository.RecordTransitionAsync(
            Transitions.For(item, from, item.State, request.DecisionNotes, user.UserId, now),
            ct);

        return projectId;
    }
}

internal sealed class ActivateItemHandler(
    IPortfolioRepository repository,
    IProjectsPort projects,
    IUserContext user) : IRequestHandler<ActivateItemCommand, Unit>
{
    public async Task<Unit> Handle(ActivateItemCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);
        var from = item.State;
        var now = DateTimeOffset.UtcNow;

        // The aggregate owns "must have a plan"; only Projects can answer "and somebody to run it", so the answer
        // is fetched here and handed in rather than guessed at.
        var hasTeam = item.ProjectId is { } projectId && await projects.HasTeamAsync(projectId, ct);

        item.Activate(hasTeam, user.UserId, now);

        await repository.RecordTransitionAsync(
            Transitions.For(item, from, item.State, "Delivery started.", user.UserId, now),
            ct);

        return Unit.Value;
    }
}

internal sealed class ArchiveItemHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<ArchiveItemCommand, Unit>
{
    public async Task<Unit> Handle(ArchiveItemCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);
        var from = item.State;
        var now = DateTimeOffset.UtcNow;

        item.Archive(request.Reason, user.UserId, now);

        await repository.RecordTransitionAsync(
            Transitions.For(item, from, item.State, request.Reason, user.UserId, now),
            ct);

        return Unit.Value;
    }
}

internal sealed class RevertItemHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<RevertItemCommand, Unit>
{
    public async Task<Unit> Handle(RevertItemCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);
        var from = item.State;
        var now = DateTimeOffset.UtcNow;

        item.Revert(
            Enum.Parse<PortfolioState>(request.TargetState, ignoreCase: true),
            request.Reason,
            user.UserId,
            now);

        await repository.RecordTransitionAsync(
            Transitions.For(item, from, item.State, request.Reason, user.UserId, now),
            ct);

        return Unit.Value;
    }
}

internal sealed class AddIterationHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<AddIterationCommand, Guid>
{
    public async Task<Guid> Handle(AddIterationCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);

        var iteration = item.AddIteration(
            request.Name,
            Enum.Parse<IterationLength>(request.Length, ignoreCase: true),
            request.StartsOn,
            request.EndsOn,
            user.UserId,
            DateTimeOffset.UtcNow);

        return iteration.Id;
    }
}

internal sealed class RescheduleIterationHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<RescheduleIterationCommand, Unit>
{
    public async Task<Unit> Handle(RescheduleIterationCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);

        item.RescheduleIteration(
            request.IterationId,
            request.Name,
            Enum.Parse<IterationLength>(request.Length, ignoreCase: true),
            request.StartsOn,
            request.EndsOn,
            user.UserId,
            DateTimeOffset.UtcNow);

        return Unit.Value;
    }
}

internal sealed class CloseIterationHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<CloseIterationCommand, Unit>
{
    public async Task<Unit> Handle(CloseIterationCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);

        item.CloseIteration(request.IterationId, user.UserId, DateTimeOffset.UtcNow);

        return Unit.Value;
    }
}

internal sealed class CancelIterationHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<CancelIterationCommand, Unit>
{
    public async Task<Unit> Handle(CancelIterationCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);

        item.CancelIteration(request.IterationId, user.UserId, DateTimeOffset.UtcNow);

        return Unit.Value;
    }
}
