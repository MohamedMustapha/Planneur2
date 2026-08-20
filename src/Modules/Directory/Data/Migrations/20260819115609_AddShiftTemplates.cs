using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "shift_templates_json",
                schema: "directory",
                table: "department_config",
                type: "jsonb",
                nullable: false,
                // "{}", not the scaffolded empty string: that is not valid jsonb and Postgres refuses the column.
                // Existing rows land on "no shift slots configured", which resolves to the platform defaults.
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shift_templates_json",
                schema: "directory",
                table: "department_config");
        }
    }
}
