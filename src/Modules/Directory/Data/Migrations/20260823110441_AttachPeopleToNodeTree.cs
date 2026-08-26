using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AttachPeopleToNodeTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "home_node_id",
                schema: "directory",
                table: "person",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid[]>(
                name: "node_ancestor_ids",
                schema: "directory",
                table: "person",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.CreateIndex(
                name: "ix_person_home_node_id",
                schema: "directory",
                table: "person",
                column: "home_node_id");

            migrationBuilder.Sql("""
                create or replace function directory.org_node_cascade_ancestors() returns trigger
                    language plpgsql as $fn$
                begin
                    perform access.refresh_node_paths(new.id);

                    update directory.org_node child
                       set parent_id = child.parent_id
                     where child.parent_id = new.id;

                    return null;
                end
                $fn$;
                """);

            migrationBuilder.Sql("""
                set local app.roles = 'system';

                update directory.person p
                   set home_node_id = coalesce(
                           (select n.id from directory.org_node n where n.id = p.primary_unit_id),
                           (select n.id from directory.org_node n where n.id = p.primary_department_id),
                           'd1000000-0000-0000-0000-000000000001')
                 where p.home_node_id = '00000000-0000-0000-0000-000000000000';

                update directory.person p
                   set node_ancestor_ids = n.ancestor_ids
                  from directory.org_node n
                 where n.id = p.home_node_id
                   and p.node_ancestor_ids is distinct from n.ancestor_ids;
                """);

            migrationBuilder.Sql("""
                create trigger person_copy_node_path
                    before insert or update of home_node_id on directory.person
                    for each row execute function access.copy_person_node_path();

                insert into access.node_scoped_table
                    (schema_name, table_name, node_column, ancestors_column)
                values ('directory', 'person', 'home_node_id', 'node_ancestor_ids')
                on conflict do nothing;

                create index ix_person_node_ancestor_ids
                    on directory.person using gin (node_ancestor_ids);
                """);

            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.attach_to_node_tree('access', 'project_membership', null, 'department_id', true);
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop index if exists directory.ix_person_node_ancestor_ids;
                drop trigger if exists person_copy_node_path on directory.person;
                delete from access.node_scoped_table
                    where schema_name = 'directory' and table_name = 'person';
                select access.detach_from_node_tree('access', 'project_membership');
                """);

            migrationBuilder.DropIndex(
                name: "ix_person_home_node_id",
                schema: "directory",
                table: "person");

            migrationBuilder.DropColumn(
                name: "home_node_id",
                schema: "directory",
                table: "person");

            migrationBuilder.DropColumn(
                name: "node_ancestor_ids",
                schema: "directory",
                table: "person");
        }
    }
}
