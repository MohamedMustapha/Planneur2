using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Portfolio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "portfolio",
                table: "portfolio_item",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<string>(
                name: "awaiting_version",
                schema: "portfolio",
                table: "portfolio_item",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "portfolio",
                table: "portfolio_item",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "classification",
                schema: "portfolio",
                table: "portfolio_item",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "portfolio",
                table: "portfolio_item",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "confidential",
                schema: "portfolio",
                table: "portfolio_item",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "portfolio",
                table: "portfolio_item",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "estimate_amount",
                schema: "portfolio",
                table: "portfolio_item",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lead_person_id",
                schema: "portfolio",
                table: "portfolio_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_node_id",
                schema: "portfolio",
                table: "portfolio_item",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "po_person_id",
                schema: "portfolio",
                table: "portfolio_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary",
                schema: "portfolio",
                table: "portfolio_item",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "type",
                schema: "portfolio",
                table: "portfolio_item",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Existing rows predate the identity card. They were all bespoke build endeavours sponsored by a
            // department, which is exactly what project/build/owner-node says — so the backfill states what was
            // already true rather than guessing.
            //
            // The suffix is a counter rather than a slice of the id. Ids here are UUIDv7, whose leading bytes are
            // a timestamp: a dozen items created in the same seeding run share their first six hex digits exactly,
            // so a name-plus-id-prefix code collides on the unique index below — which is created immediately
            // after this runs, and fails the whole deployment on data nobody could see was duplicated.
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                with numbered as (
                    select id,
                           upper(
                               coalesce(
                                   nullif(left(regexp_replace(name, '[^A-Za-z0-9]', '', 'g'), 8), ''),
                                   'ITEM')) as stem,
                           row_number() over (order by id) as ordinal
                      from portfolio.portfolio_item
                     where code = '' or code is null)
                update portfolio.portfolio_item item
                   set type = 'Project',
                       classification = 'Build',
                       currency = 'EUR',
                       owner_node_id = item.department_id,
                       code = numbered.stem || '-' || lpad(numbered.ordinal::text, 4, '0')
                  from numbered
                 where numbered.id = item.id;
                """);

            migrationBuilder.CreateTable(
                name: "epic",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    iteration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_epic", x => x.id);
                    table.ForeignKey(
                        name: "fk_epic_portfolio_item_item_id",
                        column: x => x.item_id,
                        principalSchema: "portfolio",
                        principalTable: "portfolio_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_dependency",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depends_on_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_dependency", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_dependency_portfolio_item_item_id",
                        column: x => x.item_id,
                        principalSchema: "portfolio",
                        principalTable: "portfolio_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_member",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    functional_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocation_percent = table.Column<int>(type: "integer", nullable: true),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_member", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_member_portfolio_item_item_id",
                        column: x => x.item_id,
                        principalSchema: "portfolio",
                        principalTable: "portfolio_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_item_code",
                schema: "portfolio",
                table: "portfolio_item",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_item_owner_node_id",
                schema: "portfolio",
                table: "portfolio_item",
                column: "owner_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_item_type_category",
                schema: "portfolio",
                table: "portfolio_item",
                columns: new[] { "type", "category" });

            migrationBuilder.CreateIndex(
                name: "ix_epic_item_id_sequence",
                schema: "portfolio",
                table: "epic",
                columns: new[] { "item_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_epic_iteration_id",
                schema: "portfolio",
                table: "epic",
                column: "iteration_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_dependency_depends_on_item_id",
                schema: "portfolio",
                table: "item_dependency",
                column: "depends_on_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_dependency_item_id_depends_on_item_id",
                schema: "portfolio",
                table: "item_dependency",
                columns: new[] { "item_id", "depends_on_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_member_item_id_person_id",
                schema: "portfolio",
                table: "item_member",
                columns: new[] { "item_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_item_member_node_id",
                schema: "portfolio",
                table: "item_member",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_member_person_id",
                schema: "portfolio",
                table: "item_member",
                column: "person_id");
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.attach_to_node_tree('portfolio', 'item_member', 'node_id', null, true);
                """);

            migrationBuilder.Sql("""
                alter table portfolio.epic enable row level security;
                alter table portfolio.epic force row level security;
                alter table portfolio.item_dependency enable row level security;
                alter table portfolio.item_dependency force row level security;
                alter table portfolio.item_member enable row level security;
                alter table portfolio.item_member force row level security;
                """);

            // Membership is scoped by its own node rather than by the item it belongs to, and that is not a
            // shortcut. The item's own read rule asks "is the caller on this item", which reads membership; if
            // membership in turn asked the item, Postgres would recurse between the two policies and refuse the
            // query. Scoping membership on the node it records breaks the loop, and a person can always see their
            // own row, which is all portfolio.on_item ever needs to answer.
            migrationBuilder.Sql("""
                create or replace function portfolio.on_item(p_item uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce((
                        select true from portfolio.item_member m
                        where m.item_id = p_item
                          and m.person_id = access.uid()
                          and m."to" is null
                        limit 1
                    ), false)
                    $fn$;

                alter function portfolio.on_item(uuid) owner to app_owner;
                grant execute on function portfolio.on_item(uuid) to app_rw;

                create policy item_member_read on portfolio.item_member
                    for select using (
                        person_id = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or access.same_node(node_id)
                        or access.in_my_branch(node_ancestor_ids)
                        or access.in_my_subtree(node_ancestor_ids)
                        or access.is_head()
                    );
                """);

            // The catalog's whole purpose is that somebody can find out whether a thing already exists before
            // asking for it to be built. That means heads read across branches, which is wider than anything else
            // in the matrix — so it is bounded twice: only heads, and only items nobody marked confidential.
            migrationBuilder.Sql("""
                create or replace function portfolio.can_read_item(
                    p_item uuid,
                    p_project uuid,
                    p_node uuid,
                    p_ancestors uuid[],
                    p_confidential boolean)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or portfolio.on_item(p_item)
                        or access.same_node(p_node)
                        or access.in_my_branch(p_ancestors)
                        or access.in_my_subtree(p_ancestors)
                        or (p_project is not null and access.can_read_project(p_project, p_ancestors))
                        or (access.is_head() and not p_confidential)
                    , false)
                    $fn$;

                create or replace function portfolio.can_write_item(
                    p_item uuid,
                    p_project uuid,
                    p_node uuid,
                    p_ancestors uuid[],
                    p_lead uuid,
                    p_po uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or p_lead = access.uid()
                        or p_po = access.uid()
                        or access.in_my_subtree(p_ancestors)
                        or (access.is_head() and p_node = any(access.my_path()))
                        or (p_project is not null and access.leads_project(p_project))
                    , false)
                    $fn$;

                alter function portfolio.can_read_item(uuid, uuid, uuid, uuid[], boolean) owner to app_owner;
                alter function portfolio.can_write_item(uuid, uuid, uuid, uuid[], uuid, uuid) owner to app_owner;
                grant execute on function portfolio.can_read_item(uuid, uuid, uuid, uuid[], boolean) to app_rw;
                grant execute on function portfolio.can_write_item(uuid, uuid, uuid, uuid[], uuid, uuid) to app_rw;
                """);

            migrationBuilder.Sql("""
                drop policy portfolio_item_read on portfolio.portfolio_item;
                create policy portfolio_item_read on portfolio.portfolio_item
                    for select using (
                        portfolio.can_read_item(id, project_id, node_id, node_ancestor_ids, confidential)
                    );

                drop policy portfolio_item_write on portfolio.portfolio_item;
                create policy portfolio_item_write on portfolio.portfolio_item
                    for all using (
                        portfolio.can_write_item(
                            id, project_id, node_id, node_ancestor_ids, lead_person_id, po_person_id)
                    )
                    with check (
                        portfolio.can_write_item(
                            id, project_id, node_id, node_ancestor_ids, lead_person_id, po_person_id)
                    );

                drop policy iteration_write on portfolio.iteration;
                create policy iteration_write on portfolio.iteration
                    for all using (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = portfolio_item_id
                              and portfolio.can_write_item(
                                  i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                  i.lead_person_id, i.po_person_id))
                    )
                    with check (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = portfolio_item_id
                              and portfolio.can_write_item(
                                  i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                  i.lead_person_id, i.po_person_id))
                    );

                drop policy portfolio_transition_append on portfolio.portfolio_transition;
                create policy portfolio_transition_append on portfolio.portfolio_transition
                    for insert with check (
                        access.is_system()
                        or decided_by = access.uid()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = portfolio_item_id
                              and portfolio.can_write_item(
                                  i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                  i.lead_person_id, i.po_person_id))
                    );
                """);

            // Epics and dependencies follow their item through an EXISTS, so who can see them changes with the
            // card and cannot drift because one policy of three was updated.
            migrationBuilder.Sql("""
                create policy epic_read on portfolio.epic
                    for select using (
                        exists (select 1 from portfolio.portfolio_item i where i.id = item_id)
                    );
                create policy epic_write on portfolio.epic
                    for all using (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = item_id
                              and portfolio.can_write_item(
                                  i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                  i.lead_person_id, i.po_person_id))
                    )
                    with check (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = item_id
                              and portfolio.can_write_item(
                                  i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                  i.lead_person_id, i.po_person_id))
                    );

                create policy item_dependency_read on portfolio.item_dependency
                    for select using (
                        exists (select 1 from portfolio.portfolio_item i where i.id = item_id)
                    );
                create policy item_dependency_write on portfolio.item_dependency
                    for all using (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = item_id
                              and portfolio.can_write_item(
                                  i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                  i.lead_person_id, i.po_person_id))
                    )
                    with check (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = item_id
                              and portfolio.can_write_item(
                                  i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                  i.lead_person_id, i.po_person_id))
                    );

                -- Three policies rather than one FOR ALL, and the reason is subtle enough to be worth stating:
                -- FOR ALL covers SELECT too, so a write rule that reaches back to the item would be evaluated
                -- while reading membership — and the item's own read rule asks whether the caller is a member.
                -- Postgres follows that round until it runs out of stack. Only the commands that actually write
                -- consult the item.
                create policy item_member_insert on portfolio.item_member
                    for insert with check (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = item_id
                              and portfolio.can_write_item(
                                  i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                  i.lead_person_id, i.po_person_id))
                    );

                create policy item_member_update on portfolio.item_member
                    for update using (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = item_id
                              and portfolio.can_write_item(
                                  i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                  i.lead_person_id, i.po_person_id))
                    )
                    with check (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = item_id
                              and portfolio.can_write_item(
                                  i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                  i.lead_person_id, i.po_person_id))
                    );

                create policy item_member_delete on portfolio.item_member
                    for delete using (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = item_id
                              and portfolio.can_write_item(
                                  i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                  i.lead_person_id, i.po_person_id))
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy portfolio_item_read on portfolio.portfolio_item;
                create policy portfolio_item_read on portfolio.portfolio_item
                    for select using (
                        portfolio.can_read_item(project_id, node_id, node_ancestor_ids)
                    );

                drop policy portfolio_item_write on portfolio.portfolio_item;
                create policy portfolio_item_write on portfolio.portfolio_item
                    for all using (portfolio.can_write_item(project_id, node_id, node_ancestor_ids))
                    with check (portfolio.can_write_item(project_id, node_id, node_ancestor_ids));

                drop policy iteration_write on portfolio.iteration;
                create policy iteration_write on portfolio.iteration
                    for all using (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = portfolio_item_id
                              and portfolio.can_write_item(i.project_id, i.node_id, i.node_ancestor_ids))
                    )
                    with check (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = portfolio_item_id
                              and portfolio.can_write_item(i.project_id, i.node_id, i.node_ancestor_ids))
                    );

                drop policy portfolio_transition_append on portfolio.portfolio_transition;
                create policy portfolio_transition_append on portfolio.portfolio_transition
                    for insert with check (
                        access.is_system()
                        or portfolio.can_write_item(null::uuid, node_id, node_ancestor_ids)
                        or decided_by = access.uid()
                    );

                drop function if exists portfolio.can_read_item(uuid, uuid, uuid, uuid[], boolean);
                drop function if exists portfolio.can_write_item(uuid, uuid, uuid, uuid[], uuid, uuid);
                drop function if exists portfolio.on_item(uuid);
                """);

            migrationBuilder.DropTable(
                name: "epic",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "item_dependency",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "item_member",
                schema: "portfolio");

            migrationBuilder.DropIndex(
                name: "ix_portfolio_item_code",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropIndex(
                name: "ix_portfolio_item_owner_node_id",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropIndex(
                name: "ix_portfolio_item_type_category",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "awaiting_version",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "classification",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "confidential",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "estimate_amount",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "lead_person_id",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "owner_node_id",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "po_person_id",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "summary",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.DropColumn(
                name: "type",
                schema: "portfolio",
                table: "portfolio_item");

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "portfolio",
                table: "portfolio_item",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
