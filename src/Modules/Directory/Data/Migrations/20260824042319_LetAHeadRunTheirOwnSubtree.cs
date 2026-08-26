using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <summary>
    /// v2 §08.1: a node-head administers their own branch and everything beneath it, and reads its trail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The predicate gains the row's ancestry, which is what lets it answer "beneath me" without knowing how deep
    /// anything sits. A node's ancestor list ends with the node itself, so the overlap alone would also hand a
    /// head their own row; excluding it here is §08.2's rule that a head reshapes strictly below their own node,
    /// stated once in the place that is authoritative rather than restated in every caller.
    /// </para>
    /// <para>
    /// The audit trail is attached here rather than in the Access migration that creates it, because attaching to
    /// the tree reads <c>directory.org_node</c> and the Access schema is migrated first — on an empty database
    /// that table does not exist yet. Same reason, same shape as the people attachment before it.
    /// </para>
    /// </remarks>
    public partial class LetAHeadRunTheirOwnSubtree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy if exists org_node_write on directory.org_node;

                create or replace function access.can_write_org_node(p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.has('admin')
                        or (access.in_my_subtree(p_ancestors)
                            and not p_node = any(access.headed_nodes()))
                    , false)
                    $fn$;

                create policy org_node_write on directory.org_node
                    for all using (access.can_write_org_node(id, ancestor_ids))
                    with check (access.can_write_org_node(id, ancestor_ids));

                -- The trail hangs off the tree like every other scoped table, so the row carries the ancestry
                -- of the node the act landed on and the predicate is one array overlap. Not required: an act with
                -- no node is org-wide.
                select access.attach_to_node_tree('access', 'admin_audit', 'node_id', null, false);

                -- A head reads the trail for their own branch and everything beneath it; the PMO and a global
                -- admin read all of it. An org-wide act is theirs alone -- granting PMO is not a fact a bureau
                -- head is owed.
                create or replace function access.can_read_audit(p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.has('admin')
                        or (p_node is not null and access.in_my_subtree(p_ancestors))
                    , false)
                    $fn$;

                create policy admin_audit_read on access.admin_audit
                    for select using (access.can_read_audit(node_id, node_ancestor_ids));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy if exists admin_audit_read on access.admin_audit;
                drop function if exists access.can_read_audit(uuid, uuid[]);
                select access.detach_from_node_tree('access', 'admin_audit');

                drop policy if exists org_node_write on directory.org_node;
                drop function if exists access.can_write_org_node(uuid, uuid[]);

                create policy org_node_write on directory.org_node
                    for all using (access.can_write_org_node(id))
                    with check (access.can_write_org_node(id));
                """);
        }
    }
}
