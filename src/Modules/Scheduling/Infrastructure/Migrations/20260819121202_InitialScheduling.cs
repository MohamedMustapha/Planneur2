using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "scheduling",
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
                name: "shift",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    start = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_order",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activity_type_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    assigned_to_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activity_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scheduled_start = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    scheduled_end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    estimated_hours = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "scheduling",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_shift_department_id",
                schema: "scheduling",
                table: "shift",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_person_id",
                schema: "scheduling",
                table: "shift",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_person_id_day_template_code",
                schema: "scheduling",
                table: "shift",
                columns: new[] { "person_id", "day", "template_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_unit_id_day",
                schema: "scheduling",
                table: "shift",
                columns: new[] { "unit_id", "day" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_assigned_to_person_id",
                schema: "scheduling",
                table: "work_order",
                column: "assigned_to_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_department_id",
                schema: "scheduling",
                table: "work_order",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_source_external_ref",
                schema: "scheduling",
                table: "work_order",
                columns: new[] { "source", "external_ref" },
                unique: true,
                filter: "external_ref is not null");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_unit_id",
                schema: "scheduling",
                table: "work_order",
                column: "unit_id");

            // =====================================================================================================
            // RLS for the Scheduling module.
            //
            // The boards themselves need no policy: they are joins of rows other modules already filtered. What
            // does need one is the two tables this module owns, and both are scoped the same way — a work order
            // belongs to a unit's queue and a shift belongs to a unit's roster, so the unit is the boundary.
            // =====================================================================================================
            migrationBuilder.Sql("""
                grant usage on schema scheduling to app_rw;
                grant select, insert, update, delete on all tables in schema scheduling to app_rw;
                alter default privileges in schema scheduling
                    grant select, insert, update, delete on tables to app_rw;
                """);

            migrationBuilder.Sql("""
                alter table scheduling.work_order enable row level security;
                alter table scheduling.work_order force  row level security;
                alter table scheduling.shift      enable row level security;
                alter table scheduling.shift      force  row level security;
                """);

            migrationBuilder.Sql("""
                -- READ. A unit's queue is the unit's business: everyone in it sees the pool, because seeing what
                -- is waiting is how a team picks work up. Heads see their department's, the PMO sees all, and the
                -- person a card is assigned to sees their own wherever they sit.
                create policy work_order_read on scheduling.work_order
                    for select using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or assigned_to_person_id = access.uid()
                            or unit_id = access.unit()
                            or (access.has('dept-head') and department_id = any(access.depts()))
                        , false)
                    );

                -- WRITE. Distributing work is a lead's act. A member seeing the pool does not get to assign from
                -- it — the read policy above is deliberately much wider than this one.
                create policy work_order_write on scheduling.work_order
                    for all using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or (access.has('unit-head') and unit_id = access.unit())
                            or (access.has('dept-head') and department_id = any(access.depts()))
                            or access.leads_project(project_id)
                        , false)
                    )
                    with check (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or (access.has('unit-head') and unit_id = access.unit())
                            or (access.has('dept-head') and department_id = any(access.depts()))
                            or access.leads_project(project_id)
                        , false)
                    );
                """);

            migrationBuilder.Sql("""
                -- READ. A roster only works if the team can see it: knowing who is on afternoons is the point of
                -- publishing one, and hiding a colleague's shift would make the board unreadable to the people it
                -- is for.
                create policy shift_read on scheduling.shift
                    for select using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or person_id = access.uid()
                            or unit_id = access.unit()
                            or (access.has('dept-head') and department_id = any(access.depts()))
                        , false)
                    );

                -- WRITE. Rostering is a lead's act, and deliberately not the individual's: someone putting
                -- themselves on the on-call slot is a conversation, not a self-service action.
                create policy shift_write on scheduling.shift
                    for all using (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or (access.has('unit-head') and unit_id = access.unit())
                            or (access.has('dept-head') and department_id = any(access.depts()))
                        , false)
                    )
                    with check (
                        coalesce(
                               access.is_system()
                            or access.has('pmo')
                            or (access.has('unit-head') and unit_id = access.unit())
                            or (access.has('dept-head') and department_id = any(access.depts()))
                        , false)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "shift",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "work_order",
                schema: "scheduling");
        }
    }
}
