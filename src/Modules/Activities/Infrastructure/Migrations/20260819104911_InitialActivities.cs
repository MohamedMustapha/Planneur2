using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Activities.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "activities");

            migrationBuilder.CreateTable(
                name: "activity_entry",
                schema: "activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_type_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    iteration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    external_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    slot_start = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    slot_end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    hours = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    iso_year = table.Column<int>(type: "integer", nullable: false),
                    iso_week_number = table.Column<int>(type: "integer", nullable: false),
                    supersedes_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reconciled = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "activities",
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

            migrationBuilder.CreateIndex(
                name: "ix_activity_entry_department_id",
                schema: "activities",
                table: "activity_entry",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_entry_external_ref",
                schema: "activities",
                table: "activity_entry",
                column: "external_ref");

            migrationBuilder.CreateIndex(
                name: "ix_activity_entry_person_id",
                schema: "activities",
                table: "activity_entry",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_entry_person_week",
                schema: "activities",
                table: "activity_entry",
                columns: new[] { "person_id", "iso_year", "iso_week_number" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_entry_project_id",
                schema: "activities",
                table: "activity_entry",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_entry_slot_start_slot_end",
                schema: "activities",
                table: "activity_entry",
                columns: new[] { "slot_start", "slot_end" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_entry_supersedes_entry_id",
                schema: "activities",
                table: "activity_entry",
                column: "supersedes_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_entry_unit_id",
                schema: "activities",
                table: "activity_entry",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "activities",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            // =====================================================================================================
            // RLS for the Activities module.
            //
            // The read policy is access.can_read_activity verbatim — the matrix predicate from S2, taking exactly
            // the four columns this table carries. That is why the row denormalises unit and department: the
            // predicate is evaluated per candidate row, and a policy that had to join into Directory to find them
            // would cross a module boundary on every read.
            // =====================================================================================================
            migrationBuilder.Sql("""
                grant usage on schema activities to app_rw;
                grant select, insert, update, delete on all tables in schema activities to app_rw;
                alter default privileges in schema activities
                    grant select, insert, update, delete on tables to app_rw;
                """);

            migrationBuilder.Sql("""
                alter table activities.activity_entry enable row level security;
                alter table activities.activity_entry force  row level security;
                """);

            migrationBuilder.Sql("""
                -- READ. Straight from the matrix: own entries, unit peers, your projects, your department if you
                -- head it, shared projects across departments for heads, everything for the PMO.
                create policy activity_entry_read on activities.activity_entry
                    for select using (
                        access.can_read_activity(person_id, unit_id, project_id, department_id)
                    );

                -- WRITE. Deliberately much narrower than read. A member writes their own row and nobody else's:
                -- the whole feed is visible to a unit so peers can see what each other are doing, and none of that
                -- visibility implies the right to book hours in someone else's name.
                --
                -- Leads and heads write within their scope, which is what lets a manager correct an entry or log
                -- for someone on leave. A unit head is scoped to their unit and a dept-head to their departments,
                -- rather than both being folded into "any head" — a Finance head must not be able to rewrite an IT
                -- engineer's week.
                create policy activity_entry_write on activities.activity_entry
                    for all using (
                        access.is_system()
                        or person_id = access.uid()
                        or access.has('pmo')
                        or (access.has('unit-head') and unit_id = access.unit())
                        or (access.has('dept-head') and department_id = any(access.depts()))
                        or access.leads_project(project_id)
                    )
                    with check (
                        access.is_system()
                        or person_id = access.uid()
                        or access.has('pmo')
                        or (access.has('unit-head') and unit_id = access.unit())
                        or (access.has('dept-head') and department_id = any(access.depts()))
                        or access.leads_project(project_id)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_entry",
                schema: "activities");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "activities");
        }
    }
}
