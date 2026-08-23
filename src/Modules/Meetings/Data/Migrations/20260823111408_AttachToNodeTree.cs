using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Meetings.Data.Migrations
{
    /// <inheritdoc />
    public partial class AttachToNodeTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.attach_to_node_tree('meetings', 'meeting_series', null, 'department_id', false);
                select access.attach_to_node_tree('meetings', 'meeting_occurrence', null, 'department_id', false);
                select access.attach_to_node_tree('meetings', 'special_day', null, 'department_id', false);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.detach_from_node_tree('meetings', 'meeting_series');
                select access.detach_from_node_tree('meetings', 'meeting_occurrence');
                select access.detach_from_node_tree('meetings', 'special_day');
                """);
        }
    }
}
