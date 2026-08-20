using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Meetings.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialMeetings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "meetings");

            migrationBuilder.CreateTable(
                name: "meeting_series",
                schema: "meetings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recurrence_rule = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    owner_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    video_link = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "meetings",
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
                name: "special_day",
                schema: "meetings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    all_day = table.Column<bool>(type: "boolean", nullable: false),
                    severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_special_day", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meeting_occurrence",
                schema: "meetings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    notes_ref = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_occurrence", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_occurrence_meeting_series_series_id",
                        column: x => x.series_id,
                        principalSchema: "meetings",
                        principalTable: "meeting_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meeting_attendance",
                schema: "meetings",
                columns: table => new
                {
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    response = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_attendance", x => new { x.occurrence_id, x.person_id });
                    table.ForeignKey(
                        name: "fk_meeting_attendance_meeting_occurrence_occurrence_id",
                        column: x => x.occurrence_id,
                        principalSchema: "meetings",
                        principalTable: "meeting_occurrence",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_attendance_person_id",
                schema: "meetings",
                table: "meeting_attendance",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_occurrence_series_id_starts_at",
                schema: "meetings",
                table: "meeting_occurrence",
                columns: new[] { "series_id", "starts_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meeting_occurrence_starts_at_scope_type_scope_id",
                schema: "meetings",
                table: "meeting_occurrence",
                columns: new[] { "starts_at", "scope_type", "scope_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_department_id",
                schema: "meetings",
                table: "meeting_series",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_owner_person_id",
                schema: "meetings",
                table: "meeting_series",
                column: "owner_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_scope_type_scope_id_active",
                schema: "meetings",
                table: "meeting_series",
                columns: new[] { "scope_type", "scope_id", "active" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "meetings",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_special_day_date_scope_type_scope_id",
                schema: "meetings",
                table: "special_day",
                columns: new[] { "date", "scope_type", "scope_id" });

            migrationBuilder.CreateIndex(
                name: "ix_special_day_department_id",
                schema: "meetings",
                table: "special_day",
                column: "department_id");

            // =====================================================================================================
            // Access predicates and RLS policies (conventions.md 4, visibility-matrix.md 3 and 4).
            //
            // The matrix row for meetings reads: member - "ones targeting them/unit"; unit-head - unit; dept-head
            // - dept; project-lead/PO - project; PMO - all. S7 restates it as scope targeting, which is the same
            // rule seen from the other side: a person sees what is aimed at their unit, a department they belong
            // to, a project they are on, or the whole organization.
            //
            // Note the shape change. S2 shipped a speculative can_read_meeting(target, unit, dept, project) -
            // four nullable columns, one per possible target - written before this slice knew what it needed. The
            // real model turned out to be a single tagged pair, and carrying both would leave a predicate nothing
            // calls sitting next to the one everything calls. So it is replaced here, and the Down restores it.
            // =====================================================================================================
            migrationBuilder.Sql("""
                drop function if exists access.can_read_meeting(uuid, uuid, uuid, uuid);

                -- p_dept is the denormalized department: the unit's for a unit-scoped row, its own for a
                -- department-scoped one, null for project and org. It exists so this predicate never has to read
                -- directory.unit - an access function reaching into another module's schema is a coupling no
                -- architecture test can see, expressed in the one language where it is hardest to notice.
                create or replace function access.can_read_meeting(
                    p_scope_type text, p_scope_id uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        -- Org-wide, but still only for a stamped session: an unscoped connection sees nothing.
                        or (p_scope_type = 'org' and access.is_scoped())
                        or (p_scope_type = 'unit' and p_scope_id = access.unit())
                        or (p_scope_type = 'unit' and access.has('dept-head') and p_dept = any(access.depts()))
                        -- Everyone in the department, not just its head: S7's own example is a patch party that
                        -- has to reach every member, and a department-wide audit day nobody can see is useless.
                        or (p_scope_type = 'department' and p_scope_id = any(access.depts()))
                        or (p_scope_type = 'project' and access.on_project(p_scope_id))
                        or (p_scope_type = 'project' and access.leads_project(p_scope_id))
                        -- Cross-department knowledge flow, and it stops at heads exactly as it does elsewhere.
                        or (p_scope_type = 'project' and access.has('dept-head') and access.project_in_my_depts(p_scope_id))
                        or (p_scope_type = 'project' and access.has('unit-head') and access.project_in_my_depts(p_scope_id))
                    , false)
                    $fn$;

                -- Writes are the scope owner's, which is narrower than read in every case. Being in a department
                -- lets you see its copil; scheduling one is the head's business.
                create or replace function access.can_write_meeting(
                    p_scope_type text, p_scope_id uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or (p_scope_type = 'unit' and access.has('unit-head') and p_scope_id = access.unit())
                        or (p_scope_type = 'unit' and access.has('dept-head') and p_dept = any(access.depts()))
                        or (p_scope_type = 'department' and access.has('dept-head') and p_scope_id = any(access.depts()))
                        or (p_scope_type = 'project' and access.leads_project(p_scope_id))
                        or (p_scope_type = 'project' and access.has('dept-head') and access.project_in_my_depts(p_scope_id))
                    , false)
                    $fn$;

                grant usage on schema meetings to app_rw;
                grant select, insert, update, delete on all tables in schema meetings to app_rw;
                alter default privileges in schema meetings
                    grant select, insert, update, delete on tables to app_rw;
                """);

            // Every table gets FORCE, so the policies apply even to a future owner-role connection.
            migrationBuilder.Sql("""
                alter table meetings.meeting_series enable row level security;
                alter table meetings.meeting_series force row level security;
                alter table meetings.meeting_occurrence enable row level security;
                alter table meetings.meeting_occurrence force row level security;
                alter table meetings.meeting_attendance enable row level security;
                alter table meetings.meeting_attendance force row level security;
                alter table meetings.special_day enable row level security;
                alter table meetings.special_day force row level security;
                """);

            migrationBuilder.Sql("""
                create policy meeting_series_read on meetings.meeting_series
                    for select using (access.can_read_meeting(scope_type, scope_id, department_id));
                create policy meeting_series_write on meetings.meeting_series
                    for all using (access.can_write_meeting(scope_type, scope_id, department_id))
                        with check (access.can_write_meeting(scope_type, scope_id, department_id));

                -- Occurrences answer from their own copied columns rather than through an EXISTS against the
                -- series. The rule is identical either way; the difference is a subquery per candidate row on the
                -- single query every board in the system runs. The materializer is the only writer and it
                -- restamps the whole future whenever a series changes, so the copies cannot drift.
                create policy meeting_occurrence_read on meetings.meeting_occurrence
                    for select using (access.can_read_meeting(scope_type, scope_id, department_id));
                create policy meeting_occurrence_write on meetings.meeting_occurrence
                    for all using (access.can_write_meeting(scope_type, scope_id, department_id))
                        with check (access.can_write_meeting(scope_type, scope_id, department_id));

                -- Attendance: readable by whoever can see the meeting, writable only by the person it is about.
                -- The read is an EXISTS here rather than copied columns because attendance is written once by one
                -- person, not scanned by every board - the trade that made copying worth it above does not apply.
                create policy meeting_attendance_read on meetings.meeting_attendance
                    for select using (
                        person_id = access.uid()
                        or exists (select 1 from meetings.meeting_occurrence o where o.id = occurrence_id)
                    );
                create policy meeting_attendance_write on meetings.meeting_attendance
                    for all using (access.is_system() or person_id = access.uid())
                        with check (
                            (access.is_system() or person_id = access.uid())
                            and exists (select 1 from meetings.meeting_occurrence o where o.id = occurrence_id)
                        );

                create policy special_day_read on meetings.special_day
                    for select using (access.can_read_meeting(scope_type, scope_id, department_id));
                create policy special_day_write on meetings.special_day
                    for all using (access.can_write_meeting(scope_type, scope_id, department_id))
                        with check (access.can_write_meeting(scope_type, scope_id, department_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop function if exists access.can_write_meeting(text, uuid, uuid);
                drop function if exists access.can_read_meeting(text, uuid, uuid);

                -- Restores the speculative shape S2 shipped, so rolling this migration back leaves the access
                -- schema exactly as S2 left it rather than one function short of it.
                create or replace function access.can_read_meeting(
                    p_target_person uuid, p_unit uuid, p_dept uuid, p_project uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_target_person = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or (p_unit is not null and p_unit = access.unit())
                        or (access.has('dept-head') and p_dept = any(access.depts()))
                        or (p_project is not null and access.on_project(p_project))
                        or access.leads_project(p_project)
                    , false)
                    $fn$;
                """);

            migrationBuilder.DropTable(
                name: "meeting_attendance",
                schema: "meetings");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "meetings");

            migrationBuilder.DropTable(
                name: "special_day",
                schema: "meetings");

            migrationBuilder.DropTable(
                name: "meeting_occurrence",
                schema: "meetings");

            migrationBuilder.DropTable(
                name: "meeting_series",
                schema: "meetings");
        }
    }
}
