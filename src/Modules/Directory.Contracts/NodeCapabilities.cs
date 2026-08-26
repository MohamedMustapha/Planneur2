using System.Text.Json;

namespace Cracra.Modules.Directory.Contracts;

/// <summary>
/// The one registry of capabilities a profile can switch off.
/// </summary>
/// <remarks>
/// <para>
/// Single registry on purpose, and an architecture test enforces it. The failure mode this prevents is a new page
/// shipping with its own ad-hoc flag read: the page works, nobody notices it is unhideable, and a branch that
/// switched the capability off keeps seeing a control it cannot use.
/// </para>
/// <para>
/// A capability that is off means the control is <em>absent</em>, not disabled. Rendering a greyed-out integration
/// import to an advisory branch tells them the platform has a feature they are being denied; not rendering it
/// tells them the truth, which is that it is not part of their work.
/// </para>
/// </remarks>
public static class NodeCapabilities
{
    public const string Integrations = "integrations";
    public const string ShiftScheduling = "shift_scheduling";
    public const string WorkOrderPool = "work_order_pool";
    public const string TaskProgress = "task_progress";
    public const string Kudos = "kudos";
    public const string Budget = "budget";
    public const string Strategy = "strategy";

    /// <summary>
    /// Every capability, with the value that applies where no profile in the ancestry mentions it.
    /// </summary>
    /// <remarks>
    /// Defaults are permissive because this slice must not silently remove anything from a deployment that has not
    /// authored a single profile yet. A branch loses a control by an administrator deciding so, never by the
    /// platform shipping a stricter default under them.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, bool> Defaults = new Dictionary<string, bool>(
        StringComparer.OrdinalIgnoreCase)
    {
        [Integrations] = true,
        [ShiftScheduling] = true,
        [WorkOrderPool] = true,
        [TaskProgress] = true,
        [Kudos] = true,
        [Budget] = true,
        [Strategy] = true,
    };

    public static IReadOnlyList<string> All => [.. Defaults.Keys];

    public static bool IsKnown(string code) => Defaults.ContainsKey(code);

    /// <summary>
    /// Reads a stored capabilities blob into the full set, unknown keys dropped and missing keys defaulted.
    /// </summary>
    /// <remarks>
    /// Malformed JSON resolves to the defaults rather than throwing, for the same reason the activity taxonomy
    /// does: one bad edit in a profile should degrade a refinement, not take a branch's whole UI down.
    /// </remarks>
    public static IReadOnlyDictionary<string, bool> Resolve(string? capabilitiesJson)
    {
        var resolved = new Dictionary<string, bool>(Defaults, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(capabilitiesJson))
        {
            return resolved;
        }

        try
        {
            using var document = JsonDocument.Parse(capabilitiesJson);

            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return resolved;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (IsKnown(property.Name) && property.Value.ValueKind
                    is JsonValueKind.True or JsonValueKind.False)
                {
                    resolved[property.Name] = property.Value.GetBoolean();
                }
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, bool>(Defaults, StringComparer.OrdinalIgnoreCase);
        }

        return resolved;
    }
}
