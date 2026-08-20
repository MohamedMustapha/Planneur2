using System.Text.Json;
using System.Text.Json.Serialization;
using Cracra.BuildingBlocks.Abstractions;

namespace Cracra.Modules.Activities.Domain;

/// <summary>
/// One entry in a department's activity taxonomy.
/// </summary>
/// <param name="Code">Stable, never localized. Reports and board colours key off this.</param>
/// <param name="ParentCode">Null for a top bucket; otherwise the canonical bucket it refines.</param>
/// <param name="LabelKey">Translation key. A department may relabel a canonical bucket without renaming its code.</param>
/// <param name="RequiresProject">True where an entry is meaningless without the project it was spent on.</param>
public sealed record ActivityType(
    string Code,
    string? ParentCode,
    string LabelKey,
    bool RequiresProject);

/// <summary>
/// The activity types available to one department, canonical buckets included.
/// </summary>
public sealed class ActivityTaxonomy
{
    private readonly Dictionary<string, ActivityType> _byCode;

    private ActivityTaxonomy(IEnumerable<ActivityType> types) =>
        _byCode = types.ToDictionary(type => type.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The buckets every department inherits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are not defaults a department can delete — they are the vocabulary the platform reports in. S8
    /// summarises BUILD against RUN across departments, and it can only do that if both mean the same thing
    /// everywhere. A department may relabel them and add subtypes beneath them; it may not remove them and it may
    /// not redefine what <c>project-build</c> is.
    /// </para>
    /// <para>
    /// The two project buckets require a project because an hour of BUILD that is not against anything cannot be
    /// costed, reported, or shown on a board. The other two deliberately do not: recruitment and quality-of-life
    /// work is real and belongs to nobody's project.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<ActivityType> Canonical =
    [
        new("project-build", null, "activity.type.project-build", RequiresProject: true),
        new("project-run", null, "activity.type.project-run", RequiresProject: true),
        new("quality-of-life", null, "activity.type.quality-of-life", RequiresProject: false),
        new("recruitment-admin", null, "activity.type.recruitment-admin", RequiresProject: false),
    ];

    public IReadOnlyCollection<ActivityType> Types => _byCode.Values;

    /// <summary>
    /// Merges a department's configured taxonomy over the canonical buckets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Merge, not replace. A department that has never touched its configuration still gets the four canonical
    /// buckets, and a department that has added ten subtypes still cannot lose them. This is what "canonical top
    /// buckets every department inherits and can extend/relabel" means in practice.
    /// </para>
    /// <para>
    /// Malformed configuration falls back to canonical rather than throwing. The alternative is that one bad edit
    /// in department settings stops everyone in that department logging their week — a far worse failure than
    /// quietly offering the standard buckets until someone fixes the JSON.
    /// </para>
    /// </remarks>
    public static ActivityTaxonomy Resolve(string? configJson)
    {
        var merged = Canonical.ToDictionary(type => type.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var configured in Parse(configJson))
        {
            if (string.IsNullOrWhiteSpace(configured.Code))
            {
                continue;
            }

            var code = configured.Code.Trim().ToLowerInvariant();

            // A subtype inherits its parent's project requirement: a department cannot declare a subtype of
            // project-build that needs no project, because the parent bucket's meaning is the platform's, not
            // theirs.
            var parent = configured.Parent is { Length: > 0 } parentCode
                && merged.TryGetValue(parentCode, out var found)
                    ? found
                    : null;

            merged[code] = new ActivityType(
                code,
                parent?.Code,
                string.IsNullOrWhiteSpace(configured.LabelKey) ? $"activity.type.{code}" : configured.LabelKey.Trim(),
                parent?.RequiresProject ?? configured.RequiresProject ?? false);
        }

        return new ActivityTaxonomy(merged.Values);
    }

    public bool Contains(string code) => _byCode.ContainsKey(code);

    public ActivityType Get(string code) =>
        _byCode.TryGetValue(code, out var type)
            ? type
            : throw new DomainRuleViolationException(
                $"'{code}' is not an activity type this department offers.");

    private static IReadOnlyList<ConfiguredType> Parse(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(configJson);

            // The stored shape is an object (S1's validator enforces that much), with the types under "types".
            // Anything else is treated as "nothing configured" rather than as an error, for the reason above.
            if (document.RootElement.ValueKind is not JsonValueKind.Object
                || !document.RootElement.TryGetProperty("types", out var types)
                || types.ValueKind is not JsonValueKind.Array)
            {
                return [];
            }

            return types.Deserialize<List<ConfiguredType>>() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record ConfiguredType(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("parent")] string? Parent,
        [property: JsonPropertyName("labelKey")] string? LabelKey,
        [property: JsonPropertyName("requiresProject")] bool? RequiresProject);
}
