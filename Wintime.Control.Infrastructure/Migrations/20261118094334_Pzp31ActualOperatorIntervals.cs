using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Pzp31ActualOperatorIntervals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftImmOperatorAssignments_ActiveOperator",
                table: "ShiftImmOperatorAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftImmOperatorAssignments_ActiveSlot",
                table: "ShiftImmOperatorAssignments");

            migrationBuilder.AddColumn<string>(
                name: "CreatedById",
                table: "ShiftImmOperatorAssignments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ShiftImmOperatorAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedById",
                table: "ShiftImmOperatorAssignments",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE ""ShiftImmOperatorAssignments"" AS assignment
SET ""CreatedById"" = item.""ShiftLeadId""
FROM ""ShiftPlanItems"" AS item
WHERE item.""Id"" = assignment.""ShiftPlanItemId"";");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedById",
                table: "ShiftImmOperatorAssignments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.Sql(@"CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftImmOperatorAssignments_CreatedById",
                table: "ShiftImmOperatorAssignments",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftImmOperatorAssignments_ShiftPlanItemId",
                table: "ShiftImmOperatorAssignments",
                column: "ShiftPlanItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftImmOperatorAssignments_UpdatedById",
                table: "ShiftImmOperatorAssignments",
                column: "UpdatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftImmOperatorAssignments_Users_CreatedById",
                table: "ShiftImmOperatorAssignments",
                column: "CreatedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftImmOperatorAssignments_Users_UpdatedById",
                table: "ShiftImmOperatorAssignments",
                column: "UpdatedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
ALTER TABLE ""ShiftImmOperatorAssignments""
ADD CONSTRAINT ""EX_ShiftImmOperatorAssignments_SlotRange""
EXCLUDE USING gist (
  ""ShiftPlanItemId"" WITH =,
  ""ImmId"" WITH =,
  ""Slot"" WITH =,
  tstzrange(""AssignedAt"", ""UnassignedAt"", '[)') WITH &&
);");

            migrationBuilder.Sql(@"
ALTER TABLE ""ShiftImmOperatorAssignments""
ADD CONSTRAINT ""EX_ShiftImmOperatorAssignments_OperatorRange""
EXCLUDE USING gist (
  ""ShiftPlanItemId"" WITH =,
  ""ImmId"" WITH =,
  ""OperatorId"" WITH =,
  tstzrange(""AssignedAt"", ""UnassignedAt"", '[)') WITH &&
);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""ShiftImmOperatorAssignments""
DROP CONSTRAINT ""EX_ShiftImmOperatorAssignments_SlotRange"";");

            migrationBuilder.Sql(@"
ALTER TABLE ""ShiftImmOperatorAssignments""
DROP CONSTRAINT ""EX_ShiftImmOperatorAssignments_OperatorRange"";");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftImmOperatorAssignments_Users_CreatedById",
                table: "ShiftImmOperatorAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftImmOperatorAssignments_Users_UpdatedById",
                table: "ShiftImmOperatorAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftImmOperatorAssignments_CreatedById",
                table: "ShiftImmOperatorAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftImmOperatorAssignments_ShiftPlanItemId",
                table: "ShiftImmOperatorAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftImmOperatorAssignments_UpdatedById",
                table: "ShiftImmOperatorAssignments");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "ShiftImmOperatorAssignments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ShiftImmOperatorAssignments");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "ShiftImmOperatorAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftImmOperatorAssignments_ActiveOperator",
                table: "ShiftImmOperatorAssignments",
                columns: new[] { "ShiftPlanItemId", "ImmId", "OperatorId" },
                unique: true,
                filter: "\"UnassignedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftImmOperatorAssignments_ActiveSlot",
                table: "ShiftImmOperatorAssignments",
                columns: new[] { "ShiftPlanItemId", "ImmId", "Slot" },
                unique: true,
                filter: "\"UnassignedAt\" IS NULL");
        }
    }
}
