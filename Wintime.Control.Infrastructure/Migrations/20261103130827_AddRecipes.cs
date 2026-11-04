using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductRecipeIngredients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uuid", nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRecipeIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductRecipeIngredients_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductRecipeIngredients_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftTaskRecipeIngredients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalMaterialId = table.Column<Guid>(type: "uuid", nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTaskRecipeIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftTaskRecipeIngredients_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftTaskRecipeIngredients_Materials_OriginalMaterialId",
                        column: x => x.OriginalMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftTaskRecipeIngredients_ShiftTasks_ShiftTaskId",
                        column: x => x.ShiftTaskId,
                        principalTable: "ShiftTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductRecipeIngredients_MaterialId",
                table: "ProductRecipeIngredients",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductRecipeIngredients_ProductId_MaterialId",
                table: "ProductRecipeIngredients",
                columns: new[] { "ProductId", "MaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTaskRecipeIngredients_MaterialId",
                table: "ShiftTaskRecipeIngredients",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTaskRecipeIngredients_OriginalMaterialId",
                table: "ShiftTaskRecipeIngredients",
                column: "OriginalMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTaskRecipeIngredients_ShiftTaskId_MaterialId",
                table: "ShiftTaskRecipeIngredients",
                columns: new[] { "ShiftTaskId", "MaterialId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductRecipeIngredients");

            migrationBuilder.DropTable(
                name: "ShiftTaskRecipeIngredients");
        }
    }
}
