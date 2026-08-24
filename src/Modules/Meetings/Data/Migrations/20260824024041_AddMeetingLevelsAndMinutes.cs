using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Meetings.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingLevelsAndMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "level",
                schema: "meetings",
                table: "meeting_series",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "node");

            migrationBuilder.AddColumn<Guid[]>(
                name: "scope_ids",
                schema: "meetings",
                table: "meeting_series",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.CreateTable(
                name: "meeting_minutes",
                schema: "meetings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    author_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agenda = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    attendees = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    absentees = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    summary = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: true),
                    published = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_minutes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "action_item",
                schema: "meetings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    minutes_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    owner_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    link_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    link_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_action_item_meeting_minutes_minutes_id",
                        column: x => x.minutes_id,
                        principalSchema: "meetings",
                        principalTable: "meeting_minutes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meeting_decision",
                schema: "meetings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    minutes_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    rationale = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    decided_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_decision", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_decision_meeting_minutes_minutes_id",
                        column: x => x.minutes_id,
                        principalSchema: "meetings",
                        principalTable: "meeting_minutes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_action_item_link_type_link_id",
                schema: "meetings",
                table: "action_item",
                columns: new[] { "link_type", "link_id" });

            migrationBuilder.CreateIndex(
                name: "ix_action_item_minutes_id",
                schema: "meetings",
                table: "action_item",
                column: "minutes_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_item_owner_person_id_status_due",
                schema: "meetings",
                table: "action_item",
                columns: new[] { "owner_person_id", "status", "due" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_decision_minutes_id",
                schema: "meetings",
                table: "meeting_decision",
                column: "minutes_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_minutes_author_person_id",
                schema: "meetings",
                table: "meeting_minutes",
                column: "author_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_minutes_occurrence_id",
                schema: "meetings",
                table: "meeting_minutes",
                column: "occurrence_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meeting_minutes_scope_type_scope_id_occurred_at",
                schema: "meetings",
                table: "meeting_minutes",
                columns: new[] { "scope_type", "scope_id", "occurred_at" });

            migrationBuilder.Sql("""
                update meetings.meeting_series
                   set level = case
                       when kind in ('copil', 'service-review') then 'service'
                       when scope_type = 'project' then 'project'
                       when scope_type = 'unit' then 'unit'
                       else 'node'
                   end;
                """);

            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.attach_to_node_tree('meetings', 'meeting_minutes', null, 'scope_id', false);
                """);

            migrationBuilder.Sql("""
                grant select, insert, update, delete
                    on meetings.meeting_minutes, meetings.meeting_decision, meetings.action_item to app_rw;

                alter table meetings.meeting_minutes enable row level security;
                alter table meetings.meeting_minutes force row level security;
                alter table meetings.meeting_decision enable row level security;
                alter table meetings.meeting_decision force row level security;
                alter table meetings.action_item enable row level security;
                alter table meetings.action_item force row level security;
                """);

            migrationBuilder.Sql("""
                create or replace function access.can_write_minutes(
                    p_scope uuid, p_scope_ids uuid[], p_ancestors uuid[], p_author uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or p_author = access.uid()
                        or p_scope = any(access.headed_nodes())
                        or p_scope_ids && access.headed_nodes()
                        or access.in_my_subtree(p_ancestors)
                    , false)
                    $fn$;

                create or replace function access.can_read_minutes(
                    p_level text,
                    p_scope_type text,
                    p_scope uuid,
                    p_scope_ids uuid[],
                    p_ancestors uuid[],
                    p_author uuid,
                    p_published boolean)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.can_write_minutes(p_scope, p_scope_ids, p_ancestors, p_author)
                        or (p_published and (
                               p_scope_type = 'org'
                            or p_scope = any(access.my_path())
                            or p_scope_ids && access.my_path()
                            or (p_scope_type = 'project' and access.on_project(p_scope))
                            or access.in_my_subtree(p_ancestors)))
                    , false)
                    $fn$;
                """);

            migrationBuilder.Sql("""
                create policy minutes_read on meetings.meeting_minutes
                    for select using (access.can_read_minutes(
                        level, scope_type, scope_id, scope_ids, node_ancestor_ids, author_person_id, published));
                create policy minutes_insert on meetings.meeting_minutes
                    for insert with check (access.can_write_minutes(
                        scope_id, scope_ids, node_ancestor_ids, author_person_id));
                create policy minutes_update on meetings.meeting_minutes
                    for update using (access.can_write_minutes(
                        scope_id, scope_ids, node_ancestor_ids, author_person_id))
                    with check (access.can_write_minutes(
                        scope_id, scope_ids, node_ancestor_ids, author_person_id));
                create policy minutes_delete on meetings.meeting_minutes
                    for delete using (access.can_write_minutes(
                        scope_id, scope_ids, node_ancestor_ids, author_person_id));
                """);

            migrationBuilder.Sql("""
                create policy decision_read on meetings.meeting_decision
                    for select using (
                        exists (select 1 from meetings.meeting_minutes m where m.id = minutes_id));
                create policy decision_write on meetings.meeting_decision
                    for all using (
                        access.is_system()
                        or exists (
                            select 1 from meetings.meeting_minutes m
                            where m.id = minutes_id
                              and access.can_write_minutes(
                                  m.scope_id, m.scope_ids, m.node_ancestor_ids, m.author_person_id)))
                    with check (
                        access.is_system()
                        or exists (
                            select 1 from meetings.meeting_minutes m
                            where m.id = minutes_id
                              and access.can_write_minutes(
                                  m.scope_id, m.scope_ids, m.node_ancestor_ids, m.author_person_id)));
                """);

            migrationBuilder.Sql("""
                create policy action_read on meetings.action_item
                    for select using (
                        owner_person_id = access.uid()
                        or exists (select 1 from meetings.meeting_minutes m where m.id = minutes_id));
                create policy action_write on meetings.action_item
                    for all using (
                        access.is_system()
                        or owner_person_id = access.uid()
                        or exists (
                            select 1 from meetings.meeting_minutes m
                            where m.id = minutes_id
                              and access.can_write_minutes(
                                  m.scope_id, m.scope_ids, m.node_ancestor_ids, m.author_person_id)))
                    with check (
                        access.is_system()
                        or owner_person_id = access.uid()
                        or exists (
                            select 1 from meetings.meeting_minutes m
                            where m.id = minutes_id
                              and access.can_write_minutes(
                                  m.scope_id, m.scope_ids, m.node_ancestor_ids, m.author_person_id)));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop function if exists access.can_read_minutes(text, text, uuid, uuid[], uuid[], uuid, boolean);
                drop function if exists access.can_write_minutes(uuid, uuid[], uuid[], uuid);
                """);

            migrationBuilder.DropTable(
                name: "action_item",
                schema: "meetings");

            migrationBuilder.DropTable(
                name: "meeting_decision",
                schema: "meetings");

            migrationBuilder.DropTable(
                name: "meeting_minutes",
                schema: "meetings");

            migrationBuilder.DropColumn(
                name: "level",
                schema: "meetings",
                table: "meeting_series");

            migrationBuilder.DropColumn(
                name: "scope_ids",
                schema: "meetings",
                table: "meeting_series");
        }
    }
}
