using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SwapToNodePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy project_read on projects.project;
                create policy project_read on projects.project
                    for select using (
                        access.can_read_project(id, node_ancestor_ids)
                        or owner_person_id = access.uid()
                    );

                drop policy project_write on projects.project;
                create policy project_write on projects.project
                    for all using (
                        access.is_system()
                        or access.has('pmo')
                        or owner_person_id = access.uid()
                        or access.in_my_subtree(node_ancestor_ids)
                        or access.project_in_my_subtree(id)
                    )
                    with check (
                        access.is_system()
                        or access.has('pmo')
                        or owner_person_id = access.uid()
                        or access.in_my_subtree(node_ancestor_ids)
                        or access.project_in_my_subtree(id)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy project_write on projects.project;
                create policy project_write on projects.project
                    for all using (
                        access.is_system()
                        or access.has('pmo')
                        or owner_person_id = access.uid()
                        or (access.has('dept-head') and lead_department_id = any(access.depts()))
                        or (access.has('dept-head') and access.project_in_my_depts(id))
                    )
                    with check (
                        access.is_system()
                        or access.has('pmo')
                        or owner_person_id = access.uid()
                        or (access.has('dept-head') and lead_department_id = any(access.depts()))
                        or (access.has('dept-head') and access.project_in_my_depts(id))
                    );

                drop policy project_read on projects.project;
                create policy project_read on projects.project
                    for select using (
                        access.can_read_project(id, lead_department_id)
                        or owner_person_id = access.uid()
                    );
                """);
        }
    }
}
