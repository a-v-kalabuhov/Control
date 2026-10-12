using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Pzp14SetupNormReleaseBuffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlannedSetupMinutes",
                table: "ShiftTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseBufferFixedMinutes",
                table: "ShiftTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseBufferPercent",
                table: "ShiftTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PlanningDefaults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    SetupMinutes = table.Column<int>(type: "integer", nullable: false),
                    ReleaseBufferFixedMinutes = table.Column<int>(type: "integer", nullable: false),
                    ReleaseBufferPercent = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedById = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningDefaults", x => x.Id);
                    table.CheckConstraint("CK_PlanningDefaults_ReleaseBufferFixedMinutes", "\"ReleaseBufferFixedMinutes\" BETWEEN 0 AND 1440");
                    table.CheckConstraint("CK_PlanningDefaults_ReleaseBufferPercent", "\"ReleaseBufferPercent\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_PlanningDefaults_SetupMinutes", "\"SetupMinutes\" BETWEEN 0 AND 1440");
                    table.CheckConstraint("CK_PlanningDefaults_Singleton", "\"Id\" = 1");
                    table.ForeignKey(
                        name: "FK_PlanningDefaults_Users_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "PlanningDefaults",
                columns: new[] { "Id", "ReleaseBufferFixedMinutes", "ReleaseBufferPercent", "SetupMinutes", "UpdatedAt", "UpdatedById" },
                values: new object[] { 1, 0, 0, 0, null, null });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftTasks_PlannedSetupMinutes",
                table: "ShiftTasks",
                sql: "\"PlannedSetupMinutes\" BETWEEN 0 AND 1440");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftTasks_ReleaseBufferFixedMinutes",
                table: "ShiftTasks",
                sql: "\"ReleaseBufferFixedMinutes\" BETWEEN 0 AND 1440");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShiftTasks_ReleaseBufferPercent",
                table: "ShiftTasks",
                sql: "\"ReleaseBufferPercent\" BETWEEN 0 AND 100");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningDefaults_UpdatedById",
                table: "PlanningDefaults",
                column: "UpdatedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanningDefaults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftTasks_PlannedSetupMinutes",
                table: "ShiftTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftTasks_ReleaseBufferFixedMinutes",
                table: "ShiftTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShiftTasks_ReleaseBufferPercent",
                table: "ShiftTasks");

            migrationBuilder.DropColumn(
                name: "PlannedSetupMinutes",
                table: "ShiftTasks");

            migrationBuilder.DropColumn(
                name: "ReleaseBufferFixedMinutes",
                table: "ShiftTasks");

            migrationBuilder.DropColumn(
                name: "ReleaseBufferPercent",
                table: "ShiftTasks");
        }
    }
}
