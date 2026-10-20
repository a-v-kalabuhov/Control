using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wintime.Control.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Артикулы типов — к нормализованному виду (раньше заглавные обеспечивал только фронт).
            migrationBuilder.Sql(@"UPDATE ""ProductTypes"" SET ""Article"" = upper(trim(""Article""));");

            // 1а. Артикул типа не должен совпасть с полным артикулом изделия по умолчанию другого типа
            //     (единое пространство артикулов, инвариант 1) — иначе дальше упадёт уникальный индекс.
            migrationBuilder.Sql(@"
DO $$
DECLARE conflict_list text;
BEGIN
  SELECT string_agg(a.""Article"" || ' / ' || b.""Article"", ', ') INTO conflict_list
  FROM ""ProductTypes"" a JOIN ""ProductTypes"" b ON a.""Article"" = b.""Article"" || '.0' AND a.""Id"" <> b.""Id"";
  IF conflict_list IS NOT NULL THEN
    RAISE EXCEPTION 'PZP-19: артикул типа совпадает с полным артикулом изделия по умолчанию другого типа (%). Переименуйте тип и повторите обновление.', conflict_list;
  END IF;
END $$;");

            // 2. Таблица изделий.
            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Article = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    FullArticle = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_ProductTypes_ProductTypeId",
                        column: x => x.ProductTypeId,
                        principalTable: "ProductTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "IX_Products_FullArticle", table: "Products", column: "FullArticle", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Products_ProductTypeId", table: "Products", column: "ProductTypeId");

            // 3. Изделие по умолчанию каждому типу.
            migrationBuilder.Sql(@"
INSERT INTO ""Products"" (""Id"", ""ProductTypeId"", ""Article"", ""Name"", ""FullArticle"", ""IsActive"", ""CreatedAt"")
SELECT gen_random_uuid(), pt.""Id"", '0', 'По умолчанию', pt.""Article"" || '.0', TRUE, now()
FROM ""ProductTypes"" pt;");

            // 4. Задания: изделие по умолчанию типа ПФ. ПФ без типа — стоп с понятным сообщением.
            migrationBuilder.AddColumn<Guid>(name: "ProductId", table: "ShiftTasks", type: "uuid", nullable: true);
            migrationBuilder.Sql(@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM ""ShiftTasks"" t JOIN ""Molds"" m ON m.""Id"" = t.""MoldId"" WHERE m.""ProductTypeId"" IS NULL) THEN
    RAISE EXCEPTION 'PZP-19: есть задания по пресс-формам без типа изделия. Присвойте тип этим ПФ и повторите обновление.';
  END IF;
END $$;
UPDATE ""ShiftTasks"" t SET ""ProductId"" = p.""Id""
FROM ""Molds"" m JOIN ""Products"" p ON p.""ProductTypeId"" = m.""ProductTypeId"" AND p.""Article"" = '0'
WHERE m.""Id"" = t.""MoldId"";");
            migrationBuilder.AlterColumn<Guid>(name: "ProductId", table: "ShiftTasks", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.CreateIndex(name: "IX_ShiftTasks_ProductId", table: "ShiftTasks", column: "ProductId");
            migrationBuilder.AddForeignKey(name: "FK_ShiftTasks_Products_ProductId", table: "ShiftTasks", column: "ProductId",
                principalTable: "Products", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            // 5. Заказы: изделие по умолчанию типа заказа; колонка типа удаляется.
            migrationBuilder.AddColumn<Guid>(name: "ProductId", table: "Orders", type: "uuid", nullable: true);
            migrationBuilder.Sql(@"
UPDATE ""Orders"" o SET ""ProductId"" = p.""Id""
FROM ""Products"" p WHERE p.""ProductTypeId"" = o.""ProductTypeId"" AND p.""Article"" = '0';");
            migrationBuilder.AlterColumn<Guid>(name: "ProductId", table: "Orders", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.DropForeignKey(name: "FK_Orders_ProductTypes_ProductTypeId", table: "Orders");
            migrationBuilder.DropIndex(name: "IX_Orders_ProductTypeId", table: "Orders");
            migrationBuilder.DropColumn(name: "ProductTypeId", table: "Orders");
            migrationBuilder.CreateIndex(name: "IX_Orders_ProductId", table: "Orders", column: "ProductId");
            migrationBuilder.AddForeignKey(name: "FK_Orders_Products_ProductId", table: "Orders", column: "ProductId",
                principalTable: "Products", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ВНИМАНИЕ: откат корректен только на пустой БД. Orders.ProductId хранит id изделий,
            // а не типов — после переименования в ProductTypeId значения не восстанавливаются.
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Products_ProductId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftTasks_Products_ProductId",
                table: "ShiftTasks");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ShiftTasks_ProductId",
                table: "ShiftTasks");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "ShiftTasks");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "Orders",
                newName: "ProductTypeId");

            migrationBuilder.RenameIndex(
                name: "IX_Orders_ProductId",
                table: "Orders",
                newName: "IX_Orders_ProductTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_ProductTypes_ProductTypeId",
                table: "Orders",
                column: "ProductTypeId",
                principalTable: "ProductTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
