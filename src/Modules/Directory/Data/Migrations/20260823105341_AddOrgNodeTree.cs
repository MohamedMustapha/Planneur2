using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgNodeTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "org_level",
                schema: "directory",
                columns: table => new
                {
                    level_no = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    label_plural_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    head_label_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    people_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_level", x => x.level_no);
                    table.CheckConstraint("ck_org_level_level_no", "level_no between 1 and 8");
                });

            migrationBuilder.CreateTable(
                name: "org_node",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    level_no = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ancestor_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    head_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_node", x => x.id);
                    table.ForeignKey(
                        name: "fk_org_node_node_profile_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "directory",
                        principalTable: "node_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_org_node_org_level_level_no",
                        column: x => x.level_no,
                        principalSchema: "directory",
                        principalTable: "org_level",
                        principalColumn: "level_no",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_org_node_org_node_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "directory",
                        principalTable: "org_node",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_org_level_code",
                schema: "directory",
                table: "org_level",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_node_head_person_id",
                schema: "directory",
                table: "org_node",
                column: "head_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_node_level_no",
                schema: "directory",
                table: "org_node",
                column: "level_no");

            migrationBuilder.CreateIndex(
                name: "ix_org_node_parent_id_code",
                schema: "directory",
                table: "org_node",
                columns: new[] { "parent_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_node_parent_id_level_no",
                schema: "directory",
                table: "org_node",
                columns: new[] { "parent_id", "level_no" });

            migrationBuilder.CreateIndex(
                name: "ix_org_node_profile_id",
                schema: "directory",
                table: "org_node",
                column: "profile_id");

            migrationBuilder.Sql("""
                create index ix_org_node_ancestor_ids on directory.org_node using gin (ancestor_ids);

                create unique index ux_org_node_root_code on directory.org_node (code)
                    where parent_id is null;
                """);

            migrationBuilder.Sql("""
                create or replace function directory.org_node_before_write() returns trigger
                    language plpgsql as $fn$
                declare
                    parent_path uuid[];
                    parent_level int;
                begin
                    if new.parent_id is null then
                        new.ancestor_ids := array[new.id];
                        return new;
                    end if;

                    if new.parent_id = new.id then
                        raise exception 'org_node % cannot be its own parent', new.id
                            using errcode = '23514';
                    end if;

                    select n.ancestor_ids, n.level_no into parent_path, parent_level
                    from directory.org_node n where n.id = new.parent_id;

                    if parent_path is null then
                        raise exception 'org_node % references unknown parent %', new.id, new.parent_id
                            using errcode = '23503';
                    end if;

                    if new.id = any(parent_path) then
                        raise exception 'org_node % would create a cycle under %', new.id, new.parent_id
                            using errcode = '23514';
                    end if;

                    if new.level_no <= parent_level then
                        raise exception 'org_node % at level % must sit below its parent at level %',
                            new.id, new.level_no, parent_level
                            using errcode = '23514';
                    end if;

                    new.ancestor_ids := parent_path || new.id;
                    return new;
                end
                $fn$;

                create or replace function directory.org_node_cascade_ancestors() returns trigger
                    language plpgsql as $fn$
                begin
                    update directory.org_node child
                       set parent_id = child.parent_id
                     where child.parent_id = new.id;
                    return null;
                end
                $fn$;

                create trigger org_node_before_write
                    before insert or update of parent_id, level_no on directory.org_node
                    for each row execute function directory.org_node_before_write();

                create trigger org_node_cascade_ancestors
                    after update of parent_id, level_no on directory.org_node
                    for each row when (old.ancestor_ids is distinct from new.ancestor_ids)
                    execute function directory.org_node_cascade_ancestors();
                """);

            migrationBuilder.Sql("""
                insert into directory.org_level
                    (level_no, code, label_key, label_plural_key, head_label_key, people_allowed, is_optional)
                values
                    (1, 'L1', 'directory.level.l1', 'directory.level.l1.plural', 'directory.level.l1.head', true, false),
                    (2, 'L2', 'directory.level.l2', 'directory.level.l2.plural', 'directory.level.l2.head', true, false),
                    (3, 'L3', 'directory.level.l3', 'directory.level.l3.plural', 'directory.level.l3.head', true, true),
                    (4, 'L4', 'directory.level.l4', 'directory.level.l4.plural', 'directory.level.l4.head', true, true)
                on conflict (level_no) do nothing;
                """);

            migrationBuilder.Sql("""
                create or replace function access.can_read_org_node(p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select access.is_scoped()
                    $fn$;

                create or replace function access.can_write_org_node(p_node uuid)
                    returns boolean language sql stable as $fn$
                    select access.is_system() or access.has('pmo') or access.has('admin')
                    $fn$;

                alter table directory.org_level enable row level security;
                alter table directory.org_level force row level security;
                alter table directory.org_node enable row level security;
                alter table directory.org_node force row level security;

                create policy org_level_read on directory.org_level
                    for select using (access.is_scoped());
                create policy org_level_write on directory.org_level
                    for all using (access.is_system() or access.has('admin'))
                    with check (access.is_system() or access.has('admin'));

                create policy org_node_read on directory.org_node
                    for select using (access.can_read_org_node(ancestor_ids));
                create policy org_node_write on directory.org_node
                    for all using (access.can_write_org_node(id))
                    with check (access.can_write_org_node(id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop trigger if exists org_node_cascade_ancestors on directory.org_node;
                drop trigger if exists org_node_before_write on directory.org_node;
                drop function if exists directory.org_node_cascade_ancestors();
                drop function if exists directory.org_node_before_write();
                drop function if exists access.can_read_org_node(uuid[]);
                drop function if exists access.can_write_org_node(uuid);
                """);

            migrationBuilder.DropTable(
                name: "org_node",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "org_level",
                schema: "directory");
        }
    }
}
