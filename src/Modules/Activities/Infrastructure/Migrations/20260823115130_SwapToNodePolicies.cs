using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Activities.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SwapToNodePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy activity_entry_read on activities.activity_entry;
                create policy activity_entry_read on activities.activity_entry
                    for select using (
                        access.can_read_activity(person_id, node_id, node_ancestor_ids, project_id)
                    );

                drop policy activity_entry_write on activities.activity_entry;
                create policy activity_entry_write on activities.activity_entry
                    for all using (
                        access.is_system()
                        or person_id = access.uid()
                        or access.has('pmo')
                        or access.in_my_subtree(node_ancestor_ids)
                        or access.leads_project(project_id)
                    )
                    with check (
                        access.is_system()
                        or person_id = access.uid()
                        or access.has('pmo')
                        or access.in_my_subtree(node_ancestor_ids)
                        or access.leads_project(project_id)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy activity_entry_write on activities.activity_entry;
                create policy activity_entry_write on activities.activity_entry
                    for all using (
                        access.is_system()
                        or person_id = access.uid()
                        or access.has('pmo')
                        or (access.has('unit-head') and unit_id = access.unit())
                        or (access.has('dept-head') and department_id = any(access.depts()))
                        or access.leads_project(project_id)
                    )
                    with check (
                        access.is_system()
                        or person_id = access.uid()
                        or access.has('pmo')
                        or (access.has('unit-head') and unit_id = access.unit())
                        or (access.has('dept-head') and department_id = any(access.depts()))
                        or access.leads_project(project_id)
                    );

                drop policy activity_entry_read on activities.activity_entry;
                create policy activity_entry_read on activities.activity_entry
                    for select using (
                        access.can_read_activity(person_id, unit_id, project_id, department_id)
                    );
                """);
        }
    }
}
