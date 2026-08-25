using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Portfolio.Domain;
using FluentValidation;

namespace Cracra.Modules.Portfolio.Application;

// =================================================================================================================
// The catalog's write side (v2 §03.3).
//
// One command creates any kind of item, because from the wizard's side it is one act. The optional steps stay
// optional here rather than becoming separate commands: a candidate with only a type and a name is a real card,
// and forcing a second call to make it one would put the "propose a candidate" stub back.
// =================================================================================================================

public sealed record CreateItemCommand(
    string Name,
    string Type,
    string? Code,
    string? Category,
    string? Classification,
    Guid? OwnerNodeId,
    Guid? LeadPersonId,
    Guid? PoPersonId,
    string? Summary,
    decimal? EstimateAmount,
    string? Currency,
    int Priority,
    Guid? OriginProblemId) : IRequest<Guid>, ITransactionalRequest;

public sealed class CreateItemValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Type).NotEmpty().Must(ItemTypes.All.Contains)
            .WithMessage($"Type must be one of: {string.Join(", ", ItemTypes.All)}.");
        RuleFor(command => command.Classification)
            .Must(value => value is null || ItemClassifications.All.Contains(value))
            .WithMessage($"Classification must be one of: {string.Join(", ", ItemClassifications.All)}.");
        RuleFor(command => command.Code).MaximumLength(64);
        RuleFor(command => command.Category).MaximumLength(64);
        RuleFor(command => command.Summary).MaximumLength(4000);
        RuleFor(command => command.Currency).MaximumLength(3);
        RuleFor(command => command.EstimateAmount).GreaterThanOrEqualTo(0).When(c => c.EstimateAmount is not null);
        RuleFor(command => command.Priority).InclusiveBetween(1, 999);
    }
}

internal sealed class CreateItemHandler(
    IPortfolioRepository repository,
    ICatalogReader catalog,
    IUserContext user) : IRequestHandler<CreateItemCommand, Guid>
{
    public async Task<Guid> Handle(CreateItemCommand request, CancellationToken ct)
    {
        var owner = request.OwnerNodeId ?? user.NodeId
            ?? throw new DomainRuleViolationException(
                "This item needs an owning node, and your session does not carry one.");

        var now = DateTimeOffset.UtcNow;

        var item = PortfolioItem.Create(
            await ItemCodes.AllocateAsync(catalog, request.Code, request.Name, ct),
            request.Name,
            ItemTypes.Parse(request.Type),
            request.Category,
            ItemClassifications.Parse(request.Classification ?? DefaultClassification(request.Type)),
            owner,
            request.LeadPersonId,
            request.PoPersonId,
            request.Summary,
            request.EstimateAmount,
            request.Currency,
            request.Priority,
            user.UserId,
            now);

        await repository.AddAsync(item, ct);

        return item.Id;
    }

    /// <summary>
    /// What the type usually means, when nobody said.
    /// </summary>
    /// <remarks>
    /// A default rather than a rule: a platform under active extension is legitimately mixed, and a project can be
    /// pure run. The wizard offers this and the author overrides it, which is why classification stayed a separate
    /// axis instead of being derived.
    /// </remarks>
    private static string DefaultClassification(string type) => type switch
    {
        ItemTypes.Project => ItemClassifications.Build,
        ItemTypes.Platform => ItemClassifications.Mixed,
        _ => ItemClassifications.Run,
    };
}

public sealed record UpdateItemCommand(
    Guid ItemId,
    string? Name,
    string? Category,
    string? Classification,
    Guid? LeadPersonId,
    Guid? PoPersonId,
    string? Summary,
    decimal? EstimateAmount,
    bool? Confidential) : IRequest<Unit>, ITransactionalRequest;

internal sealed class UpdateItemHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<UpdateItemCommand, Unit>
{
    public async Task<Unit> Handle(UpdateItemCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);

        item.UpdateIdentity(
            request.Name,
            request.Category,
            request.Classification is null ? null : ItemClassifications.Parse(request.Classification),
            request.LeadPersonId,
            request.PoPersonId,
            request.Summary,
            request.EstimateAmount,
            request.Confidential,
            user.UserId,
            DateTimeOffset.UtcNow);

        return Unit.Value;
    }
}

public sealed record AwaitNextVersionCommand(Guid ItemId, string Version) : IRequest<Unit>, ITransactionalRequest;

public sealed class AwaitNextVersionValidator : AbstractValidator<AwaitNextVersionCommand>
{
    public AwaitNextVersionValidator()
    {
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.Version).NotEmpty().MaximumLength(32);
    }
}

internal sealed class AwaitNextVersionHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<AwaitNextVersionCommand, Unit>
{
    public async Task<Unit> Handle(AwaitNextVersionCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);
        var from = item.State;
        var now = DateTimeOffset.UtcNow;

        item.AwaitNextVersion(request.Version, user.UserId, now);

        await repository.RecordTransitionAsync(
            Transitions.For(item, from, item.State, $"Awaiting {request.Version}.", user.UserId, now),
            ct);

        return Unit.Value;
    }
}

public sealed record AddEpicCommand(
    Guid ItemId,
    string Name,
    string? Description,
    string? Status,
    string? TargetVersion) : IRequest<Guid>, ITransactionalRequest;

