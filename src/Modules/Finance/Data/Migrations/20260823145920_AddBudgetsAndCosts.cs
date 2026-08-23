using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Finance.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetsAndCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    planned_amount = table.Column<decimal>(type: "numeric(16,2)", precision: 16, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cost_component",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    treatment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    treatment_overridden = table.Column<bool>(type: "boolean", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(16,2)", precision: 16, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    license_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_worker_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_component", x => x.id);
                    table.CheckConstraint("ck_cost_component_owner", "(item_id is null) <> (node_id is null)");
                });

            migrationBuilder.CreateTable(
                name: "external_worker",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    vendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    role = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    rate = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    rate_unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    contract_start = table.Column<DateOnly>(type: "date", nullable: false),
                    contract_end = table.Column<DateOnly>(type: "date", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_worker", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "license",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    vendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    seats = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    billing_cycle = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    renewal_date = table.Column<DateOnly>(type: "date", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_license", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budget_owner_node_id_fiscal_year",
                schema: "finance",
                table: "budget",
                columns: new[] { "owner_node_id", "fiscal_year" });

            migrationBuilder.CreateIndex(
                name: "ix_budget_scope_type_scope_id_fiscal_year",
                schema: "finance",
                table: "budget",
                columns: new[] { "scope_type", "scope_id", "fiscal_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_component_item_id",
                schema: "finance",
                table: "cost_component",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_component_node_id",
                schema: "finance",
                table: "cost_component",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_component_owner_node_id",
                schema: "finance",
                table: "cost_component",
                column: "owner_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_component_period_start_period_end",
                schema: "finance",
                table: "cost_component",
                columns: new[] { "period_start", "period_end" });

            migrationBuilder.CreateIndex(
                name: "ix_external_worker_contract_end",
                schema: "finance",
                table: "external_worker",
                column: "contract_end");

            migrationBuilder.CreateIndex(
                name: "ix_external_worker_item_id",
                schema: "finance",
                table: "external_worker",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_worker_node_id",
                schema: "finance",
                table: "external_worker",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "ix_license_item_id",
                schema: "finance",
                table: "license",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_license_node_id",
                schema: "finance",
                table: "license",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "ix_license_renewal_date",
                schema: "finance",
                table: "license",
                column: "renewal_date");
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.attach_to_node_tree('finance', 'budget', 'owner_node_id', null, true);
                select access.attach_to_node_tree('finance', 'cost_component', 'owner_node_id', null, true);
                select access.attach_to_node_tree('finance', 'license', 'node_id', null, true);
                select access.attach_to_node_tree('finance', 'external_worker', 'node_id', null, true);
                """);

            migrationBuilder.Sql("""
                alter table finance.budget enable row level security;
                alter table finance.budget force row level security;
                alter table finance.cost_component enable row level security;
                alter table finance.cost_component force row level security;
                alter table finance.license enable row level security;
                alter table finance.license force row level security;
                alter table finance.external_worker enable row level security;
                alter table finance.external_worker force row level security;
                """);

            // §04.4: a head reads their node and everything beneath it, and reads upward within their own line so
            // the service total they are measured against is visible to them. Members and POs are refused the
            // module outright — this is the one area of the product where seeing the numbers is itself the
            // privilege, which is why read and write are nearly the same predicate.
            migrationBuilder.Sql("""
                create or replace function access.can_read_budget(p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.in_my_subtree(p_ancestors)
                        or (access.is_head() and p_node = any(access.my_path()))
                    , false)
                    $fn$;

                create or replace function access.can_write_budget(p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.in_my_subtree(p_ancestors)
                    , false)
                    $fn$;
                """);

            migrationBuilder.Sql("""
                create policy budget_read on finance.budget
                    for select using (access.can_read_budget(node_id, node_ancestor_ids));
                create policy budget_write on finance.budget
                    for all using (access.can_write_budget(node_id, node_ancestor_ids))
                        with check (access.can_write_budget(node_id, node_ancestor_ids));

                create policy cost_component_read on finance.cost_component
                    for select using (access.can_read_budget(node_id, node_ancestor_ids));
                create policy cost_component_write on finance.cost_component
                    for all using (access.can_write_budget(node_id, node_ancestor_ids))
                        with check (access.can_write_budget(node_id, node_ancestor_ids));

                create policy license_read on finance.license
                    for select using (access.can_read_budget(node_id, node_ancestor_ids));
                create policy license_write on finance.license
                    for all using (access.can_write_budget(node_id, node_ancestor_ids))
                        with check (access.can_write_budget(node_id, node_ancestor_ids));

                create policy external_worker_read on finance.external_worker
                    for select using (access.can_read_budget(node_id, node_ancestor_ids));
                create policy external_worker_write on finance.external_worker
                    for all using (access.can_write_budget(node_id, node_ancestor_ids))
                        with check (access.can_write_budget(node_id, node_ancestor_ids));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop function if exists access.can_read_budget(uuid, uuid[]);
                drop function if exists access.can_write_budget(uuid, uuid[]);
                """);

            migrationBuilder.DropTable(
                name: "budget",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "cost_component",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "external_worker",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "license",
                schema: "finance");
        }
    }
}
