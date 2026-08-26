using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <summary>
    /// v2 §08.1–§08.4: a head grants roles strictly beneath their own node, and nowhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until now <c>override_write</c> asked only <c>access.is_head()</c>, which answers "does this session head
    /// anything at all". That is a door with no lock behind it: a unit-head could write themselves a
    /// <c>node-head</c> grant scoped to the root, and the enricher turns a granted head role into an entry in
    /// <c>app.headed_nodes</c> on the very next request. One row, and the whole tree is readable. The matrix
    /// exists to make that impossible, so the predicate has to answer the question the matrix asks: not "are you
    /// a head" but "is what you are granting inside the branch you head".
    /// </para>
    /// <para>
    /// Three clauses, each from a different sentence of §08. A head may not grant <c>pmo</c>, <c>admin</c> or
    /// <c>system</c>, because none of them stop at a branch — granting a role that outranks the granter is the
    /// escalation in its plainest form. A head may not grant globally, for the same reason. And the scope must sit
    /// *strictly* below one of their own nodes: the ancestor list of a node ends with the node itself, so the
    /// overlap alone would hand a head their own seat, which is §08.1's "a head can never grant at or above their
    /// own node" — the same exclusion <c>can_write_org_node</c> already makes, made once more where granting
    /// happens rather than restated in the service.
    /// </para>
    /// <para>
    /// The target person is checked too. Granting somebody outside your branch a role inside it is a way of
    /// handing your rows to a stranger, and the audit line would name you as having done it deliberately.
    /// </para>
    /// <para>
    /// It lives in Directory rather than Access even though it rewrites an Access policy, because the body reads
    /// <c>directory.org_node</c> and <c>directory.person</c>. Postgres parses a SQL function body at creation, the
    /// Access schema migrates first, and on an empty database those tables do not exist yet — the same reason the
    /// audit trail was attached to the tree from here.
    /// </para>
    /// </remarks>
    public partial class LetAHeadGrantOnlyBeneathThemselves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Strictly beneath a node I head. Deliberately false for a node I head myself, and false for a
                -- node id that names nothing, so a scope that has gone missing refuses rather than admits.
                create or replace function access.below_my_nodes(p_node uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce((
                        select n.ancestor_ids && access.headed_nodes()
                           and not n.id = any(access.headed_nodes())
                          from directory.org_node n
                         where n.id = p_node
                    ), false)
                    $fn$;

                create or replace function access.can_grant_override(
                    p_person uuid, p_role text, p_scope_type text, p_scope_id uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.has('admin')
                        or (
                               access.is_head()
                           and not access.outranks_a_branch(p_role)
                           and p_scope_id is not null
                           and case p_scope_type
                                   -- A project the granter's branch actually runs, which is the same question
                                   -- every project-scoped predicate already asks.
                                   when 'Project' then access.project_in_my_subtree(p_scope_id)
                                   when 'Global'  then false
                                   else access.below_my_nodes(p_scope_id)
                               end
                           and exists (
                               select 1 from directory.person p
                                where p.id = p_person
                                  and access.in_my_subtree(p.node_ancestor_ids))
                        )
                    , false)
                    $fn$;

                -- Reading follows the same boundary (§08.4: a head reads their own node's admin rows). Your own
                -- overrides stay readable whoever you are, because "why can I not do this" has to be answerable
                -- by the person it happened to.
                drop policy if exists override_read on access.rbac_override;
                create policy override_read on access.rbac_override
                    for select using (
                        person_id = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or access.has('admin')
                        or exists (
                            select 1 from directory.person p
                             where p.id = person_id
                               and access.in_my_subtree(p.node_ancestor_ids))
                    );

                drop policy if exists override_audit_read on access.rbac_override_audit;
                create policy override_audit_read on access.rbac_override_audit
                    for select using (
                        person_id = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or access.has('admin')
                        or exists (
                            select 1 from directory.person p
                             where p.id = person_id
                               and access.in_my_subtree(p.node_ancestor_ids))
                    );

                drop policy if exists override_write on access.rbac_override;
                create policy override_write on access.rbac_override
                    for all using (access.can_grant_override(person_id, role, scope_type, scope_id))
                    with check (access.can_grant_override(person_id, role, scope_type, scope_id));

                -- The trail is guarded by the person it is about rather than by re-deriving the grant: the write
                -- it accompanies has already been through the check above, and asking the same question twice
                -- would tie the trail's policy to the order EF happens to insert two rows in.
                drop policy if exists override_audit_append on access.rbac_override_audit;
                create policy override_audit_append on access.rbac_override_audit
                    for insert with check (
                        access.is_system() or access.has('pmo') or access.has('admin')
                        or exists (
                            select 1 from directory.person p
                             where p.id = person_id
                               and access.in_my_subtree(p.node_ancestor_ids))
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy if exists override_audit_append on access.rbac_override_audit;
                create policy override_audit_append on access.rbac_override_audit
                    for insert with check (
                        access.is_system() or access.has('pmo') or access.is_head()
                    );

                drop policy if exists override_write on access.rbac_override;
                create policy override_write on access.rbac_override
                    for all using (access.is_system() or access.has('pmo') or access.is_head())
                    with check (access.is_system() or access.has('pmo') or access.is_head());

                drop policy if exists override_audit_read on access.rbac_override_audit;
                create policy override_audit_read on access.rbac_override_audit
                    for select using (
                        person_id = access.uid() or access.is_system() or access.has('pmo') or access.is_head()
                    );

                drop policy if exists override_read on access.rbac_override;
                create policy override_read on access.rbac_override
                    for select using (
                        person_id = access.uid() or access.is_system() or access.has('pmo') or access.is_head()
                    );

                drop function if exists access.can_grant_override(uuid, text, text, uuid);
                drop function if exists access.below_my_nodes(uuid);
                """);
        }
    }
}
