using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Portfolio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttachToNodeTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.attach_to_node_tree('portfolio', 'portfolio_item', null, 'department_id', true);
                select access.attach_to_node_tree('portfolio', 'portfolio_transition', null, 'department_id', true);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.detach_from_node_tree('portfolio', 'portfolio_item');
                select access.detach_from_node_tree('portfolio', 'portfolio_transition');
                """);
        }
    }
}
