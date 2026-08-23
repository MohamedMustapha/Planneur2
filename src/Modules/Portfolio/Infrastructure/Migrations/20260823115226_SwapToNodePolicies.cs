using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Portfolio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SwapToNodePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create or replace function portfolio.can_read_item(
                    p_project uuid, p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.same_node(p_node)
                        or access.in_my_branch(p_ancestors)
                        or access.in_my_subtree(p_ancestors)
                        or (p_project is not null and access.can_read_project(p_project, p_ancestors))
                    , false)
                    $fn$;

                create or replace function portfolio.can_write_item(
                    p_project uuid, p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.in_my_subtree(p_ancestors)
                        -- A head writes an item sponsored at or above their own branch, not only beneath it: the
                        -- transition endpoints admit any head, and a predicate that refused them would surface as
                        -- a 404 on an item sitting in front of them on the board. Gated on being a head at all,
                        -- because every path shares the root and a member must not inherit it.
                        or (access.is_head() and p_node = any(access.my_path()))
                        or (p_project is not null and access.leads_project(p_project))
                    , false)
                    $fn$;

                alter function portfolio.can_read_item(uuid, uuid, uuid[]) owner to app_owner;
                alter function portfolio.can_write_item(uuid, uuid, uuid[]) owner to app_owner;
                grant execute on function portfolio.can_read_item(uuid, uuid, uuid[]) to app_rw;
                grant execute on function portfolio.can_write_item(uuid, uuid, uuid[]) to app_rw;
                """);

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

                drop policy portfolio_transition_read on portfolio.portfolio_transition;
                create policy portfolio_transition_read on portfolio.portfolio_transition
                    for select using (
                        access.is_system()
                        or access.has('pmo')
                        or access.same_node(node_id)
                        or access.in_my_branch(node_ancestor_ids)
                        or access.in_my_subtree(node_ancestor_ids)
                        or decided_by = access.uid()
                    );

                drop policy portfolio_transition_append on portfolio.portfolio_transition;
                create policy portfolio_transition_append on portfolio.portfolio_transition
                    for insert with check (
                        access.is_system()
                        or portfolio.can_write_item(null::uuid, node_id, node_ancestor_ids)
                        or decided_by = access.uid()
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy portfolio_transition_append on portfolio.portfolio_transition;
                create policy portfolio_transition_append on portfolio.portfolio_transition
                    for insert with check (
                        access.is_system()
                        or portfolio.can_write_item(null::uuid, department_id)
                        or decided_by = access.uid()
                    );

                drop policy portfolio_transition_read on portfolio.portfolio_transition;
                create policy portfolio_transition_read on portfolio.portfolio_transition
                    for select using (
                        access.is_system()
                        or access.has('pmo')
                        or department_id = any(access.depts())
                        or decided_by = access.uid()
                    );

                drop policy iteration_write on portfolio.iteration;
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

                drop policy portfolio_item_write on portfolio.portfolio_item;
                create policy portfolio_item_write on portfolio.portfolio_item
                    for all using (portfolio.can_write_item(project_id, department_id))
                    with check (portfolio.can_write_item(project_id, department_id));

                drop policy portfolio_item_read on portfolio.portfolio_item;
                create policy portfolio_item_read on portfolio.portfolio_item
                    for select using (portfolio.can_read_item(project_id, department_id));

                drop function if exists portfolio.can_write_item(uuid, uuid, uuid[]);
                drop function if exists portfolio.can_read_item(uuid, uuid, uuid[]);
                """);
        }
    }
}
