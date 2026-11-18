using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductRunnerWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RunnerWeightGrams",
                table: "Products",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            // Как и при переносе веса детали (PZP-24), изделия типа получают значение
            // самой ранней ПФ типа: старая схема не позволяет восстановить разные значения изделий.
            migrationBuilder.Sql("""
                UPDATE "Products" AS p
                SET "RunnerWeightGrams" = COALESCE((
                    SELECT m."RunnerWeightGrams"
                    FROM "Molds" AS m
                    WHERE m."ProductTypeId" = p."ProductTypeId"
                    ORDER BY m."CreatedAt", m."Id"
                    LIMIT 1
                ), 0);
                """);

            // Приводим сохранённый расход к той же формуле, которую после миграции
            // используют отчёты и откат ретро-привязки.
            migrationBuilder.Sql("""
                UPDATE "ShiftTasks" AS t
                SET "ActualMaterialWeightGrams" = COALESCE((
                    SELECT SUM(c."Cavities" * t."PartWeightGrams" + p."RunnerWeightGrams")
                    FROM "ImmCycles" AS c
                    WHERE c."TaskId" = t."Id" AND c."IsSuccessful" AND c."EndTime" IS NOT NULL
                ), 0)
                FROM "Products" AS p
                WHERE p."Id" = t."ProductId";
                """);

            migrationBuilder.DropColumn(
                name: "RunnerWeightGrams",
                table: "Molds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RunnerWeightGrams",
                table: "Molds",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "Molds" AS m
                SET "RunnerWeightGrams" = COALESCE((
                    SELECT p."RunnerWeightGrams"
                    FROM "Products" AS p
                    WHERE p."ProductTypeId" = m."ProductTypeId"
                    ORDER BY p."Article", p."Id"
                    LIMIT 1
                ), 0);
                """);

            migrationBuilder.DropColumn(
                name: "RunnerWeightGrams",
                table: "Products");
        }
    }
}
