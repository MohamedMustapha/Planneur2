using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Portfolio.Infrastructure.Migrations
{
    /// <summary>
    /// v2 §01 §3.1: the discovery projection, readable org-wide; the identity card stays where it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>can_read_item</c> ends with <c>access.is_head() and not p_confidential</c>, which made cross-branch
    /// discovery a privilege of heads. The spec asks for the opposite reach and a much smaller answer: anybody
    /// authenticated may learn that a thing exists and what it is called, so that duplicate work surfaces before
    /// it is commissioned; only the people the matrix already admits may open the card.
    /// </para>
    /// <para>
    /// Row-level security cannot express that on one table, because it is row-level. A policy loose enough to show
    /// a stranger the name also shows them the estimate, the lead and the decision notes, since those live in the
    /// same row. So the narrow answer gets a row of its own, with its own policy, and the wide row is left exactly
    /// as strict as it was.
    /// </para>
    /// <para>
    /// A trigger maintains it rather than the application, so no command can forget: an item is created by the
    /// wizard, renamed by an editor, reclassified by an admin and moved through its lifecycle by a board, and a
    /// projection that four call sites remember to update is a projection that three of them will.
    /// </para>
    /// </remarks>
    public partial class AddOrgWideItemDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "item_discovery",
                schema: "portfolio",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    owner_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    confidential = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_discovery", x => x.item_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_discovery_owner_node_id",
                schema: "portfolio",
                table: "item_discovery",
                column: "owner_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_discovery_state",
                schema: "portfolio",
                table: "item_discovery",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_item_discovery_type",
                schema: "portfolio",
                table: "item_discovery",
                column: "type");

            migrationBuilder.Sql("""
                -- The item's own write rule, reached by id, because the projection row carries none of the
                -- columns can_write_item needs. Same predicate, one lookup -- not a second copy of it.
                create or replace function portfolio.can_write_item_by_id(p_item uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce((
                        select portfolio.can_write_item(
                                   i.id, i.project_id, i.node_id, i.node_ancestor_ids,
                                   i.lead_person_id, i.po_person_id)
                          from portfolio.portfolio_item i
                         where i.id = p_item
                    ), false)
                    $fn$;

                alter function portfolio.can_write_item_by_id(uuid) owner to app_owner;
                grant execute on function portfolio.can_write_item_by_id(uuid) to app_rw;

                alter table portfolio.item_discovery enable row level security;
                alter table portfolio.item_discovery force  row level security;

                -- The whole point of the slice, in one line: anybody with a session sees that the thing exists.
                -- Confidential opts an item out entirely rather than blurring it, because a row that says "there
                -- is something here you may not know about" is itself a disclosure.
                create policy item_discovery_read on portfolio.item_discovery
                    for select using (access.is_scoped() and not confidential);

                -- Writable by exactly whoever may write the item it projects, which in practice means the trigger
                -- below running inside their statement. Not "whoever may read it": a reader who could rewrite the
                -- projection could rename somebody else's item org-wide, or clear its confidential flag and expose
                -- it. And not a scope the trigger claims for itself either -- an elevation held across somebody
                -- else's statement is a wider hole than the one it closes.
                create policy item_discovery_write on portfolio.item_discovery
                    for all
                    using (access.is_system() or portfolio.can_write_item_by_id(item_id))
                    with check (access.is_system() or portfolio.can_write_item_by_id(item_id));
                """);

            // Maintained by a trigger rather than by the handlers, so no command can forget. It runs under the
            // caller, inside the statement that just wrote the item, and the write policy above is what decides
            // whether that was allowed -- the projection is never a way to reach further than the item was.
            //
            // Two triggers because of DELETE: the row has to be found to be removed, and after the item is gone
            // the write policy can no longer see it to say yes.
            migrationBuilder.Sql("""
                create or replace function portfolio.project_item_discovery() returns trigger
                    language plpgsql
                as $fn$
                begin
                    insert into portfolio.item_discovery (
                        item_id, code, name, type, classification, category,
                        owner_node_id, state, summary, confidential, updated_at)
                    values (
                        new.id, new.code, new.name, new.type, new.classification, new.category,
                        new.owner_node_id, new.state, new.summary, new.confidential, now())
                    on conflict (item_id) do update set
                        code = excluded.code,
                        name = excluded.name,
                        type = excluded.type,
                        classification = excluded.classification,
                        category = excluded.category,
                        owner_node_id = excluded.owner_node_id,
                        state = excluded.state,
                        summary = excluded.summary,
                        confidential = excluded.confidential,
                        updated_at = excluded.updated_at;

                    return new;
                end
                $fn$;

                create or replace function portfolio.unproject_item_discovery() returns trigger
                    language plpgsql
                as $fn$
                begin
                    delete from portfolio.item_discovery where item_id = old.id;

                    return old;
                end
                $fn$;

                create trigger portfolio_item_project_discovery
                    after insert or update on portfolio.portfolio_item
                    for each row execute function portfolio.project_item_discovery();

                create trigger portfolio_item_unproject_discovery
                    before delete on portfolio.portfolio_item
                    for each row execute function portfolio.unproject_item_discovery();
                """);

            // Everything already in the catalog, so the projection is complete the moment it exists rather than
            // filling in as items happen to be touched.
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                insert into portfolio.item_discovery (
                    item_id, code, name, type, classification, category,
                    owner_node_id, state, summary, confidential, updated_at)
                select i.id, i.code, i.name, i.type, i.classification, i.category,
                       i.owner_node_id, i.state, i.summary, i.confidential, now()
                  from portfolio.portfolio_item i
                on conflict (item_id) do nothing;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop trigger if exists portfolio_item_unproject_discovery on portfolio.portfolio_item;
                drop trigger if exists portfolio_item_project_discovery on portfolio.portfolio_item;
                drop function if exists portfolio.unproject_item_discovery();
                drop function if exists portfolio.project_item_discovery();
                drop function if exists portfolio.can_write_item_by_id(uuid);
                """);

            migrationBuilder.DropTable(
                name: "item_discovery",
                schema: "portfolio");
        }
    }
}
