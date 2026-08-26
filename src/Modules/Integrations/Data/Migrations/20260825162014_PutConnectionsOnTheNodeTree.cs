using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Integrations.Data.Migrations
{
    /// <summary>
    /// A connection is addressed by branch rather than by department (v2 §00 §3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>node_id</c> columns already exist: <c>attach_to_node_tree</c> added and backfilled them when this
    /// module was put behind the tree's policies. What changes here is which column the module <em>writes</em>.
    /// So there is no AddColumn — scaffolding produced three, because the model had never mapped what the SQL
    /// had already created, and adding them a second time would fail on any database that has run.
    /// </para>
    /// <para>
    /// <c>department_id</c> becomes nullable rather than dropped. Dropping it is the legacy decommission's job,
    /// and doing it here would take the shim's backstop away from four other modules in a migration whose subject
    /// is one.
    /// </para>
    /// </remarks>
    public partial class PutConnectionsOnTheNodeTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_external_mapping_department_id",
                schema: "integrations",
                table: "external_mapping");

            migrationBuilder.DropIndex(
                name: "ix_external_connection_department_id_provider",
                schema: "integrations",
                table: "external_connection");

            foreach (var table in new[] { "external_connection", "external_mapping", "external_work_item" })
            {
                migrationBuilder.AlterColumn<Guid>(
                    name: "department_id",
                    schema: "integrations",
                    table: table,
                    type: "uuid",
                    nullable: true,
                    oldClrType: typeof(Guid),
                    oldType: "uuid");
            }

            migrationBuilder.CreateIndex(
                name: "ix_external_mapping_node_id",
                schema: "integrations",
                table: "external_mapping",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_connection_node_id_provider",
                schema: "integrations",
                table: "external_connection",
                columns: new[] { "node_id", "provider" });

            // Two changes, and the second is the one with teeth.
            //
            // The mapping policies still read department_id, which the module has stopped writing. Left alone
            // they would refuse every mapping on a connection wired at a node that is not also a department.
            //
            // And a head may now configure a branch *beneath* the one they head, not only the one they head
            // exactly. That is what "a branch can wire its own source" needs: without it a service head can
            // configure the service and nothing under it, and every branch that wanted its own connection would
            // have to be handed a head role it should not have. Confined to this module's policies rather than
            // widened in can_write_node_config, whose other callers are somebody else's slice to reason about.
            migrationBuilder.Sql("""
                drop policy external_connection_read on integrations.external_connection;
                create policy external_connection_read on integrations.external_connection
                    for select using (
                        access.can_write_node_config(node_id) or access.below_my_nodes(node_id));
                drop policy external_connection_write on integrations.external_connection;
                create policy external_connection_write on integrations.external_connection
                    for all using (
                        access.can_write_node_config(node_id) or access.below_my_nodes(node_id))
                        with check (
                            access.can_write_node_config(node_id) or access.below_my_nodes(node_id));

                drop policy external_mapping_read on integrations.external_mapping;
                create policy external_mapping_read on integrations.external_mapping
                    for select using (
                        access.can_write_node_config(node_id) or access.below_my_nodes(node_id));
                drop policy external_mapping_write on integrations.external_mapping;
                create policy external_mapping_write on integrations.external_mapping
                    for all using (
                        access.can_write_node_config(node_id) or access.below_my_nodes(node_id))
                        with check (
                            access.can_write_node_config(node_id) or access.below_my_nodes(node_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy external_connection_read on integrations.external_connection;
                create policy external_connection_read on integrations.external_connection
                    for select using (access.can_write_node_config(node_id));
                drop policy external_connection_write on integrations.external_connection;
                create policy external_connection_write on integrations.external_connection
                    for all using (access.can_write_node_config(node_id))
                        with check (access.can_write_node_config(node_id));

                drop policy external_mapping_read on integrations.external_mapping;
                create policy external_mapping_read on integrations.external_mapping
                    for select using (access.can_write_node_config(department_id));
                drop policy external_mapping_write on integrations.external_mapping;
                create policy external_mapping_write on integrations.external_mapping
                    for all using (access.can_write_node_config(department_id))
                        with check (access.can_write_node_config(department_id));
                """);

            migrationBuilder.DropIndex(
                name: "ix_external_mapping_node_id",
                schema: "integrations",
                table: "external_mapping");

            migrationBuilder.DropIndex(
                name: "ix_external_connection_node_id_provider",
                schema: "integrations",
                table: "external_connection");

            // Back to the legacy address, from the node it was derived from in the first place. A row whose
            // branch no longer projects to a department keeps a null and is caught by the not-null below, which
            // is the honest failure: there is nowhere for it to go.
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                update integrations.external_connection s
                   set department_id = coalesce(s.department_id, d.id)
                  from directory.department d
                 where d.id = s.node_id;

                update integrations.external_mapping s
                   set department_id = coalesce(s.department_id, d.id)
                  from directory.department d
                 where d.id = s.node_id;

                update integrations.external_work_item s
                   set department_id = coalesce(s.department_id, d.id)
                  from directory.department d
                 where d.id = s.node_id;
                """);

            foreach (var table in new[] { "external_connection", "external_mapping", "external_work_item" })
            {
                migrationBuilder.AlterColumn<Guid>(
                    name: "department_id",
                    schema: "integrations",
                    table: table,
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true);
            }

            migrationBuilder.CreateIndex(
                name: "ix_external_mapping_department_id",
                schema: "integrations",
                table: "external_mapping",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_connection_department_id_provider",
                schema: "integrations",
                table: "external_connection",
                columns: new[] { "department_id", "provider" });
        }
    }
}
