using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Cracra.BuildingBlocks.Persistence.Naming;

/// <summary>
/// Renames every table, column, key and index to snake_case. Postgres folds unquoted identifiers to lower case, so
/// leaving PascalCase names in place means every hand-written statement — RLS policies included — has to quote
/// them. Since the policies are raw SQL by design, snake_case is the difference between readable and unreadable.
/// </summary>
public static class SnakeCaseNaming
{
    public static ModelBuilder UseSnakeCaseNames(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } tableName)
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                if (key.GetName() is { } keyName)
                {
                    key.SetName(ToSnakeCase(keyName));
                }
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                if (foreignKey.GetConstraintName() is { } constraintName)
                {
                    foreignKey.SetConstraintName(ToSnakeCase(constraintName));
                }
            }

            foreach (var index in entity.GetIndexes())
            {
                if (index.GetDatabaseName() is { } indexName)
                {
                    index.SetDatabaseName(ToSnakeCase(indexName));
                }
            }
        }

        return modelBuilder;
    }

    /// <summary>
    /// "OccurredAt" -&gt; "occurred_at", "ProjectID" -&gt; "project_id", "ix_Outbox" -&gt; "ix_outbox".
    /// Runs digits together with the preceding word so "Iso8601Code" is "iso8601_code", not "iso_8601_code".
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (char.IsUpper(current))
            {
                var previous = i > 0 ? name[i - 1] : '\0';
                var next = i + 1 < name.Length ? name[i + 1] : '\0';

                var startsWord = i > 0
                                 && previous != '_'
                                 && (!char.IsUpper(previous) || (char.IsUpper(previous) && char.IsLower(next)));

                if (startsWord)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
