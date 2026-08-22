using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <summary>
    /// The job titles the wider delivery org needs, with fixed ids like the original six.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sync would create any of these on its own the first time it saw one in LDAP — but with a generated id, and
    /// a project team row references a functional role by id. A seeded team would therefore point at a role that
    /// exists on one dev box and not on the next. Fixing the ids here is what makes the seed reproducible.
    /// </para>
    /// <para>
    /// Two of them, <c>directeur</c> and <c>directeur-adjoint</c>, sit above <c>chef-de-pole</c> in the org chart
    /// and are still only job titles. What a director may see comes from their contextual role, which Access
    /// materializes separately; a rename here changes the label on a card and nothing else.
    /// </para>
    /// <para>
    /// <c>product-owner</c> and <c>responsable-pmo</c> are spelt out rather than abbreviated to <c>po</c> and
    /// <c>pmo</c> precisely because those two strings are contextual roles. A functional role sharing a
    /// contextual role's wire format is the confusion this codebase keeps apart everywhere else, and the
    /// architecture rule that scans migrations for role literals reads it as a policy naming a role directly —
    /// correctly, since it cannot tell the two apart from the SQL alone.
    /// </para>
    /// </remarks>
    public partial class AddDeliveryFunctionalRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                -- functional_role carries `force row level security` and a write policy gated on
                -- access.is_system(), so an unstamped migration connection cannot insert into it. The original
                -- six rows only got in because that migration seeded them before it enabled RLS, which is a trick
                -- available exactly once. Every data migration after it has to claim the scope it writes under,
                -- and this is the one the policy already names — the same role the outbox drainer and the
                -- directory sync run as. `set local` so it dies with the migration's transaction.
                set local app.roles = 'system';

                insert into directory.functional_role (id, code, label_key, department_id, active) values
                    ('f0000000-0000-0000-0000-000000000007', 'ops',               'directory.functionalRole.ops',              null, true),
                    ('f0000000-0000-0000-0000-000000000008', 'rssi',              'directory.functionalRole.securityOfficer',  null, true),
                    ('f0000000-0000-0000-0000-000000000009', 'support',           'directory.functionalRole.support',          null, true),
                    ('f0000000-0000-0000-0000-00000000000a', 'product-owner',     'directory.functionalRole.productOwner',     null, true),
                    ('f0000000-0000-0000-0000-00000000000b', 'responsable-pmo',   'directory.functionalRole.pmo',              null, true),
                    ('f0000000-0000-0000-0000-00000000000c', 'designer',          'directory.functionalRole.designer',         null, true),
                    ('f0000000-0000-0000-0000-00000000000d', 'directeur',         'directory.functionalRole.director',         null, true),
                    ('f0000000-0000-0000-0000-00000000000e', 'directeur-adjoint', 'directory.functionalRole.deputyDirector',   null, true)
                on conflict (id) do nothing;

                set local app.roles = '';
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            // Only the rows nobody has been given. Dropping a role someone holds would take their assignment with
            // it, and a down-migration that quietly deletes people's job titles is not a reversal.
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                delete from directory.functional_role role
                where role.id between 'f0000000-0000-0000-0000-000000000007'
                                  and 'f0000000-0000-0000-0000-00000000000e'
                  and not exists (
                      select 1 from directory.person_functional_role held
                      where held.functional_role_id = role.id);

                set local app.roles = '';
                """);
    }
}
