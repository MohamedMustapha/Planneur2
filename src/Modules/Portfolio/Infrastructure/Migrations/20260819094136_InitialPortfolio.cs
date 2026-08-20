using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Portfolio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPortfolio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "portfolio");

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "portfolio",
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
                name: "portfolio_item",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    considered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_transition",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    portfolio_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    to_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_reversal = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_transition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iteration",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    portfolio_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    length = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iteration", x => x.id);
                    table.ForeignKey(
                        name: "fk_iteration_portfolio_item_portfolio_item_id",
                        column: x => x.portfolio_item_id,
                        principalSchema: "portfolio",
                        principalTable: "portfolio_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_iteration_portfolio_item_id_sequence",
                schema: "portfolio",
                table: "iteration",
                columns: new[] { "portfolio_item_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "portfolio",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_item_department_id",
                schema: "portfolio",
                table: "portfolio_item",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_item_project_id",
                schema: "portfolio",
                table: "portfolio_item",
                column: "project_id",
                unique: true,
                filter: "project_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_item_state_priority",
                schema: "portfolio",
                table: "portfolio_item",
                columns: new[] { "state", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_transition_department_id",
                schema: "portfolio",
                table: "portfolio_transition",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_transition_portfolio_item_id_decided_at",
                schema: "portfolio",
                table: "portfolio_transition",
                columns: new[] { "portfolio_item_id", "decided_at" });

            // =====================================================================================================
            // RLS for the Portfolio module.
            //
            // Delegates to the shared access.* predicates from S2 wherever a project exists, which is what the
            // "module migrations use the shared predicates" architecture rule requires. What Portfolio adds is the
            // case those predicates cannot express: a considered candidate has no project yet, so nothing can be
            // derived from team membership and the sponsoring department is the only scope there is.
            // =====================================================================================================
            migrationBuilder.Sql("""
                grant usage on schema portfolio to app_rw;
                grant select, insert, update, delete on all tables in schema portfolio to app_rw;
                alter default privileges in schema portfolio
                    grant select, insert, update, delete on tables to app_rw;
                """);

            migrationBuilder.Sql("""
                alter table portfolio.portfolio_item       enable row level security;
                alter table portfolio.portfolio_item       force  row level security;
                alter table portfolio.iteration            enable row level security;
                alter table portfolio.iteration            force  row level security;
                alter table portfolio.portfolio_transition enable row level security;
                alter table portfolio.portfolio_transition force  row level security;
                """);

            migrationBuilder.Sql("""
                -- The portfolio is the department's shared plan, so reading it is deliberately wide: anyone in the
                -- sponsoring department sees its candidates, and once a project exists the project's own audience
                -- sees it too. Narrowing this to heads would mean a team could not see the roadmap they are on.
                create function portfolio.can_read_item(p_project uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or p_dept = any(access.depts())
                        or (p_project is not null and access.can_read_project(p_project, p_dept))
                        -- No project yet: the candidate is only visible inside the department that raised it,
                        -- which is the whole scope a stub has.
                        or (p_project is null and p_dept = any(access.depts()))
                    , false)
                    $fn$;

                -- Writing moves money and people. Anyone who runs delivery inside the sponsoring department, the
                -- PMO, or whoever may already write the underlying project — the last clause is what lets a project
                -- lead close an iteration on work they run without making them a head.
                --
                -- Unit heads are here deliberately, and it is not merely permissive: the transition endpoints admit
                -- them through the delivery-lead and any-head policies, so leaving them out would let a unit head
                -- past the door only to be refused by RLS — which surfaces as a 404 on an item they can plainly see
                -- on the board. A policy and a predicate that disagree produce exactly that kind of bug report.
                create function portfolio.can_write_item(p_project uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or (access.has('dept-head') and p_dept = any(access.depts()))
                        or (access.has('unit-head') and p_dept = any(access.depts()))
                        or (p_project is not null and access.leads_project(p_project))
                    , false)
                    $fn$;

                alter function portfolio.can_read_item(uuid, uuid) owner to app_owner;
                alter function portfolio.can_write_item(uuid, uuid) owner to app_owner;
                grant execute on function portfolio.can_read_item(uuid, uuid) to app_rw;
                grant execute on function portfolio.can_write_item(uuid, uuid) to app_rw;
                """);

            migrationBuilder.Sql("""
                create policy portfolio_item_read on portfolio.portfolio_item
                    for select using (portfolio.can_read_item(project_id, department_id));

                create policy portfolio_item_write on portfolio.portfolio_item
                    for all using (portfolio.can_write_item(project_id, department_id))
                    with check (portfolio.can_write_item(project_id, department_id));
                """);

            migrationBuilder.Sql("""
                -- Children follow the parent through an EXISTS, so who can see an iteration changes with the item
                -- and cannot drift because one policy of three was updated.
                create policy iteration_read on portfolio.iteration
                    for select using (
                        exists (select 1 from portfolio.portfolio_item i where i.id = portfolio_item_id)
                    );
                create policy iteration_write on portfolio.iteration
                    for all using (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = portfolio_item_id
                              and portfolio.can_write_item(i.project_id, i.department_id))
                    )
                    with check (
                        access.is_system()
                        or exists (
                            select 1 from portfolio.portfolio_item i
                            where i.id = portfolio_item_id
                              and portfolio.can_write_item(i.project_id, i.department_id))
                    );
                """);

            migrationBuilder.Sql("""
                -- The trail scopes on its own denormalised department rather than joining back to the item: the
                -- record of why something was déphasé has to stay readable even to someone who can no longer read
                -- the item, and it must not silently vanish if the item is ever removed.
                create policy portfolio_transition_read on portfolio.portfolio_transition
                    for select using (
                        access.is_system()
                        or access.has('pmo')
                        or department_id = any(access.depts())
                        or decided_by = access.uid()
                    );

                -- Insert only. An audit trail that anyone can update or delete is not one, so there is deliberately
                -- no policy for those commands and FORCE ROW LEVEL SECURITY makes the absence bite.
                create policy portfolio_transition_append on portfolio.portfolio_transition
                    for insert with check (
                        access.is_system()
                        or portfolio.can_write_item(null::uuid, department_id)
                        or decided_by = access.uid()
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "iteration",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "portfolio_transition",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "portfolio_item",
                schema: "portfolio");

            migrationBuilder.Sql("""
                drop function if exists portfolio.can_write_item(uuid, uuid);
                drop function if exists portfolio.can_read_item(uuid, uuid);
                """);
        }
    }
}
