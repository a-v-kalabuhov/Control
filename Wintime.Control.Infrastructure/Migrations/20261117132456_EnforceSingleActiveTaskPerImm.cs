using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveTaskPerImm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE duplicate_imms text;
                BEGIN
                    SELECT string_agg("ImmId"::text, ', ') INTO duplicate_imms
                    FROM (
                        SELECT "ImmId"
                        FROM "ShiftTasks"
                        WHERE "Status" IN (2, 5)
                        GROUP BY "ImmId"
                        HAVING COUNT(*) > 1
                    ) duplicates;
                    IF duplicate_imms IS NOT NULL THEN
                        RAISE EXCEPTION 'EnforceSingleActiveTaskPerImm: на одном ТПА несколько заданий Setup/InProgress. ImmId: %. Завершите или закройте лишние задания и повторите миграцию.', duplicate_imms;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_ShiftTasks_ImmId_Active",
                table: "ShiftTasks",
                column: "ImmId",
                unique: true,
                filter: "\"Status\" IN (2, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ShiftTasks_ImmId_Active",
                table: "ShiftTasks");
        }
    }
}
