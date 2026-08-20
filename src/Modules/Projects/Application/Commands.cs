using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Projects.Contracts;
using Cracra.Modules.Projects.Domain;
using FluentValidation;

namespace Cracra.Modules.Projects.Application;

// =================================================================================================================
// Commands. Each is ITransactionalRequest, so the transaction behavior wraps it and the outbox row commits with
// the change it describes.
//
// Handlers are thin: they load the aggregate, call one method on it, and let the aggregate refuse. None of them
// re-implements a rule the domain already owns.
// =================================================================================================================

public sealed record CreateProjectCommand(
    string Code,
    string Name,
    string? Description,
    string Classification,
    decimal CostAmount,
    string CostCurrency,
    string? CostNotes,
    Guid LeadDepartmentId,
    IReadOnlyList<Guid> ContributingDepartmentIds) : IRequest<Guid>, ITransactionalRequest;

public sealed class CreateProjectValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Description).MaximumLength(4000);
        RuleFor(command => command.CostAmount).GreaterThanOrEqualTo(0);
        RuleFor(command => command.LeadDepartmentId).NotEmpty();

        RuleFor(command => command.CostCurrency)
            .Must(currency => Money.AllowedCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Currency must be one of: {string.Join(", ", Money.AllowedCurrencies)}.");

        RuleFor(command => command.Classification)
            .Must(value => Enum.TryParse<Classification>(value, ignoreCase: true, out _))
            .WithMessage("Classification must be build, run or mixed.");
    }
}

public sealed record UpdateProjectCommand(
    Guid ProjectId,
    string Name,
    string? Description,
    string Classification,
    decimal CostAmount,
    string CostCurrency,
    string? CostNotes) : IRequest<Unit>, ITransactionalRequest;

public sealed class UpdateProjectValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectValidator()
    {
        RuleFor(command => command.ProjectId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.CostAmount).GreaterThanOrEqualTo(0);

        RuleFor(command => command.CostCurrency)
            .Must(currency => Money.AllowedCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase));

        RuleFor(command => command.Classification)
            .Must(value => Enum.TryParse<Classification>(value, ignoreCase: true, out _));
    }
}

public sealed record AddProjectMemberCommand(
    Guid ProjectId,
    Guid PersonId,
    Guid DepartmentId,
    Guid FunctionalRoleId,
    int? AllocationPercent,
    DateOnly? From) : IRequest<Unit>, ITransactionalRequest;

public sealed class AddProjectMemberValidator : AbstractValidator<AddProjectMemberCommand>
{
    public AddProjectMemberValidator()
    {
        RuleFor(command => command.ProjectId).NotEmpty();
        RuleFor(command => command.PersonId).NotEmpty();
        RuleFor(command => command.DepartmentId).NotEmpty();
        RuleFor(command => command.FunctionalRoleId).NotEmpty();
        RuleFor(command => command.AllocationPercent).InclusiveBetween(1, 100).When(c => c.AllocationPercent is not null);
    }
}

public sealed record RemoveProjectMemberCommand(Guid ProjectId, Guid PersonId, DateOnly? To)
    : IRequest<Unit>, ITransactionalRequest;

public sealed record AddProjectDepartmentCommand(Guid ProjectId, Guid DepartmentId)
    : IRequest<Unit>, ITransactionalRequest;

public sealed record RemoveProjectDepartmentCommand(Guid ProjectId, Guid DepartmentId)
    : IRequest<Unit>, ITransactionalRequest;

// --- Handlers ----------------------------------------------------------------------------------------------------

internal sealed class CreateProjectHandler(
    IProjectRepository repository,
    IDirectoryPort directory,
    IUserContext user) : IRequestHandler<CreateProjectCommand, Guid>
{
    public async Task<Guid> Handle(CreateProjectCommand request, CancellationToken ct)
    {
        var departments = request.ContributingDepartmentIds
            .Append(request.LeadDepartmentId)
            .Distinct()
            .ToArray();

        foreach (var departmentId in departments)
        {
            if (!await directory.DepartmentExistsAsync(departmentId, ct))
            {
                throw new DomainRuleViolationException($"Department {departmentId} does not exist.");
            }
        }

        if (await repository.CodeExistsAsync(request.Code, ct))
        {
            throw new DomainRuleViolationException($"A project with code '{request.Code}' already exists.");
        }

        var project = Project.Create(
            Guid.CreateVersion7(),
            request.Code,
            request.Name,
            request.Description,
            Enum.Parse<Classification>(request.Classification, ignoreCase: true),
            new Money(request.CostAmount, request.CostCurrency),
            // The creator owns it. S4 will let a PMO reassign; until then the person who created it is the one who
            // can always reach it, which beats a project nobody can open.
            user.UserId,
            request.LeadDepartmentId,
            departments,
            user.UserId,
            DateTimeOffset.UtcNow);

        await repository.AddAsync(project, ct);

        return project.Id;
    }
}

