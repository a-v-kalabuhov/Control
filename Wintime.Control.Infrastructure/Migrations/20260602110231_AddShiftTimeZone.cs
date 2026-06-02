using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Shifts",
                type: "text",
                nullable: false,
                defaultValue: "Europe/Moscow");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Shifts");
        }
    }
}
