using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <summary>
    /// v2 §08.1: an administrator corrects where the directory says somebody sits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The correction is a column of its own rather than a write to the derived one, so the sync can tell what
    /// LDAP says from what somebody decided. Without that distinction the next sync either undoes the fix or
    /// forgets the value it was correcting, and both are discovered weeks later by the person who was moved.
    /// </para>
    /// <para>
    /// The predicate is not <c>can_write_org_node</c>. That one refuses a head their own node, which is right for
    /// reshaping the tree and wrong for the people on it: a head administers the members of their own branch, and
    /// most of a branch's members sit exactly there. Both ends are checked — the branch somebody leaves and the
    /// branch they arrive in — because moving a person into a branch you do not run is a way of reading it.
    /// </para>
    /// </remarks>
    public partial class LetAnAdminMoveSomebody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "home_node_override_id",
                schema: "directory",
                table: "person",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                create or replace function access.can_admin_person(p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.has('admin')
                        or access.in_my_subtree(p_ancestors)
                    , false)
                    $fn$;

                create policy person_admin_move on directory.person
                    for update using (access.can_admin_person(home_node_id, node_ancestor_ids))
                    with check (access.can_admin_person(home_node_id, node_ancestor_ids));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy if exists person_admin_move on directory.person;
                drop function if exists access.can_admin_person(uuid, uuid[]);
                """);

            migrationBuilder.DropColumn(
                name: "home_node_override_id",
                schema: "directory",
                table: "person");
        }
    }
}
