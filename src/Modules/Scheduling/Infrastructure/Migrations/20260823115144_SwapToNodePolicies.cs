using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SwapToNodePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy work_order_read on scheduling.work_order;
                create policy work_order_read on scheduling.work_order
                    for select using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or assigned_to_person_id = access.uid()
                            or access.same_node(node_id)
                            or access.in_my_subtree(node_ancestor_ids)
                        , false)
                    );

                drop policy work_order_write on scheduling.work_order;
                create policy work_order_write on scheduling.work_order
                    for all using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or access.in_my_subtree(node_ancestor_ids)
                            or access.leads_project(project_id)
                        , false)
                    )
                    with check (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or access.in_my_subtree(node_ancestor_ids)
                            or access.leads_project(project_id)
                        , false)
                    );

                drop policy shift_read on scheduling.shift;
                create policy shift_read on scheduling.shift
                    for select using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or person_id = access.uid()
                            or access.same_node(node_id)
                            or access.in_my_subtree(node_ancestor_ids)
                        , false)
                    );

                drop policy shift_write on scheduling.shift;
                create policy shift_write on scheduling.shift
                    for all using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or access.in_my_subtree(node_ancestor_ids)
                        , false)
                    )
                    with check (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or access.in_my_subtree(node_ancestor_ids)
                        , false)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy shift_write on scheduling.shift;
                create policy shift_write on scheduling.shift
                    for all using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or (access.has('unit-head') and unit_id = access.unit())
                            or (access.has('dept-head') and department_id = any(access.depts()))
                        , false)
                    )
                    with check (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or (access.has('unit-head') and unit_id = access.unit())
                            or (access.has('dept-head') and department_id = any(access.depts()))
                        , false)
                    );

                drop policy shift_read on scheduling.shift;
                create policy shift_read on scheduling.shift
                    for select using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or person_id = access.uid()
                            or unit_id = access.unit()
                            or (access.has('dept-head') and department_id = any(access.depts()))
                        , false)
                    );

                drop policy work_order_write on scheduling.work_order;
                create policy work_order_write on scheduling.work_order
                    for all using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or (access.has('unit-head') and unit_id = access.unit())
                            or (access.has('dept-head') and department_id = any(access.depts()))
                            or access.leads_project(project_id)
                        , false)
                    )
                    with check (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or (access.has('unit-head') and unit_id = access.unit())
                            or (access.has('dept-head') and department_id = any(access.depts()))
                            or access.leads_project(project_id)
                        , false)
                    );

                drop policy work_order_read on scheduling.work_order;
                create policy work_order_read on scheduling.work_order
                    for select using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or assigned_to_person_id = access.uid()
                            or unit_id = access.unit()
                            or (access.has('dept-head') and department_id = any(access.depts()))
                        , false)
                    );
                """);
        }
    }
}
