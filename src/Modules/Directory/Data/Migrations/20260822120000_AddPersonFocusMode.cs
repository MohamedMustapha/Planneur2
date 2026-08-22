using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonFocusMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable and without a default, for the same reason as the three preference columns beside it: null
            // says "never chosen", which is what lets the per-role default from v2 §02.2 — on for members, off for
            // heads, PO and PMO — apply until the person reaches for the toggle. A `default false` would silently
            // opt every existing member out of the mode the spec wants them in.
            migrationBuilder.AddColumn<bool>(
                name: "focus_mode",
                schema: "directory",
                table: "person",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.DropColumn(name: "focus_mode", schema: "directory", table: "person");
    }
}
