using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Integrations.Data.Migrations
{
    /// <inheritdoc />
    public partial class AttachToNodeTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.attach_to_node_tree('integrations', 'external_connection', null, 'department_id', true);
                select access.attach_to_node_tree('integrations', 'external_mapping', 'unit_id', 'department_id', true);
                select access.attach_to_node_tree('integrations', 'external_work_item', 'unit_id', 'department_id', true);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.detach_from_node_tree('integrations', 'external_connection');
                select access.detach_from_node_tree('integrations', 'external_mapping');
                select access.detach_from_node_tree('integrations', 'external_work_item');
                """);
        }
    }
}
