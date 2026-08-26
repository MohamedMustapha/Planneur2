using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttachToNodeTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.attach_to_node_tree('projects', 'project', null, 'lead_department_id', true);
                select access.attach_to_node_tree('projects', 'project_department', null, 'department_id', true);
                select access.attach_to_node_tree('projects', 'project_member', null, 'department_id', true);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.detach_from_node_tree('projects', 'project');
                select access.detach_from_node_tree('projects', 'project_department');
                select access.detach_from_node_tree('projects', 'project_member');
                """);
        }
    }
}
