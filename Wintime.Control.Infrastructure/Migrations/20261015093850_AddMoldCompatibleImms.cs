using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMoldCompatibleImms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MoldCompatibleImms",
                columns: table => new
                {
                    MoldId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImmId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoldCompatibleImms", x => new { x.MoldId, x.ImmId });
                    table.ForeignKey(
                        name: "FK_MoldCompatibleImms_Imms_ImmId",
                        column: x => x.ImmId,
                        principalTable: "Imms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoldCompatibleImms_Molds_MoldId",
                        column: x => x.MoldId,
                        principalTable: "Molds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MoldCompatibleImms_ImmId",
                table: "MoldCompatibleImms",
                column: "ImmId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MoldCompatibleImms");
        }
    }
}
