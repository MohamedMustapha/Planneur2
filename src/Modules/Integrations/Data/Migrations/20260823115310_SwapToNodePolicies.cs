using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Integrations.Data.Migrations
{
    /// <inheritdoc />
    public partial class SwapToNodePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create or replace function access.can_read_external_item(
                    p_project uuid, p_node uuid, p_ancestors uuid[], p_assignee uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_assignee = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or access.same_node(p_node)
                        or access.in_my_subtree(p_ancestors)
                        or (p_project is not null and access.on_project(p_project))
                        or access.leads_project(p_project)
                        or access.project_in_my_subtree(p_project)
                    , false)
                    $fn$;
                """);

            migrationBuilder.Sql("""
                drop policy external_connection_read on integrations.external_connection;
                create policy external_connection_read on integrations.external_connection
                    for select using (access.can_write_node_config(node_id));
                drop policy external_connection_write on integrations.external_connection;
                create policy external_connection_write on integrations.external_connection
                    for all using (access.can_write_node_config(node_id))
                        with check (access.can_write_node_config(node_id));

                drop policy external_mapping_read on integrations.external_mapping;
                create policy external_mapping_read on integrations.external_mapping
                    for select using (access.can_write_node_config(department_id));
                drop policy external_mapping_write on integrations.external_mapping;
                create policy external_mapping_write on integrations.external_mapping
                    for all using (access.can_write_node_config(department_id))
                        with check (access.can_write_node_config(department_id));

                drop policy external_work_item_read on integrations.external_work_item;
                create policy external_work_item_read on integrations.external_work_item
                    for select using (
                        access.can_read_external_item(
                            project_id, node_id, node_ancestor_ids, assigned_person_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy external_work_item_read on integrations.external_work_item;
                create policy external_work_item_read on integrations.external_work_item
                    for select using (
                        access.can_read_external_item(project_id, unit_id, department_id, assigned_person_id));

                drop policy external_mapping_write on integrations.external_mapping;
                create policy external_mapping_write on integrations.external_mapping
                    for all using (access.can_write_department_config(department_id))
                        with check (access.can_write_department_config(department_id));
                drop policy external_mapping_read on integrations.external_mapping;
                create policy external_mapping_read on integrations.external_mapping
                    for select using (access.can_write_department_config(department_id));

                drop policy external_connection_write on integrations.external_connection;
                create policy external_connection_write on integrations.external_connection
                    for all using (access.can_write_department_config(department_id))
                        with check (access.can_write_department_config(department_id));
                drop policy external_connection_read on integrations.external_connection;
                create policy external_connection_read on integrations.external_connection
                    for select using (access.can_write_department_config(department_id));

                drop function if exists access.can_read_external_item(uuid, uuid, uuid[], uuid);
                """);
        }
    }
}
