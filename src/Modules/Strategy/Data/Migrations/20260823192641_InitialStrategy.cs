using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Strategy.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialStrategy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "strategy");

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "strategy",
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
                name: "strategy",
                schema: "strategy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    narrative = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: true),
                    owner_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "objective",
                schema: "strategy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    metric_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    baseline = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    target = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    current = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    due = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status_overridden = table.Column<bool>(type: "boolean", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_objective", x => x.id);
                    table.ForeignKey(
                        name: "fk_objective_strategy_strategy_id",
                        column: x => x.strategy_id,
                        principalSchema: "strategy",
                        principalTable: "strategy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "key_result",
                schema: "strategy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    target = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    current = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_key_result", x => x.id);
                    table.ForeignKey(
                        name: "fk_key_result_objective_objective_id",
                        column: x => x.objective_id,
                        principalSchema: "strategy",
                        principalTable: "objective",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "objective_contribution",
                schema: "strategy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_objective_contribution", x => x.id);
                    table.ForeignKey(
                        name: "fk_objective_contribution_objective_objective_id",
                        column: x => x.objective_id,
                        principalSchema: "strategy",
                        principalTable: "objective",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_key_result_objective_id",
                schema: "strategy",
                table: "key_result",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_objective_strategy_id",
                schema: "strategy",
                table: "objective",
                column: "strategy_id");

            migrationBuilder.CreateIndex(
                name: "ix_objective_contribution_objective_id_source_type_source_id",
                schema: "strategy",
                table: "objective_contribution",
                columns: new[] { "objective_id", "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_objective_contribution_source_type_source_id",
                schema: "strategy",
                table: "objective_contribution",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "strategy",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_strategy_scope_id_status",
                schema: "strategy",
                table: "strategy",
                columns: new[] { "scope_id", "status" });
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.attach_to_node_tree('strategy', 'strategy', null, 'scope_id', true);
                """);

            migrationBuilder.Sql("""
                grant usage on schema strategy to app_rw;
                grant select, insert, update, delete on all tables in schema strategy to app_rw;
                alter default privileges in schema strategy
                    grant select, insert, update, delete on tables to app_rw;

                alter table strategy.strategy enable row level security;
                alter table strategy.strategy force row level security;
                alter table strategy.objective enable row level security;
                alter table strategy.objective force row level security;
                alter table strategy.key_result enable row level security;
                alter table strategy.key_result force row level security;
                alter table strategy.objective_contribution enable row level security;
                alter table strategy.objective_contribution force row level security;
                """);

            // v2 §06.4. Two clauses do the interesting work. `in_my_branch` is the knowledge-flow widening the
            // platform already grants elsewhere: a member reads the strategy of the node they hang off, because
            // a strategy nobody below the head can see is a poster rather than a spine. `in_my_subtree` is
            // supervision — a head reads every strategy set beneath them, at any depth, with one array overlap
            // and no recursion.
            //
            // The last clause is the cross-scope read §06.4 asks for, and it is deliberately gated on being a
            // head: heads read each other's strategies so two bureaux do not commit to the same thing twice.
            // Nobody who is not a head sees outside their own path.
            migrationBuilder.Sql("""
                create or replace function access.can_read_strategy(p_scope uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.same_node(p_scope)
                        or p_scope = any(access.my_path())
                        or access.in_my_branch(p_ancestors)
                        or access.in_my_subtree(p_ancestors)
                        or access.is_head()
                    , false)
                    $fn$;

                create or replace function access.can_write_strategy(p_scope uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or p_scope = any(access.headed_nodes())
                        or access.in_my_subtree(p_ancestors)
                    , false)
                    $fn$;
                """);

            migrationBuilder.Sql("""
                create policy strategy_read on strategy.strategy
                    for select using (access.can_read_strategy(scope_id, node_ancestor_ids));

                -- Not FOR ALL: that would apply to SELECT too and narrow the read above to the heads who may
                -- write, which is exactly the failure this slice exists to avoid.
                create policy strategy_insert on strategy.strategy
                    for insert with check (access.can_write_strategy(scope_id, node_ancestor_ids));
                create policy strategy_update on strategy.strategy
                    for update using (access.can_write_strategy(scope_id, node_ancestor_ids))
                    with check (access.can_write_strategy(scope_id, node_ancestor_ids));
                create policy strategy_delete on strategy.strategy
                    for delete using (access.can_write_strategy(scope_id, node_ancestor_ids));
                """);

            // Objectives, key results and contributions follow their strategy through an EXISTS, so who may see
            // and change them moves with it and no predicate is restated. The EXISTS runs in the same session,
            // which means the read policy above is what it actually tests.
            migrationBuilder.Sql("""
                create policy objective_read on strategy.objective
                    for select using (
                        exists (select 1 from strategy.strategy s where s.id = strategy_id));
                create policy objective_write on strategy.objective
                    for all using (
                        access.is_system()
                        or exists (
                            select 1 from strategy.strategy s
                            where s.id = strategy_id
                              and access.can_write_strategy(s.scope_id, s.node_ancestor_ids)))
                    with check (
                        access.is_system()
                        or exists (
                            select 1 from strategy.strategy s
                            where s.id = strategy_id
                              and access.can_write_strategy(s.scope_id, s.node_ancestor_ids)));

                create policy key_result_read on strategy.key_result
                    for select using (
                        exists (select 1 from strategy.objective o where o.id = objective_id));
                create policy key_result_write on strategy.key_result
                    for all using (
                        access.is_system()
                        or exists (
                            select 1 from strategy.objective o
                            join strategy.strategy s on s.id = o.strategy_id
                            where o.id = objective_id
                              and access.can_write_strategy(s.scope_id, s.node_ancestor_ids)))
                    with check (
                        access.is_system()
                        or exists (
                            select 1 from strategy.objective o
                            join strategy.strategy s on s.id = o.strategy_id
                            where o.id = objective_id
                              and access.can_write_strategy(s.scope_id, s.node_ancestor_ids)));

                create policy contribution_read on strategy.objective_contribution
                    for select using (
                        exists (select 1 from strategy.objective o where o.id = objective_id));
                create policy contribution_write on strategy.objective_contribution
                    for all using (
                        access.is_system()
                        or exists (
                            select 1 from strategy.objective o
                            join strategy.strategy s on s.id = o.strategy_id
                            where o.id = objective_id
                              and access.can_write_strategy(s.scope_id, s.node_ancestor_ids)))
                    with check (
                        access.is_system()
                        or exists (
                            select 1 from strategy.objective o
                            join strategy.strategy s on s.id = o.strategy_id
                            where o.id = objective_id
                              and access.can_write_strategy(s.scope_id, s.node_ancestor_ids)));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop function if exists access.can_read_strategy(uuid, uuid[]);
                drop function if exists access.can_write_strategy(uuid, uuid[]);
                """);

            migrationBuilder.DropTable(
                name: "key_result",
                schema: "strategy");

            migrationBuilder.DropTable(
                name: "objective_contribution",
                schema: "strategy");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "strategy");

            migrationBuilder.DropTable(
                name: "objective",
                schema: "strategy");

            migrationBuilder.DropTable(
                name: "strategy",
                schema: "strategy");
        }
    }
}
