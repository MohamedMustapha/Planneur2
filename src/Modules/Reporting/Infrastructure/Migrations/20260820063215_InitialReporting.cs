using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Reporting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "generated_summary",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    prompt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    prompt_characters = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generated_summary", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "reporting",
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
                name: "ix_generated_summary_owner_person_id_scope_scope_id_period_fro~",
                schema: "reporting",
                table: "generated_summary",
                columns: new[] { "owner_person_id", "scope", "scope_id", "period_from", "period_to", "language" });

            migrationBuilder.CreateIndex(
                name: "ix_generated_summary_prompt_hash_owner_person_id",
                schema: "reporting",
                table: "generated_summary",
                columns: new[] { "prompt_hash", "owner_person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "reporting",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            // =====================================================================================================
            // Access predicate and RLS policies (conventions.md 4, visibility-matrix.md 6).
            //
            // A cached narrative is written from rows its author was allowed to see, and it is prose rather than
            // rows - so it cannot be re-filtered after the fact. Whoever reads it back has to be somebody who
            // could have asked the same question in the first place. That is a different rule from every other
            // predicate in this schema, which is why it gets its own rather than reusing can_read_activity: the
            // question is not "may you see this data" but "may you hold this scope".
            //
            // It mirrors ReportScope.Available, which is the application-level 403. Two statements of one rule is
            // the thing conventions.md warns about, and here it is deliberate and worth being explicit about: the
            // application decides which question may be asked (and says so, with a 403), while this decides which
            // stored answer may be read (and says nothing, by returning no row). Both derive from section 6, and
            // the integration matrix test asserts them against each other rather than trusting they agree.
            // =====================================================================================================
            migrationBuilder.Sql("""
                create or replace function access.can_read_report(
                    p_scope text, p_scope_id uuid, p_owner uuid, p_unit uuid, p_dept uuid)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        -- Your own narrative, always. Every scope narrows from here.
                        or p_owner = access.uid()
                        or access.has('pmo')
                        or (p_scope = 'unit' and access.has('unit-head') and p_unit = access.unit())
                        or (p_scope = 'unit' and access.has('dept-head') and p_dept = any(access.depts()))
                        or (p_scope = 'department' and access.has('dept-head') and p_dept = any(access.depts()))
                        or (p_scope = 'project' and access.leads_project(p_scope_id))
                        or (p_scope = 'project' and access.has('dept-head') and access.project_in_my_depts(p_scope_id))
                    , false)
                    $fn$;

                grant usage on schema reporting to app_rw;
                grant select, insert, update, delete on all tables in schema reporting to app_rw;
                alter default privileges in schema reporting
                    grant select, insert, update, delete on tables to app_rw;
                """);

            migrationBuilder.Sql("""
                alter table reporting.generated_summary enable row level security;
                alter table reporting.generated_summary force row level security;
                """);

            migrationBuilder.Sql("""
                create policy generated_summary_read on reporting.generated_summary
                    for select using (
                        access.can_read_report(scope, scope_id, owner_person_id, unit_id, department_id)
                    );

                -- Written only by the person the narrative is for. A head may read a summary somebody in their
                -- unit generated; they may not author one in that person's name, and the owner column is what
                -- makes the difference enforceable rather than merely intended.
                create policy generated_summary_write on reporting.generated_summary
                    for insert with check (
                        access.is_system() or owner_person_id = access.uid()
                    );

                -- No update policy and no delete policy: a narrative is a record of what the model said at a
                -- moment, and one that could be edited afterwards would be evidence of nothing. Regenerating
                -- writes a new row, which is why the cache reads the most recent rather than the only one.
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop function if exists access.can_read_report(text, uuid, uuid, uuid, uuid);");

            migrationBuilder.DropTable(
                name: "generated_summary",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "reporting");
        }
    }
}
