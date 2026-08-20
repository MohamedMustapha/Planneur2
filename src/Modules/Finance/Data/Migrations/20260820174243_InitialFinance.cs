using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Finance.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "capex_opex_rule",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    build_treatment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    run_treatment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    qol_treatment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    admin_treatment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capex_opex_rule", x => x.id);
                    table.CheckConstraint("ck_capex_opex_rule_treatments", "build_treatment in ('capex', 'opex', 'excluded')\nand run_treatment in ('capex', 'opex', 'excluded')\nand qol_treatment in ('capex', 'opex', 'excluded')\nand admin_treatment in ('capex', 'opex', 'excluded')");
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rate_card",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    functional_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hourly_rate = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_card", x => x.id);
                    table.CheckConstraint("ck_rate_card_range", "effective_to is null or effective_to > effective_from");
                });

            migrationBuilder.CreateIndex(
                name: "ix_capex_opex_rule_department_id",
                schema: "finance",
                table: "capex_opex_rule",
                column: "department_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "finance",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_rate_card_department_id_functional_role_id_effective_from",
                schema: "finance",
                table: "rate_card",
                columns: new[] { "department_id", "functional_role_id", "effective_from" });

            // =====================================================================================================
            // RLS (conventions.md 4, visibility-matrix.md 4).
            //
            // The matrix row for Capex/Opex reads: member no, unit-head no, dept-head yes (their department),
            // project-lead no, PMO yes. That is exactly access.can_write_department_config, which S1 defined and
            // S10 already reuses for connection configuration - so it is reused again here rather than copied
            // into a can_read_finance that would say the same thing in different words and drift from it later.
            //
            // The slice text says "access.is_head() (dept-scoped) or pmo", which would admit a unit-head. Where a
            // slice sketch and the matrix disagree the matrix wins - it is the document every slice is told to
            // reference - and the practical difference is small and deliberate: a unit-head passes the endpoint
            // policy, because they are a head, and then sees nothing at all. A member or a project-lead never
            // gets that far.
            //
            // Read and write are the same predicate, which is unusual in this system and correct here. A rate
            // card states what an organization pays a job title. There is no audience for that between
            // "administers this department" and "nobody", and the derived view built on it is head-only by the
            // matrix anyway.
            // =====================================================================================================
            migrationBuilder.Sql("""
                grant usage on schema finance to app_rw;
                grant select, insert, update, delete on all tables in schema finance to app_rw;
                alter default privileges in schema finance
                    grant select, insert, update, delete on tables to app_rw;
                """);

            // Every table gets FORCE, so the policies apply even to a future owner-role connection.
            migrationBuilder.Sql("""
                alter table finance.rate_card enable row level security;
                alter table finance.rate_card force row level security;
                alter table finance.capex_opex_rule enable row level security;
                alter table finance.capex_opex_rule force row level security;
                """);

            migrationBuilder.Sql("""
                create policy rate_card_read on finance.rate_card
                    for select using (access.can_write_department_config(department_id));
                create policy rate_card_write on finance.rate_card
                    for all using (access.can_write_department_config(department_id))
                        with check (access.can_write_department_config(department_id));

                create policy capex_opex_rule_read on finance.capex_opex_rule
                    for select using (access.can_write_department_config(department_id));
                create policy capex_opex_rule_write on finance.capex_opex_rule
                    for all using (access.can_write_department_config(department_id))
                        with check (access.can_write_department_config(department_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No predicate to drop. This slice defined none of its own - it reuses the one S1 shipped - so
            // rolling it back leaves the access schema exactly as it found it.
            migrationBuilder.DropTable(
                name: "capex_opex_rule",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "rate_card",
                schema: "finance");
        }
    }
}
