using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Pzp24ProductPartWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PartWeightGrams",
                table: "ShiftTasks",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PartWeightGrams",
                table: "Products",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PlannedInjectionCycleSeconds",
                table: "Products",
                type: "integer",
                nullable: true);

            // PZP-24: изделия типа получают вес самой ранней ПФ этого типа.
            // Задание получает вес именно своей ПФ — это его исходный снапшот.
            migrationBuilder.Sql("""
                UPDATE "Products" AS p
                SET "PartWeightGrams" = COALESCE((
                    SELECT m."PartWeightGrams"
                    FROM "Molds" AS m
                    WHERE m."ProductTypeId" = p."ProductTypeId"
                    ORDER BY m."CreatedAt", m."Id"
                    LIMIT 1
                ), 0);
                """);
            migrationBuilder.Sql("""
                UPDATE "ShiftTasks" AS t
                SET "PartWeightGrams" = m."PartWeightGrams"
                FROM "Molds" AS m
                WHERE m."Id" = t."MoldId";
                """);

            migrationBuilder.DropColumn(
                name: "PartWeightGrams",
                table: "Molds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PartWeightGrams",
                table: "Molds",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "Molds" AS m
                SET "PartWeightGrams" = COALESCE((
                    SELECT p."PartWeightGrams"
                    FROM "Products" AS p
                    WHERE p."ProductTypeId" = m."ProductTypeId"
                    ORDER BY p."Article", p."Id"
                    LIMIT 1
                ), 0);
                """);

            migrationBuilder.DropColumn(
                name: "PartWeightGrams",
                table: "ShiftTasks");

            migrationBuilder.DropColumn(
                name: "PartWeightGrams",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PlannedInjectionCycleSeconds",
                table: "Products");
        }
    }
}
