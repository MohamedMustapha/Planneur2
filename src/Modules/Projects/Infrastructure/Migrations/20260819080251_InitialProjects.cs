using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Projects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "projects");

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "projects",
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
                name: "project",
                schema: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cost_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cost_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    owner_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_department",
                schema: "projects",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_lead_department = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_department", x => new { x.project_id, x.department_id });
                    table.ForeignKey(
                        name: "fk_project_department_project_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_member",
                schema: "projects",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    functional_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allocation_percent = table.Column<int>(type: "integer", nullable: true),
                    to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_member", x => new { x.project_id, x.person_id, x.from });
                    table.ForeignKey(
                        name: "fk_project_member_project_project_id",
                        column: x => x.project_id,
                        principalSchema: "projects",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "projects",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_project_code",
                schema: "projects",
                table: "project",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_lead_department_id",
                schema: "projects",
                table: "project",
                column: "lead_department_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_owner_person_id",
                schema: "projects",
                table: "project",
                column: "owner_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_department_department_id",
                schema: "projects",
                table: "project_department",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_active",
                schema: "projects",
                table: "project_member",
                columns: new[] { "project_id", "person_id" },
                filter: "\"to\" is null");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_department_id",
                schema: "projects",
                table: "project_member",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_person_id",
                schema: "projects",
                table: "project_member",
                column: "person_id");

            // =====================================================================================================
            // RLS for the Projects module.
            //
            // Every policy delegates to the shared access.* predicates from S2 rather than restating role logic —
            // which is what the "module migrations use the shared predicates" architecture rule enforces, and what
            // keeps the cross-department knowledge-flow rule defined in exactly one place.
            //
            // The one thing added on top is the owner clause. access.can_read_project cannot see project.owner,
            // because the predicates deliberately do not reach into another module's tables; the owner comparison
            // is local to this schema and belongs in the policy.
            // =====================================================================================================
            migrationBuilder.Sql("""
                grant usage on schema projects to app_rw;
                grant select, insert, update, delete on all tables in schema projects to app_rw;
                alter default privileges in schema projects
                    grant select, insert, update, delete on tables to app_rw;
                """);

            migrationBuilder.Sql("""
                alter table projects.project            enable row level security;
                alter table projects.project            force  row level security;
                alter table projects.project_department enable row level security;
                alter table projects.project_department force  row level security;
                alter table projects.project_member     enable row level security;
                alter table projects.project_member     force  row level security;
                """);

            migrationBuilder.Sql("""
                -- READ. A head sees any project touching their department, including the other departments'
                -- members on it: that is the cross-department knowledge flow the matrix calls for, and it is why a
                -- dept-head can open a project their own people merely contribute to.
                create policy project_read on projects.project
                    for select using (
                        access.can_read_project(id, lead_department_id)
                        or owner_person_id = access.uid()
                    );

                -- WRITE. The project's own lead or PO, a head of a contributing department, or the PMO.
                -- Deliberately narrower than read: plenty of people can see a project they must not re-cost.
                create policy project_write on projects.project
                    for all using (
                        access.is_system()
                        or access.has('pmo')
                        or owner_person_id = access.uid()
                        or (access.has('dept-head') and lead_department_id = any(access.depts()))
                        or (access.has('dept-head') and access.project_in_my_depts(id))
                    )
                    with check (
                        access.is_system()
                        or access.has('pmo')
                        or owner_person_id = access.uid()
                        or (access.has('dept-head') and lead_department_id = any(access.depts()))
                        or (access.has('dept-head') and access.project_in_my_depts(id))
                    );
                """);

            migrationBuilder.Sql("""
                -- Children follow the parent. Expressed as an EXISTS against projects.project so the rule lives in
                -- one place: change who can read a project and its departments and members follow automatically,
                -- rather than drifting because someone updated one policy of three.
                create policy project_department_read on projects.project_department
                    for select using (
                        exists (select 1 from projects.project p where p.id = project_id)
                    );
                create policy project_department_write on projects.project_department
                    for all using (
                        access.is_system() or exists (select 1 from projects.project p where p.id = project_id)
                    )
                    with check (
                        access.is_system() or exists (select 1 from projects.project p where p.id = project_id)
                    );

                create policy project_member_read on projects.project_member
                    for select using (
                        person_id = access.uid()
                        or exists (select 1 from projects.project p where p.id = project_id)
                    );
                create policy project_member_write on projects.project_member
                    for all using (
                        access.is_system() or exists (select 1 from projects.project p where p.id = project_id)
                    )
                    with check (
                        access.is_system() or exists (select 1 from projects.project p where p.id = project_id)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "project_department",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "project_member",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "project",
                schema: "projects");
        }
    }
}
