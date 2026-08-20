using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Kudos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialKudos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "kudos");

            migrationBuilder.CreateTable(
                name: "kudo",
                schema: "kudos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month_number = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kudo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "kudos",
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

            migrationBuilder.CreateIndex(
                name: "ix_kudo_created_at",
                schema: "kudos",
                table: "kudo",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_kudo_department_id",
                schema: "kudos",
                table: "kudo",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_kudo_from_person_id",
                schema: "kudos",
                table: "kudo",
                column: "from_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_kudo_giver_month",
                schema: "kudos",
                table: "kudo",
                columns: new[] { "from_person_id", "year", "month_number" });

            migrationBuilder.CreateIndex(
                name: "ix_kudo_to_person_id",
                schema: "kudos",
                table: "kudo",
                column: "to_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_kudo_unit_id",
                schema: "kudos",
                table: "kudo",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "kudos",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            // =====================================================================================================
            // RLS for the Kudos module.
            //
            // The read policy is access.can_read_kudo verbatim — the matrix predicate S2 created before this table
            // existed, taking exactly the four columns the row carries. Both parties always see it, plus whoever
            // can see the receiver's unit; that is why the row denormalises the receiver's unit and department
            // rather than joining into Directory on every candidate row.
            //
            // One consequence of keying visibility to the *receiver's* unit is worth stating: every colleague who
            // can see one of somebody's kudos can see all of them, so a badge derived from that record is the same
            // badge for all of them. Visibility that varied per reader would mean a ladder that did too.
            // =====================================================================================================
            migrationBuilder.Sql("""
                grant usage on schema kudos to app_rw;
                grant select, insert, update, delete on all tables in schema kudos to app_rw;
                alter default privileges in schema kudos
                    grant select, insert, update, delete on tables to app_rw;
                """);

            migrationBuilder.Sql("""
                alter table kudos.kudo enable row level security;
                alter table kudos.kudo force  row level security;
                """);

            migrationBuilder.Sql("""
                -- READ. Straight from the matrix.
                create policy kudo_read on kudos.kudo
                    for select using (
                        access.can_read_kudo(from_person_id, to_person_id, unit_id, department_id)
                    );

                -- WRITE. An insert policy and nothing else, deliberately.
                --
                -- Append-only by construction, the same shape as access.rbac_override_audit: with no update and no
                -- delete policy, Postgres refuses both whatever the application asks for. A kudo is a thing
                -- somebody said about somebody else at a moment, and a platform whose author can rewrite it
                -- afterwards is not storing recognition, it is storing a draft.
                --
                -- Authored by the caller, always. Not "within your unit" and not "if you are a head": those decide
                -- who may be recognised, which is the eligibility rule in the module and cannot be expressed here
                -- because the row does not exist yet. What the database is asked to guarantee is the narrower and
                -- more important thing — that nobody can put words in a colleague's mouth.
                create policy kudo_insert on kudos.kudo
                    for insert with check (
                        access.is_system() or from_person_id = access.uid()
                    );

                -- One deliberate exception to append-only, and it is not for a person. Somebody leaving asks for
                -- their record to be erased eventually, and retention has to be able to run; both are platform
                -- acts under the system context. There is still no update policy for anybody at all, so the one
                -- thing nobody can do — including the author, including the system — is change what was said.
                create policy kudo_purge on kudos.kudo
                    for delete using (access.is_system());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kudo",
                schema: "kudos");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "kudos");
        }
    }
}
