using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalMaterialArticleUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Materials_Type_Article",
                table: "Materials");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Article",
                table: "Materials",
                column: "Article",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Materials_Article",
                table: "Materials");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Type_Article",
                table: "Materials",
                columns: new[] { "Type", "Article" },
                unique: true);
        }
    }
}