internal sealed class UpdateProjectHandler(IProjectRepository repository, IUserContext user)
    : IRequestHandler<UpdateProjectCommand, Unit>
{
    public async Task<Unit> Handle(UpdateProjectCommand request, CancellationToken ct)
    {
        var project = await repository.GetAsync(request.ProjectId, ct);

        project.UpdateDetails(
            request.Name,
            request.Description,
            Enum.Parse<Classification>(request.Classification, ignoreCase: true),
            new Money(request.CostAmount, request.CostCurrency),
            request.CostNotes,
            user.UserId,
            DateTimeOffset.UtcNow);

        return Unit.Value;
    }
}

internal sealed class AddProjectMemberHandler(
    IProjectRepository repository,
    IDirectoryPort directory,
    IProjectAccessProjection projection,
    IUserContext user) : IRequestHandler<AddProjectMemberCommand, Unit>
{
    public async Task<Unit> Handle(AddProjectMemberCommand request, CancellationToken ct)
    {
        var project = await repository.GetAsync(request.ProjectId, ct);

        // Checked against the directory, not just against the project. The aggregate enforces "the department must
        // contribute"; only Directory can answer "and this person is actually in it".
        if (!await directory.IsPersonInDepartmentAsync(request.PersonId, request.DepartmentId, ct))
        {
            throw new DomainRuleViolationException(
                "That person does not belong to the department they would contribute from.");
        }

        if (!await directory.FunctionalRoleExistsAsync(request.FunctionalRoleId, ct))
        {
            throw new DomainRuleViolationException("Unknown functional role.");
        }

        project.AddMember(
            request.PersonId,
            request.DepartmentId,
            request.FunctionalRoleId,
            request.AllocationPercent,
            request.From ?? DateOnly.FromDateTime(DateTime.UtcNow),
            user.UserId,
            DateTimeOffset.UtcNow);

        await SyncAccessProjection(project, projection, ct);

        return Unit.Value;
    }

    internal static async Task SyncAccessProjection(
        Project project,
        IProjectAccessProjection projection,
        CancellationToken ct) =>
        await projection.ReplaceProjectMembershipAsync(
            project.Id,
            [.. project.Members.Where(member => member.IsActive).Select(member => (member.PersonId, member.DepartmentId))],
            ct);
}

internal sealed class RemoveProjectMemberHandler(
    IProjectRepository repository,
    IProjectAccessProjection projection,
    IUserContext user) : IRequestHandler<RemoveProjectMemberCommand, Unit>
{
    public async Task<Unit> Handle(RemoveProjectMemberCommand request, CancellationToken ct)
    {
        var project = await repository.GetAsync(request.ProjectId, ct);

        project.RemoveMember(
            request.PersonId,
            request.To ?? DateOnly.FromDateTime(DateTime.UtcNow),
            user.UserId,
            DateTimeOffset.UtcNow);

        // In the same transaction as the removal. Someone taken off a project must stop being able to read it at
        // the moment they are removed, not whenever a queue next drains.
        await AddProjectMemberHandler.SyncAccessProjection(project, projection, ct);

        return Unit.Value;
    }
}

internal sealed class AddProjectDepartmentHandler(
    IProjectRepository repository,
    IDirectoryPort directory,
    IUserContext user) : IRequestHandler<AddProjectDepartmentCommand, Unit>
{
    public async Task<Unit> Handle(AddProjectDepartmentCommand request, CancellationToken ct)
    {
        if (!await directory.DepartmentExistsAsync(request.DepartmentId, ct))
        {
            throw new DomainRuleViolationException($"Department {request.DepartmentId} does not exist.");
        }

        var project = await repository.GetAsync(request.ProjectId, ct);

        project.AddDepartment(request.DepartmentId, user.UserId, DateTimeOffset.UtcNow);

        return Unit.Value;
    }
}

internal sealed class RemoveProjectDepartmentHandler(IProjectRepository repository, IUserContext user)
    : IRequestHandler<RemoveProjectDepartmentCommand, Unit>
{
    public async Task<Unit> Handle(RemoveProjectDepartmentCommand request, CancellationToken ct)
    {
        var project = await repository.GetAsync(request.ProjectId, ct);

        project.RemoveDepartment(request.DepartmentId, user.UserId, DateTimeOffset.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Returns aggregates, per conventions.md §2. Queries read projections directly instead.</summary>
public interface IProjectRepository
{
    Task<Project> GetAsync(Guid projectId, CancellationToken ct);

    Task AddAsync(Project project, CancellationToken ct);

    Task<bool> CodeExistsAsync(string code, CancellationToken ct);
}
