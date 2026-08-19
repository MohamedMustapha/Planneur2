using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.BuildingBlocks.Persistence.Migrations.Platform
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "platform",
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
                name: "ix_outbox_message_pending",
                schema: "platform",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at is null");

            // conventions.md §4: the migration that creates a module's tables is also the migration that creates
            // their RLS policies, so a table can never exist for even one deploy without the policy that guards it.
            migrationBuilder.Sql("""
                grant usage on schema platform to app_rw;
                grant select, insert, update, delete on all tables in schema platform to app_rw;
                alter default privileges in schema platform
                    grant select, insert, update, delete on tables to app_rw;

                alter table platform.outbox_message enable row level security;
                alter table platform.outbox_message force row level security;

                -- Enqueue: any scoped session may add a message, because an integration event is written inside
                -- the user transaction whose change it describes.
                create policy outbox_enqueue on platform.outbox_message
                    for insert with check (access.is_scoped() or access.is_system());

                -- Read and mark-processed: the drainer only. A user session has no business seeing the queue.
                create policy outbox_drain on platform.outbox_message
                    for select using (access.is_system());

                create policy outbox_mark_processed on platform.outbox_message
                    for update using (access.is_system()) with check (access.is_system());

                -- Purge: processed messages are retained for audit, not forever. Retention runs under the system
                -- context. Without this policy the delete is silently refused rather than failing, which is the
                -- worst of both worlds — a purge job that reports success and frees nothing.
                create policy outbox_purge on platform.outbox_message
                    for delete using (access.is_system());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop policy if exists outbox_purge on platform.outbox_message;
                drop policy if exists outbox_mark_processed on platform.outbox_message;
                drop policy if exists outbox_drain on platform.outbox_message;
                drop policy if exists outbox_enqueue on platform.outbox_message;
                """);

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "platform");
        }
    }
}
