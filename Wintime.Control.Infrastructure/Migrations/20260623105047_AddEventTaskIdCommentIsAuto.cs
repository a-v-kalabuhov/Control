using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventTaskIdCommentIsAuto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAuto",
                table: "Events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TaskId",
                table: "Events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_TaskId",
                table: "Events",
                column: "TaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_ShiftTasks_TaskId",
                table: "Events",
                column: "TaskId",
                principalTable: "ShiftTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_ShiftTasks_TaskId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_TaskId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Comment",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "IsAuto",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TaskId",
                table: "Events");
        }
    }
}
