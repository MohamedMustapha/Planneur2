using Cracra.BuildingBlocks.Abstractions;
using Cracra.Modules.Strategy.Contracts;

namespace Cracra.Modules.Strategy.Domain;

/// <summary>
/// A strategy: a period, a narrative, and the objectives that make it measurable (v2 §06.1).
/// </summary>
/// <remarks>
/// <para>
/// Named <c>StrategyPlan</c> rather than <c>Strategy</c> only because the module's namespace is already
/// <c>Cracra.Modules.Strategy</c> and a type of the same name inside it turns every mention into a question about
/// which one was meant. The table, the wire and the screens all say strategy.
/// </para>
/// <para>
/// Objectives, key results and contributions are inside the aggregate because the rules that matter span them: an
/// objective's weight is only meaningful against its siblings, and a contribution linked to a deleted objective is
/// a dangling promise. Progress is computed here (<see cref="ObjectiveMath"/>) rather than in a handler, so the
/// number a card shows, the number a COPIL minute embeds and the number a test asserts are one implementation.
/// </para>
/// </remarks>
public sealed class StrategyPlan
{
    private readonly List<Objective> _objectives = [];

    private StrategyPlan()
    {
    }

    public Guid Id { get; private init; }

    public string ScopeType { get; private set; } = StrategyScopeTypes.Node;

    /// <summary>The node the strategy belongs to. Also the RLS scope — see the migration.</summary>
    public Guid ScopeId { get; private set; }

    public DateOnly PeriodFrom { get; private set; }

    public DateOnly PeriodTo { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Narrative { get; private set; }

    public Guid OwnerPersonId { get; private set; }

    public string Status { get; private set; } = StrategyStatuses.Draft;

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public IReadOnlyCollection<Objective> Objectives => _objectives;

    public static StrategyPlan Open(
        string scopeType,
        Guid scopeId,
        DateOnly periodFrom,
        DateOnly periodTo,
        string title,
        string? narrative,
        Guid ownerPersonId,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleViolationException("A strategy needs a title.");
        }

        if (!StrategyScopeTypes.All.Contains(scopeType, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{scopeType}' is not a strategy scope.");
        }

        if (scopeId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A strategy needs the node it belongs to.");
        }

        if (periodTo < periodFrom)
        {
            throw new DomainRuleViolationException("A strategy cannot end before it starts.");
        }

        return new StrategyPlan
        {
            Id = Guid.CreateVersion7(),
            ScopeType = scopeType,
            ScopeId = scopeId,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            Title = title.Trim(),
            Narrative = Blank(narrative),
            OwnerPersonId = ownerPersonId,
            Status = StrategyStatuses.Draft,
            CreatedAt = now,
            ModifiedAt = now,
        };
    }

    public void Amend(
        string? title,
        string? narrative,
        DateOnly? periodFrom,
        DateOnly? periodTo,
        string? status,
        DateTimeOffset now)
    {
        if (title is { } newTitle)
        {
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                throw new DomainRuleViolationException("A strategy needs a title.");
            }

            Title = newTitle.Trim();
        }

        if (narrative is not null)
        {
            Narrative = Blank(narrative);
        }

        var from = periodFrom ?? PeriodFrom;
        var to = periodTo ?? PeriodTo;

        if (to < from)
        {
            throw new DomainRuleViolationException("A strategy cannot end before it starts.");
        }

        PeriodFrom = from;
        PeriodTo = to;

        if (status is { } wanted)
        {
            if (!StrategyStatuses.All.Contains(wanted, StringComparer.Ordinal))
            {
                throw new DomainRuleViolationException($"'{wanted}' is not a strategy status.");
            }

            Status = wanted;
        }

        ModifiedAt = now;
    }

    public Objective AddObjective(
        string title,
        string? description,
        string metricKind,
        decimal? baseline,
        decimal? target,
        decimal? current,
        string? unit,
        DateOnly? due,
        decimal weight,
        DateTimeOffset now)
    {
        RefuseWhenClosed();

        var objective = Objective.For(
            Id,
            title,
            description,
            metricKind,
            baseline,
            target,
            current,
            unit,
            due,
            weight,
            now);

        _objectives.Add(objective);
        ModifiedAt = now;

        return objective;
    }

    public Objective ObjectiveById(Guid objectiveId) =>
        _objectives.SingleOrDefault(objective => objective.Id == objectiveId)
        ?? throw new ResourceNotFoundException($"No objective {objectiveId} on this strategy.");

    public void RemoveObjective(Guid objectiveId, DateTimeOffset now)
    {
        RefuseWhenClosed();

        _objectives.Remove(ObjectiveById(objectiveId));
        ModifiedAt = now;
    }

