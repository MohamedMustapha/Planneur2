using System.Globalization;
using System.Text.Json;
using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Directory.Services;

/// <summary>
/// Structural validation of a department configuration.
/// </summary>
/// <remarks>
/// Structure only: the JSON must parse and be the right shape. What a taxonomy <em>means</em> is S5's business,
/// and duplicating that judgement here would give two places to disagree about it. Kept separate from the service
/// so it can be tested without a database — the rules are the part worth exercising exhaustively.
/// </remarks>
public static class DepartmentConfigValidator
{
    /// <summary>A week has 168 hours; a target above 60 is a typo, not a policy.</summary>
    public const decimal MaximumWeeklyTargetHours = 60m;

    public static void Validate(UpdateDepartmentConfigRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        EnsureJsonObject(request.ActivityTaxonomyJson, nameof(request.ActivityTaxonomyJson));
        EnsureJsonObject(request.RoleLabelsJson, nameof(request.RoleLabelsJson));
        EnsureJsonObject(request.KudoRulesJson, nameof(request.KudoRulesJson));
        EnsureJsonObject(request.ShiftTemplatesJson, nameof(request.ShiftTemplatesJson));
        EnsureJsonArray(request.IterationPresetsJson, nameof(request.IterationPresetsJson));

        if (request.WeeklyTargetHours <= 0 || request.WeeklyTargetHours > MaximumWeeklyTargetHours)
        {
            throw new DomainRuleViolationException(
                $"The weekly target must be greater than zero and no more than {MaximumWeeklyTargetHours} hours.");
        }

        if (string.IsNullOrWhiteSpace(request.DefaultBoardLayout))
        {
            throw new DomainRuleViolationException("A default board layout is required.");
        }

        ValidateWorkingDay(request.WorkingDayJson);
    }

    /// <summary>
    /// The working day must parse, and must not contradict itself.
    /// </summary>
    /// <remarks>
    /// Still structure rather than judgement, in the spirit of the rest of this class: whether 07:00 is a sensible
    /// hour to start is the department's business, but an afternoon ending before it starts — or sitting outside
    /// the day it belongs to — is not a policy anyone meant. S5 ignores an incoherent day and falls back to the
    /// platform default, which means a bad edit accepted here would save cleanly and then quietly do nothing.
    /// Better to say so while the person is still looking at the form.
    /// </remarks>
    private static void ValidateWorkingDay(string value)
    {
        var root = Parse(value, nameof(UpdateDepartmentConfigRequest.WorkingDayJson));

        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw new DomainRuleViolationException("WorkingDayJson must be a JSON object.");
        }

        // An empty object is how a department says "use the defaults", and is the value every existing row holds.
        if (!root.EnumerateObject().Any())
        {
            return;
        }

        var dayStart = ReadTime(root, "dayStart");
        var dayEnd = ReadTime(root, "dayEnd");
        var (morningStart, morningEnd) = ReadSession(root, "morning");
        var (afternoonStart, afternoonEnd) = ReadSession(root, "afternoon");

        Ensure(dayStart < dayEnd, "The working day must end after it starts.");
        Ensure(morningStart < morningEnd, "The morning session must end after it starts.");
        Ensure(afternoonStart < afternoonEnd, "The afternoon session must end after it starts.");
        Ensure(morningEnd <= afternoonStart, "The morning session must finish before the afternoon begins.");
        Ensure(
            morningStart >= dayStart && afternoonEnd <= dayEnd,
            "Both sessions must fall inside the working day.");
    }

    private static (TimeOnly Start, TimeOnly End) ReadSession(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var session) || session.ValueKind is not JsonValueKind.Object)
        {
            throw new DomainRuleViolationException($"WorkingDayJson needs a '{name}' object.");
        }

        return (ReadTime(session, "start"), ReadTime(session, "end"));
    }

    private static TimeOnly ReadTime(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var property)
            || property.ValueKind is not JsonValueKind.String
            || !TimeOnly.TryParse(property.GetString(), CultureInfo.InvariantCulture, out var parsed))
        {
            throw new DomainRuleViolationException($"WorkingDayJson needs '{name}' as a time such as \"09:00\".");
        }

        return parsed;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainRuleViolationException(message);
        }
    }

    private static void EnsureJsonObject(string value, string field)
    {
        if (Parse(value, field).ValueKind is not JsonValueKind.Object)
        {
            throw new DomainRuleViolationException($"{field} must be a JSON object.");
        }
    }

    private static void EnsureJsonArray(string value, string field)
    {
        if (Parse(value, field).ValueKind is not JsonValueKind.Array)
        {
            throw new DomainRuleViolationException($"{field} must be a JSON array.");
        }
    }

    private static JsonElement Parse(string value, string field)
    {
        try
        {
            return JsonDocument.Parse(value).RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new DomainRuleViolationException($"{field} is not valid JSON: {exception.Message}");
        }
    }
}
