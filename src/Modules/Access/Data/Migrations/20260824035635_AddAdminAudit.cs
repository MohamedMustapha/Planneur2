using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Access.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_audit",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_audit", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_actor_person_id",
                schema: "access",
                table: "admin_audit",
                column: "actor_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_node_id_occurred_at",
                schema: "access",
                table: "admin_audit",
                columns: new[] { "node_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_occurred_at",
                schema: "access",
                table: "admin_audit",
                column: "occurred_at");

            migrationBuilder.Sql("""
                alter table access.admin_audit enable row level security;
                alter table access.admin_audit force row level security;

                -- Insert only, and no update or delete policy at all. A trail somebody can correct afterwards
                -- answers "what does the record say now" rather than "what did somebody do".
                create policy admin_audit_append on access.admin_audit
                    for insert with check (access.is_scoped() or access.is_system());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_audit",
                schema: "access");
        }
    }
}
