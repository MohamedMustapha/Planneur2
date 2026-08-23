using System.Reflection;
using System.Text.RegularExpressions;
using Cracra.Modules.Directory.Contracts;

namespace Cracra.Tests.Architecture;

/// <summary>
/// Rules that keep node profiles data rather than semantics (v2 §10.6).
/// </summary>
/// <remarks>
/// The whole slice is one claim: a deployment's organizational vocabulary lives in rows, not in code. That claim
/// is undone by a single well-meaning <c>if (profile.SourceCode == "DELIVERY")</c> — which will work, pass review,
/// and quietly reintroduce the hardcoded semantics the previous archetype design was rewritten to remove. Nothing
/// but a rule catches it, because the code that violates it looks entirely reasonable in isolation.
/// </remarks>
public sealed partial class NodeProfileRules
{
    /// <summary>
    /// The seeded profile codes. Naming any of them outside the seed is the violation.
    /// </summary>
    /// <remarks>
    /// Checking against the shipped examples rather than against all possible codes is the practical form of the
    /// rule: a deployment's own codes are unknowable here, but the shipped ones are exactly what a developer
    /// reaches for when they want to special-case "the IT one", so they are the codes worth guarding.
    /// </remarks>
    private static readonly string[] SeededCodes = ["DELIVERY", "DISPATCH", "ADVISORY"];

    [Fact]
    public void No_code_branches_on_a_profile_code()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            // Two exemptions, both for code that *assigns* a profile rather than reacting to one.
            //
            // Migrations: the seed has to name the rows it inserts.
            //
            // DevSeed: the dev box decides which of its make-believe units runs which profile, which is a
            // deployment's own choice expressed in code because the deployment happens to be fake. It reads a
            // code to look a row up; it never asks what the code means. That distinction is the whole rule, and
            // it is why the exemption is by directory rather than by a suppression somebody could sprinkle.
            if (file.Contains("Migrations", StringComparison.Ordinal)
                || file.Contains("DevSeed", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var line in File.ReadLines(file))
            {
                if (SeededCodes.Any(code => line.Contains($"\"{code}\"", StringComparison.Ordinal)))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {line.Trim()}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "A profile's code is a label for administrators, never a branch. Behaviour belongs in the profile's "
            + "fields, which every branch already reads:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Nothing_switches_on_the_resolved_profiles_source_code()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var source = File.ReadAllText(file);

            foreach (Match match in SourceCodeComparison().Matches(source))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value.Trim()}");
            }
        }

        offenders.ShouldBeEmpty(
            "SourceCode is for display and support only. Comparing it is how profiles stop being data:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The client's capability map must match the server's, exactly.
    /// </summary>
    /// <remarks>
    /// This is the rule behind v2 §10.6's "every UI control that a capability can hide is registered in one
    /// capability map". The map is the registry; the failure it prevents is drift — a capability added on the
    /// server that no screen ever consults reads as "nothing can hide this yet", and one named only on the client
    /// silently never turns off. Both are invisible until someone switches the capability and nothing happens.
    /// </remarks>
    [Fact]
    public void The_client_and_the_server_agree_on_the_capability_registry()
    {
        var clientCodes = CapabilityLiteral()
            .Matches(CapabilityRegistrySource())
            .Select(match => match.Groups["code"].Value)
            .ToHashSet(StringComparer.Ordinal);

        clientCodes.ShouldBe(
            NodeCapabilities.All.ToHashSet(StringComparer.Ordinal),
            "NODE_CAPABILITIES in directory.models.ts and NodeCapabilities on the server are one registry in two "
            + "languages. A capability in only one of them can never actually hide anything.");
    }

    /// <summary>
    /// Every capability the navigation names must be a registered one.
    /// </summary>
    /// <remarks>
    /// A nav entry gated on a capability the server never sends is an entry that hides for nobody — the store
    /// defaults an unmentioned capability to on. It is the exact shape of bug this whole registry exists to make
    /// impossible, so it gets asserted rather than trusted to review.
    /// </remarks>
    [Fact]
    public void Every_navigation_capability_is_registered()
    {
        var navigation = File.ReadAllText(WebFile("core/navigation/navigation.ts"));

        var used = NavigationCapability()
            .Matches(navigation)
            .Select(match => match.Groups["name"].Value)
            .ToArray();

        // A rule that passes because it found nothing is worse than no rule: this slice's whole point on the
        // client is that at least one entry hides.
        used.ShouldNotBeEmpty("No nav entry is gated on a capability, so this rule would pass vacuously.");

        var known = CapabilityLiteral()
            .Matches(CapabilityRegistrySource())
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in used)
        {
            known.ShouldContain(name, $"NAVIGATION gates an entry on NODE_CAPABILITIES.{name}, which is not registered.");
        }
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = RepositoryRoot();

        var files = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        files.ShouldNotBeEmpty("No source files were found; these rules would pass vacuously.");

        return files;
    }

    /// <summary>
    /// Just the NODE_CAPABILITIES block.
    /// </summary>
    /// <remarks>
    /// Scoped to the declaration rather than scanning the whole file, so an unrelated object literal added to
    /// directory.models.ts later cannot start registering phantom capabilities and turn this rule into noise.
    /// </remarks>
    private static string CapabilityRegistrySource()
    {
        var source = File.ReadAllText(WebFile("core/directory/directory.models.ts"));

        var start = source.IndexOf("NODE_CAPABILITIES = {", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, "NODE_CAPABILITIES is missing from directory.models.ts.");

        var end = source.IndexOf("} as const;", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(-1, "NODE_CAPABILITIES is not closed with `} as const;`.");

        return source[start..end];
    }

    private static string WebFile(string relativePath) =>
        Path.Combine(RepositoryRoot(), "web", "src", "app", relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not locate the repository root from the test assembly.");

        return directory.FullName;
    }

    [GeneratedRegex(@"SourceCode\s*(==|!=|is\s+""|\.Equals\()")]
    private static partial Regex SourceCodeComparison();

    [GeneratedRegex(@"^\s*(?<name>[a-zA-Z]+):\s*'(?<code>[a-z_]+)',\s*$", RegexOptions.Multiline)]
    private static partial Regex CapabilityLiteral();

    [GeneratedRegex(@"NODE_CAPABILITIES\.(?<name>[a-zA-Z]+)")]
    private static partial Regex NavigationCapability();
}
