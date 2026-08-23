using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Finance.Data.Migrations
{
    /// <inheritdoc />
    public partial class SwapToNodePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy rate_card_read on finance.rate_card;
                create policy rate_card_read on finance.rate_card
                    for select using (access.can_write_node_config(node_id));
                drop policy rate_card_write on finance.rate_card;
                create policy rate_card_write on finance.rate_card
                    for all using (access.can_write_node_config(node_id))
                        with check (access.can_write_node_config(node_id));

                drop policy capex_opex_rule_read on finance.capex_opex_rule;
                create policy capex_opex_rule_read on finance.capex_opex_rule
                    for select using (access.can_write_node_config(node_id));
                drop policy capex_opex_rule_write on finance.capex_opex_rule;
                create policy capex_opex_rule_write on finance.capex_opex_rule
                    for all using (access.can_write_node_config(node_id))
                        with check (access.can_write_node_config(node_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy capex_opex_rule_write on finance.capex_opex_rule;
                create policy capex_opex_rule_write on finance.capex_opex_rule
                    for all using (access.can_write_department_config(department_id))
                        with check (access.can_write_department_config(department_id));
                drop policy capex_opex_rule_read on finance.capex_opex_rule;
                create policy capex_opex_rule_read on finance.capex_opex_rule
                    for select using (access.can_write_department_config(department_id));

                drop policy rate_card_write on finance.rate_card;
                create policy rate_card_write on finance.rate_card
                    for all using (access.can_write_department_config(department_id))
                        with check (access.can_write_department_config(department_id));
                drop policy rate_card_read on finance.rate_card;
                create policy rate_card_read on finance.rate_card
                    for select using (access.can_write_department_config(department_id));
                """);
        }
    }
}
