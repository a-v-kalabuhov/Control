using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkModeAndCycleDurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlannedFullCycleSeconds",
                table: "ShiftTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlannedInjectionCycleSeconds",
                table: "ShiftTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkMode",
                table: "ShiftTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InjectionDurationMs",
                table: "ImmCycles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PauseDurationMs",
                table: "ImmCycles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlannedFullCycleSeconds",
                table: "ShiftTasks");

            migrationBuilder.DropColumn(
                name: "PlannedInjectionCycleSeconds",
                table: "ShiftTasks");

            migrationBuilder.DropColumn(
                name: "WorkMode",
                table: "ShiftTasks");

            migrationBuilder.DropColumn(
                name: "InjectionDurationMs",
                table: "ImmCycles");

            migrationBuilder.DropColumn(
                name: "PauseDurationMs",
                table: "ImmCycles");
        }
    }
}
