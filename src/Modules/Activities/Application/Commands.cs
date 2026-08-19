using Cracra.BuildingBlocks.Abstractions;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence.Behaviors;
using Cracra.BuildingBlocks.Web.Users;
using Cracra.Modules.Activities.Domain;
using FluentValidation;

namespace Cracra.Modules.Activities.Application;

// =================================================================================================================
// Commands. The aggregate owns the taxonomy and slot rules; these handlers supply what it cannot see for itself —
// the department's policy, whether the person is on the project, and what their week already holds.
// =================================================================================================================

public sealed record LogActivityCommand(
    Guid? PersonId,
    string ActivityTypeCode,
    Guid? ProjectId,
    Guid? IterationId,
    string Kind,
    string Source,
    string? ExternalRef,
    DateTimeOffset SlotStart,
    DateTimeOffset SlotEnd,
    decimal? Hours,
    string? Note) : IRequest<LogActivityResult>, ITransactionalRequest;

/// <summary>
/// The entry, plus what the guardrail had to say about it.
/// </summary>
/// <remarks>
/// The warning travels back with the write rather than being fetched separately. A soft warn that the client has
/// to go and ask for is a warn most clients will not show.
/// </remarks>
public sealed record LogActivityResult(
    Guid Id,
    string GuardrailStatus,
    decimal WeekHours,
    decimal TargetHours,
    decimal Overtime,
    Guid? ReconciledPlanId);

public sealed class LogActivityValidator : AbstractValidator<LogActivityCommand>
{
    public LogActivityValidator()
    {
        RuleFor(command => command.ActivityTypeCode).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Note).MaximumLength(2000);
        RuleFor(command => command.ExternalRef).MaximumLength(256);
        RuleFor(command => command.SlotEnd).GreaterThan(command => command.SlotStart);
        RuleFor(command => command.Hours).GreaterThan(0).When(command => command.Hours is not null);

        RuleFor(command => command.Kind)
            .Must(value => Enum.TryParse<ActivityKind>(value, ignoreCase: true, out _))
            .WithMessage("Kind must be planned or actual.");

        RuleFor(command => command.Source)
            .Must(value => SourceCodes.TryParse(value, out _))
            .WithMessage("Source must be manual, azure-devops or servicenow.");
    }
}

public sealed record AmendActivityCommand(
    Guid Id,
    string ActivityTypeCode,
    Guid? ProjectId,
    Guid? IterationId,
    DateTimeOffset SlotStart,
    DateTimeOffset SlotEnd,
    decimal? Hours,
    string? Note) : IRequest<LogActivityResult>, ITransactionalRequest;

public sealed class AmendActivityValidator : AbstractValidator<AmendActivityCommand>
{
    public AmendActivityValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.ActivityTypeCode).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Note).MaximumLength(2000);
        RuleFor(command => command.SlotEnd).GreaterThan(command => command.SlotStart);
    }
}

public sealed record DeleteActivityCommand(Guid Id) : IRequest<Unit>, ITransactionalRequest;

/// <summary>
/// The hyphenated source codes the API speaks, mapped to the enum.
/// </summary>
/// <remarks>
/// <c>azure-devops</c> rather than <c>AzureDevOps</c> because the spec fixes those strings and they appear in URLs.
/// Enum.TryParse would not accept the hyphen, and inventing a second spelling for the wire would mean two names
/// for one thing.
/// </remarks>
public static class SourceCodes
{
    public const string Manual = "manual";
    public const string AzureDevOps = "azure-devops";
    public const string ServiceNow = "servicenow";

    public static bool TryParse(string? value, out ActivitySource source)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case Manual or "":
                source = ActivitySource.Manual;
                return true;
            case AzureDevOps:
                source = ActivitySource.AzureDevOps;
                return true;
            case ServiceNow:
                source = ActivitySource.ServiceNow;
                return true;
            default:
                source = ActivitySource.Manual;
                return false;
        }
    }

    public static string ToCode(ActivitySource source) => source switch
    {
        ActivitySource.AzureDevOps => AzureDevOps,
        ActivitySource.ServiceNow => ServiceNow,
        _ => Manual,
    };
}

// --- Handlers ----------------------------------------------------------------------------------------------------

