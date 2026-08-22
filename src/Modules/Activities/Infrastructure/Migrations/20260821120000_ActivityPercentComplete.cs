using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Activities.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ActivityPercentComplete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "percent_complete",
                schema: "activities",
                table: "activity_entry",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "percent_complete",
                schema: "activities",
                table: "activity_entry");
        }
    }
}
