using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConflictThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConflictThresholdMinutes",
                table: "PlanningDefaults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "PlanningDefaults",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConflictThresholdMinutes",
                value: 20);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlanningDefaults_ConflictThresholdMinutes",
                table: "PlanningDefaults",
                sql: "\"ConflictThresholdMinutes\" BETWEEN 0 AND 1440");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PlanningDefaults_ConflictThresholdMinutes",
                table: "PlanningDefaults");

            migrationBuilder.DropColumn(
                name: "ConflictThresholdMinutes",
                table: "PlanningDefaults");
        }
    }
}
