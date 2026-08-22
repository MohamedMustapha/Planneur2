using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Directory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentWorkingDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "working_day_json",
                schema: "directory",
                table: "department_config",
                type: "jsonb",
                nullable: false,
                // "{}" for the same reason the shift templates use it: an empty string is not valid jsonb, and
                // every existing row should land on "this department has said nothing", which resolves to the
                // platform's own 06:00-20:00 day rather than to a broken one.
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "working_day_json",
                schema: "directory",
                table: "department_config");
        }
    }
}
