using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Integrations.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integrations");

            migrationBuilder.CreateTable(
                name: "external_connection",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    base_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    auth_ref = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    project_or_queue = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    current_sprint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    poll_interval = table.Column<TimeSpan>(type: "interval", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_sync_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_sync_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    last_sync_item_count = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_connection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "integrations",
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
                name: "external_mapping",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_mapping", x => x.id);
                    table.CheckConstraint("ck_external_mapping_target", "(kind in ('area-path', 'iteration') and project_id is not null and unit_id is null)\nor (kind = 'assignment-group' and unit_id is not null and project_id is null)");
                    table.ForeignKey(
                        name: "fk_external_mapping_external_connection_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "integrations",
                        principalTable: "external_connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_work_item",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    assigned_to_ldap_uid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    assigned_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sprint_or_queue = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    is_current_sprint = table.Column<bool>(type: "boolean", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    estimated_hours = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    updated_at_source = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    mirror_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_work_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_work_item_external_connection_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "integrations",
                        principalTable: "external_connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_connection_active",
                schema: "integrations",
                table: "external_connection",
                column: "active");

            migrationBuilder.CreateIndex(
                name: "ix_external_connection_department_id_provider",
                schema: "integrations",
                table: "external_connection",
                columns: new[] { "department_id", "provider" });

            migrationBuilder.CreateIndex(
                name: "ix_external_mapping_connection_id_kind_external_value",
                schema: "integrations",
                table: "external_mapping",
                columns: new[] { "connection_id", "kind", "external_value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_mapping_department_id",
                schema: "integrations",
                table: "external_mapping",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_work_item_connection_id_external_id",
                schema: "integrations",
                table: "external_work_item",
                columns: new[] { "connection_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_work_item_mirror_state_assigned_person_id",
                schema: "integrations",
                table: "external_work_item",
                columns: new[] { "mirror_state", "assigned_person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_external_work_item_mirror_state_project_id_is_current_sprint",
                schema: "integrations",
                table: "external_work_item",
                columns: new[] { "mirror_state", "project_id", "is_current_sprint" });

            migrationBuilder.CreateIndex(
                name: "ix_external_work_item_mirror_state_unit_id_assigned_person_id",
                schema: "integrations",
                table: "external_work_item",
                columns: new[] { "mirror_state", "unit_id", "assigned_person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "integrations",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            // =====================================================================================================
            // Access predicates and RLS policies (conventions.md 4, visibility-matrix.md 3 and 4).
            //
            // S10 states the rule in operational terms: "a dev sees DevOps items assigned to them or in their
            // project's current sprint; a helpdesk agent sees ServiceNow items in their unit's queue; heads see
            // their department's items; PMO all." Read as a predicate over a mirror row, that is the same shape
            // the activity rule already has - owner, unit, project, department - which is not a coincidence: a
            // mirrored item is a piece of work somebody may end up booking against, and the two must not disagree
            // about who may see it.
            //
            // It is a separate function rather than a reuse of can_read_activity for one reason: the owner column
            // means something different. An activity's owner logged it; a mirror row's assignee was named by
            // another system, and may be somebody this platform has never heard of. Sharing a predicate would tie
            // those two facts together permanently.
            // =====================================================================================================
            migrationBuilder.Sql("""
                create or replace function access.can_read_external_item(
                    p_project uuid, p_unit uuid, p_dept uuid, p_assignee uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_assignee = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        -- The helpdesk case. A queue maps to a unit, and everyone in that unit works it: the 6a
                        -- pool is a shared list by design, and a per-person rule would empty it.
                        or (p_unit is not null and p_unit = access.unit())
                        or (access.has('dept-head') and p_dept = any(access.depts()))
                        -- The dev case. Not "the current sprint" - being on the project is what grants sight of
                        -- its work, and which sprint an item sits in is a filter the caller applies rather than a
                        -- permission. Encoding the sprint here would mean an item silently vanishing from a
                        -- developer's view on the morning somebody rolled the iteration over.
                        or (p_project is not null and access.on_project(p_project))
                        or access.leads_project(p_project)
                        -- Cross-department knowledge flow, stopping at heads exactly as it does everywhere else.
                        or (access.has('dept-head') and access.project_in_my_depts(p_project))
                    , false)
                    $fn$;

                grant usage on schema integrations to app_rw;
                grant select, insert, update, delete on all tables in schema integrations to app_rw;
                alter default privileges in schema integrations
                    grant select, insert, update, delete on tables to app_rw;
                """);

            // Every table gets FORCE, so the policies apply even to a future owner-role connection.
            migrationBuilder.Sql("""
                alter table integrations.external_connection enable row level security;
                alter table integrations.external_connection force row level security;
                alter table integrations.external_mapping enable row level security;
                alter table integrations.external_mapping force row level security;
                alter table integrations.external_work_item enable row level security;
                alter table integrations.external_work_item force row level security;
                """);

            migrationBuilder.Sql("""
                -- CONFIGURATION. Read and write are the same predicate here, and it is the one department
                -- settings already uses: S10 says connections are "visible/editable to dept-head/PMO only",
                -- which is precisely what can_write_department_config decides. Defining a second, identical
                -- can_write_connection would give the matrix two places to be changed and one to be forgotten.
                --
                -- Read equalling write is unusual in this system and deliberate: a connection row names a
                -- department's supplier, its queue, and the secret it authenticates with. There is no audience
                -- for that between "administers this department" and "nobody".
                create policy external_connection_read on integrations.external_connection
                    for select using (access.can_write_department_config(department_id));
                create policy external_connection_write on integrations.external_connection
                    for all using (access.can_write_department_config(department_id))
                        with check (access.can_write_department_config(department_id));

                create policy external_mapping_read on integrations.external_mapping
                    for select using (access.can_write_department_config(department_id));
                create policy external_mapping_write on integrations.external_mapping
                    for all using (access.can_write_department_config(department_id))
                        with check (access.can_write_department_config(department_id));
                """);

            migrationBuilder.Sql("""
                -- THE MIRROR. Read by the matrix; written by nobody but the synchronizer.
                --
                -- The write policy is access.is_system() and nothing else, which is stronger than any endpoint
                -- policy could be. The module exposes no endpoint that writes a work item, but "there is no
                -- endpoint today" is a fact about this deploy; "the runtime role cannot write these rows outside
                -- a system-context job" is a fact about the database. S10's read-only guarantee is about the
                -- other system, and this is its mirror image: our copy is not editable here either, because a
                -- locally edited mirror row is a lie about somebody else's data that the next pull erases.
                create policy external_work_item_read on integrations.external_work_item
                    for select using (
                        access.can_read_external_item(project_id, unit_id, department_id, assigned_person_id));
                create policy external_work_item_write on integrations.external_work_item
                    for all using (access.is_system()) with check (access.is_system());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The predicate goes with the tables that use it. Nothing else references it, and leaving behind a
            // function whose only callers have been dropped is how an access schema accumulates rules nobody can
            // tell are dead.
            migrationBuilder.Sql("drop function if exists access.can_read_external_item(uuid, uuid, uuid, uuid);");

            migrationBuilder.DropTable(
                name: "external_mapping",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "external_work_item",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "external_connection",
                schema: "integrations");
        }
    }
}