    /// <summary>
    /// The strategy's own progress: its objectives', weighted (v2 §06.2).
    /// </summary>
    /// <remarks>
    /// Weights that do not add to one are normalised rather than refused. Somebody writing four objectives at
    /// weight 1 meant "equally", and a form that rejects that in favour of 0.25 four times is arithmetic homework,
    /// not a rule. A strategy with no objectives is 0 and not 1 — nothing was achieved, rather than everything.
    /// </remarks>
    public decimal Progress()
    {
        var weighted = _objectives.Sum(objective => objective.Weight * objective.Progress());
        var total = _objectives.Sum(objective => objective.Weight);

        return total <= 0m ? 0m : ObjectiveMath.Bounded(weighted / total);
    }

    private void RefuseWhenClosed()
    {
        if (Status == StrategyStatuses.Closed)
        {
            throw new DomainRuleViolationException("This strategy is closed; reopen it before changing objectives.");
        }
    }

    internal static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// One measurable commitment, and the work linked to it.
/// </summary>
/// <remarks>
/// The status is derived and overridable, in that order. Deriving it means an objective nobody touched still tells
/// the truth at the next COPIL; letting it be overridden means an owner who knows the number is misleading — the
/// measure lags, the last milestone lands next week — can say so without falsifying the measure itself.
/// </remarks>
public sealed class Objective
{
    private readonly List<KeyResult> _keyResults = [];
    private readonly List<ObjectiveContribution> _contributions = [];

    private Objective()
    {
    }

    public Guid Id { get; private init; }

    public Guid StrategyId { get; private init; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string MetricKind { get; private set; } = MetricKinds.Number;

    public decimal? Baseline { get; private set; }

    public decimal? Target { get; private set; }

    public decimal? Current { get; private set; }

    public string? Unit { get; private set; }

    public DateOnly? Due { get; private set; }

    /// <summary>The derived status, or the overridden one when <see cref="StatusOverridden"/> is set.</summary>
    public string Status { get; private set; } = ObjectiveStatuses.OnTrack;

    public bool StatusOverridden { get; private set; }

    public decimal Weight { get; private set; } = 1m;

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ModifiedAt { get; private set; }

    /// <summary>
    /// The day the clock starts for "am I on track".
    /// </summary>
    /// <remarks>
    /// The objective's own creation, not its strategy's period start. An objective added in month nine of a
    /// three-year strategy has nine months of catching up to do only in a reading nobody would defend; measured
    /// from when it was written, it is on track on day one, which is true.
    /// </remarks>
    public DateOnly CreatedOn => DateOnly.FromDateTime(CreatedAt.UtcDateTime);

    public IReadOnlyCollection<KeyResult> KeyResults => _keyResults;

    public IReadOnlyCollection<ObjectiveContribution> Contributions => _contributions;

    internal static Objective For(
        Guid strategyId,
        string title,
        string? description,
        string metricKind,
        decimal? baseline,
        decimal? target,
        decimal? current,
        string? unit,
        DateOnly? due,
        decimal weight,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleViolationException("An objective needs a title somebody could hold you to.");
        }

        if (!MetricKinds.All.Contains(metricKind, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{metricKind}' is not a metric kind.");
        }

        if (MetricKinds.IsMeasured(metricKind) && target is null)
        {
            throw new DomainRuleViolationException(
                "A measured objective needs a target; use a milestone objective when there is no number.");
        }

        if (weight <= 0m)
        {
            throw new DomainRuleViolationException("A weight of zero is an objective nobody is committing to.");
        }

        var objective = new Objective
        {
            Id = Guid.CreateVersion7(),
            StrategyId = strategyId,
            Title = title.Trim(),
            Description = StrategyPlan.Blank(description),
            MetricKind = metricKind,
            Baseline = baseline,
            Target = target,
            Current = current ?? baseline,
            Unit = StrategyPlan.Blank(unit),
            Due = due,
            Weight = weight,
            CreatedAt = now,
            ModifiedAt = now,
        };

        objective.Status = ObjectiveMath.Derive(objective, DateOnly.FromDateTime(now.UtcDateTime));

        return objective;
    }

    public void Amend(
        string? title,
        string? description,
        decimal? baseline,
        decimal? target,
        string? unit,
        DateOnly? due,
        decimal? weight,
        DateTimeOffset now)
    {
        if (title is { } newTitle)
        {
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                throw new DomainRuleViolationException("An objective needs a title.");
            }

            Title = newTitle.Trim();
        }

        if (description is not null)
        {
            Description = StrategyPlan.Blank(description);
        }

        if (baseline is not null)
        {
            Baseline = baseline;
        }

        if (target is not null)
        {
            Target = target;
        }

        if (unit is not null)
        {
            Unit = StrategyPlan.Blank(unit);
        }

        if (due is not null)
        {
            Due = due;
        }

        if (weight is { } newWeight)
        {
            if (newWeight <= 0m)
            {
                throw new DomainRuleViolationException("A weight of zero is an objective nobody is committing to.");
            }

            Weight = newWeight;
        }

        Restate(now);
    }

