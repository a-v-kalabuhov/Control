using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUnplannedRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnplannedRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImmId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AssignedTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedByUserId = table.Column<string>(type: "text", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnplannedRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnplannedRuns_Imms_ImmId",
                        column: x => x.ImmId,
                        principalTable: "Imms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UnplannedRuns_ShiftTasks_AssignedTaskId",
                        column: x => x.AssignedTaskId,
                        principalTable: "ShiftTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImmCycles_Imm_Orphan",
                table: "ImmCycles",
                columns: new[] { "ImmId", "EndTime" },
                filter: "\"TaskId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UnplannedRuns_AssignedTaskId",
                table: "UnplannedRuns",
                column: "AssignedTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_UnplannedRuns_Imm_Open",
                table: "UnplannedRuns",
                column: "ImmId",
                filter: "\"ClosedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnplannedRuns");

            migrationBuilder.DropIndex(
                name: "IX_ImmCycles_Imm_Orphan",
                table: "ImmCycles");
        }
    }
}
