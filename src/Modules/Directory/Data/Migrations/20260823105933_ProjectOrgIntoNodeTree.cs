using Cracra.Modules.Directory.Services;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectOrgIntoNodeTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';
                """);

            migrationBuilder.Sql(OrgTreeSql.Project);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                delete from directory.org_node n
                where exists (select 1 from directory.unit u where u.id = n.id)
                   or exists (select 1 from directory.department d where d.id = n.id)
                   or n.id = 'd1000000-0000-0000-0000-000000000001';
                """);
        }
    }
}