internal sealed class LogActivityHandler(
    IActivityRepository repository,
    IDirectoryPort directory,
    IProjectsPort projects,
    IUserContext user) : IRequestHandler<LogActivityCommand, LogActivityResult>
{
    public async Task<LogActivityResult> Handle(LogActivityCommand request, CancellationToken ct)
    {
        // Logging for someone else is a lead's act, and RLS decides whether the row survives. Defaulting to the
        // caller keeps the common case — logging your own week — free of a field nobody should have to fill in.
        var personId = request.PersonId ?? user.UserId;

        var placement = await directory.GetPlacementAsync(personId, ct)
            ?? throw new DomainRuleViolationException("That person is not in the directory.");

        var policy = await directory.GetPolicyAsync(placement.DepartmentId, ct);

        var kind = Enum.Parse<ActivityKind>(request.Kind, ignoreCase: true);
        SourceCodes.TryParse(request.Source, out var source);

        var slot = new TimeSlot(request.SlotStart, request.SlotEnd);

        if (request.ProjectId is { } projectId && !await projects.IsMemberAsync(projectId, personId, ct))
        {
            // The spec asks for this as validation on top of RLS. RLS stops them reading a project they are not
            // on; this stops them booking time to one they can read but do not work on — a head, for instance.
            throw new DomainRuleViolationException(
                "Time can only be booked to a project the person is a member of.");
        }

        var entry = ActivityEntry.Log(
            personId,
            placement.UnitId,
            placement.DepartmentId,
            policy.Taxonomy,
            request.ActivityTypeCode,
            request.ProjectId,
            request.IterationId,
            kind,
            source,
            request.ExternalRef,
            slot,
            request.Hours is { } hours ? new WorkHours(hours) : null,
            request.Note,
            user.UserId,
            DateTimeOffset.UtcNow);

        var verdict = await GuardrailFor(repository, entry, policy, excluding: null, ct);

        await repository.AddAsync(entry, ct);

        // Reconciliation is automatic where a plan is sitting in the window. Making the user find and name their
        // own planned slot would mean most actuals never get linked, and the planned-versus-actual comparison the
        // module exists for would quietly stop working.
        Guid? reconciled = null;

        if (kind is ActivityKind.Actual
            && await repository.FindOpenPlanAsync(personId, slot, ct) is { } plan)
        {
            entry.Reconcile(plan, user.UserId, DateTimeOffset.UtcNow);
            reconciled = plan.Id;
        }

        return Result(entry.Id, verdict, reconciled);
    }

    /// <summary>
    /// Checks the week and applies the department's policy.
    /// </summary>
    /// <remarks>
    /// Only actual entries are counted and only actual entries are checked: a planned week is an intention, and
    /// blocking someone from sketching out a full week they will then trim helps nobody.
    /// </remarks>
    internal static async Task<GuardrailVerdict> GuardrailFor(
        IActivityRepository repository,
        ActivityEntry entry,
        DepartmentPolicy policy,
        Guid? excluding,
        CancellationToken ct)
    {
        if (entry.Kind is not ActivityKind.Actual)
        {
            return new GuardrailVerdict(GuardrailOutcome.Within, 0m, policy.WeeklyTargetHours, 0m);
        }

        var already = await repository.RecordedHoursAsync(entry.PersonId, entry.Week, excluding, ct);

        return WeeklyGuardrail.Enforce(
            WeeklyGuardrail.Check(already, entry.Hours, policy.WeeklyTargetHours, policy.EnforceWeeklyTarget));
    }

    internal static LogActivityResult Result(Guid id, GuardrailVerdict verdict, Guid? reconciled) =>
        new(
            id,
            verdict.Outcome.ToString().ToLowerInvariant(),
            verdict.RecordedHours,
            verdict.TargetHours,
            verdict.Overtime,
            reconciled);
}

internal sealed class AmendActivityHandler(
    IActivityRepository repository,
    IDirectoryPort directory,
    IProjectsPort projects,
    IUserContext user) : IRequestHandler<AmendActivityCommand, LogActivityResult>
{
    public async Task<LogActivityResult> Handle(AmendActivityCommand request, CancellationToken ct)
    {
        var entry = await repository.GetAsync(request.Id, ct);

        var policy = await directory.GetPolicyAsync(entry.DepartmentId, ct);

        if (request.ProjectId is { } projectId && !await projects.IsMemberAsync(projectId, entry.PersonId, ct))
        {
            throw new DomainRuleViolationException(
                "Time can only be booked to a project the person is a member of.");
        }

        entry.Amend(
            policy.Taxonomy,
            request.ActivityTypeCode,
            request.ProjectId,
            request.IterationId,
            new TimeSlot(request.SlotStart, request.SlotEnd),
            request.Hours is { } hours ? new WorkHours(hours) : null,
            request.Note,
            user.UserId,
            DateTimeOffset.UtcNow);

        // Excluding itself: the entry's own current hours are already in the week's total, and counting them twice
        // would refuse a correction that lowers the number.
        var verdict = await LogActivityHandler.GuardrailFor(repository, entry, policy, excluding: entry.Id, ct);

        return LogActivityHandler.Result(entry.Id, verdict, entry.SupersedesEntryId);
    }
}

internal sealed class DeleteActivityHandler(IActivityRepository repository)
    : IRequestHandler<DeleteActivityCommand, Unit>
{
    public async Task<Unit> Handle(DeleteActivityCommand request, CancellationToken ct)
    {
        var entry = await repository.GetAsync(request.Id, ct);

        await repository.DeleteAsync(entry, ct);

        return Unit.Value;
    }
}
