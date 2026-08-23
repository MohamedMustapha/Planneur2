using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Meetings.Data.Migrations
{
    /// <inheritdoc />
    public partial class SwapToNodePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The signature is unchanged so no policy has to move: a meeting's scope id is already a node id, and
            // the denormalized department the third argument carries stops being consulted.
            migrationBuilder.Sql("""
                create or replace function access.can_read_meeting(
                    p_scope_type text, p_scope_id uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or (p_scope_type = 'org' and access.is_scoped())
                        or (p_scope_type in ('unit', 'department')
                            and (p_scope_id = any(access.my_path()) or access.heads_node(p_scope_id)))
                        or (p_scope_type = 'project' and access.on_project(p_scope_id))
                        or (p_scope_type = 'project' and access.leads_project(p_scope_id))
                        or (p_scope_type = 'project' and access.project_in_my_subtree(p_scope_id))
                    , false)
                    $fn$;

                create or replace function access.can_write_meeting(
                    p_scope_type text, p_scope_id uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or (p_scope_type in ('unit', 'department') and access.heads_node(p_scope_id))
                        or (p_scope_type = 'project' and access.leads_project(p_scope_id))
                        or (p_scope_type = 'project' and access.project_in_my_subtree(p_scope_id))
                    , false)
                    $fn$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create or replace function access.can_read_meeting(
                    p_scope_type text, p_scope_id uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or (p_scope_type = 'org' and access.is_scoped())
                        or (p_scope_type = 'unit' and p_scope_id = access.unit())
                        or (p_scope_type = 'unit' and access.has('dept-head') and p_dept = any(access.depts()))
                        or (p_scope_type = 'department' and p_scope_id = any(access.depts()))
                        or (p_scope_type = 'project' and access.on_project(p_scope_id))
                        or (p_scope_type = 'project' and access.leads_project(p_scope_id))
                        or (p_scope_type = 'project' and access.has('dept-head') and access.project_in_my_depts(p_scope_id))
                        or (p_scope_type = 'project' and access.has('unit-head') and access.project_in_my_depts(p_scope_id))
                    , false)
                    $fn$;

                create or replace function access.can_write_meeting(
                    p_scope_type text, p_scope_id uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or (p_scope_type = 'unit' and access.has('unit-head') and p_scope_id = access.unit())
                        or (p_scope_type = 'unit' and access.has('dept-head') and p_dept = any(access.depts()))
                        or (p_scope_type = 'department' and access.has('dept-head') and p_scope_id = any(access.depts()))
                        or (p_scope_type = 'project' and access.leads_project(p_scope_id))
                        or (p_scope_type = 'project' and access.has('dept-head') and access.project_in_my_depts(p_scope_id))
                    , false)
                    $fn$;
                """);
        }
    }
}
