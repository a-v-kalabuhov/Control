using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftDefaultLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftDefaultLeads",
                columns: table => new
                {
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ShiftLeadId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftDefaultLeads", x => x.Position);
                    table.CheckConstraint("CK_ShiftDefaultLeads_Position", "\"Position\" >= 1");
                    table.ForeignKey(
                        name: "FK_ShiftDefaultLeads_Users_ShiftLeadId",
                        column: x => x.ShiftLeadId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftDefaultLeads_ShiftLeadId",
                table: "ShiftDefaultLeads",
                column: "ShiftLeadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftDefaultLeads");
        }
    }
}
