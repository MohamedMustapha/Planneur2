using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable, with no default. Null is the meaningful state here: it says this person has never chosen,
            // which is what lets the synced time_zone and ui_language beside them act as seeds rather than as
            // competing answers. A default would erase that distinction on every existing row.
            migrationBuilder.AddColumn<string>(
                name: "preferred_language",
                schema: "directory",
                table: "person",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preferred_time_zone",
                schema: "directory",
                table: "person",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preferred_theme",
                schema: "directory",
                table: "person",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "preferred_language", schema: "directory", table: "person");
            migrationBuilder.DropColumn(name: "preferred_time_zone", schema: "directory", table: "person");
            migrationBuilder.DropColumn(name: "preferred_theme", schema: "directory", table: "person");
        }
    }
}
