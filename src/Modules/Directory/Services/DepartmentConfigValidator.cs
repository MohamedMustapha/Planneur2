using System.Text.Json;
using Cracra.BuildingBlocks.Web.Errors;

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
