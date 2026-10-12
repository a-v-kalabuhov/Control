using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlannedStartDateTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "ShiftTasks"
                   SET "PlannedDate" = COALESCE("PlannedDate", "IssuedAt", "CreatedAt")
                 WHERE "PlannedDate" IS NULL;
                ALTER TABLE "ShiftTasks"
                  ALTER COLUMN "PlannedDate" TYPE date
                  USING ("PlannedDate" AT TIME ZONE 'Europe/Moscow')::date;
                ALTER TABLE "ShiftTasks" ALTER COLUMN "PlannedDate" SET NOT NULL;
                ALTER TABLE "ShiftTasks" ADD COLUMN "PlannedSetupStartTime" time without time zone NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "ShiftTasks" DROP COLUMN "PlannedSetupStartTime";
                ALTER TABLE "ShiftTasks" ALTER COLUMN "PlannedDate" DROP NOT NULL;
                ALTER TABLE "ShiftTasks"
                  ALTER COLUMN "PlannedDate" TYPE timestamp with time zone
                  USING ("PlannedDate"::timestamp AT TIME ZONE 'UTC');
                """);
        }
    }
}
