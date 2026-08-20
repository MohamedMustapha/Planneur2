using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "directory");

            migrationBuilder.CreateTable(
                name: "department",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    parent_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_department", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "department_config_audit",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_department_config_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "functional_role",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_functional_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "directory",
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
                name: "person",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ldap_uid = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    primary_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    primary_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ui_language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "department_config",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_taxonomy_json = table.Column<string>(type: "jsonb", nullable: false),
                    role_labels_json = table.Column<string>(type: "jsonb", nullable: false),
                    kudo_rules_json = table.Column<string>(type: "jsonb", nullable: false),
                    default_board_layout = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    iteration_presets_json = table.Column<string>(type: "jsonb", nullable: false),
                    weekly_target_hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    enforce_weekly_target = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_department_config", x => x.id);
                    table.ForeignKey(
                        name: "fk_department_config_department_department_id",
                        column: x => x.department_id,
                        principalSchema: "directory",
                        principalTable: "department",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "unit",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ldap_fonction = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit", x => x.id);
                    table.ForeignKey(
                        name: "fk_unit_department_department_id",
                        column: x => x.department_id,
                        principalSchema: "directory",
                        principalTable: "department",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "person_functional_role",
                schema: "directory",
                columns: table => new
                {
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    functional_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person_functional_role", x => new { x.person_id, x.functional_role_id, x.unit_id });
                    table.ForeignKey(
                        name: "fk_person_functional_role_functional_role_functional_role_id",
                        column: x => x.functional_role_id,
                        principalSchema: "directory",
                        principalTable: "functional_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_person_functional_role_person_person_id",
                        column: x => x.person_id,
                        principalSchema: "directory",
                        principalTable: "person",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "person_unit",
                schema: "directory",
                columns: table => new
                {
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person_unit", x => new { x.person_id, x.unit_id });
                    table.ForeignKey(
                        name: "fk_person_unit_person_person_id",
                        column: x => x.person_id,
                        principalSchema: "directory",
                        principalTable: "person",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_person_unit_unit_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "directory",
                        principalTable: "unit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_department_code",
                schema: "directory",
                table: "department",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_department_config_department_id",
                schema: "directory",
                table: "department_config",
                column: "department_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_department_config_audit_department_id_version",
                schema: "directory",
                table: "department_config_audit",
                columns: new[] { "department_id", "version" });

            migrationBuilder.CreateIndex(
                name: "ix_functional_role_department_id_code",
                schema: "directory",
                table: "functional_role",
                columns: new[] { "department_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "directory",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_person_ldap_uid",
                schema: "directory",
                table: "person",
                column: "ldap_uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_person_primary_department_id",
                schema: "directory",
                table: "person",
                column: "primary_department_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_primary_unit_id",
                schema: "directory",
                table: "person",
                column: "primary_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_functional_role_functional_role_id",
                schema: "directory",
                table: "person_functional_role",
                column: "functional_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_unit_unit_id",
                schema: "directory",
                table: "person_unit",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_department_id_code",
                schema: "directory",
                table: "unit",
                columns: new[] { "department_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_ldap_fonction",
                schema: "directory",
                table: "unit",
                column: "ldap_fonction",
                unique: true,
                filter: "ldap_fonction is not null");

            // The functional roles from the glossary, with fixed ids so a department configuration that references
            // one keeps referencing the same row across every environment. Sync adds any others it finds in LDAP.
            migrationBuilder.Sql("""
                insert into directory.functional_role (id, code, label_key, department_id, active) values
                    ('f0000000-0000-0000-0000-000000000001', 'dev',              'directory.functionalRole.dev',             null, true),
                    ('f0000000-0000-0000-0000-000000000002', 'tech-lead',        'directory.functionalRole.techLead',        null, true),
                    ('f0000000-0000-0000-0000-000000000003', 'architecte',       'directory.functionalRole.architect',       null, true),
                    ('f0000000-0000-0000-0000-000000000004', 'chef-de-pole',     'directory.functionalRole.unitHead',        null, true),
                    ('f0000000-0000-0000-0000-000000000005', 'comptable',        'directory.functionalRole.accountant',      null, true),
                    ('f0000000-0000-0000-0000-000000000006', 'expert-comptable', 'directory.functionalRole.seniorAccountant', null, true)
                on conflict (id) do nothing;
                """);

            // =====================================================================================================
            // Access predicates and RLS policies (conventions.md 4, visibility-matrix.md 3).
            //
            // Directory is deliberately the most open module in the system: you cannot collaborate with colleagues
            // you cannot see, and every board renders names. It is still department-scoped for members  S1 states
            // this widening explicitly, and it applies to *directory rows only*. It does not widen activity, which
            // stays unit-scoped for members per the matrix: a member can see that Sofia exists without being able
            // to see what Sofia worked on.
            // =====================================================================================================
            migrationBuilder.Sql("""
                -- Can the current session see this person? Takes the person id so "always yourself" holds even for
                -- someone whose unit and department have not been synced yet.
                create or replace function access.can_read_person(p_person uuid, p_unit uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select p_person = access.uid()                  -- always yourself
                        or access.is_system()                       -- sync and the outbox
                        or access.has('pmo')                        -- portfolio-wide
                        or p_dept = any(access.depts())             -- anyone in a department I belong to
                        or (access.has('unit-head') and p_unit = access.unit())
                    $fn$;

                create or replace function access.can_read_department(p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select access.is_system()
                        or access.has('pmo')
                        or p_dept = any(access.depts())
                    $fn$;

                -- Only a head of *that* department, or the PMO, may change its configuration. Deliberately not
                -- "any dept-head": a head of Finance must not be able to retune IT's activity taxonomy.
                create or replace function access.can_write_department_config(p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select access.is_system()
                        or access.has('pmo')
                        or (access.has('dept-head') and p_dept = any(access.depts()))
                    $fn$;

                grant usage on schema directory to app_rw;
                grant select, insert, update, delete on all tables in schema directory to app_rw;
                alter default privileges in schema directory
                    grant select, insert, update, delete on tables to app_rw;
                """);

            // Every table below gets FORCE, so the policies apply even to a future owner-role connection.
            migrationBuilder.Sql("""
                alter table directory.department              enable row level security;
                alter table directory.department              force  row level security;
                alter table directory.unit                    enable row level security;
                alter table directory.unit                    force  row level security;
                alter table directory.person                  enable row level security;
                alter table directory.person                  force  row level security;
                alter table directory.person_unit             enable row level security;
                alter table directory.person_unit             force  row level security;
                alter table directory.functional_role         enable row level security;
                alter table directory.functional_role         force  row level security;
                alter table directory.person_functional_role  enable row level security;
                alter table directory.person_functional_role  force  row level security;
                alter table directory.department_config       enable row level security;
                alter table directory.department_config       force  row level security;
                alter table directory.department_config_audit enable row level security;
                alter table directory.department_config_audit force  row level security;
                """);

            migrationBuilder.Sql("""
                -- Departments and units: readable within scope, writable only by sync.
                create policy department_read on directory.department
                    for select using (access.can_read_department(id));
                create policy department_write on directory.department
                    for all using (access.is_system()) with check (access.is_system());

                create policy unit_read on directory.unit
                    for select using (access.can_read_department(department_id));
                create policy unit_write on directory.unit
                    for all using (access.is_system()) with check (access.is_system());

                -- People and their memberships.
                create policy person_read on directory.person
                    for select using (access.can_read_person(id, primary_unit_id, primary_department_id));
                create policy person_write on directory.person
                    for all using (access.is_system()) with check (access.is_system());

                create policy person_unit_read on directory.person_unit
                    for select using (access.can_read_person(person_id, unit_id, department_id));
                create policy person_unit_write on directory.person_unit
                    for all using (access.is_system()) with check (access.is_system());

                -- Functional roles: the shared seeded ones (department_id null) are visible to everyone
                -- authenticated, because they are vocabulary rather than data.
                create policy functional_role_read on directory.functional_role
                    for select using (
                        access.is_scoped() and (department_id is null or access.can_read_department(department_id))
                    );
                create policy functional_role_write on directory.functional_role
                    for all using (access.is_system()) with check (access.is_system());

                create policy person_functional_role_read on directory.person_functional_role
                    for select using (
                        exists (
                            select 1 from directory.person p
                            where p.id = person_id
                              and access.can_read_person(p.id, p.primary_unit_id, p.primary_department_id)
                        )
                    );
                create policy person_functional_role_write on directory.person_functional_role
                    for all using (access.is_system()) with check (access.is_system());

                -- Config: anyone in the department reads it (boards need it to render); only that department's
                -- head or the PMO writes it.
                create policy department_config_read on directory.department_config
                    for select using (access.can_read_department(department_id));
                create policy department_config_write on directory.department_config
                    for update using (access.can_write_department_config(department_id))
                              with check (access.can_write_department_config(department_id));
                create policy department_config_insert on directory.department_config
                    for insert with check (access.can_write_department_config(department_id));

                -- Audit is append-only by policy, not merely by convention: there is no update policy and no
                -- delete policy, so Postgres refuses both regardless of what application code tries.
                create policy department_config_audit_read on directory.department_config_audit
                    for select using (access.can_read_department(department_id));
                create policy department_config_audit_append on directory.department_config_audit
                    for insert with check (access.can_write_department_config(department_id));
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop function if exists access.can_write_department_config(uuid);
                drop function if exists access.can_read_department(uuid);
                drop function if exists access.can_read_person(uuid, uuid, uuid);
                """);

            migrationBuilder.DropTable(
                name: "department_config",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "department_config_audit",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "person_functional_role",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "person_unit",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "functional_role",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "person",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "unit",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "department",
                schema: "directory");
        }
    }
}
