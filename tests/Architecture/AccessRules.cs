using System.Reflection;
using System.Text.RegularExpressions;

namespace Cracra.Tests.Architecture;

/// <summary>
/// Rules that keep authorization in one place.
/// </summary>
/// <remarks>
/// The visibility matrix is only trustworthy while it has a single implementation. Every one of these rules exists
/// because the tempting shortcut — a WHERE on role, a policy with its own role list, a service that filters in C#
/// — produces something that works, passes review, and then drifts from the matrix without anyone noticing.
/// </remarks>
public sealed partial class AccessRules
{
    /// <summary>
    /// Migration SQL that mentions a contextual role must go through an <c>access.*</c> predicate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two implementations of "is this person a department head" will eventually disagree, and the one in a
    /// module's migration is the one nobody re-reads when the matrix changes. The access schema is exempt: it is
    /// where the predicates are defined, so it necessarily names the roles.
    /// </para>
    /// <para>
    /// The system stamp is exempt too, and only in its exact form. A data migration writing into an RLS table has
    /// to claim a scope — the tables carry <c>force row level security</c>, so the connection is subject to its
    /// own policies — and the scope the write policies already name is <c>system</c>. Stamping the GUC is the
    /// opposite of rolling your own check: it hands the decision back to the policy instead of bypassing it. The
    /// match is the whole trimmed line rather than a substring, so nothing that also evaluates a role slips
    /// through on the same line.
    /// </para>
    /// </remarks>
    [Fact]
    public void Module_migrations_use_the_shared_predicates_rather_than_their_own_role_checks()
    {
        var offenders = new List<string>();

        foreach (var file in MigrationFiles().Where(path => !path.Contains("Modules\\Access", StringComparison.OrdinalIgnoreCase)
                                                            && !path.Contains("Modules/Access", StringComparison.Ordinal)))
        {
            var sql = File.ReadAllText(file);

            foreach (Match match in RoleLiteral().Matches(sql))
            {
                var line = LineContaining(sql, match.Index);

                // access.has('...') inside a predicate definition is the sanctioned form; what this catches is a
                // policy comparing a role literal directly, or a WHERE that rolls its own check.
                if (!line.Contains("access.has(", StringComparison.Ordinal)
                    && !line.Contains("create or replace function access.", StringComparison.Ordinal)
                    && !IsSystemStamp(line))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {line.Trim()}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Module migrations must attach policies to access.* predicates instead of naming roles directly:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Nothing on the administration surface claims the system scope (v2 §08.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// §08.2 is explicit: admin actions run under the actor's context, not <c>system</c>, so that RLS and the
    /// audit trail both apply. Both halves fail together the moment somebody reaches for
    /// <c>UserContext.SystemJob</c> to get past a policy that was refusing them — the write succeeds, and the
    /// trail records that nobody did it. That is not a bug anything else would catch: the feature works, the
    /// tests pass, and the record is quietly false.
    /// </para>
    /// <para>
    /// The surface is identified by what it does rather than by where it sits: a file that writes an admin audit
    /// line is performing an administrative act, whatever it is called. Background jobs stay free to claim the
    /// scope — they write no trail, and the exemption is the absence of one rather than a list of names to keep
    /// up to date.
    /// </para>
    /// </remarks>
    [Fact]
    public void Administrative_actions_run_under_the_caller_rather_than_the_system_scope()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var source = File.ReadAllText(file);

            if (!source.Contains("IAdminAudit", StringComparison.Ordinal)
                && !source.Contains("audit.RecordAsync(", StringComparison.Ordinal))
            {
                continue;
            }

            if (source.Contains("UserContext.SystemJob", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        offenders.ShouldBeEmpty(
            "Administrative actions must run under the caller, never the system scope:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The admin audit records whoever actually performed the act.
    /// </summary>
    /// <remarks>
    /// The companion to the rule above, and the half a source scan cannot infer: a writer that stamped a constant
    /// would satisfy "does not claim the system scope" while producing a trail with one name in it.
    /// </remarks>
    [Fact]
    public void The_admin_trail_stamps_the_person_who_performed_the_act()
    {
        var writer = SourceFiles().SingleOrDefault(
            path => Path.GetFileName(path).Equals("AdminAuditWriter.cs", StringComparison.Ordinal));

        writer.ShouldNotBeNull("The admin audit writer was not found; this rule would pass vacuously.");

        var source = File.ReadAllText(writer);

        source.ShouldContain(
            "IUserContext user",
            customMessage: "The audit writer must take the caller's context rather than deriving an actor.");

        source.ShouldContain(
            "user.UserId",
            customMessage: "The audit writer must stamp the caller as the actor.");
    }

    /// <summary>
    /// Every table a module creates must end up with row-level security enabled.
    /// </summary>
    /// <remarks>
    /// This is the rule that catches the most dangerous omission in the whole system. A table without RLS is not
    /// visibly broken — it returns rows, the feature works, the tests pass — it simply returns them to everyone.
    /// Nothing else in the build would notice.
    /// </remarks>
    [Fact]
    public void Every_module_table_has_row_level_security_enabled()
    {
        var offenders = new List<string>();

        foreach (var file in MigrationFiles())
        {
            var sql = File.ReadAllText(file);

            var created = CreateTableName().Matches(sql)
                .Select(match => match.Groups["table"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var table in created)
            {
                // The outbox is the platform's own, created by the shared base context in every module schema, and
                // its policies come from the platform migration rather than the module's.
                if (table.Equals("outbox_message", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var enabled = sql.Contains($"{table} enable row level security", StringComparison.OrdinalIgnoreCase)
                              || sql.Contains($"{table}\n", StringComparison.Ordinal) && sql.Contains(
                                  "enable row level security", StringComparison.OrdinalIgnoreCase)
                                  && Regex.IsMatch(sql, $@"{Regex.Escape(table)}\s+enable\s+row\s+level\s+security",
                                      RegexOptions.IgnoreCase);

                if (!enabled)
                {
                    offenders.Add($"{Path.GetFileName(file)}: {table}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Every table must enable row level security in the migration that creates it. Missing:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// A table with RLS enabled must also be FORCEd.
    /// </summary>
    /// <remarks>
    /// Postgres exempts a table's owner from its own policies unless FORCE is set. The runtime role is a non-owner
    /// precisely so a missed FORCE cannot open the data up — but migrations run as the owner, and anything that
    /// ever connects as it would read straight past every policy.
    /// </remarks>
    [Fact]
    public void Every_table_with_row_level_security_also_forces_it()
    {
        var offenders = new List<string>();

        foreach (var file in MigrationFiles())
        {
            var sql = File.ReadAllText(file);

            foreach (Match match in EnableRls().Matches(sql))
            {
                var table = match.Groups["table"].Value;

                if (!Regex.IsMatch(sql, $@"{Regex.Escape(table)}\s+force\s+row\s+level\s+security",
                        RegexOptions.IgnoreCase))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {table}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Tables with RLS must also FORCE it, or the owner role reads past every policy. Missing:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static string LineContaining(string text, int index)
    {
        var start = text.LastIndexOf('\n', Math.Min(index, text.Length - 1)) + 1;
        var end = text.IndexOf('\n', index);

        return end < 0 ? text[start..] : text[start..end];
    }

    /// <summary>
    /// Walks up from the test assembly to the repository root, then finds every migration.
    /// </summary>
    /// <remarks>
    /// These rules read SQL, which no reflection-based tool can see — the policies only exist as strings inside
    /// migration files, which is exactly why they need a rule of their own.
    /// </remarks>
    private static IEnumerable<string> MigrationFiles()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains("Migrations", StringComparison.Ordinal))
            .Where(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(path => !path.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .ToArray();

        // A silently empty file list would make all three rules pass while checking nothing.
        files.ShouldNotBeEmpty("No migration files were found; these rules would pass vacuously.");

        return files;
    }

    /// <summary>Every hand-written source file under <c>src</c> — generated and build output excluded.</summary>
    private static IEnumerable<string> SourceFiles()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains("Migrations", StringComparison.Ordinal))
            .ToArray();

        files.ShouldNotBeEmpty("No source files were found; these rules would pass vacuously.");

        return files;
    }

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

    /// <summary>The one sanctioned way a migration may name a role: claiming the system scope for its own write.</summary>
    private static bool IsSystemStamp(string line) =>
        line.Trim().Equals("set local app.roles = 'system';", StringComparison.Ordinal);

    [GeneratedRegex(@"'(member|unit-head|dept-head|project-lead|po|pmo|system)'")]
    private static partial Regex RoleLiteral();

    [GeneratedRegex(@"CreateTable\(\s*name:\s*""(?<table>[a-z_]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex CreateTableName();

    [GeneratedRegex(@"(?<table>[a-z_.]+)\s+enable\s+row\s+level\s+security", RegexOptions.IgnoreCase)]
    private static partial Regex EnableRls();
}
