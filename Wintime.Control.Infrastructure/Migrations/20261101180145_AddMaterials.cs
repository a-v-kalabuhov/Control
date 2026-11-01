using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Article = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Manufacturer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ColorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ColorCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Type_Article",
                table: "Materials",
                columns: new[] { "Type", "Article" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Type_IsActive",
                table: "Materials",
                columns: new[] { "Type", "IsActive" });

            // Регистронезависимая уникальность (тип, производитель, наименование); пустой производитель — одно значение.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_Materials_Type_Manufacturer_Name\" ON \"Materials\" " +
                "(\"Type\", lower(coalesce(\"Manufacturer\", '')), lower(\"Name\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Materials_Type_Manufacturer_Name\";");

            migrationBuilder.DropTable(
                name: "Materials");
        }
    }
}
