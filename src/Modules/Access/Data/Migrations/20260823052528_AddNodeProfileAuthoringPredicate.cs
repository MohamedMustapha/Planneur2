using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Access.Data.Migrations
{
    /// <summary>
    /// The predicate that gates authoring and attaching node profiles (v2 §10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It lives in Access rather than in Directory's migration for the reason the architecture test enforces: a
    /// policy that named <c>pmo</c> directly would be a second implementation of "who administers configuration",
    /// and the one written inside a module migration is the one nobody re-reads when the matrix changes.
    /// </para>
    /// <para>
    /// Two predicates rather than one, because they answer different questions. A profile row is global — it is
    /// shared vocabulary, and editing it changes behaviour for every branch pointing at it, so only the PMO and
    /// system may write one. Attaching an existing profile to a node is a decision about that node, so a head may
    /// do it for a department they own. v2 §08 will revisit both when per-bureau admin arrives; until then this is
    /// deliberately the narrower reading.
    /// </para>
    /// </remarks>
    public partial class AddNodeProfileAuthoringPredicate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                create or replace function access.can_author_node_profile()
                    returns boolean language sql stable as $fn$
                    select coalesce(access.is_system() or access.has('pmo'), false)
                    $fn$;

                create or replace function access.can_attach_node_profile(p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select access.can_write_department_config(p_dept)
                    $fn$;
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                drop function if exists access.can_attach_node_profile(uuid);
                drop function if exists access.can_author_node_profile();
                """);
    }
}
