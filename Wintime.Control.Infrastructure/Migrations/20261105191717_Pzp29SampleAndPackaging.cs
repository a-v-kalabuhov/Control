using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Pzp29SampleAndPackaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PackagingUnit",
                table: "ShiftTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackagingUnit",
                table: "ProductTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasSample",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackagingUnit",
                table: "ShiftTasks");

            migrationBuilder.DropColumn(
                name: "PackagingUnit",
                table: "ProductTypes");

            migrationBuilder.DropColumn(
                name: "HasSample",
                table: "Products");
        }
    }
}
