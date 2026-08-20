using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Access.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "access");

            migrationBuilder.CreateTable(
                name: "contextual_role_assignment",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contextual_role_assignment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "access",
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
                name: "project_membership",
                schema: "access",
                columns: table => new
                {
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_membership", x => new { x.person_id, x.project_id });
                });

            migrationBuilder.CreateTable(
                name: "rbac_override",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_grant = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rbac_override", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rbac_override_audit",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    override_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rbac_override_audit", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contextual_role_assignment_person_id",
                schema: "access",
                table: "contextual_role_assignment",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_contextual_role_assignment_person_id_role_scope_type_scope_~",
                schema: "access",
                table: "contextual_role_assignment",
                columns: new[] { "person_id", "role", "scope_type", "scope_id", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "access",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_project_membership_department_id",
                schema: "access",
                table: "project_membership",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_membership_project_id",
                schema: "access",
                table: "project_membership",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_rbac_override_active",
                schema: "access",
                table: "rbac_override",
                columns: new[] { "person_id", "expires_at" },
                filter: "revoked_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_rbac_override_person_id",
                schema: "access",
                table: "rbac_override",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_rbac_override_audit_override_id",
                schema: "access",
                table: "rbac_override_audit",
                column: "override_id");

            migrationBuilder.CreateIndex(
                name: "ix_rbac_override_audit_person_id",
                schema: "access",
                table: "rbac_override_audit",
                column: "person_id");

            // =====================================================================================================
            // The shared predicates — visibility-matrix.md §3 made executable.
            //
            // Every later slice's migration attaches its policies to these rather than writing its own role
            // checks, and an architecture test enforces that. The matrix then lives in exactly one place: when the
            // cross-department rule changes, it changes here, once, and every table that referenced it follows.
            //
            // Two things are true of all of them.
            //
            // They are STABLE, not VOLATILE: Postgres evaluates an RLS predicate once per candidate row, and a
            // function it cannot cache within a statement turns a scan into one function call per row.
            //
            // They are wrapped in coalesce(..., false). In SQL `NULL or false` is NULL, so an unstamped session —
            // where access.uid() is NULL — would otherwise make every predicate return NULL rather than a definite
            // deny. RLS reads NULL as "no", so this was never a hole; but a predicate that can return NULL is one
            // where `not access.can_read_x(...)` quietly stops meaning what it reads like.
            // =====================================================================================================
            migrationBuilder.Sql("""
                -- Membership helpers, reading access.project_membership — the projection S3 maintains from the
                -- Projects module's integration events. Not projects.project_member directly: an RLS predicate
                -- reaching into another module's schema is a coupling no architecture test can see, expressed in
                -- the one language where it is hardest to notice.
                create or replace function access.on_project(p_project uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce((
                        select true from access.project_membership m
                        where m.project_id = p_project and m.person_id = access.uid()
                        limit 1
                    ), false)
                    $fn$;

                create or replace function access.project_in_my_depts(p_project uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce((
                        select true from access.project_membership m
                        where m.project_id = p_project and m.department_id = any(access.depts())
                        limit 1
                    ), false)
                    $fn$;

                -- Leads the project, whether titled project-lead or PO. The matrix treats the two identically for
                -- visibility; they differ in what they may do, which is an endpoint policy concern.
                create or replace function access.leads_project(p_project uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                        (access.has('project-lead') or access.has('po')) and access.on_project(p_project),
                        false)
                    $fn$;
                """);

            migrationBuilder.Sql("""
                -- ACTIVITY. The canonical predicate, quoted almost verbatim from the matrix.
                --
                -- The member clause is unit-scoped, not department-scoped: a member sees their unit peers, because
                -- kudos require it, and no further. S1 widened the directory to the department so colleagues are
                -- findable; activity stays narrow. That is what keeps "who exists" and "what they worked on"
                -- separate questions.
                create or replace function access.can_read_activity(
                    p_owner uuid, p_unit uuid, p_project uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_owner = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or (access.has('member')    and p_unit = access.unit())
                        or (access.has('unit-head') and p_unit = access.unit())
                        or (access.has('dept-head') and p_dept = any(access.depts()))
                        -- Cross-department knowledge flow: heads read shared projects beyond their own boundary.
                        -- The one deliberate widening in the matrix, and it stops at heads.
                        or (access.has('dept-head') and access.project_in_my_depts(p_project))
                        or access.leads_project(p_project)
                    , false)
                    $fn$;

                -- PROJECT. Anyone on it sees it; heads see their department's and shared ones; PMO sees all.
                create or replace function access.can_read_project(p_project uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.on_project(p_project)
                        or (access.has('dept-head') and p_dept = any(access.depts()))
                        or (access.has('dept-head') and access.project_in_my_depts(p_project))
                        or (access.has('unit-head') and access.project_in_my_depts(p_project))
                    , false)
                    $fn$;

                -- KUDO. Both parties always, plus whoever can see the receiver's unit.
                --
                -- Both parties, unconditionally: a kudo you gave disappearing because the recipient moved unit
                -- would read as the system having lost it.
                create or replace function access.can_read_kudo(
                    p_from uuid, p_to uuid, p_unit uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_from = access.uid()
                        or p_to = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or (access.has('member')    and p_unit = access.unit())
                        or (access.has('unit-head') and p_unit = access.unit())
                        or (access.has('dept-head') and p_dept = any(access.depts()))
                    , false)
                    $fn$;

                -- MEETING / SPECIAL DAY. p_target_person for one aimed at a person, p_unit for a unit-wide one,
                -- p_dept for a department-wide one; a meeting may carry any combination.
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

                -- SCHEDULE ROW (RUN work orders and shifts). Same shape as activity.
                create or replace function access.can_read_schedule_row(
                    p_assignee uuid, p_unit uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_assignee = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or (access.has('member')    and p_unit = access.unit())
                        or (access.has('unit-head') and p_unit = access.unit())
                        or (access.has('dept-head') and p_dept = any(access.depts()))
                    , false)
                    $fn$;
                """);

            migrationBuilder.Sql("""
                -- WRITE predicates. Matrix §5: members write only their own rows, heads within their scope.
                -- Separate from the read predicates on purpose — being able to see a colleague's week is not the
                -- same as being able to edit it, and one predicate serving both would quietly make it so.
                create or replace function access.can_write_own(p_owner uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(p_owner = access.uid() or access.is_system(), false)
                    $fn$;

                create or replace function access.can_write_in_unit(p_unit uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or (access.has('unit-head') and p_unit = access.unit())
                        or (access.has('dept-head') and p_unit is not null)
                    , false)
                    $fn$;
                """);

            migrationBuilder.Sql("""
                -- S1 defined these three before this slice existed, with the same NULL-instead-of-false shape.
                -- Redefined here rather than edited in S1's migration: that one has shipped, and rewriting an
                -- applied migration means environments that already ran it never pick up the change.
                create or replace function access.can_read_person(p_person uuid, p_unit uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_person = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or p_dept = any(access.depts())
                        or (access.has('unit-head') and p_unit = access.unit())
                    , false)
                    $fn$;

                create or replace function access.can_read_department(p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or p_dept = any(access.depts())
                    , false)
                    $fn$;

                create or replace function access.can_write_department_config(p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or (access.has('dept-head') and p_dept = any(access.depts()))
                    , false)
                    $fn$;
                """);

            migrationBuilder.Sql("""
                grant usage on schema access to app_rw;
                grant select, insert, update, delete on all tables in schema access to app_rw;
                grant execute on all functions in schema access to app_rw;
                alter default privileges in schema access
                    grant select, insert, update, delete on tables to app_rw;
                alter default privileges in schema access grant execute on functions to app_rw;
                """);

            // ---------------------------------------------------------------------------------------------------
            // Policies on the Access module's own tables.
            //
            // These rows decide everyone else's visibility, so they are the ones most worth getting wrong quietly.
            // Deliberately narrow: you always read your own, heads read within scope, and only the PMO or a head
            // writes.
            // ---------------------------------------------------------------------------------------------------
            migrationBuilder.Sql("""
                alter table access.contextual_role_assignment enable row level security;
                alter table access.contextual_role_assignment force  row level security;
                alter table access.rbac_override              enable row level security;
                alter table access.rbac_override              force  row level security;
                alter table access.rbac_override_audit        enable row level security;
                alter table access.rbac_override_audit        force  row level security;
                alter table access.project_membership         enable row level security;
                alter table access.project_membership         force  row level security;

                create policy role_assignment_read on access.contextual_role_assignment
                    for select using (
                        person_id = access.uid() or access.is_system() or access.has('pmo') or access.is_head()
                    );
                -- Written by sync only. Hand-editing an LDAP assignment is pointless anyway: the next
                -- reconciliation rewrites the lot.
                create policy role_assignment_write on access.contextual_role_assignment
                    for all using (access.is_system()) with check (access.is_system());

                create policy override_read on access.rbac_override
                    for select using (
                        person_id = access.uid() or access.is_system() or access.has('pmo') or access.is_head()
                    );
                create policy override_write on access.rbac_override
                    for all using (access.is_system() or access.has('pmo') or access.has('dept-head'))
                    with check (access.is_system() or access.has('pmo') or access.has('dept-head'));

                -- Append-only by construction: read and insert policies, and nothing for update or delete, so
                -- Postgres refuses both whatever the application asks for.
                create policy override_audit_read on access.rbac_override_audit
                    for select using (
                        person_id = access.uid() or access.is_system() or access.has('pmo') or access.is_head()
                    );
                create policy override_audit_append on access.rbac_override_audit
                    for insert with check (
                        access.is_system() or access.has('pmo') or access.has('dept-head')
                    );

                -- The projection every project-scoped predicate consults. Readable by anyone scoped, so those
                -- predicates can run at all; writable only by the system, because S3 maintains it from integration
                -- events and a human editing it would silently rewrite who can see which project.
                create policy project_membership_read on access.project_membership
                    for select using (access.is_scoped() or access.is_system());
                create policy project_membership_write on access.project_membership
                    for all using (access.is_system()) with check (access.is_system());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop function if exists access.can_write_in_unit(uuid);
                drop function if exists access.can_write_own(uuid);
                drop function if exists access.can_read_schedule_row(uuid, uuid, uuid);
                drop function if exists access.can_read_meeting(uuid, uuid, uuid, uuid);
                drop function if exists access.can_read_kudo(uuid, uuid, uuid, uuid);
                drop function if exists access.can_read_project(uuid, uuid);
                drop function if exists access.can_read_activity(uuid, uuid, uuid, uuid);
                drop function if exists access.leads_project(uuid);
                drop function if exists access.project_in_my_depts(uuid);
                drop function if exists access.on_project(uuid);
                """);

            migrationBuilder.DropTable(
                name: "contextual_role_assignment",
                schema: "access");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "access");

            migrationBuilder.DropTable(
                name: "project_membership",
                schema: "access");

            migrationBuilder.DropTable(
                name: "rbac_override",
                schema: "access");

            migrationBuilder.DropTable(
                name: "rbac_override_audit",
                schema: "access");
        }
    }
}
