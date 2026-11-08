using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Pzp32ShiftLeadStaffing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftPlanDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftPlanDays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShiftPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftPlanDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    StartAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShiftLeadId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftPlanItems", x => x.Id);
                    table.CheckConstraint("CK_ShiftPlanItems_Position", "\"Position\" >= 1");
                    table.CheckConstraint("CK_ShiftPlanItems_StartAtUtc_EndAtUtc", "\"StartAtUtc\" < \"EndAtUtc\"");
                    table.ForeignKey(
                        name: "FK_ShiftPlanItems_ShiftPlanDays_ShiftPlanDayId",
                        column: x => x.ShiftPlanDayId,
                        principalTable: "ShiftPlanDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftPlanItems_Users_ShiftLeadId",
                        column: x => x.ShiftLeadId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftImmOperatorAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftPlanItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImmId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorId = table.Column<string>(type: "text", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UnassignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftImmOperatorAssignments", x => x.Id);
                    table.CheckConstraint("CK_ShiftImmOperatorAssignments_Slot", "\"Slot\" IN (1, 2)");
                    table.CheckConstraint("CK_ShiftImmOperatorAssignments_UnassignedAt", "\"UnassignedAt\" IS NULL OR \"UnassignedAt\" >= \"AssignedAt\"");
                    table.ForeignKey(
                        name: "FK_ShiftImmOperatorAssignments_Imms_ImmId",
                        column: x => x.ImmId,
                        principalTable: "Imms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftImmOperatorAssignments_ShiftPlanItems_ShiftPlanItemId",
                        column: x => x.ShiftPlanItemId,
                        principalTable: "ShiftPlanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftImmOperatorAssignments_Users_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_ShiftImmOperatorAssignments_ImmId",
                table: "ShiftImmOperatorAssignments",
                column: "ImmId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftImmOperatorAssignments_OperatorId",
                table: "ShiftImmOperatorAssignments",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPlanDays_ShiftDate",
                table: "ShiftPlanDays",
                column: "ShiftDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPlanItems_ShiftLeadId",
                table: "ShiftPlanItems",
                column: "ShiftLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftPlanItems_ShiftPlanDayId_Position",
                table: "ShiftPlanItems",
                columns: new[] { "ShiftPlanDayId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftImmOperatorAssignments");

            migrationBuilder.DropTable(
                name: "ShiftPlanItems");

            migrationBuilder.DropTable(
                name: "ShiftPlanDays");
        }
    }
}
