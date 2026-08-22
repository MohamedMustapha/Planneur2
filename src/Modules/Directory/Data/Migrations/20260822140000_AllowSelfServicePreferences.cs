using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowSelfServicePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            // Until this policy existed, `directory.person` could only be written by the sync job: every other
            // update matched no row, so PUT /directory/me/preferences saved nothing and came back a 500. The
            // preference columns are the person's own answer, and there has to be a way for them to give it.
            //
            // Scoped to the caller's own row, which is the whole permission — there is no id in the route or the
            // body, so there is nothing wider to grant. RLS is row-scoped rather than column-scoped, so this does
            // technically open the rest of the row to its owner; the only write path in the module sets the four
            // preference columns, and the sync rewrites everything it owns on its next run regardless.
            migrationBuilder.Sql(
                """
                create policy person_self_preferences on directory.person
                    for update using (id = access.uid()) with check (id = access.uid());
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("drop policy if exists person_self_preferences on directory.person;");
    }
}
