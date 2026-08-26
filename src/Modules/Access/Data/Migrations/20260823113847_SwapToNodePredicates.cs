using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Access.Data.Migrations
{
    /// <inheritdoc />
    public partial class SwapToNodePredicates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The columns, not the attachment. Access migrates before Directory, so the org tree the full
            // attachment needs does not exist yet — but the predicates below are validated at creation and cannot
            // reference a column that is missing. Directory's own migration finishes the job: backfill, NOT NULL,
            // index, trigger, registration.
            migrationBuilder.Sql("""
                alter table access.project_membership
                    add column if not exists node_id uuid,
                    add column if not exists node_ancestor_ids uuid[] default '{}'::uuid[];
                """);

            migrationBuilder.Sql("""
                create or replace function access.project_in_my_subtree(p_project uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce((
                        select true from access.project_membership m
                        where m.project_id = p_project
                          and m.node_ancestor_ids && access.headed_nodes()
                        limit 1
                    ), false)
                    $fn$;

                create or replace function access.can_read_activity(
                    p_owner uuid, p_node uuid, p_ancestors uuid[], p_project uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_owner = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or access.same_node(p_node)
                        or access.in_my_subtree(p_ancestors)
                        or access.project_in_my_subtree(p_project)
                        or access.leads_project(p_project)
                    , false)
                    $fn$;

                create or replace function access.project_in_my_branch(p_project uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce((
                        select true from access.project_membership m
                        where m.project_id = p_project
                          and access.in_my_branch(m.node_ancestor_ids)
                        limit 1
                    ), false)
                    $fn$;

                create or replace function access.can_read_project(p_project uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.on_project(p_project)
                        or access.in_my_subtree(p_ancestors)
                        or access.project_in_my_subtree(p_project)
                        -- Knowledge flow, v1's one deliberate widening: a head reads a project their own branch
                        -- contributes to, even when the project is sponsored above or beside them. Read only, and
                        -- gated on being a head, exactly as it was.
                        or (access.is_head() and access.project_in_my_branch(p_project))
                    , false)
                    $fn$;

                create or replace function access.can_read_kudo(
                    p_from uuid, p_to uuid, p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_from = access.uid()
                        or p_to = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or access.same_node(p_node)
                        or access.in_my_subtree(p_ancestors)
                    , false)
                    $fn$;

                create or replace function access.can_read_schedule_row(
                    p_assignee uuid, p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_assignee = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or access.same_node(p_node)
                        or access.in_my_subtree(p_ancestors)
                    , false)
                    $fn$;

                create or replace function access.can_write_in_node(p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.in_my_subtree(p_ancestors)
                    , false)
                    $fn$;

                create or replace function access.can_read_person(
                    p_person uuid, p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_person = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or access.same_node(p_node)
                        or access.in_my_branch(p_ancestors)
                        or access.in_my_subtree(p_ancestors)
                    , false)
                    $fn$;

                create or replace function access.can_read_node(p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.in_my_branch(p_ancestors)
                        or access.in_my_subtree(p_ancestors)
                    , false)
                    $fn$;

                create or replace function access.can_write_node_config(p_node uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or p_node = any(access.headed_nodes())
                    , false)
                    $fn$;
                """);

            migrationBuilder.Sql("""
                drop policy override_write on access.rbac_override;
                create policy override_write on access.rbac_override
                    for all using (access.is_system() or access.has('pmo') or access.is_head())
                    with check (access.is_system() or access.has('pmo') or access.is_head());

                drop policy override_audit_append on access.rbac_override_audit;
                create policy override_audit_append on access.rbac_override_audit
                    for insert with check (
                        access.is_system() or access.has('pmo') or access.is_head()
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy override_audit_append on access.rbac_override_audit;
                create policy override_audit_append on access.rbac_override_audit
                    for insert with check (
                        access.is_system() or access.has('pmo') or access.has('dept-head')
                    );

                drop policy override_write on access.rbac_override;
                create policy override_write on access.rbac_override
                    for all using (access.is_system() or access.has('pmo') or access.has('dept-head'))
                    with check (access.is_system() or access.has('pmo') or access.has('dept-head'));

                drop function if exists access.can_write_node_config(uuid);
                drop function if exists access.can_read_node(uuid, uuid[]);
                drop function if exists access.can_read_person(uuid, uuid, uuid[]);
                drop function if exists access.can_write_in_node(uuid, uuid[]);
                drop function if exists access.can_read_schedule_row(uuid, uuid, uuid[]);
                drop function if exists access.can_read_kudo(uuid, uuid, uuid, uuid[]);
                drop function if exists access.can_read_project(uuid, uuid[]);
                drop function if exists access.can_read_activity(uuid, uuid, uuid[], uuid);
                drop function if exists access.project_in_my_branch(uuid);
                drop function if exists access.project_in_my_subtree(uuid);
                """);
        }
    }
}
