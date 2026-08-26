using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttachToNodeTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.attach_to_node_tree('scheduling', 'shift', 'unit_id', 'department_id', true);
                select access.attach_to_node_tree('scheduling', 'work_order', 'unit_id', 'department_id', true);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.detach_from_node_tree('scheduling', 'shift');
                select access.detach_from_node_tree('scheduling', 'work_order');
                """);
        }
    }
}
