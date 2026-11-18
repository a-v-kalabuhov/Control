using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Pzp31ShiftClosures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftClosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftPlanItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedById = table.Column<string>(type: "text", nullable: false),
                    ClosedById = table.Column<string>(type: "text", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftClosures", x => x.Id);
                    table.CheckConstraint("CK_ShiftClosures_State_Audit", "(\"State\" = 0 AND \"ClosedAt\" IS NULL AND \"ClosedById\" IS NULL) OR (\"State\" = 1 AND \"ClosedAt\" IS NOT NULL AND \"ClosedById\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ShiftClosures_ShiftPlanItems_ShiftPlanItemId",
                        column: x => x.ShiftPlanItemId,
                        principalTable: "ShiftPlanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftClosures_Users_ClosedById",
                        column: x => x.ClosedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftClosures_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftClosureCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftClosureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedById = table.Column<string>(type: "text", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ChangesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftClosureCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftClosureCorrections_ShiftClosures_ShiftClosureId",
                        column: x => x.ShiftClosureId,
                        principalTable: "ShiftClosures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftClosureCorrections_Users_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftClosureEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftClosureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImmId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImmNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    PlasticIngredientsSnapshot = table.Column<string>(type: "text", nullable: false),
                    ColorantIngredientsSnapshot = table.Column<string>(type: "text", nullable: false),
                    RemainingAtStart = table.Column<int>(type: "integer", nullable: false),
                    NormPartWeightGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    PlannedFullCycleSeconds = table.Column<int>(type: "integer", nullable: true),
                    TaskPackagingUnit = table.Column<int>(type: "integer", nullable: true),
                    Cavities = table.Column<int>(type: "integer", nullable: false),
                    ShiftPlanQuantity = table.Column<int>(type: "integer", nullable: false),
                    PlannedMaterialWeightGrams = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AutomaticOutputQuantity = table.Column<int>(type: "integer", nullable: false),
                    AutomaticAverageFullCycleSeconds = table.Column<double>(type: "double precision", nullable: true),
                    IsManualFallbackRequired = table.Column<bool>(type: "boolean", nullable: false),
                    ActualPartWeightGrams = table.Column<decimal>(type: "numeric", nullable: true),
                    ManualFullCycleSeconds = table.Column<int>(type: "integer", nullable: true),
                    ActualPackagingUnit = table.Column<int>(type: "integer", nullable: true),
                    PackagingPlaces = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ManualGoodQuantity = table.Column<int>(type: "integer", nullable: true),
                    ManualDefectQuantity = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftClosureEntries", x => x.Id);
                    table.CheckConstraint("CK_ShiftClosureEntries_ActualPackagingUnit", "\"ActualPackagingUnit\" IS NULL OR \"ActualPackagingUnit\" > 0");
                    table.CheckConstraint("CK_ShiftClosureEntries_ActualPartWeightGrams", "\"ActualPartWeightGrams\" IS NULL OR \"ActualPartWeightGrams\" > 0");
                    table.CheckConstraint("CK_ShiftClosureEntries_ManualDefectQuantity", "\"ManualDefectQuantity\" IS NULL OR \"ManualDefectQuantity\" >= 0");
                    table.CheckConstraint("CK_ShiftClosureEntries_ManualFullCycleSeconds", "\"ManualFullCycleSeconds\" IS NULL OR \"ManualFullCycleSeconds\" > 0");
                    table.CheckConstraint("CK_ShiftClosureEntries_ManualGoodQuantity", "\"ManualGoodQuantity\" IS NULL OR \"ManualGoodQuantity\" >= 0");
                    table.CheckConstraint("CK_ShiftClosureEntries_PackagingPlaces", "\"PackagingPlaces\" IS NULL OR \"PackagingPlaces\" >= 0");
                    table.CheckConstraint("CK_ShiftClosureEntries_PlannedFullCycleSeconds", "\"PlannedFullCycleSeconds\" IS NULL OR \"PlannedFullCycleSeconds\" > 0");
                    table.CheckConstraint("CK_ShiftClosureEntries_TaskPackagingUnit", "\"TaskPackagingUnit\" IS NULL OR \"TaskPackagingUnit\" > 0");
                    table.ForeignKey(
                        name: "FK_ShiftClosureEntries_Imms_ImmId",
                        column: x => x.ImmId,
                        principalTable: "Imms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftClosureEntries_ShiftClosures_ShiftClosureId",
                        column: x => x.ShiftClosureId,
                        principalTable: "ShiftClosures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftClosureEntries_ShiftTasks_ShiftTaskId",
                        column: x => x.ShiftTaskId,
                        principalTable: "ShiftTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftClosureOutputShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftClosureEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntervalStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IntervalEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OperatorId = table.Column<string>(type: "text", nullable: false),
                    OperatorFullNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    OperatorEmployeeIdSnapshot = table.Column<string>(type: "text", nullable: true),
                    SlotAtIntervalStart = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftClosureOutputShares", x => x.Id);
                    table.CheckConstraint("CK_ShiftClosureOutputShares_Quantity", "\"Quantity\" >= 0");
                    table.CheckConstraint("CK_ShiftClosureOutputShares_SlotAtIntervalStart", "\"SlotAtIntervalStart\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_ShiftClosureOutputShares_ShiftClosureEntries_ShiftClosureEn~",
                        column: x => x.ShiftClosureEntryId,
                        principalTable: "ShiftClosureEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftClosureOutputShares_Users_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImmCycles_TaskId_EndTime",
                table: "ImmCycles",
                columns: new[] { "TaskId", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftClosureCorrections_ChangedById",
                table: "ShiftClosureCorrections",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftClosureCorrections_ShiftClosureId",
                table: "ShiftClosureCorrections",
                column: "ShiftClosureId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftClosureEntries_ImmId",
                table: "ShiftClosureEntries",
                column: "ImmId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftClosureEntries_ShiftClosureId_ShiftTaskId",
                table: "ShiftClosureEntries",
                columns: new[] { "ShiftClosureId", "ShiftTaskId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftClosureEntries_ShiftTaskId",
                table: "ShiftClosureEntries",
                column: "ShiftTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftClosureOutputShares_OperatorId",
                table: "ShiftClosureOutputShares",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftClosureOutputShares_ShiftClosureEntryId_IntervalStartU~",
                table: "ShiftClosureOutputShares",
                columns: new[] { "ShiftClosureEntryId", "IntervalStartUtc", "IntervalEndUtc", "OperatorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftClosures_ClosedById",
                table: "ShiftClosures",
                column: "ClosedById");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftClosures_CreatedById",
                table: "ShiftClosures",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftClosures_ShiftPlanItemId",
                table: "ShiftClosures",
                column: "ShiftPlanItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftClosureCorrections");

            migrationBuilder.DropTable(
                name: "ShiftClosureOutputShares");

            migrationBuilder.DropTable(
                name: "ShiftClosureEntries");

            migrationBuilder.DropTable(
                name: "ShiftClosures");

            migrationBuilder.DropIndex(
                name: "IX_ImmCycles_TaskId_EndTime",
                table: "ImmCycles");
        }
    }
}
