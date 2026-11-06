using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Pzp36Cavities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Cavities",
                table: "ShiftTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Cavities",
                table: "Products",
                type: "integer",
                nullable: true);

            // PZP-36: рабочая копия существующего задания заполняется один раз.
            // Исторические снапшоты ImmCycle.Cavities не пересчитываются.
            migrationBuilder.Sql("""
                UPDATE "ShiftTasks" AS t
                SET "Cavities" = COALESCE(p."Cavities", m."Cavities")
                FROM "Products" AS p, "Molds" AS m
                WHERE p."Id" = t."ProductId" AND m."Id" = t."MoldId";
                """);
            migrationBuilder.Sql("""ALTER TABLE "ShiftTasks" ALTER COLUMN "Cavities" DROP DEFAULT;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cavities",
                table: "ShiftTasks");

            migrationBuilder.DropColumn(
                name: "Cavities",
                table: "Products");
        }
    }
}
