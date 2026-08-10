using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MqttContractV2CycleBoundaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "EndTime",
                table: "ImmCycles",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<decimal>(
                name: "Cushion",
                table: "ImmCycles",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CycleNumber",
                table: "ImmCycles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InjectionStartTime",
                table: "ImmCycles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImmCycles_Imm_CycleNumber_StartTime",
                table: "ImmCycles",
                columns: new[] { "ImmId", "CycleNumber", "StartTime" },
                unique: true,
                filter: "\"CycleNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImmCycles_Imm_CycleNumber_StartTime",
                table: "ImmCycles");

            migrationBuilder.DropColumn(
                name: "Cushion",
                table: "ImmCycles");

            migrationBuilder.DropColumn(
                name: "CycleNumber",
                table: "ImmCycles");

            migrationBuilder.DropColumn(
                name: "InjectionStartTime",
                table: "ImmCycles");

            migrationBuilder.AlterColumn<DateTime>(
                name: "EndTime",
                table: "ImmCycles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
