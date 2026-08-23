using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Reporting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SwapToNodePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create or replace function access.can_read_report(
                    p_scope text, p_scope_id uuid, p_owner uuid, p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or p_owner = access.uid()
                        or access.has('pmo')
                        or (p_scope in ('unit', 'department') and access.heads_node(p_scope_id))
                        or (p_scope = 'project' and access.leads_project(p_scope_id))
                        or (p_scope = 'project' and access.project_in_my_subtree(p_scope_id))
                    , false)
                    $fn$;

                drop policy generated_summary_read on reporting.generated_summary;
                create policy generated_summary_read on reporting.generated_summary
                    for select using (
                        access.can_read_report(scope, scope_id, owner_person_id, node_id, node_ancestor_ids)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy generated_summary_read on reporting.generated_summary;
                create policy generated_summary_read on reporting.generated_summary
                    for select using (
                        access.can_read_report(scope, scope_id, owner_person_id, unit_id, department_id)
                    );

                drop function if exists access.can_read_report(text, uuid, uuid, uuid, uuid[]);
                """);
        }
    }
}