    /// <summary>
    /// Records where the measure stands now.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Amend"/> because it is a different act performed by different people at a
    /// different cadence: amending is rewriting the commitment, measuring is reporting against it. Folding them
    /// would mean every monthly reading arrived as a chance to move the target.
    /// </remarks>
    public void Measure(decimal current, DateTimeOffset now)
    {
        if (!MetricKinds.IsMeasured(MetricKind))
        {
            throw new DomainRuleViolationException(
                $"A {MetricKind} objective is not measured with a number; set its status instead.");
        }

        Current = current;
        Restate(now);
    }

    /// <summary>Says what the status is, whatever the arithmetic says. Passing null hands it back to the derivation.</summary>
    public void OverrideStatus(string? status, DateTimeOffset now)
    {
        if (status is null)
        {
            StatusOverridden = false;
            Restate(now);

            return;
        }

        if (!ObjectiveStatuses.All.Contains(status, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{status}' is not an objective status.");
        }

        Status = status;
        StatusOverridden = true;
        ModifiedAt = now;
    }

    public KeyResult AddKeyResult(string title, decimal target, decimal current, DateTimeOffset now)
    {
        var keyResult = KeyResult.For(Id, title, target, current);

        _keyResults.Add(keyResult);
        ModifiedAt = now;

        return keyResult;
    }

    public void RemoveKeyResult(Guid keyResultId, DateTimeOffset now)
    {
        var found = _keyResults.SingleOrDefault(keyResult => keyResult.Id == keyResultId)
                    ?? throw new ResourceNotFoundException($"No key result {keyResultId}.");

        _keyResults.Remove(found);
        ModifiedAt = now;
    }

    /// <summary>
    /// Links a portfolio item or a problem as work that moves this objective.
    /// </summary>
    /// <remarks>
    /// Idempotent on the pair, so linking twice from two screens is not an error somebody has to understand — it
    /// re-weights instead, which is what a second link was going to mean anyway.
    /// </remarks>
    public ObjectiveContribution Link(
        string sourceType,
        Guid sourceId,
        decimal weight,
        string? note,
        DateTimeOffset now)
    {
        if (!ContributionSources.All.Contains(sourceType, StringComparer.Ordinal))
        {
            throw new DomainRuleViolationException($"'{sourceType}' is not a contribution source.");
        }

        if (sourceId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A contribution needs something to point at.");
        }

        if (weight <= 0m)
        {
            throw new DomainRuleViolationException("A contribution weighing nothing is not a contribution.");
        }

        var existing = _contributions.SingleOrDefault(
            contribution => contribution.SourceType == sourceType && contribution.SourceId == sourceId);

        if (existing is not null)
        {
            existing.Reweigh(weight, note);
            ModifiedAt = now;

            return existing;
        }

        var linked = ObjectiveContribution.For(Id, sourceType, sourceId, weight, note, now);

        _contributions.Add(linked);
        ModifiedAt = now;

        return linked;
    }

    public ObjectiveContribution Unlink(Guid contributionId, DateTimeOffset now)
    {
        var found = _contributions.SingleOrDefault(contribution => contribution.Id == contributionId)
                    ?? throw new ResourceNotFoundException($"No contribution {contributionId}.");

        _contributions.Remove(found);
        ModifiedAt = now;

        return found;
    }

    public decimal Progress() => ObjectiveMath.Progress(this);

    private void Restate(DateTimeOffset now)
    {
        if (!StatusOverridden)
        {
            Status = ObjectiveMath.Derive(this, DateOnly.FromDateTime(now.UtcDateTime));
        }

        ModifiedAt = now;
    }
}

public sealed class KeyResult
{
    private KeyResult()
    {
    }

    public Guid Id { get; private init; }

    public Guid ObjectiveId { get; private init; }

    public string Title { get; private set; } = string.Empty;

    public decimal Target { get; private set; }

    public decimal Current { get; private set; }

    public decimal Progress => Target == 0m ? 0m : ObjectiveMath.Bounded(Current / Target);

    internal static KeyResult For(Guid objectiveId, string title, decimal target, decimal current)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleViolationException("A key result needs a title.");
        }

        return new KeyResult
        {
            Id = Guid.CreateVersion7(),
            ObjectiveId = objectiveId,
            Title = title.Trim(),
            Target = target,
            Current = current,
        };
    }

    public void Measure(decimal current) => Current = current;
}

public sealed class ObjectiveContribution
{
    private ObjectiveContribution()
    {
    }

    public Guid Id { get; private init; }

    public Guid ObjectiveId { get; private init; }

    public string SourceType { get; private init; } = ContributionSources.Item;

    public Guid SourceId { get; private init; }

    public decimal Weight { get; private set; } = 1m;

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    internal static ObjectiveContribution For(
        Guid objectiveId,
        string sourceType,
        Guid sourceId,
        decimal weight,
        string? note,
        DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ObjectiveId = objectiveId,
        SourceType = sourceType,
        SourceId = sourceId,
        Weight = weight,
        Note = StrategyPlan.Blank(note),
        CreatedAt = now,
    };

    internal void Reweigh(decimal weight, string? note)
    {
        Weight = weight;
        Note = StrategyPlan.Blank(note) ?? Note;
    }
}
