using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMoldUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MoldUsages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MoldUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImmId = table.Column<Guid>(type: "uuid", nullable: false),
                    MoldId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CyclesEnd = table.Column<int>(type: "integer", nullable: false),
                    CyclesStart = table.Column<int>(type: "integer", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoldUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoldUsages_Imms_ImmId",
                        column: x => x.ImmId,
                        principalTable: "Imms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MoldUsages_Molds_MoldId",
                        column: x => x.MoldId,
                        principalTable: "Molds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MoldUsages_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MoldUsages_ImmId",
                table: "MoldUsages",
                column: "ImmId");

            migrationBuilder.CreateIndex(
                name: "IX_MoldUsages_MoldId",
                table: "MoldUsages",
                column: "MoldId");

            migrationBuilder.CreateIndex(
                name: "IX_MoldUsages_TaskId",
                table: "MoldUsages",
                column: "TaskId");
        }
    }
}
