using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Kudos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SwapToNodePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy kudo_read on kudos.kudo;
                create policy kudo_read on kudos.kudo
                    for select using (
                        access.can_read_kudo(from_person_id, to_person_id, node_id, node_ancestor_ids)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy kudo_read on kudos.kudo;
                create policy kudo_read on kudos.kudo
                    for select using (
                        access.can_read_kudo(from_person_id, to_person_id, unit_id, department_id)
                    );
                """);
        }
    }
}