public sealed class AddEpicValidator : AbstractValidator<AddEpicCommand>
{
    public AddEpicValidator()
    {
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Description).MaximumLength(4000);
        RuleFor(command => command.Status)
            .Must(value => value is null || EpicStatuses.All.Contains(value))
            .WithMessage($"Status must be one of: {string.Join(", ", EpicStatuses.All)}.");
        RuleFor(command => command.TargetVersion).MaximumLength(32);
    }
}

internal sealed class AddEpicHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<AddEpicCommand, Guid>
{
    public async Task<Guid> Handle(AddEpicCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);

        var epic = item.AddEpic(
            request.Name,
            request.Description,
            EpicStatuses.Parse(request.Status ?? EpicStatuses.Idea),
            request.TargetVersion,
            user.UserId,
            DateTimeOffset.UtcNow);

        return epic.Id;
    }
}

public sealed record UpdateEpicCommand(
    Guid ItemId,
    Guid EpicId,
    string? Name,
    string? Description,
    string? Status,
    string? TargetVersion,
    Guid? IterationId) : IRequest<Unit>, ITransactionalRequest;

internal sealed class UpdateEpicHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<UpdateEpicCommand, Unit>
{
    public async Task<Unit> Handle(UpdateEpicCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);

        item.UpdateEpic(
            request.EpicId,
            request.Name,
            request.Description,
            request.Status is null ? null : EpicStatuses.Parse(request.Status),
            request.TargetVersion,
            request.IterationId,
            user.UserId,
            DateTimeOffset.UtcNow);

        return Unit.Value;
    }
}

public sealed record AddDependencyCommand(Guid ItemId, Guid DependsOnItemId, string? Kind, string? Note)
    : IRequest<Guid>, ITransactionalRequest;

public sealed class AddDependencyValidator : AbstractValidator<AddDependencyCommand>
{
    public AddDependencyValidator()
    {
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.DependsOnItemId).NotEmpty();
        RuleFor(command => command.Kind)
            .Must(value => value is null || DependencyKinds.All.Contains(value))
            .WithMessage($"Kind must be one of: {string.Join(", ", DependencyKinds.All)}.");
        RuleFor(command => command.Note).MaximumLength(1000);
    }
}

internal sealed class AddDependencyHandler(
    IPortfolioRepository repository,
    ICatalogReader catalog,
    IUserContext user) : IRequestHandler<AddDependencyCommand, Guid>
{
    public async Task<Guid> Handle(AddDependencyCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);

        if (!await catalog.ItemExistsAsync(request.DependsOnItemId, ct))
        {
            throw new ResourceNotFoundException($"No portfolio item {request.DependsOnItemId}.");
        }

        // The whole graph, because a cycle is a property of the graph and not of either end of the new edge.
        var edges = await catalog.GetDependencyEdgesAsync(ct);

        var dependency = item.DependOn(
            request.DependsOnItemId,
            DependencyKinds.Parse(request.Kind ?? DependencyKinds.Consumes),
            request.Note,
            DependencyGraph.WouldCycle(edges, request.ItemId, request.DependsOnItemId),
            user.UserId,
            DateTimeOffset.UtcNow);

        return dependency.Id;
    }
}

public sealed record RemoveDependencyCommand(Guid ItemId, Guid DependencyId) : IRequest<Unit>, ITransactionalRequest;

internal sealed class RemoveDependencyHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<RemoveDependencyCommand, Unit>
{
    public async Task<Unit> Handle(RemoveDependencyCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);

        item.RemoveDependency(request.DependencyId, user.UserId, DateTimeOffset.UtcNow);

        return Unit.Value;
    }
}

public sealed record AddItemMemberCommand(
    Guid ItemId,
    Guid PersonId,
    Guid? NodeId,
    Guid? FunctionalRoleId,
    int? AllocationPercent,
    DateOnly? From,
    DateOnly? To) : IRequest<Guid>, ITransactionalRequest;

internal sealed class AddItemMemberHandler(
    IPortfolioRepository repository,
    IDirectoryNodePort directory,
    IUserContext user) : IRequestHandler<AddItemMemberCommand, Guid>
{
    public async Task<Guid> Handle(AddItemMemberCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);

        var node = request.NodeId
                   ?? await directory.HomeNodeOfAsync(request.PersonId, ct)
                   ?? throw new DomainRuleViolationException(
                       "That person is not attached to a node, so there is nothing to record them as contributing from.");

        var member = item.AddMember(
            request.PersonId,
            node,
            request.FunctionalRoleId,
            request.AllocationPercent,
            request.From ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            request.To,
            user.UserId,
            DateTimeOffset.UtcNow);

        return member.Id;
    }
}

public sealed record RemoveItemMemberCommand(Guid ItemId, Guid PersonId, DateOnly? On)
    : IRequest<Unit>, ITransactionalRequest;

internal sealed class RemoveItemMemberHandler(IPortfolioRepository repository, IUserContext user)
    : IRequestHandler<RemoveItemMemberCommand, Unit>
{
    public async Task<Unit> Handle(RemoveItemMemberCommand request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.ItemId, ct);

        item.RemoveMember(
            request.PersonId,
            request.On ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            user.UserId,
            DateTimeOffset.UtcNow);

        return Unit.Value;
    }
}
