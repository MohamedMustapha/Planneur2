using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <inheritdoc />
    public partial class SwapToNodePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Resolved live rather than denormalized. A department or a unit *is* a node — the projection makes
            // the node out of the row — so a copied path would have to exist before the row that produces it.
            // These tables hold tens of rows, so the lookup costs nothing worth the ordering problem.
            migrationBuilder.Sql("""
                create or replace function access.heads_node(p_node uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(exists (
                        select 1 from directory.org_node n
                        where n.id = p_node and n.ancestor_ids && access.headed_nodes()
                    ), false)
                    $fn$;

                create or replace function access.can_read_node_by_id(p_node uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or p_node = any(access.my_path())
                        or exists (
                            select 1 from directory.org_node n
                            where n.id = p_node and access.in_my_branch(n.ancestor_ids)
                        )
                        or access.heads_node(p_node)
                    , false)
                    $fn$;

                create or replace function access.can_attach_profile_to_node(p_node uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.heads_node(p_node)
                    , false)
                    $fn$;
                """);

            migrationBuilder.Sql("""
                drop policy department_read on directory.department;
                create policy department_read on directory.department
                    for select using (access.can_read_node_by_id(id));

                drop policy unit_read on directory.unit;
                create policy unit_read on directory.unit
                    for select using (access.can_read_node_by_id(id));

                drop policy person_read on directory.person;
                create policy person_read on directory.person
                    for select using (access.can_read_person(id, home_node_id, node_ancestor_ids));

                drop policy person_unit_read on directory.person_unit;
                create policy person_unit_read on directory.person_unit
                    for select using (
                        exists (
                            select 1 from directory.person p
                            where p.id = person_id
                              and access.can_read_person(p.id, p.home_node_id, p.node_ancestor_ids)
                        )
                    );

                drop policy functional_role_read on directory.functional_role;
                create policy functional_role_read on directory.functional_role
                    for select using (
                        access.is_scoped()
                        and (department_id is null or access.can_read_node_by_id(department_id))
                    );

                drop policy person_functional_role_read on directory.person_functional_role;
                create policy person_functional_role_read on directory.person_functional_role
                    for select using (
                        exists (
                            select 1 from directory.person p
                            where p.id = person_id
                              and access.can_read_person(p.id, p.home_node_id, p.node_ancestor_ids)
                        )
                    );

                drop policy department_config_read on directory.department_config;
                create policy department_config_read on directory.department_config
                    for select using (access.can_read_node_by_id(department_id));

                drop policy department_config_write on directory.department_config;
                create policy department_config_write on directory.department_config
                    for update using (access.can_write_node_config(department_id))
                              with check (access.can_write_node_config(department_id));

                drop policy department_config_insert on directory.department_config;
                create policy department_config_insert on directory.department_config
                    for insert with check (access.can_write_node_config(department_id));

                drop policy department_config_audit_read on directory.department_config_audit;
                create policy department_config_audit_read on directory.department_config_audit
                    for select using (access.can_read_node_by_id(department_id));

                drop policy department_config_audit_append on directory.department_config_audit;
                create policy department_config_audit_append on directory.department_config_audit
                    for insert with check (access.can_write_node_config(department_id));

                drop policy department_attach_profile on directory.department;
                create policy department_attach_profile on directory.department
                    for update using (access.can_attach_profile_to_node(id))
                               with check (access.can_attach_profile_to_node(id));

                drop policy unit_attach_profile on directory.unit;
                create policy unit_attach_profile on directory.unit
                    for update using (access.can_attach_profile_to_node(id))
                               with check (access.can_attach_profile_to_node(id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy unit_attach_profile on directory.unit;
                create policy unit_attach_profile on directory.unit
                    for update using (access.can_attach_node_profile(department_id))
                               with check (access.can_attach_node_profile(department_id));

                drop policy department_attach_profile on directory.department;
                create policy department_attach_profile on directory.department
                    for update using (access.can_attach_node_profile(id))
                               with check (access.can_attach_node_profile(id));

                drop policy department_config_audit_append on directory.department_config_audit;
                create policy department_config_audit_append on directory.department_config_audit
                    for insert with check (access.can_write_department_config(department_id));

                drop policy department_config_audit_read on directory.department_config_audit;
                create policy department_config_audit_read on directory.department_config_audit
                    for select using (access.can_read_department(department_id));

                drop policy department_config_insert on directory.department_config;
                create policy department_config_insert on directory.department_config
                    for insert with check (access.can_write_department_config(department_id));

                drop policy department_config_write on directory.department_config;
                create policy department_config_write on directory.department_config
                    for update using (access.can_write_department_config(department_id))
                              with check (access.can_write_department_config(department_id));

                drop policy department_config_read on directory.department_config;
                create policy department_config_read on directory.department_config
                    for select using (access.can_read_department(department_id));

                drop policy person_functional_role_read on directory.person_functional_role;
                create policy person_functional_role_read on directory.person_functional_role
                    for select using (
                        exists (
                            select 1 from directory.person p
                            where p.id = person_id
                              and access.can_read_person(p.id, p.primary_unit_id, p.primary_department_id)
                        )
                    );

                drop policy functional_role_read on directory.functional_role;
                create policy functional_role_read on directory.functional_role
                    for select using (
                        access.is_scoped()
                        and (department_id is null or access.can_read_department(department_id))
                    );

                drop policy person_unit_read on directory.person_unit;
                create policy person_unit_read on directory.person_unit
                    for select using (access.can_read_person(person_id, unit_id, department_id));

                drop policy person_read on directory.person;
                create policy person_read on directory.person
                    for select using (access.can_read_person(id, primary_unit_id, primary_department_id));

                drop policy unit_read on directory.unit;
                create policy unit_read on directory.unit
                    for select using (access.can_read_department(department_id));

                drop policy department_read on directory.department;
                create policy department_read on directory.department
                    for select using (access.can_read_department(id));

                drop function if exists access.can_attach_profile_to_node(uuid);
                drop function if exists access.can_read_node_by_id(uuid);
                drop function if exists access.heads_node(uuid);
                """);
        }
    }
}
