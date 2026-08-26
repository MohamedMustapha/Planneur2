using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Problems.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialProblems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "problems");

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "problems",
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
                name: "problem",
                schema: "problems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    origin_scope_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    origin_scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    impact_time_loss = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    impact_frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    affected_people_estimate = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    converted_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    duplicate_of_problem_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_problem", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "problem_comment",
                schema: "problems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_problem_comment", x => x.id);
                    table.ForeignKey(
                        name: "fk_problem_comment_problem_problem_id",
                        column: x => x.problem_id,
                        principalSchema: "problems",
                        principalTable: "problem",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "problem_vote",
                schema: "problems",
                columns: table => new
                {
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_problem_vote", x => new { x.problem_id, x.person_id });
                    table.ForeignKey(
                        name: "fk_problem_vote_problem_problem_id",
                        column: x => x.problem_id,
                        principalSchema: "problems",
                        principalTable: "problem",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposal",
                schema: "problems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    effort_guess = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposal", x => x.id);
                    table.ForeignKey(
                        name: "fk_proposal_problem_problem_id",
                        column: x => x.problem_id,
                        principalSchema: "problems",
                        principalTable: "problem",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "problems",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_problem_code",
                schema: "problems",
                table: "problem",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_problem_node_id",
                schema: "problems",
                table: "problem",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "ix_problem_reporter_person_id",
                schema: "problems",
                table: "problem",
                column: "reporter_person_id");

            migrationBuilder.CreateIndex(
                name: "ix_problem_status_category",
                schema: "problems",
                table: "problem",
                columns: new[] { "status", "category" });

            migrationBuilder.CreateIndex(
                name: "ix_problem_comment_problem_id",
                schema: "problems",
                table: "problem_comment",
                column: "problem_id");

            migrationBuilder.CreateIndex(
                name: "ix_problem_vote_person_id",
                schema: "problems",
                table: "problem_vote",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposal_problem_id",
                schema: "problems",
                table: "proposal",
                column: "problem_id");
            migrationBuilder.Sql("""
                set local app.roles = 'system';

                select access.attach_to_node_tree('problems', 'problem', 'node_id', null, true);
                """);

            migrationBuilder.Sql("""
                grant usage on schema problems to app_rw;
                grant select, insert, update, delete on all tables in schema problems to app_rw;
                alter default privileges in schema problems
                    grant select, insert, update, delete on tables to app_rw;

                alter table problems.problem enable row level security;
                alter table problems.problem force row level security;
                alter table problems.proposal enable row level security;
                alter table problems.proposal force row level security;
                alter table problems.problem_vote enable row level security;
                alter table problems.problem_vote force row level security;
                alter table problems.problem_comment enable row level security;
                alter table problems.problem_comment force row level security;
                """);

            // The last clause is the one that makes this slice work as an intake pipeline rather than as four
            // hundred private complaint boxes. A branch whose node profile declares it solves a category reads
            // those problems from anywhere in the organisation — that is how another directorate "fills in needs
            // for IT" without anybody quietly building their own tool instead. It is bounded by the profile: a
            // deployment that has declared nothing gets no cross-branch read at all.
            migrationBuilder.Sql("""
                create or replace function access.can_read_problem(
                    p_reporter uuid, p_node uuid, p_ancestors uuid[], p_category text)
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           p_reporter = access.uid()
                        or access.is_system()
                        or access.has('pmo')
                        or access.same_node(p_node)
                        or access.in_my_branch(p_ancestors)
                        or access.in_my_subtree(p_ancestors)
                        or exists (
                            select 1
                            from directory.org_node n
                            join directory.node_profile pr on pr.id = n.profile_id
                            where n.id = any(access.headed_nodes())
                              and pr.solves_categories is not null
                              and p_category = any(pr.solves_categories))
                    , false)
                    $fn$;

                create or replace function access.can_triage_problem(p_node uuid, p_ancestors uuid[])
                    returns boolean language sql stable as $fn$
                    select coalesce(
                           access.is_system()
                        or access.has('pmo')
                        or access.in_my_subtree(p_ancestors)
                    , false)
                    $fn$;
                """);

            migrationBuilder.Sql("""
                create policy problem_read on problems.problem
                    for select using (
                        access.can_read_problem(reporter_person_id, node_id, node_ancestor_ids, category)
                    );

                -- Anybody may file one, and only for themselves. A problem filed in somebody else's name is a
                -- complaint nobody can follow up.
                create policy problem_insert on problems.problem
                    for insert with check (access.is_system() or reporter_person_id = access.uid());

                -- Update covers both the reporter editing their own and a head triaging. It is deliberately not
                -- FOR ALL: that would apply to SELECT too and narrow the read above to the people who may write.
                create policy problem_update on problems.problem
                    for update using (
                        access.is_system()
                        or reporter_person_id = access.uid()
                        or access.can_triage_problem(node_id, node_ancestor_ids)
                    )
                    with check (
                        access.is_system()
                        or reporter_person_id = access.uid()
                        or access.can_triage_problem(node_id, node_ancestor_ids)
                    );

                create policy problem_delete on problems.problem
                    for delete using (access.is_system());
                """);

            // Proposals, votes and comments follow the problem through an EXISTS, so who can see them changes
            // with it. Writing one only needs to be able to read the problem: anybody in scope may propose, vote
            // and comment, which is what §05.4 says and what makes the intake worth having.
            migrationBuilder.Sql("""
                create policy proposal_read on problems.proposal
                    for select using (exists (select 1 from problems.problem p where p.id = problem_id));
                create policy proposal_insert on problems.proposal
                    for insert with check (
                        access.is_system()
                        or (author_person_id = access.uid()
                            and exists (select 1 from problems.problem p where p.id = problem_id))
                    );

                create policy problem_vote_read on problems.problem_vote
                    for select using (exists (select 1 from problems.problem p where p.id = problem_id));
                create policy problem_vote_insert on problems.problem_vote
                    for insert with check (
                        access.is_system()
                        or (person_id = access.uid()
                            and exists (select 1 from problems.problem p where p.id = problem_id))
                    );
                create policy problem_vote_delete on problems.problem_vote
                    for delete using (access.is_system() or person_id = access.uid());

                create policy problem_comment_read on problems.problem_comment
                    for select using (exists (select 1 from problems.problem p where p.id = problem_id));
                create policy problem_comment_insert on problems.problem_comment
                    for insert with check (
                        access.is_system()
                        or (author_person_id = access.uid()
                            and exists (select 1 from problems.problem p where p.id = problem_id))
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop function if exists access.can_read_problem(uuid, uuid, uuid[], text);
                drop function if exists access.can_triage_problem(uuid, uuid[]);
                """);

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "problems");

            migrationBuilder.DropTable(
                name: "problem_comment",
                schema: "problems");

            migrationBuilder.DropTable(
                name: "problem_vote",
                schema: "problems");

            migrationBuilder.DropTable(
                name: "proposal",
                schema: "problems");

            migrationBuilder.DropTable(
                name: "problem",
                schema: "problems");
        }
    }
}
