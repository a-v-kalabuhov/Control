using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameTasksTableToShiftTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImmCycles_Tasks_TaskId",
                table: "ImmCycles");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Imms_ImmId",
                table: "Tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Molds_MoldId",
                table: "Tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Users_PersonnelId",
                table: "Tasks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Tasks",
                table: "Tasks");

            migrationBuilder.RenameTable(
                name: "Tasks",
                newName: "ShiftTasks");

            migrationBuilder.RenameIndex(
                name: "IX_Tasks_PersonnelId",
                table: "ShiftTasks",
                newName: "IX_ShiftTasks_PersonnelId");

            migrationBuilder.RenameIndex(
                name: "IX_Tasks_MoldId",
                table: "ShiftTasks",
                newName: "IX_ShiftTasks_MoldId");

            migrationBuilder.RenameIndex(
                name: "IX_Tasks_ImmId",
                table: "ShiftTasks",
                newName: "IX_ShiftTasks_ImmId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ShiftTasks",
                table: "ShiftTasks",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ImmCycles_ShiftTasks_TaskId",
                table: "ImmCycles",
                column: "TaskId",
                principalTable: "ShiftTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTasks_Imms_ImmId",
                table: "ShiftTasks",
                column: "ImmId",
                principalTable: "Imms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTasks_Molds_MoldId",
                table: "ShiftTasks",
                column: "MoldId",
                principalTable: "Molds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftTasks_Users_PersonnelId",
                table: "ShiftTasks",
                column: "PersonnelId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImmCycles_ShiftTasks_TaskId",
                table: "ImmCycles");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTasks_Imms_ImmId",
                table: "ShiftTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTasks_Molds_MoldId",
                table: "ShiftTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTasks_Users_PersonnelId",
                table: "ShiftTasks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ShiftTasks",
                table: "ShiftTasks");

            migrationBuilder.RenameTable(
                name: "ShiftTasks",
                newName: "Tasks");

            migrationBuilder.RenameIndex(
                name: "IX_ShiftTasks_PersonnelId",
                table: "Tasks",
                newName: "IX_Tasks_PersonnelId");

            migrationBuilder.RenameIndex(
                name: "IX_ShiftTasks_MoldId",
                table: "Tasks",
                newName: "IX_Tasks_MoldId");

            migrationBuilder.RenameIndex(
                name: "IX_ShiftTasks_ImmId",
                table: "Tasks",
                newName: "IX_Tasks_ImmId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Tasks",
                table: "Tasks",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ImmCycles_Tasks_TaskId",
                table: "ImmCycles",
                column: "TaskId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Imms_ImmId",
                table: "Tasks",
                column: "ImmId",
                principalTable: "Imms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Molds_MoldId",
                table: "Tasks",
                column: "MoldId",
                principalTable: "Molds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Users_PersonnelId",
                table: "Tasks",
                column: "PersonnelId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
