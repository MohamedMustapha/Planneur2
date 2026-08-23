using System.Reflection;
using System.Text.RegularExpressions;
using Cracra.BuildingBlocks.Web.Users;

namespace Cracra.Tests.Architecture;

/// <summary>
/// The invariants that keep the org model generic (v2 §01).
/// </summary>
/// <remarks>
/// <para>
/// §01's central claim is that the platform does not know what its levels are called or how many there are. That
/// claim is easy to state and easy to lose: one <c>access.depts()</c> in a new policy, one extra role named after
/// a rung of the ladder, and depth is hardcoded again in the one place nobody re-reads.
/// </para>
/// <para>
/// These rules deliberately do not forbid the words department and unit outright. The legacy tables are still
/// there, by design, for the shim release §09 Phase 4 ends with — so a blanket ban would be a rule that has to be
/// suppressed to pass, which is worse than no rule. What is checked instead is the v2 surface: the predicates and
/// policies written against the tree, and the role vocabulary the whole system compares against.
/// </para>
/// </remarks>
public sealed partial class OrgTreeRules
{
    /// <summary>
    /// A migration that swaps a module onto the tree must not still be reading the level-scoped session.
    /// </summary>
    /// <remarks>
    /// The failure this catches is a half-translated policy: one clause moved to <c>in_my_subtree</c> and another
    /// left on <c>access.depts()</c>. Both compile, both run, and the row set is wrong only for the people whose
    /// branch does not happen to be shaped like the one the author had in mind.
    /// </remarks>
    [Fact]
    public void The_node_swap_migrations_do_not_read_the_level_scoped_session()
    {
        var offenders = new List<string>();

        foreach (var file in SwapMigrations())
        {
            var up = UpBlock(File.ReadAllText(file));

            foreach (Match match in LegacyScope().Matches(up))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        offenders.ShouldBeEmpty(
            "A migration that moves a module onto the org tree must scope on the node, not on a level:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>The same rule for the role names those migrations may compare against.</summary>
    [Fact]
    public void The_node_swap_migrations_do_not_name_a_level_scoped_role()
    {
        var offenders = new List<string>();

        foreach (var file in SwapMigrations())
        {
            var up = UpBlock(File.ReadAllText(file));

            foreach (Match match in LegacyRole().Matches(up))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        offenders.ShouldBeEmpty(
            "unit-head and dept-head are one role now. Use node-head, or better, in_my_subtree:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// "Any head" must mean any head, at any depth.
    /// </summary>
    /// <remarks>
    /// <see cref="ContextualRole.Heads"/> feeds the endpoint policies. A level-named role reappearing in it would
    /// close a door on a head at some other depth, and the symptom — a 403 for one kind of head only — is the sort
    /// of thing that gets reported months later as "it does not work for us".
    /// </remarks>
    [Fact]
    public void The_head_vocabulary_does_not_name_a_level()
    {
        ContextualRole.Heads.ShouldNotContain(ContextualRole.UnitHead);
        ContextualRole.Heads.ShouldNotContain(ContextualRole.DepartmentHead);
        ContextualRole.Heads.ShouldContain(ContextualRole.NodeHead);
    }

    /// <summary>
    /// Depth is data.
    /// </summary>
    /// <remarks>
    /// The org level table caps at eight and is seeded with four, and §01 says the code must read the maximum
    /// rather than know it. This checks the cheap half of that: nothing compares a level number to a literal.
    /// </remarks>
    [Fact]
    public void No_code_compares_a_level_to_a_literal_depth()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            foreach (Match match in HardcodedDepth().Matches(File.ReadAllText(file)))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value.Trim()}");
            }
        }

        offenders.ShouldBeEmpty(
            "Depth is configuration. Read max(level_no) rather than comparing against a literal:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Everything up to the Down override, which legitimately restores the pre-v2 shape.</summary>
    private static string UpBlock(string source)
    {
        var down = source.IndexOf("protected override void Down", StringComparison.Ordinal);

        return down < 0 ? source : source[..down];
    }

    private static IEnumerable<string> SwapMigrations()
    {
        var files = SourceFiles()
            .Where(path => Path.GetFileName(path).Contains("SwapToNode", StringComparison.Ordinal))
            .ToArray();

        files.ShouldNotBeEmpty("No swap migrations were found; these rules would pass vacuously.");

        return files;
    }

    private static IEnumerable<string> SourceFiles()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not locate the repository root from the test assembly.");

        return Directory
            .EnumerateFiles(Path.Combine(directory.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(path => !path.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"access\.(depts|unit)\(\)")]
    private static partial Regex LegacyScope();

    [GeneratedRegex(@"'(unit-head|dept-head)'")]
    private static partial Regex LegacyRole();

    [GeneratedRegex(@"[Ll]evel_?[Nn]o\s*(==|<=|>=|<|>)\s*\d")]
    private static partial Regex HardcodedDepth();
}
