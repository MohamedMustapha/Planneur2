using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "profile_id",
                schema: "directory",
                table: "unit",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "profile_id",
                schema: "directory",
                table: "department",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "node_profile",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    activity_taxonomy_json = table.Column<string>(type: "jsonb", nullable: true),
                    board_archetypes = table.Column<string[]>(type: "text[]", nullable: true),
                    item_types = table.Column<string[]>(type: "text[]", nullable: true),
                    capabilities_json = table.Column<string>(type: "jsonb", nullable: true),
                    solves_categories = table.Column<string[]>(type: "text[]", nullable: true),
                    budget_defaults_json = table.Column<string>(type: "jsonb", nullable: true),
                    headline_pattern = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_node_profile", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_unit_profile_id",
                schema: "directory",
                table: "unit",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_department_profile_id",
                schema: "directory",
                table: "department",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_node_profile_code",
                schema: "directory",
                table: "node_profile",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_department_node_profile_profile_id",
                schema: "directory",
                table: "department",
                column: "profile_id",
                principalSchema: "directory",
                principalTable: "node_profile",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_unit_node_profile_profile_id",
                schema: "directory",
                table: "unit",
                column: "profile_id",
                principalSchema: "directory",
                principalTable: "node_profile",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                alter table directory.node_profile enable row level security;
                alter table directory.node_profile force  row level security;
                """);

            migrationBuilder.Sql("""
                -- Profiles are vocabulary, like functional roles: everyone scoped reads them, because a board
                -- cannot render without the profile in force and a picker cannot offer what it may not see.
                create policy node_profile_read on directory.node_profile
                    for select using (access.is_scoped());
                create policy node_profile_write on directory.node_profile
                    for all using (access.can_author_node_profile())
                            with check (access.can_author_node_profile());

                -- Attaching is a decision about the node, not about the profile, so it is gated separately and
                -- more widely. These are additional permissive policies: the existing sync-only write policies
                -- stay exactly as they were, and a head gains UPDATE on their own branch and nothing else.
                create policy department_attach_profile on directory.department
                    for update using (access.can_attach_node_profile(id))
                               with check (access.can_attach_node_profile(id));

                create policy unit_attach_profile on directory.unit
                    for update using (access.can_attach_node_profile(department_id))
                               with check (access.can_attach_node_profile(department_id));
                """);

            // Example profiles, shipped as data rather than as classes (v2 §10.2). An administrator may delete
            // all three and author their own without a code change, which is the property the whole slice exists
            // to have — so these are seeded with `on conflict do nothing` and never re-asserted afterwards.
            //
            // The subtypes below are illustrations of three different trades, not a taxonomy the platform
            // believes in. Nothing reads these codes; the four parent buckets are the only fixed vocabulary.
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                insert into directory.node_profile
                    (id, code, label_key, activity_taxonomy_json, board_archetypes, item_types,
                     capabilities_json, solves_categories, budget_defaults_json, headline_pattern,
                     created_at, modified_at, modified_by)
                values
                    ('d0000000-0000-0000-0000-000000000001', 'DELIVERY', 'profile.delivery',
                     $json${"types":[
                        {"code":"dev","parent":"project-build","labelKey":"activity.type.dev"},
                        {"code":"architecture","parent":"project-build","labelKey":"activity.type.architecture"},
                        {"code":"testing","parent":"project-build","labelKey":"activity.type.testing"},
                        {"code":"deployment","parent":"project-build","labelKey":"activity.type.deployment"},
                        {"code":"incident","parent":"project-run","labelKey":"activity.type.incident"},
                        {"code":"request","parent":"project-run","labelKey":"activity.type.request"},
                        {"code":"patching","parent":"project-run","labelKey":"activity.type.patching"},
                        {"code":"on-call","parent":"project-run","labelKey":"activity.type.on-call"}
                     ]}$json$,
                     array['task-progress','week-grid'], array['project','product','run-service'],
                     $json${"integrations":true,"shift_scheduling":false,"work_order_pool":false,
                             "task_progress":true,"kudos":true,"budget":true,"strategy":true}$json$,
                     array['tooling','technical-debt'],
                     $json${"project-build":"capex","project-run":"opex"}$json$,
                     '{people} pers · ~{hours} h · {c1} {c1_label} · {c2} {c2_label} · risques : {risks}',
                     now(), now(), 'seed'),

                    ('d0000000-0000-0000-0000-000000000002', 'DISPATCH', 'profile.dispatch',
                     $json${"types":[
                        {"code":"triage","parent":"project-run","labelKey":"activity.type.triage"},
                        {"code":"intervention","parent":"project-run","labelKey":"activity.type.intervention"},
                        {"code":"escalation","parent":"project-run","labelKey":"activity.type.escalation"},
                        {"code":"on-call","parent":"project-run","labelKey":"activity.type.on-call"}
                     ]}$json$,
                     array['work-orders'], array['run-service'],
                     $json${"integrations":true,"shift_scheduling":true,"work_order_pool":true,
                             "task_progress":false,"kudos":true,"budget":false,"strategy":false}$json$,
                     array['incident','quality-of-life'],
                     $json${"project-run":"opex"}$json$,
                     '{people} pers · ~{hours} h · {c1} {c1_label} · {c2} {c2_label}',
                     now(), now(), 'seed'),

                    ('d0000000-0000-0000-0000-000000000003', 'ADVISORY', 'profile.advisory',
                     $json${"types":[
                        {"code":"research","parent":"quality-of-life","labelKey":"activity.type.research"},
                        {"code":"analysis","parent":"quality-of-life","labelKey":"activity.type.analysis"},
                        {"code":"note-production","parent":"quality-of-life","labelKey":"activity.type.note"},
                        {"code":"watch","parent":"quality-of-life","labelKey":"activity.type.watch"}
                     ]}$json$,
                     array['week-grid'], array['study'],
                     $json${"integrations":false,"shift_scheduling":false,"work_order_pool":false,
                             "task_progress":false,"kudos":true,"budget":false,"strategy":true}$json$,
                     array['process'],
                     $json${"quality-of-life":"opex"}$json$,
                     '{people} pers · ~{hours} h · {c1} {c1_label}',
                     now(), now(), 'seed')
                on conflict (code) do nothing;

                set local app.roles = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy if exists unit_attach_profile       on directory.unit;
                drop policy if exists department_attach_profile on directory.department;
                drop policy if exists node_profile_write        on directory.node_profile;
                drop policy if exists node_profile_read         on directory.node_profile;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_department_node_profile_profile_id",
                schema: "directory",
                table: "department");

            migrationBuilder.DropForeignKey(
                name: "fk_unit_node_profile_profile_id",
                schema: "directory",
                table: "unit");

            migrationBuilder.DropTable(
                name: "node_profile",
                schema: "directory");

            migrationBuilder.DropIndex(
                name: "ix_unit_profile_id",
                schema: "directory",
                table: "unit");

            migrationBuilder.DropIndex(
                name: "ix_department_profile_id",
                schema: "directory",
                table: "department");

            migrationBuilder.DropColumn(
                name: "profile_id",
                schema: "directory",
                table: "unit");

            migrationBuilder.DropColumn(
                name: "profile_id",
                schema: "directory",
                table: "department");
        }
    }
}
