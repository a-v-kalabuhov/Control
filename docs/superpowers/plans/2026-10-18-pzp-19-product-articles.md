# PZP-19 Типы изделий и изделия (артикулы) — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ввести внутри типа изделия (`ProductType`) список изделий (`Product`) с полным артикулом `ТИП.ЧАСТЬ`; заказ и задание ссылаются на изделие, ПФ — по-прежнему на тип.

**Architecture:** Новая сущность `Product` (таблица `Products`, хранимый `FullArticle` с unique-индексом). `Order.ProductTypeId` заменяется на `Order.ProductId`, у `ShiftTask` появляется обязательный `ProductId`. Правила — чистые функции в `Core/Policies` (`ProductArticle`, `ProductSelection`, `OrderTaskBinding`) и метод сущности `ShiftTask.SetProduct` (ADR-0002). Справочник изделий — вложенный ресурс `ProductTypesController`.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql (PostgreSQL 16), xUnit + FluentAssertions + Testcontainers; Vue 3 + Element Plus + Vitest.

**Спека:** `docs/superpowers/specs/2026-10-18-pzp-19-product-articles-design.md` — источник правды по правилам и сообщениям. При расхождении плана и спеки — спека.

## Global Constraints

- Ветка `feature/products-articles` (уже создана, в ней спека). Прямой push в master запрещён.
- Термины UI: «Тип изделия» (`ProductType`), «Изделие» (`Product`), «Полный артикул изделия» (`FullArticle`).
- Полный артикул = `ProductArticle.Normalize(typeArticle) + "." + ProductArticle.Normalize(part)`. Нормализация = `Trim()` + `ToUpperInvariant()`.
- Изделие по умолчанию: часть `0`, наименование `По умолчанию`.
- Физически удалять можно только `Product` без заказов и заданий и не последнее у типа. Всё остальное — только `IsActive = false`.
- Все `DateTime` в EF-запросах — `Kind=Utc` (CLAUDE.md).
- Доменные ошибки — `DomainException` (глобальный хендлер → 400). Дубли артикулов и запреты справочника — `409 Conflict` с текстом.
- Legacy-данных нет: никаких fallback и nullable ради совместимости.
- Тесты: xUnit (.NET), Vitest (фронт). Интеграционные тесты требуют Docker (Testcontainers).
- Команды:
  - unit: `dotnet test Wintime.Control.Tests.Unit`
  - integration: `dotnet test Wintime.Control.Tests.Integration`
  - фильтр: `dotnet test <проект> --filter "FullyQualifiedName~<Имя>"`
  - фронт: `cd Wintime-Control-Frontend && npx vitest run <путь>`
- Коммиты заканчиваются строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Стиль кода — как в окружающих файлах (комментарии на русском, плотность комментариев как рядом).

---

### Task 1: Политики артикула и выбора изделия (Core)

**Files:**
- Create: `Wintime.Control.Core/Entities/Product.cs`
- Create: `Wintime.Control.Core/Policies/ProductArticle.cs`
- Create: `Wintime.Control.Core/Policies/ProductSelection.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ProductArticleTests.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ProductSelectionTests.cs`

**Interfaces:**
- Produces:
  - `class Product : BaseEntity { Guid ProductTypeId; ProductType? ProductType; string Article; string Name; string FullArticle; bool IsActive = true; }`
  - `static class ProductArticle { const string Separator = "."; const string DefaultPart = "0"; const string DefaultName = "По умолчанию"; static string Normalize(string? article); static string Compose(string typeArticle, string part); }`
  - `static class ProductSelection { static void EnsureSelectable(Product product); static void EnsureBelongsToMold(Product product, Guid? moldProductTypeId); }`

Сущность `Product` в этой задаче **не** подключается к `DbContext` и не получает навигацию из `ProductType` — иначе EF увидит изменение модели без миграции. Подключение — Task 2.

- [ ] **Step 1: Написать сущность `Product`**

```csharp
namespace Wintime.Control.Core.Entities;

/// <summary>
/// Изделие — товарная позиция внутри типа изделия (PZP-19, ADR-0020), например цвет.
/// Полный артикул = артикул типа + "." + часть артикула изделия; уникален в БД.
/// </summary>
public class Product : BaseEntity
{
    public Guid ProductTypeId { get; set; }
    public ProductType? ProductType { get; set; }
    public string Article { get; set; } = string.Empty;     // часть артикула (заглавными)
    public string Name { get; set; } = string.Empty;        // наименование изделия
    public string FullArticle { get; set; } = string.Empty; // хранимый, пересчитывается при правке артикулов
    public bool IsActive { get; set; } = true;              // архивный флаг
}
```

- [ ] **Step 2: Написать падающие тесты `ProductArticleTests`**

```csharp
using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class ProductArticleTests
{
    [Theory]
    [InlineData("  a123 ", "A123")]
    [InlineData("blk", "BLK")]
    [InlineData("123.а", "123.А")]   // кириллица
    [InlineData(null, "")]
    public void Normalize_TrimsAndUppercases(string? input, string expected) =>
        ProductArticle.Normalize(input).Should().Be(expected);

    [Fact]
    public void Compose_JoinsWithDotAndNormalizes() =>
        ProductArticle.Compose(" a123", "blk ").Should().Be("A123.BLK");

    [Fact]
    public void Compose_DefaultPart() =>
        ProductArticle.Compose("A123", ProductArticle.DefaultPart).Should().Be("A123.0");

    [Fact]
    public void Compose_DotsInsideParts_CanCollide()
    {
        // Коллизия из спеки (решение 4): обе пары дают один полный артикул — её ловит проверка уникальности.
        ProductArticle.Compose("123.А", "0").Should().Be(ProductArticle.Compose("123", "А.0"));
    }
}
```

- [ ] **Step 3: Запустить — FAIL (нет `ProductArticle`)**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ProductArticleTests"`
Expected: ошибка компиляции `ProductArticle` не найден.

- [ ] **Step 4: Реализовать `ProductArticle`**

```csharp
namespace Wintime.Control.Core.Policies;

/// <summary>
/// Нормализация и склейка артикулов (PZP-19). Полный артикул изделия =
/// артикул типа + "." + часть артикула изделия. Точки внутри частей разрешены,
/// поэтому склейка неоднозначна — уникальность полного артикула проверяется отдельно.
/// </summary>
public static class ProductArticle
{
    public const string Separator = ".";
    public const string DefaultPart = "0";
    public const string DefaultName = "По умолчанию";

    public static string Normalize(string? article) =>
        (article ?? string.Empty).Trim().ToUpperInvariant();

    public static string Compose(string typeArticle, string part) =>
        Normalize(typeArticle) + Separator + Normalize(part);
}
```

- [ ] **Step 5: Написать падающие тесты `ProductSelectionTests`**

```csharp
using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class ProductSelectionTests
{
    private static Product MakeProduct(bool productActive = true, bool typeActive = true)
    {
        var type = new ProductType { Article = "A1", Name = "Тип", IsActive = typeActive };
        return new Product { ProductTypeId = type.Id, ProductType = type, Article = "0",
                             Name = "По умолчанию", FullArticle = "A1.0", IsActive = productActive };
    }

    [Fact]
    public void EnsureSelectable_Active_DoesNotThrow() =>
        FluentActions.Invoking(() => ProductSelection.EnsureSelectable(MakeProduct())).Should().NotThrow();

    [Fact]
    public void EnsureSelectable_ArchivedProduct_Throws() =>
        FluentActions.Invoking(() => ProductSelection.EnsureSelectable(MakeProduct(productActive: false)))
            .Should().Throw<DomainException>().WithMessage("Изделие в архиве");

    [Fact]
    public void EnsureSelectable_ArchivedType_Throws() =>
        FluentActions.Invoking(() => ProductSelection.EnsureSelectable(MakeProduct(typeActive: false)))
            .Should().Throw<DomainException>().WithMessage("Тип изделия в архиве");

    [Fact]
    public void EnsureSelectable_TypeNotLoaded_ThrowsInvalidOperation()
    {
        var p = MakeProduct();
        p.ProductType = null;
        FluentActions.Invoking(() => ProductSelection.EnsureSelectable(p)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureBelongsToMold_SameType_DoesNotThrow()
    {
        var p = MakeProduct();
        FluentActions.Invoking(() => ProductSelection.EnsureBelongsToMold(p, p.ProductTypeId)).Should().NotThrow();
    }

    [Fact]
    public void EnsureBelongsToMold_OtherType_Throws() =>
        FluentActions.Invoking(() => ProductSelection.EnsureBelongsToMold(MakeProduct(), Guid.NewGuid()))
            .Should().Throw<DomainException>().WithMessage("Изделие не относится к типу изделия пресс-формы");

    [Fact]
    public void EnsureBelongsToMold_MoldWithoutType_Throws() =>
        FluentActions.Invoking(() => ProductSelection.EnsureBelongsToMold(MakeProduct(), null))
            .Should().Throw<DomainException>().WithMessage("У пресс-формы не задан тип изделия");
}
```

- [ ] **Step 6: Реализовать `ProductSelection`**

```csharp
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Можно ли выбрать изделие в новом заказе/задании (PZP-19): изделие и его тип активны;
/// для задания — изделие принадлежит типу ПФ. Чистые функции; вызывающий код грузит ProductType.
/// </summary>
public static class ProductSelection
{
    public static void EnsureSelectable(Product product)
    {
        if (product.ProductType == null)
            throw new InvalidOperationException("Product.ProductType должен быть загружен (Include)");
        if (!product.IsActive)
            throw new DomainException("Изделие в архиве");
        if (!product.ProductType.IsActive)
            throw new DomainException("Тип изделия в архиве");
    }

    public static void EnsureBelongsToMold(Product product, Guid? moldProductTypeId)
    {
        if (moldProductTypeId == null)
            throw new DomainException("У пресс-формы не задан тип изделия");
        if (product.ProductTypeId != moldProductTypeId.Value)
            throw new DomainException("Изделие не относится к типу изделия пресс-формы");
    }
}
```

- [ ] **Step 7: Запустить unit-тесты — PASS**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ProductArticleTests|FullyQualifiedName~ProductSelectionTests"`
Expected: все PASS. Затем весь `dotnet test Wintime.Control.Tests.Unit` — зелёный.

- [ ] **Step 8: Commit**

```bash
git add Wintime.Control.Core/Entities/Product.cs Wintime.Control.Core/Policies/ProductArticle.cs Wintime.Control.Core/Policies/ProductSelection.cs Wintime.Control.Tests.Unit/Policies/ProductArticleTests.cs Wintime.Control.Tests.Unit/Policies/ProductSelectionTests.cs
git commit -m "feat(core): Product entity and article/selection policies (PZP-19)"
```

---

### Task 2: Переключение схемы — `Products`, `Order.ProductId`, `ShiftTask.ProductId`

Самая большая задача: после неё весь бэкенд компилируется и **все** существующие тесты зелёные на новой схеме.

**Files:**
- Modify: `Wintime.Control.Core/Entities/ProductType.cs` (+ `Products`)
- Modify: `Wintime.Control.Core/Entities/Order.cs` (`ProductTypeId`/`ProductType` → `ProductId`/`Product`)
- Modify: `Wintime.Control.Core/Entities/ShiftTask.cs` (+ `ProductId`, `Product`, `SetProduct`)
- Modify: `Wintime.Control.Core/Policies/OrderTaskBinding.cs`
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs`
- Create: миграция `Wintime.Control.Infrastructure/Migrations/<timestamp>_AddProducts.cs` (+ Designer, snapshot)
- Modify: `Wintime.Control.Core/DTOs/Order/OrderDto.cs`, `CreateOrderRequestDto.cs`, `UpdateOrderRequestDto.cs`, `OrderTaskSummaryDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Tasks/CreateTaskRequestDto.cs`
- Modify: `Wintime.Control.API/Controllers/OrdersController.cs`
- Modify: `Wintime.Control.API/Controllers/TasksController.cs` (только `CreateTask`, `SetOrder`, `ResolveOrderForBindingAsync`)
- Modify: `Wintime.Control.API/Controllers/UnplannedRunController.cs:167`
- Modify: `Wintime.Control.API/Controllers/ProductTypesController.cs` (только `Create`: изделие по умолчанию + нормализация)
- Modify: `Wintime.Control.Tests.Integration/Infrastructure/IntegrationTestFactory.cs`
- Modify: все интеграционные тесты, создающие `ShiftTask`/`Order`/`CreateTaskRequestDto`/`CreateOrderRequestDto` (≈21 файл, см. Step 9)
- Modify: `Wintime.Control.Tests.Unit/Policies/OrderTaskBindingTests.cs`
- Create: `Wintime.Control.Tests.Unit/Entities/ShiftTaskSetProductTests.cs` (если папки `Entities` нет — положить рядом с существующими тестами `ShiftTask`, найти `grep -rl "class .*ShiftTask.*Tests" Wintime.Control.Tests.Unit`)
- Create: `Wintime.Control.Tests.Integration/Migrations/AddProductsMigrationTests.cs`

**Interfaces:**
- Consumes: Task 1 — `Product`, `ProductArticle`.
- Produces:
  - `ProductType.Products : ICollection<Product>`
  - `Order.ProductId : Guid`, `Order.Product : Product?` (свойств `ProductTypeId`/`ProductType` у `Order` больше нет)
  - `ShiftTask.ProductId : Guid`, `ShiftTask.Product : Product?`, `ShiftTask.SetProduct(Guid productId)`
  - `OrderTaskBinding.EnsureCanBind(Order order, Guid taskProductId)`
  - `DbSet<Product> ControlDbContext.Products`
  - `CreateOrderRequestDto.ProductId : Guid`; `UpdateOrderRequestDto.ProductId : Guid?`
  - `OrderDto`: `ProductId`, `ProductFullArticle`, `ProductName`, `ProductTypeId`, `ProductTypeName` (поля `ProductTypeArticle` больше нет)
  - `OrderTaskSummaryDto.ProductFullArticle : string?`
  - `CreateTaskRequestDto.ProductId : Guid?`
  - `GET /api/orders?productTypeId=&productId=`
  - `IntegrationTestFactory.TestProductId : Guid` (изделие `0` типа `TestProductTypeId`)

- [ ] **Step 1: Unit-тесты `OrderTaskBinding` и `SetProduct` (падающие)**

Переписать `Wintime.Control.Tests.Unit/Policies/OrderTaskBindingTests.cs`:

```csharp
private static Order ActiveOrder(Guid productId) =>
    new() { Status = OrderStatus.Active, ProductId = productId, Quantity = 100, Number = "O-1" };

[Fact]
public void EnsureCanBind_MatchingActive_DoesNotThrow()
{
    var p = Guid.NewGuid();
    var act = () => OrderTaskBinding.EnsureCanBind(ActiveOrder(p), p);
    act.Should().NotThrow();
}

[Fact]
public void EnsureCanBind_ProductMismatch_Throws()
{
    var act = () => OrderTaskBinding.EnsureCanBind(ActiveOrder(Guid.NewGuid()), Guid.NewGuid());
    act.Should().Throw<DomainException>().WithMessage("Изделие задания не совпадает с изделием заказа");
}
```
Тест `EnsureCanBind_MoldWithoutType_Throws` удалить (проверки больше нет). Тест с неактивным заказом оставить, поменяв `ProductTypeId` на `ProductId`.

Новый `ShiftTaskSetProductTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Entities;

public class ShiftTaskSetProductTests
{
    [Fact]
    public void SetProduct_Planned_WithoutOrder_Changes()
    {
        var t = new ShiftTask { Status = TaskStatus.Planned, ProductId = Guid.NewGuid() };
        var p = Guid.NewGuid();
        t.SetProduct(p);
        t.ProductId.Should().Be(p);
    }

    [Fact]
    public void SetProduct_SameValue_IsNoOp_InAnyStatus()
    {
        var p = Guid.NewGuid();
        var t = new ShiftTask { Status = TaskStatus.InProgress, ProductId = p, OrderId = Guid.NewGuid() };
        FluentActions.Invoking(() => t.SetProduct(p)).Should().NotThrow();
    }

    [Fact]
    public void SetProduct_Issued_Throws()
    {
        var t = new ShiftTask { Status = TaskStatus.Issued, ProductId = Guid.NewGuid() };
        FluentActions.Invoking(() => t.SetProduct(Guid.NewGuid())).Should().Throw<DomainException>();
    }

    [Fact]
    public void SetProduct_WithOrder_Throws()
    {
        var t = new ShiftTask { Status = TaskStatus.Planned, ProductId = Guid.NewGuid(), OrderId = Guid.NewGuid() };
        FluentActions.Invoking(() => t.SetProduct(Guid.NewGuid()))
            .Should().Throw<DomainException>().WithMessage("Изделие задания с заказом задаёт заказ");
    }
}
```

Run: `dotnet test Wintime.Control.Tests.Unit` → Expected: ошибки компиляции (нет `Order.ProductId`, `ShiftTask.SetProduct`).

- [ ] **Step 2: Изменить сущности**

`ProductType.cs` — добавить навигацию:
```csharp
    public ICollection<Product> Products { get; set; } = new List<Product>(); // всегда ≥ 1 (PZP-19)
```
Обновить XML-комментарий класса: «Тип изделия. Содержит изделия (`Product`), ПФ ссылается на тип. См. ADR-0020.»

`Order.cs` — заменить пару `ProductTypeId`/`ProductType`:
```csharp
    public Guid ProductId { get; set; }                  // изделие заказа (PZP-19); тип — через Product
    public Product? Product { get; set; }
```
Комментарий класса: «Заказ — одна номенклатурная позиция (Product + Quantity)…».

`ShiftTask.cs` — после `MoldId`:
```csharp
    public Guid ProductId { get; set; }   // изделие задания (PZP-19); принадлежит типу ПФ задания
```
в блок Navigation:
```csharp
    public Product? Product { get; set; }
```
метод рядом с `SetPlanQuantity`:
```csharp
    /// <summary>
    /// PZP-19: сменить изделие. Только в «Планируется» и только без заказа (изделие задаёт заказ);
    /// неизменённое значение — no-op в любом статусе (форма шлёт поле при каждом сохранении).
    /// Принадлежность изделия типу ПФ проверяет вызывающий код (ProductSelection).
    /// </summary>
    public void SetProduct(Guid productId)
    {
        if (productId == ProductId)
            return;

        EnsureEditable();
        if (OrderId != null)
            throw new DomainException("Изделие задания с заказом задаёт заказ");
        ProductId = productId;
    }
```

`OrderTaskBinding.cs`:
```csharp
/// <summary>
/// Правило привязки задания к заказу (PZP-05, PZP-19): заказ активен, изделие задания совпадает
/// с изделием заказа. Чистая функция — вызывающий код грузит сущности.
/// </summary>
public static class OrderTaskBinding
{
    public static void EnsureCanBind(Order order, Guid taskProductId)
    {
        if (order.Status != OrderStatus.Active)
            throw new DomainException("Привязать задание можно только к активному заказу");
        if (taskProductId != order.ProductId)
            throw new DomainException("Изделие задания не совпадает с изделием заказа");
    }
}
```

- [ ] **Step 3: `ControlDbContext`**

Добавить `public DbSet<Product> Products { get; set; }` рядом с `ProductTypes`. После конфигурации `ProductType`:
```csharp
        // Конфигурация Product (PZP-19, ADR-0020): изделие внутри типа; FullArticle уникален в БД.
        builder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.ProductType)
                  .WithMany(pt => pt.Products)
                  .HasForeignKey(e => e.ProductTypeId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.FullArticle).IsUnique();
            entity.ToTable("Products");
        });
```
В конфигурации `Order` заменить блок `HasOne(e => e.ProductType)` на:
```csharp
            entity.HasOne(e => e.Product)
                  .WithMany()
                  .HasForeignKey(e => e.ProductId)
                  .OnDelete(DeleteBehavior.Restrict);   // использованное изделие физически не удаляют
```
В конфигурации `ShiftTask` (первый блок) добавить:
```csharp
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 4: Сгенерировать миграцию и переписать `Up`**

Run: `dotnet ef migrations add AddProducts --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`

Сгенерированный `Up` добавит NOT NULL-колонки с `defaultValue: Guid.Empty` — с данными это упадёт на FK. Заменить **тело `Up` целиком** на (порядок важен; имена индексов/FK сверить с тем, что сгенерировал EF, и оставить сгенерированные):

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Артикулы типов — к нормализованному виду (раньше заглавные обеспечивал только фронт).
            migrationBuilder.Sql(@"UPDATE ""ProductTypes"" SET ""Article"" = upper(trim(""Article""));");

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
```

`Down` — оставить сгенерированный EF (обратная миграция данных не нужна: legacy-данных нет), но проверить, что он компилируется.

Проверка: `dotnet ef migrations has-pending-model-changes --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API` → «No changes have been made to the model since the last migration.» Если не так — расхождение конфигурации и `Up`: исправить `Up`, не перегенерируя снапшот вручную.

- [ ] **Step 5: `ProductTypesController.Create` — изделие по умолчанию**

В `Create` артикул нормализовать через `ProductArticle.Normalize` (вместо `Trim()`), проверку дубля оставить (`p.Article == article`). Создавать тип вместе с изделием:
```csharp
        var pt = new ProductType { Article = article, Name = request.Name.Trim(), IsActive = true };
        pt.Products.Add(new Product
        {
            Article = ProductArticle.DefaultPart,
            Name = ProductArticle.DefaultName,
            FullArticle = ProductArticle.Compose(article, ProductArticle.DefaultPart),
            IsActive = true
        });
```
(Полная проверка уникальности против полных артикулов и CRUD изделий — Task 3.) Добавить `using Wintime.Control.Core.Policies;`.

- [ ] **Step 6: `OrdersController` на `ProductId`**

- `ToDto`: вместо трёх строк про тип:
```csharp
            ProductId = o.ProductId,
            ProductFullArticle = o.Product?.FullArticle,
            ProductName = o.Product?.Name,
            ProductTypeId = o.Product?.ProductTypeId ?? Guid.Empty,
            ProductTypeName = o.Product?.ProductType?.Name,
```
- Везде, где был `.Include(o => o.ProductType)`: `.Include(o => o.Product).ThenInclude(p => p!.ProductType)`.
- `GetList`: параметры `Guid? productTypeId = null, Guid? productId = null`;
```csharp
        if (productTypeId.HasValue)
            query = query.Where(o => o.Product!.ProductTypeId == productTypeId.Value);
        if (productId.HasValue)
            query = query.Where(o => o.ProductId == productId.Value);
```
- `GetById`: в `OrderTaskSummaryDto` добавить `ProductFullArticle = t.Product?.FullArticle`, в запрос — `.Include(o => o.Tasks).ThenInclude(t => t.Product)`.
- `Create`:
```csharp
        var product = await _context.Products.Include(p => p.ProductType)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId);
        if (product == null)
            return BadRequest("Указано несуществующее изделие.");
        ProductSelection.EnsureSelectable(product);   // DomainException → 400
        ...
            ProductId = product.Id,
        ...
        order.Product = product;
```
- `Update`: блок смены типа заменить на:
```csharp
        if (request.ProductId.HasValue && request.ProductId.Value != order.ProductId)
        {
            // Смена изделия запрещена, если по заказу уже есть привязанные задания (инвариант 8 PZP-19).
            var hasTasks = await _context.ShiftTasks.AnyAsync(t => t.OrderId == id);
            if (hasTasks)
                return Conflict("Нельзя сменить изделие заказа: к нему привязаны задания.");
            var product = await _context.Products.Include(p => p.ProductType)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId.Value);
            if (product == null)
                return BadRequest("Указано несуществующее изделие.");
            ProductSelection.EnsureSelectable(product);
            order.ProductId = product.Id;
        }
```
- `AttachTask`: `.Include(t => t.Mold)` убрать; `OrderTaskBinding.EnsureCanBind(order, task.ProductId);`.
- DTO-файлы: `CreateOrderRequestDto.ProductTypeId` → `public Guid ProductId { get; set; }`; `UpdateOrderRequestDto.ProductTypeId` → `public Guid? ProductId { get; set; }`; в `OrderDto` заменить блок типа на `ProductId`, `ProductFullArticle`, `ProductName`, `ProductTypeId`, `ProductTypeName`; в `OrderTaskSummaryDto` добавить `public string? ProductFullArticle { get; set; }`.

- [ ] **Step 7: `TasksController` и `UnplannedRunController` — минимум для компиляции**

`CreateTaskRequestDto`: после `MoldId`
```csharp
    /// <summary>Изделие задания (PZP-19). Обязательно (null → 400); принадлежит типу ПФ.</summary>
    public Guid? ProductId { get; set; }
```
`TasksController.ResolveOrderForBindingAsync(Guid orderId, Guid taskProductId)` → `EnsureCanBind(order, taskProductId)`.
`CreateTask`: сразу после проверки `mold.ProductTypeId == null`:
```csharp
        if (request.ProductId is null)
            return BadRequest("Укажите изделие.");
```
в инициализаторе задания `ProductId = request.ProductId.Value,`; привязка к заказу — `ResolveOrderForBindingAsync(request.OrderId.Value, request.ProductId.Value)`. (Проверки выбираемости и принадлежности типу ПФ — Task 4.)
`SetOrder`: `.Include(t => t.Mold)` убрать, `ResolveOrderForBindingAsync(request.OrderId, task.ProductId)`.
`UnplannedRunController.Assign`: удалить проверку `if (task.Mold.ProductTypeId == null) ...` (строки ~167-168) — у задания изделие есть всегда.

Run: `dotnet build` → Expected: 0 ошибок. Затем `dotnet test Wintime.Control.Tests.Unit` → PASS.

- [ ] **Step 8: `IntegrationTestFactory` — тестовое изделие**

```csharp
    public Guid TestProductId { get; } = Guid.NewGuid();
```
В `SeedTestEntitiesAsync` после типа:
```csharp
        if (!db.Products.Any(p => p.Id == TestProductId))
        {
            db.Products.Add(new Product
            {
                Id            = TestProductId,
                ProductTypeId = TestProductTypeId,
                Article       = "0",
                Name          = "По умолчанию",
                FullArticle   = "TYPE-TEST.0",
                IsActive      = true
            });
        }
```
Добавить хелпер, которым будут пользоваться тесты, создающие собственный тип изделия напрямую в БД:
```csharp
    /// <summary>Тип изделия + изделие по умолчанию напрямую в БД (минуя API). Возвращает (typeId, productId).</summary>
    public async Task<(Guid TypeId, Guid ProductId)> CreateProductTypeAsync(string? article = null, bool isActive = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var a = article ?? $"PT-{Guid.NewGuid():N}".ToUpperInvariant();
        var pt = new ProductType { Article = a, Name = "Тип " + a, IsActive = isActive };
        var p = new Product { ProductTypeId = pt.Id, Article = "0", Name = "По умолчанию", FullArticle = a + ".0" };
        pt.Products.Add(p);
        db.ProductTypes.Add(pt);
        await db.SaveChangesAsync();
        return (pt.Id, p.Id);
    }
```

- [ ] **Step 9: Починить существующие интеграционные тесты**

Найти все места:
```bash
grep -rln "new ShiftTask\|Entities.ShiftTask\|CreateTaskRequestDto\|CreateOrderRequestDto\|UpdateOrderRequestDto\|ProductTypeId\|productTypeArticle\|ProductTypeArticle" Wintime.Control.Tests.Integration
```
Правила правки:
- Каждое `new ShiftTask { ... }` в тестах получает `ProductId = <изделие типа ПФ задания>`. Для `TestMoldId` — `_factory.TestProductId`. Если тест создаёт свою ПФ со своим типом — создать тип через `_factory.CreateProductTypeAsync()` и использовать оба id.
- Каждый `CreateTaskRequestDto` (и анонимные тела `POST /api/tasks`) получает `ProductId`.
- `CreateOrderRequestDto { ProductTypeId = X }` → `ProductId = <изделие>`. Ассерты `ProductTypeArticle` → `ProductFullArticle`.
- Тесты, которые проверяли «ПФ без типа нельзя привязать к заказу» через `OrderTaskBinding` (`TaskOrderBindingApiTests`, `OrderTaskBindingApiTests`) — сценарий невозможен (у задания всегда есть изделие): заменить на «изделие задания ≠ изделию заказа → 400» (две разные ПФ двух типов, или два изделия одного типа).
- `TaskProductTypeGuardTests` (ПФ без типа → 400) — тест сохранить: `ProductId` передать любой существующий (`_factory.TestProductId`), ожидаемое сообщение прежнее «У пресс-формы не задан тип изделия».
- `OrdersCrudTests` строка ~108 (`body.ProductTypeId = Guid.NewGuid()` → 400) → `body.ProductId = Guid.NewGuid()` → 400.
- `ProductTypesControllerTests` и `ProductTypeEntityTests`: где тип создаётся напрямую через `db.ProductTypes.Add` и затем используется в заказе/задании — добавить изделие; артикулы в ассертах — с учётом нормализации к заглавным (`$"ART-{Guid.NewGuid():N}"` даёт строчные hex — ассерт сравнивать с `.ToUpperInvariant()`).

Run: `dotnet test Wintime.Control.Tests.Integration` → Expected: всё PASS. Не переходить дальше, пока не зелёно.

- [ ] **Step 10: Тест миграции `AddProductsMigrationTests` (падает до правильного `Up`, проходит после)**

NOT NULL-колонки схемы до `AddProducts` (сверено по `ControlDbContextModelSnapshot.cs` на 2026-10-18): `Templates` (Id, Author, CreatedAt, IsActive, JsonConfig, Manufacturer, Model, Name, UpdatedAt, Version), `Imms` (Id, CreatedAt, IsActive, Name, TemplateId), `ProductTypes` (Id, Article, CreatedAt, IsActive, Name), `Molds` (Id, Cavities, CreatedAt, FormId, IsActive, MaxResourceCycles, Name, PartWeightGrams, RunnerWeightGrams), `Orders` (Id, CreatedAt, DueDate, Number, ProductTypeId, Quantity, Status), `ShiftTasks` (Id, ActualMaterialWeightGrams, ActualQuantity, CreatedAt, DefectQuantity, ImmId, MoldId, PlanQuantity, PlannedDate, PlannedSetupMinutes, ReleaseBufferFixedMinutes, ReleaseBufferPercent, Status, WorkMode). Если `INSERT` упадёт на NOT NULL — дополнить колонку по сообщению об ошибке.

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

/// <summary>
/// Перенос данных миграции AddProducts на непустой БД: обычные тесты гоняют миграции на пустой.
/// Отдельный контейнер на тест — миграции до предыдущей, сид сырым SQL, затем AddProducts.
/// </summary>
public class AddProductsMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private ControlDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    private static readonly Guid TemplateId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ImmId      = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TypeId     = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid MoldId     = Guid.Parse("00000000-0000-0000-0000-000000000004");

    // Сид в схеме ДО AddProducts. moldTypeSql — значение "ProductTypeId" ПФ ('...' или NULL).
    private static string SeedSql(string moldTypeSql) => $@"
INSERT INTO ""Templates"" (""Id"",""Author"",""CreatedAt"",""IsActive"",""JsonConfig"",""Manufacturer"",""Model"",""Name"",""UpdatedAt"",""Version"")
VALUES ('{TemplateId}','t',now(),TRUE,'{{}}','m','m','tpl',now(),'1');
INSERT INTO ""Imms"" (""Id"",""CreatedAt"",""IsActive"",""Name"",""TemplateId"")
VALUES ('{ImmId}',now(),TRUE,'ТПА-1','{TemplateId}');
INSERT INTO ""ProductTypes"" (""Id"",""Article"",""CreatedAt"",""IsActive"",""Name"")
VALUES ('{TypeId}','abc ',now(),TRUE,'Крышка');
INSERT INTO ""Molds"" (""Id"",""Cavities"",""CreatedAt"",""FormId"",""IsActive"",""MaxResourceCycles"",""Name"",""PartWeightGrams"",""RunnerWeightGrams"",""ProductTypeId"")
VALUES ('{MoldId}',2,now(),'F-1',TRUE,100000,'Форма',10,2,{moldTypeSql});
INSERT INTO ""Orders"" (""Id"",""CreatedAt"",""DueDate"",""Number"",""ProductTypeId"",""Quantity"",""Status"")
VALUES (gen_random_uuid(),now(),now() + interval '7 days','ORD-1','{TypeId}',100,0);
INSERT INTO ""ShiftTasks"" (""Id"",""ActualMaterialWeightGrams"",""ActualQuantity"",""CreatedAt"",""DefectQuantity"",""ImmId"",""MoldId"",""PlanQuantity"",""PlannedDate"",""PlannedSetupMinutes"",""ReleaseBufferFixedMinutes"",""ReleaseBufferPercent"",""Status"",""WorkMode"")
VALUES (gen_random_uuid(),0,0,now(),0,'{ImmId}','{MoldId}',100,current_date,0,0,0,0,0);";

    private async Task<(IMigrator Migrator, string Target)> MigrateToPreviousAndSeedAsync(ControlDbContext db, string moldTypeSql)
    {
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_AddProducts"));
        await migrator.MigrateAsync(all[all.IndexOf(target) - 1]);

        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(SeedSql(moldTypeSql), conn);
        await cmd.ExecuteNonQueryAsync();
        return (migrator, target);
    }

    [Fact]
    public async Task AddProducts_MigratesExistingTypesOrdersAndTasks()
    {
        await using var db = NewContext();
        var (migrator, target) = await MigrateToPreviousAndSeedAsync(db, $"'{TypeId}'");

        await migrator.MigrateAsync(target);

        await using var check = NewContext();
        (await check.ProductTypes.SingleAsync()).Article.Should().Be("ABC");   // 'abc ' нормализован
        var product = await check.Products.SingleAsync();
        product.FullArticle.Should().Be("ABC.0");
        product.Article.Should().Be("0");
        product.Name.Should().Be("По умолчанию");
        (await check.ShiftTasks.SingleAsync()).ProductId.Should().Be(product.Id);
        (await check.Orders.SingleAsync()).ProductId.Should().Be(product.Id);
    }

    [Fact]
    public async Task AddProducts_TaskOnMoldWithoutType_FailsWithMessage()
    {
        await using var db = NewContext();
        var (migrator, target) = await MigrateToPreviousAndSeedAsync(db, "NULL");

        await FluentActions.Awaiting(() => migrator.MigrateAsync(target))
            .Should().ThrowAsync<PostgresException>()
            .Where(e => e.MessageText.Contains("PZP-19"));
    }
}
```
Enum-колонки (`Status`, `WorkMode`) хранятся числами (`HasConversion` в `ControlDbContext` нет): `0` = `Planned`/`Auto`/`Active`.

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~AddProductsMigrationTests"` → PASS.

- [ ] **Step 11: Полный прогон и commit**

Run: `dotnet build && dotnet test Wintime.Control.Tests.Unit && dotnet test Wintime.Control.Tests.Integration` → всё PASS.

```bash
git add -A Wintime.Control.Core Wintime.Control.Infrastructure Wintime.Control.API Wintime.Control.Tests.Unit Wintime.Control.Tests.Integration
git commit -m "feat(products): Products table, Order/ShiftTask.ProductId, migration AddProducts (PZP-19)"
```

---

### Task 3: API справочника — изделия и уникальность артикулов

**Files:**
- Modify: `Wintime.Control.API/Controllers/ProductTypesController.cs`
- Create: `Wintime.Control.Core/DTOs/ProductType/ProductDto.cs`, `CreateProductRequestDto.cs`, `UpdateProductRequestDto.cs`
- Modify: `Wintime.Control.Core/DTOs/ProductType/ProductTypeDto.cs` (+ `Products`)
- Test: `Wintime.Control.Tests.Integration/ProductTypes/ProductTypesControllerTests.cs` (дополнить)
- Test: `Wintime.Control.Tests.Integration/ProductTypes/ProductsApiTests.cs` (новый)

**Interfaces:**
- Consumes: Task 1 (`ProductArticle`), Task 2 (`DbSet<Product>`, `ProductType.Products`, `Order.ProductId`, `ShiftTask.ProductId`).
- Produces (HTTP, роли записи Admin+Manager, чтения + Adjuster):
  - `GET /api/producttypes?isActive=&search=` → `ProductTypeDto[]` с `products: ProductDto[]` (сорт. по `article`)
  - `GET /api/producttypes/{id}` → `ProductTypeDto`
  - `POST /api/producttypes` `{article, name}` → 201 `ProductTypeDto` (с изделием `0`)
  - `PUT /api/producttypes/{id}` `{article?, name?, isActive?}` → 204
  - `POST /api/producttypes/{id}/products` `{article, name}` → 201 `ProductDto`
  - `PUT /api/producttypes/{id}/products/{productId}` `{article?, name?, isActive?}` → 204
  - `DELETE /api/producttypes/{id}/products/{productId}` → 204
  - `ProductDto { Guid Id; string Article; string FullArticle; string Name; bool IsActive; bool IsUsed; }`

- [ ] **Step 1: DTO**

```csharp
namespace Wintime.Control.Core.DTOs.ProductType;

public class ProductDto
{
    public Guid Id { get; set; }
    public string Article { get; set; } = string.Empty;      // часть артикула
    public string FullArticle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsUsed { get; set; }                         // есть в заказах/заданиях — удалить нельзя
}

public class CreateProductRequestDto
{
    public string Article { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class UpdateProductRequestDto
{
    public string? Article { get; set; }
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
}
```
(каждый класс — в своём файле). В `ProductTypeDto`: `public List<ProductDto> Products { get; set; } = new();`.

- [ ] **Step 2: Падающие интеграционные тесты `ProductsApiTests`**

Класс по образцу `ProductTypesControllerTests` (`[Collection("Integration")]`, `IClassFixture<IntegrationTestFactory>`, `ManagerClientAsync`). Вспомогательный метод:
```csharp
    private static string Uniq(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private async Task<ProductTypeDto> CreateTypeAsync(HttpClient c, string article)
    {
        var r = await c.PostAsJsonAsync("/api/producttypes", new CreateProductTypeRequestDto { Article = article, Name = "Тип" });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<ProductTypeDto>())!;
    }
```
Тесты (каждый — отдельный `[Fact]`, уникальные артикулы через `Uniq`):
1. `CreateType_HasDefaultProduct` — `dto.Products` = один элемент `{Article "0", Name "По умолчанию", FullArticle $"{a}.0", IsActive true, IsUsed false}`.
2. `CreateType_NormalizesArticle` — `" ab1 "` → `Article == "AB1"`.
3. `AddProduct_ReturnsCreatedWithFullArticle` — POST `{article:"blk", name:"Чёрный"}` → 201, `FullArticle == $"{a}.BLK"`.
4. `AddProduct_DuplicatePartInType_Conflict` — два раза `BLK` → второй 409.
5. `AddProduct_FullArticleCollisionAcrossTypes_Conflict` — тип `X.А` (у него `X.А.0`), тип `X`, добавить в `X` часть `А.0` → 409, текст содержит `X.А.0` (X = `Uniq("C")`).
6. `AddProduct_FullArticleEqualsTypeArticle_Conflict` — тип `Y.B`, тип `Y`, добавить в `Y` часть `B` → 409.
7. `CreateType_ArticleEqualsExistingFullArticle_Conflict` — тип `Z` (есть `Z.0`), создать тип `Z.0` → 409.
8. `RenameType_RecomputesFullArticles` — тип `R1` + изделие `BLK`; PUT `{article:"R2"}` (R1/R2 уникальные) → 204; GET → полные `R2.0`, `R2.BLK`.
9. `RenameType_TypeArticleEqualsFullArticle_Conflict` — тип `K` с изделием `X` (полный `K.X`), тип `M` с изделием `N`. PUT `M` `{article:"K.X"}` → 409; GET `M` — артикул `M` и полный `M.N` прежние.
10. `RenameType_RecomputedFullArticleCollides_RollsBack` — тип `S.T` (полный `S.T.0`), тип `U` с изделием `T.0` (полный `U.T.0`). PUT `U` `{article:"S"}` → пересчёт даёт `S.0` и `S.T.0`, второй конфликтует → 409; GET `U` — артикул `U`, полные `U.0`, `U.T.0` прежние. (`S`, `U` — уникальные префиксы через `Uniq`.)
11. `RenameProductPart_RecomputesFullArticle` — PUT products/{pid} `{article:"wht"}` → GET → `{a}.WHT`.
12. `ArchiveLastActiveProduct_Conflict` — у нового типа архивировать `0` → 409, текст «Это последнее активное изделие типа. Чтобы вывести тип из оборота, переведите в архив сам тип».
13. `ArchiveProduct_WhenAnotherActive_Ok` — добавить `BLK`, архивировать `0` → 204; GET → `0` `IsActive=false`.
14. `ArchivedType_ProductsCanBeArchived` — PUT тип `{isActive:false}`; архивировать `0` → 204.
15. `RestoreType_WithoutActiveProducts_Conflict` — продолжение 14: PUT тип `{isActive:true}` → 409 «Сначала верните из архива хотя бы одно изделие типа».
16. `DeleteUnusedProduct_Ok` — добавить `BLK`, DELETE → 204; GET → одно изделие.
17. `DeleteLastProduct_Conflict` — DELETE `0` у нового типа → 409.
18. `DeleteUsedProduct_Conflict` — создать заказ на добавленное изделие `BLK` через `POST /api/orders` (`ProductId`, `Number`, `DueDate = DateTime.UtcNow.AddDays(7)`, `Quantity = 1`), DELETE → 409 «Изделие используется в заказах или заданиях — его можно только отправить в архив»; GET → `IsUsed == true`.
19. `Search_FindsByFullArticleOfProduct` — `GET /api/producttypes?search={a}.BLK` (после добавления BLK) → содержит тип.
20. `Adjuster_CannotAddProduct` — клиент `test_adjuster` / `Adjuster123!` → 403.

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ProductsApiTests"` → FAIL (404/нет эндпоинтов).

- [ ] **Step 3: Реализовать контроллер**

Ключевые части (вставить в `ProductTypesController`):

```csharp
    private static ProductDto ToDto(Product p, ISet<Guid> usedIds) => new()
    {
        Id = p.Id, Article = p.Article, FullArticle = p.FullArticle, Name = p.Name,
        IsActive = p.IsActive, IsUsed = usedIds.Contains(p.Id)
    };

    private static ProductTypeDto ToDto(ProductType t, ISet<Guid> usedIds) => new()
    {
        Id = t.Id, Article = t.Article, Name = t.Name, IsActive = t.IsActive,
        Products = t.Products.OrderBy(p => p.Article).Select(p => ToDto(p, usedIds)).ToList()
    };

    // Изделия из набора, которые есть в заказах или заданиях (одним запросом на каждую таблицу).
    private async Task<HashSet<Guid>> UsedProductIdsAsync(IReadOnlyCollection<Guid> productIds)
    {
        var inOrders = await _context.Orders.Where(o => productIds.Contains(o.ProductId))
            .Select(o => o.ProductId).Distinct().ToListAsync();
        var inTasks = await _context.ShiftTasks.Where(t => productIds.Contains(t.ProductId))
            .Select(t => t.ProductId).Distinct().ToListAsync();
        return inOrders.Concat(inTasks).ToHashSet();
    }

    // ── Единое пространство артикулов (PZP-19, решение 4) ──
    // Артикул типа не совпадает ни с другим артикулом типа, ни с полным артикулом изделия.
    private async Task<string?> TypeArticleConflictAsync(string article, Guid? exceptTypeId)
    {
        var type = await _context.ProductTypes.FirstOrDefaultAsync(t => t.Article == article && t.Id != exceptTypeId);
        if (type != null)
            return $"Артикул '{article}' уже используется типом изделия «{type.Name}».";
        var product = await _context.Products.Include(p => p.ProductType)
            .FirstOrDefaultAsync(p => p.FullArticle == article);
        return product == null ? null
            : $"Артикул {article} совпадает с полным артикулом изделия «{product.Name}» (тип {product.ProductType!.Article}).";
    }

    // Полный артикул не совпадает ни с полным артикулом другого изделия, ни с артикулом типа.
    private async Task<string?> FullArticleConflictAsync(string fullArticle, IReadOnlyCollection<Guid> exceptProductIds)
    {
        var product = await _context.Products.Include(p => p.ProductType)
            .FirstOrDefaultAsync(p => p.FullArticle == fullArticle && !exceptProductIds.Contains(p.Id));
        if (product != null)
            return $"Полный артикул {fullArticle} уже есть у изделия «{product.Name}» (тип {product.ProductType!.Article}).";
        var type = await _context.ProductTypes.FirstOrDefaultAsync(t => t.Article == fullArticle);
        return type == null ? null
            : $"Полный артикул {fullArticle} совпадает с артикулом типа изделия «{type.Name}».";
    }

    // Подстраховка от гонки: unique-индекс сработал между проверкой и сохранением.
    private static bool IsUniqueViolation(DbUpdateException e) =>
        e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };
```

Поведение эндпоинтов:
- `GetList`/`GetById`: `.Include(t => t.Products)`; поиск:
  ```csharp
  query = query.Where(p => p.Name.Contains(search) || p.Article.Contains(search)
      || p.Products.Any(x => x.FullArticle.Contains(search) || x.Name.Contains(search)));
  ```
  `usedIds = await UsedProductIdsAsync(все id изделий результата)`.
- `Create`: `article = ProductArticle.Normalize(request.Article)`; пустой → 400; `TypeArticleConflictAsync(article, null)` → 409; `FullArticleConflictAsync(Compose(article,"0"), [])` → 409; создать (как в Task 2); `SaveChanges` в `try/catch (DbUpdateException e) when (IsUniqueViolation(e))` → 409 «Артикул уже используется.».
- `Update` (тип): `Include(Products)`.
  - Смена `Article`: `trimmed = Normalize`; пустой → 400; если отличается — `TypeArticleConflictAsync(trimmed, id)` → 409; для каждого изделия `newFull = Compose(trimmed, p.Article)` → `FullArticleConflictAsync(newFull, ids изделий этого типа)` → 409; затем присвоить `pt.Article` и все `p.FullArticle`. Всё до `SaveChanges` — откат при 409 автоматический (ничего не сохранено).
  - `IsActive == true` при `!pt.IsActive` и `!pt.Products.Any(p => p.IsActive)` → 409 «Сначала верните из архива хотя бы одно изделие типа».
  - `SaveChanges` с тем же `catch`.
- `POST {id}/products`: тип не найден → 404; `part = Normalize`, пустой → 400 «Артикул изделия обязателен.»; `Name` пустой → 400 «Наименование обязательно.»; `full = Compose(pt.Article, part)`; `FullArticleConflictAsync(full, [])` → 409; добавить `Product { ProductTypeId = id, Article = part, Name = Trim, FullArticle = full, IsActive = true }`; 201 `CreatedAtAction(nameof(GetById), new { id }, ToDto(product, empty))`.
- `PUT {id}/products/{pid}`: изделие типа `id` не найдено → 404.
  - `Article`: Normalize, пустой → 400, `full = Compose(pt.Article, part)`, `FullArticleConflictAsync(full, [pid])` → 409, присвоить оба поля.
  - `Name`: пустой → 400, `Trim`.
  - `IsActive == false` при `product.IsActive && pt.IsActive` и других активных изделий типа нет → 409 «Это последнее активное изделие типа. Чтобы вывести тип из оборота, переведите в архив сам тип».
  - `IsActive == true` — без ограничений.
- `DELETE {id}/products/{pid}`: 404 если нет; используется (`UsedProductIdsAsync([pid])`) → 409 «Изделие используется в заказах или заданиях — его можно только отправить в архив»; у типа одно изделие → 409 «Нельзя удалить последнее изделие типа.»; иначе `Remove` + `SaveChanges` → 204. Комментарий над методом: «Физическое удаление — сознательное исключение (ADR-0020): у неиспользованного изделия нет истории.»
- Атрибуты ролей — как у существующих методов (`[Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]` на запись, `+ Roles.Adjuster` на чтение).

Существующие тесты `ProductTypesControllerTests` на дубль артикула (`Create_DuplicateArticle_ReturnsConflict`) должны остаться зелёными (теперь сравнение по нормализованному артикулу).

- [ ] **Step 4: PASS**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ProductTypes"` → PASS. Затем полный `dotnet test Wintime.Control.Tests.Integration` → PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.API/Controllers/ProductTypesController.cs Wintime.Control.Core/DTOs/ProductType Wintime.Control.Tests.Integration/ProductTypes
git commit -m "feat(products): products CRUD in product type catalog, article uniqueness (PZP-19)"
```

---

### Task 4: Правила изделия в заданиях + показ изделия в DTO

**Files:**
- Modify: `Wintime.Control.API/Controllers/TasksController.cs` (`CreateTask`, `UpdateTask`, все `Include(t => t.Mold)` для чтения, поиск `:138` и `GetList`)
- Modify: `Wintime.Control.Core/DTOs/Tasks/UpdateTaskRequestDto.cs`, `TaskDto.cs`, `TaskMappingExtensions.cs`
- Modify: `Wintime.Control.API/Controllers/UnplannedRunController.cs` (метки `:71`, `:145`; `Include` изделия)
- Modify: `Wintime.Control.API/Controllers/ImmController.cs` (`CurrentProductArticle`), `Wintime.Control.Core/DTOs/Imm/ImmDto.cs`
- Test: `Wintime.Control.Tests.Integration/Tasks/TaskProductGuardTests.cs` (переименовать `TaskProductTypeGuardTests.cs` через `git mv` и дополнить)

**Interfaces:**
- Consumes: Task 1 `ProductSelection`; Task 2 `ShiftTask.SetProduct`, `CreateTaskRequestDto.ProductId`.
- Produces:
  - `UpdateTaskRequestDto.ProductId : Guid?` (`null` — не менять)
  - `TaskDto.ProductId : Guid`, `ProductFullArticle : string?`, `ProductName : string?`, `ProductTypeName : string?`
  - `ImmDto.CurrentProductArticle : string?`
  - `GET /api/tasks?search=` и `GET /api/tasks/my?search=` ищут ещё по `Product.FullArticle` и `Product.Name`

- [ ] **Step 1: Падающие тесты `TaskProductGuardTests`**

`git mv Wintime.Control.Tests.Integration/Tasks/TaskProductTypeGuardTests.cs Wintime.Control.Tests.Integration/Tasks/TaskProductGuardTests.cs`, класс переименовать. Добавить тесты (тело запроса — как у существующих тестов файла, с `ProductId`):
1. `Create_WithoutProduct_BadRequest` — `ProductId = null` → 400.
2. `Create_ProductOfOtherType_BadRequest` — `_factory.CreateProductTypeAsync()` → его `ProductId` с `TestMoldId` → 400, текст «Изделие не относится к типу изделия пресс-формы».
3. `Create_ArchivedProduct_BadRequest` — добавить в тестовый тип второе изделие через API (`POST /api/producttypes/{TestProductTypeId}/products`), архивировать его (`PUT … {isActive:false}`), создать задание с ним → 400 «Изделие в архиве».
4. `Create_ReturnsProductInDto` — успешное создание → `TaskDto.ProductId == TestProductId`, `ProductFullArticle == "TYPE-TEST.0"`.
5. `Update_ChangeProduct_Planned_Ok` — второе активное изделие тестового типа; PUT `/api/tasks/{id}` `{productId}` → 204, GET → новое изделие.
6. `Update_ChangeProduct_Issued_BadRequest` — задание выдать (`POST /api/tasks/{id}/issue` с наладчиком, см. существующие тесты выдачи) → PUT с другим изделием → 400.
7. `Update_ChangeProduct_WithOrder_BadRequest` — задание с заказом (заказ на `TestProductId`) → PUT с другим изделием → 400 «Изделие задания с заказом задаёт заказ».
8. `Update_ProductOfOtherType_BadRequest` → 400.
9. `Update_SameProduct_InIssued_Ok` — PUT с тем же `productId` в «Выдано» → 204 (форма шлёт поле всегда).
10. `Search_ByProductFullArticle` — `GET /api/tasks?search=TYPE-TEST.0` возвращает созданное задание.

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~TaskProductGuardTests"` → FAIL.

- [ ] **Step 2: Реализация**

`CreateTask` — после проверки `request.ProductId is null` (Task 2):
```csharp
        var product = await _context.Products.Include(p => p.ProductType)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId.Value);
        if (product == null)
            return BadRequest("Указано несуществующее изделие.");
        ProductSelection.EnsureSelectable(product);                       // DomainException → 400
        ProductSelection.EnsureBelongsToMold(product, mold.ProductTypeId);
```
перед `CreatedAtAction`: `task.Product = product;`.

`UpdateTaskRequestDto`:
```csharp
    /// <summary>Изделие задания (PZP-19); <c>null</c> — не менять.</summary>
    public Guid? ProductId { get; set; }
```
`UpdateTask` — `FindAsync` заменить на `.Include(t => t.Mold).FirstOrDefaultAsync(t => t.Id == id)`; первым шагом после `NotFound`:
```csharp
        if (request.ProductId.HasValue && request.ProductId.Value != task.ProductId)
        {
            var product = await _context.Products.Include(p => p.ProductType)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId.Value);
            if (product == null)
                return BadRequest("Указано несуществующее изделие.");
            ProductSelection.EnsureSelectable(product);
            ProductSelection.EnsureBelongsToMold(product, task.Mold.ProductTypeId);
            task.SetProduct(product.Id);   // статус и заказ — гард в сущности (ADR-0002)
        }
```

`TaskDto` — после `MoldName`:
```csharp
    // PZP-19: изделие задания
    public Guid ProductId { get; set; }
    public string? ProductFullArticle { get; set; }
    public string? ProductName { get; set; }
    public string? ProductTypeName { get; set; }
```
`TaskMappingExtensions.ToDto`:
```csharp
            ProductId = t.ProductId,
            ProductFullArticle = t.Product?.FullArticle,
            ProductName = t.Product?.Name,
            ProductTypeName = t.Product?.ProductType?.Name,
```
обновить XML-комментарий («…ТПА/ПФ/изделия/наладчика заполняются, только если загружены навигации»).

Во всех запросах `TasksController`, которые маппят в `TaskDto` (строки с `.Include(t => t.Mold)` ~58, ~128, ~191 и `GetTaskById`), добавить `.Include(t => t.Product).ThenInclude(p => p!.ProductType)`. Поиск (оба места, где `ILike(t.Mold.Name, pattern)`):
```csharp
                EF.Functions.ILike(t.Mold.Name, pattern) ||
                EF.Functions.ILike(t.Product!.FullArticle, pattern) ||
                EF.Functions.ILike(t.Product!.Name, pattern));
```
(проверить `GetList` около строки 43-110 на наличие своего поиска и сделать то же).

`UnplannedRunController` — метки:
```csharp
                    : $"{r.AssignedTask.Product!.FullArticle} · {r.AssignedTask.Mold.Name} · план {r.AssignedTask.PlanQuantity}",
```
```csharp
                Label = $"{e.Task.Product!.FullArticle} · {e.Task.Mold.Name} · план {e.Task.PlanQuantity} · {e.Task.PlannedDate:dd.MM.yyyy}",
```
и в соответствующие запросы `.Include(t => t.Product)` (для первой — `.Include(r => r.AssignedTask).ThenInclude(t => t!.Product)`, по образцу того, как там подгружается `Mold`). Существующие тесты журнала, проверяющие `Label`, обновить под новый формат.

`ImmDto`: `public string? CurrentProductArticle { get; set; }`; в `ImmController` рядом с `CurrentMoldName` (строки ~78-80) — тот же подзапрос с `.Select(t => t.Product!.FullArticle)`.

- [ ] **Step 3: PASS + полный прогон**

Run: `dotnet test Wintime.Control.Tests.Integration` → PASS; `dotnet test Wintime.Control.Tests.Unit` → PASS.

- [ ] **Step 4: Commit**

```bash
git add -A Wintime.Control.API Wintime.Control.Core Wintime.Control.Tests.Integration
git commit -m "feat(tasks): product selection rules and product in task DTOs (PZP-19)"
```

---

### Task 5: Фронт — API и справочник «Типы изделий»

**Files:**
- Modify: `Wintime-Control-Frontend/src/api/productTypes.js`
- Modify: `Wintime-Control-Frontend/src/views/dictionary/ProductTypeDictionary.vue`
- Modify: `Wintime-Control-Frontend/src/layouts/DefaultLayout.vue:81`
- Create: `Wintime-Control-Frontend/src/utils/productLabel.js`
- Test: `Wintime-Control-Frontend/src/views/dictionary/__tests__/ProductTypeDictionary.spec.js` (создать)
- Test: `Wintime-Control-Frontend/src/utils/__tests__/productLabel.spec.js`

**Interfaces:**
- Consumes: HTTP из Task 3.
- Produces:
  - `productTypesApi.addProduct(typeId, data)`, `updateProduct(typeId, productId, data)`, `deleteProduct(typeId, productId)`
  - `productLabel.js`: `export function productSecondary(typeName, productName)` → `'Крышка 48мм · Чёрный'` (пропускает пустые части); `export function activeProducts(productType)` → массив активных изделий `productType?.products ?? []`

- [ ] **Step 1: API**

```js
  addProduct(typeId, data) {
    return apiClient.post(`/producttypes/${typeId}/products`, data)
  },
  updateProduct(typeId, productId, data) {
    return apiClient.put(`/producttypes/${typeId}/products/${productId}`, data)
  },
  deleteProduct(typeId, productId) {
    return apiClient.delete(`/producttypes/${typeId}/products/${productId}`)
  }
```

- [ ] **Step 2: Утилита + тест**

```js
// Подпись изделия (PZP-19): основная строка — полный артикул, вторичная — тип · изделие.
export function productSecondary(typeName, productName) {
  return [typeName, productName].filter(Boolean).join(' · ')
}

export function activeProducts(productType) {
  return (productType?.products ?? []).filter(p => p.isActive)
}
```
Тест: `productSecondary('Крышка', 'Чёрный') === 'Крышка · Чёрный'`, `productSecondary(null, 'Чёрный') === 'Чёрный'`, `activeProducts({products:[{isActive:true},{isActive:false}]}).length === 1`, `activeProducts(null)` → `[]`.

- [ ] **Step 3: Падающие тесты справочника**

Мок `@/api/productTypes` (`getList`, `create`, `update`, `addProduct`, `updateProduct`, `deleteProduct`), `mount` с `ElementPlus`, как в `MoldDictionary.spec.js`. Тесты:
1. Заголовок страницы «Справочник типов изделий», кнопка «Добавить тип изделия».
2. В строке типа видно число активных изделий (тип с 2 активными и 1 архивным → «2»).
3. После `create` окно остаётся открытым в режиме редактирования (`wrapper.vm.editing` = созданный тип) и показывает блок «Изделия» с изделием `0`.
4. «Добавить изделие» → ввод части `blk` приводится к `BLK`, живой полный артикул `<артикул типа>.BLK`; сохранение вызывает `addProduct(typeId, { article: 'BLK', name })`, затем перезагружает тип.
5. Кнопка «Удалить» есть только у изделия с `isUsed: false`.
6. Ошибка сервера (`addProduct` reject с `response.data = 'Полный артикул … уже есть …'`) показывается через `ElMessage.error` текстом сервера.

Run: `cd Wintime-Control-Frontend && npx vitest run src/views/dictionary/__tests__/ProductTypeDictionary.spec.js` → FAIL.

- [ ] **Step 4: Реализация `ProductTypeDictionary.vue`**

- Тексты: «Справочник типов изделий» / «Номенклатура: типы изделий и их изделия» / «Добавить тип изделия»; окно — «Новый тип изделия» / «Редактирование типа изделия»; сообщения «Тип изделия создан/обновлён/в архиве/возвращён».
- Таблица типов: `el-table` с `type="expand"`-колонкой — внутри мини-таблица изделий (полный артикул, наименование, статус); новая колонка «Изделия» = `activeProducts(row).length`.
- `save()` при создании: после `productTypesApi.create(form)` — `editing.value = data` (тип с изделиями из ответа), окно не закрывать, `loadItems()`.
- Блок «Изделия» в окне (`v-if="editing"`): таблица по `editing.products` — колонки «Артикул» (`article`), «Полный артикул», «Наименование», «Статус»; кнопки «Ред.» (окно изделия), «В архив»/«Вернуть» (`updateProduct(…, { isActive: !p.isActive })`), «Удалить» (`v-if="!p.isUsed"`, `ElMessageBox.confirm` → `deleteProduct`). После каждой операции — `reloadEditing()`: `productTypesApi.getById(editing.value.id)` → `editing.value = data` и `loadItems()`.
- Вложенное окно изделия (`el-dialog append-to-body`): поля «Артикул» (`@input` → `toUpperCase()`, как у типа) и «Наименование»; под полем артикула — `Полный артикул: {{ editing.article }}.{{ productForm.article || '…' }}`. Сохранение: `addProduct` или `updateProduct(typeId, id, { article, name })`.
- Ошибки — `ElMessage.error(error.response?.data ?? 'Ошибка сохранения изделия')` (как в существующем `save`).
- Добавить `getById` в `productTypesApi`, если его нет (есть — `GET /producttypes/{id}`).

`DefaultLayout.vue:81`: «Изделия» → «Типы изделий».

- [ ] **Step 5: PASS + полный фронт-прогон**

Run: `cd Wintime-Control-Frontend && npx vitest run` → PASS.

- [ ] **Step 6: Commit**

```bash
git add Wintime-Control-Frontend/src
git commit -m "feat(ui): product type catalog with products (PZP-19)"
```

---

### Task 6: Фронт — заказы (форма, список, карточка)

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/OrdersView.vue`
- Modify: `Wintime-Control-Frontend/src/components/orders/OrderDetailModal.vue`
- Test: `Wintime-Control-Frontend/src/views/__tests__/OrdersView.spec.js`, `src/components/orders/__tests__/OrderDetailModal.spec.js`

**Interfaces:**
- Consumes: `OrderDto` (`productId`, `productFullArticle`, `productName`, `productTypeId`, `productTypeName`), `GET /producttypes?isActive=true` с `products`, `activeProducts` из Task 5; `TaskDto.productId` (Task 4).
- Produces: `OrderDetailModal` передаёт в `TaskFormModal` `lockedOrder` = `{ id, number, productId, productTypeId, productFullArticle }`.

- [ ] **Step 1: Падающие тесты**

`OrdersView.spec.js` (переписать тесты, использующие `productTypeArticle`/`productTypeId` в форме):
1. Поле «Изделие» заблокировано, пока не выбран тип.
2. Выбор типа с одним активным изделием подставляет его в `form.productId`.
3. Тип с двумя активными изделиями — `form.productId` остаётся `null`, в опциях оба (архивное — нет).
4. Смена типа сбрасывает `form.productId`.
5. Создание шлёт `productId`, а не `productTypeId` (`ordersApi.create` вызван с `expect.objectContaining({ productId: 'p-1' })` и `not.toHaveProperty('productTypeId')`).
6. Колонка «Изделие» показывает `productFullArticle`.
7. Редактирование заказа: форма заполняется `productTypeId` и `productId` из строки.

`OrderDetailModal.spec.js`:
1. Кандидаты «Привязать существующее» — задания без заказа с `productId === order.productId`; `moldsApi.getList` не вызывается.
2. «Создать задание» передаёт `lockedOrder` с `productId`, `productTypeId`, `productFullArticle`.
3. В таблице заданий колонка «Изделие» = `productFullArticle`.

Run: `npx vitest run src/views/__tests__/OrdersView.spec.js src/components/orders/__tests__/OrderDetailModal.spec.js` → FAIL.

- [ ] **Step 2: Реализация**

`OrdersView.vue`:
- `form`: `productTypeId: null, productId: null`; правило `productId: [{ required: true, message: 'Выберите изделие', trigger: 'change' }]`.
- Под «Тип изделия» — `el-form-item label="Изделие" prop="productId" required` с `el-select :disabled="!form.productTypeId"`, опции `productOptions`: `activeProducts(productTypes.find(t => t.id === form.productTypeId))`, `label` = `${p.fullArticle} — ${p.name}`.
- `watch(() => form.productTypeId, (id, old) => { if (old !== undefined && id !== old) form.productId = null; const list = productOptions.value; if (list.length === 1) form.productId = list[0].id })`. При открытии на редактирование присвоить сначала тип, потом изделие (чтобы watch не сбросил изделие — выставлять флаг `populating` или присваивать `productId` в `nextTick`).
- Тело запроса: без `productTypeId` (`const { productTypeId, ...payload } = form`).
- Колонка списка: `prop="productFullArticle" label="Изделие"`.

`OrderDetailModal.vue`:
- `:198-201`: убрать `moldsApi.getList`, фильтр `(t) => !t.orderId && t.productId === order.value?.productId` (+ прежнее условие о незавершённости, если оно есть рядом).
- `lockedOrder` (`:150-155`): `{ id, number, productId: order.value.productId, productTypeId: order.value.productTypeId, productFullArticle: order.value.productFullArticle }`.
- Таблица заданий: колонка `prop="productFullArticle" label="Изделие"` перед «ПФ»; метки кандидатов `${t.immName} / ${t.moldName} / ${t.productFullArticle}`.

- [ ] **Step 3: PASS + полный фронт-прогон, commit**

Run: `npx vitest run` → PASS.
```bash
git add Wintime-Control-Frontend/src
git commit -m "feat(ui): product selection in orders (PZP-19)"
```

---

### Task 7: Фронт — форма задания

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/tasks/TaskFormModal.vue`
- Test: `Wintime-Control-Frontend/src/views/tasks/__tests__/TaskFormModal.spec.js`

**Interfaces:**
- Consumes: `productTypesApi.getList({ isActive: true })` (с `products`), `activeProducts`; `lockedOrder` из Task 6; `TaskDto.productId`; `ordersApi.getList({ status: 'Active', productId })`.
- Produces: `tasksApi.create`/`update` шлют `productId`.

- [ ] **Step 1: Падающие тесты**

В `TaskFormModal.spec.js` добавить мок:
```js
vi.mock('@/api/productTypes', () => ({
  productTypesApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      { id: 'pt-X', article: 'ART-X', name: 'Тип X', isActive: true, products: [
        { id: 'p-X0', article: '0', fullArticle: 'ART-X.0', name: 'По умолчанию', isActive: true }
      ] },
      { id: 'pt-Y', article: 'ART-Y', name: 'Тип Y', isActive: true, products: [
        { id: 'p-Y0', article: '0', fullArticle: 'ART-Y.0', name: 'По умолчанию', isActive: true },
        { id: 'p-YB', article: 'BLK', fullArticle: 'ART-Y.BLK', name: 'Чёрный', isActive: true },
        { id: 'p-YW', article: 'WHT', fullArticle: 'ART-Y.WHT', name: 'Белый', isActive: false }
      ] }
    ] }))
  }
}))
```
Существующие тесты «заказ по типу изделия ПФ» переписать: заказы запрашиваются по `productId`, не по `productTypeId`. Новые тесты:
1. Выбор `mold-1` (тип с одним изделием) → `form.productId === 'p-X0'` и `ordersApi.getList` вызван с `{ status: 'Active', productId: 'p-X0' }`.
2. Выбор `mold-2` (два активных) → `form.productId === null`, опций изделия 2 (архивного `p-YW` нет), поле «Заказ» заблокировано до выбора изделия.
3. Смена ПФ сбрасывает изделие и заказ, если они не подходят новой ПФ.
4. `lockedOrder = { id: 'order-1', number: 'ORD-1', productId: 'p-YB', productTypeId: 'pt-Y', productFullArticle: 'ART-Y.BLK' }` → `form.productId === 'p-YB'`, селект изделия заблокирован, список ПФ — только `mold-2`.
5. Создание шлёт `productId` в `tasksApi.create`.
6. Редактирование задания `{ status: 'Planned', orderId: null, productId: 'p-Y0', moldId: 'mold-2', ... }` — селект изделия активен; со `orderId: 'order-1'` — заблокирован; в `status: 'Issued'` — заблокирован.
7. Редактирование: `tasksApi.update` получает `productId`.

Существующий тест `оставляет в списке ПФ только пресс-формы с типом изделия заказа` — адаптировать: `lockedOrder.productTypeId` (поле осталось).

Run: `npx vitest run src/views/tasks/__tests__/TaskFormModal.spec.js` → FAIL.

- [ ] **Step 2: Реализация**

- `form.productId: null` (в `reactive`, `resetForm`, `populateForm` = `task.productId`), правило `productId: [{ required: true, message: 'Выберите изделие', trigger: 'change' }]`.
- `productTypes = ref([])`, `loadProductTypes()` (один раз, как `loadMolds`; вызывать вместе с `loadMolds` в `applyLockedOrder`, `populateForm` и на `@focus` селекта изделия).
- ```js
  const productOptions = computed(() => {
    const type = productTypes.value.find(t => t.id === selectedProductTypeId.value)
    const list = activeProducts(type)
    // Текущее (архивное) изделие редактируемого задания показываем по артикулу из задания
    const task = editingTask.value
    if (task?.productId && !list.some(p => p.id === task.productId))
      return [...list, { id: task.productId, fullArticle: task.productFullArticle || task.productId, name: task.productName, isActive: false }]
    return list
  })
  const isProductLocked = computed(() =>
    !!props.lockedOrder || (!!editingTask.value && (editingTask.value.status !== 'Planned' || !!editingTask.value.orderId)))
  ```
- Шаблон: новый `el-form-item label="Изделие" prop="productId"` между «Пресс-форма» и «Заказ»; `el-select` с `:disabled="isProductLocked || !form.moldId"`, `filterable`, опции `${p.fullArticle} — ${p.name}`, `:disabled="!p.isActive"`. Сетка двухколоночная — порядок полей ТПА, ПФ, Изделие, Заказ, Наладчик… (сетка сама перестроится).
- Автоподстановка: `watch(selectedProductTypeId, …)` переделать: если `props.lockedOrder` — `form.productId = props.lockedOrder.productId`; иначе если редактирование — ничего не менять; иначе — если текущее `form.productId` не входит в `productOptions` → `form.productId = productOptions.value.length === 1 ? productOptions.value[0].id : null`. Учесть, что `productTypes` могут загрузиться позже ПФ: `watch([selectedProductTypeId, productTypes], …)`.
- Заказы: `loadOrdersForProductType` → `loadOrdersForProduct(productId)` (`ordersApi.getList({ status: 'Active', productId })`), `watch(() => form.productId, …)` вместо `watch(selectedProductTypeId, …)`; селект заказа `:disabled="isRestricted || !!lockedOrder || !form.productId"`; метка опции заказа `${order.number}${order.productFullArticle ? ' (' + order.productFullArticle + ')' : ''}`; `orderNoDataText` — «Нет активных заказов на изделие {fullArticle выбранного изделия || 'выбранное изделие'}».
- `applyLockedOrder`: дополнительно `form.productId = props.lockedOrder.productId` и `loadProductTypes()`.
- `handleSubmit`: `productId` уже в `form` — уходит и в `create`, и в `update` (`updatePayload` содержит его; бэкенд — no-op при совпадении).
- Импорт `productTypesApi` и `activeProducts`.

- [ ] **Step 3: PASS + полный фронт-прогон, commit**

Run: `npx vitest run` → PASS.
```bash
git add Wintime-Control-Frontend/src
git commit -m "feat(ui): product field in task form (PZP-19)"
```

---

### Task 8: Фронт — показ изделия в заданиях, на плитке ТПА, в мобильном и журнале

**Files:**
- Modify: `src/views/tasks/TasksView.vue` (колонка «Изделие» с сортировкой рядом с «Пресс-форма», `:111-115`)
- Modify: `src/stores/tasks.js:62` (поиск: + `productFullArticle`, `productName`)
- Modify: `src/views/tasks/TaskDetailModal.vue:27`, `src/views/tasks/IssueTaskDialog.vue:3`
- Modify: `src/components/mobile/MobileTaskCard.vue:22`, `src/views/mobile/MobileTaskDetailView.vue:21`
- Modify: `src/components/dashboard/ShiftTimeline.vue:54,82,257,265`
- Modify: `src/components/dashboard/ImmCard.vue:22-26`, `src/stores/dashboard.js:102` (поиск + `currentProductArticle`)
- Test: `src/components/mobile/__tests__/MobileTaskCard.spec.js` (создать, если нет), существующие спеки этих компонентов (`grep -rl "MobileTaskCard\|ShiftTimeline\|ImmCard\|TasksView\|IssueTaskDialog\|TaskDetailModal" src --include=*.spec.js`)

Все пути — от `Wintime-Control-Frontend/`.

**Interfaces:**
- Consumes: `TaskDto.productFullArticle`, `productName`, `productTypeName` (Task 4); `ImmDto.currentProductArticle` (Task 4); `productSecondary` (Task 5).

- [ ] **Step 1: Падающие тесты**

1. `MobileTaskCard`: задание `{ moldName: 'Форма 1', productFullArticle: 'A123.BLK', productTypeName: 'Крышка', productName: 'Чёрный' }` → в разметке есть `A123.BLK` и `Крышка · Чёрный`.
2. `TasksView` (если есть спека): колонка «Изделие» рендерит `productFullArticle`.
3. `ImmCard`: при `currentProductArticle: 'A123.BLK'` текст виден под названием ПФ.
4. `ShiftTimeline`: подпись бара `A123.BLK · Форма 1` (если `productFullArticle` есть), подсказка начинается с той же строки.

Run: `npx vitest run <соответствующие спеки>` → FAIL.

- [ ] **Step 2: Реализация**

- Формат: основная строка — `productFullArticle`; вторичная — `productSecondary(productTypeName, productName)`; в узких местах (бар Гантта, плитка ТПА) — только полный артикул.
- `MobileTaskCard`/`MobileTaskDetailView`/`TaskDetailModal`: под строкой ПФ — блок «Изделие»: **полный артикул** и серой строкой вторичная подпись.
- `IssueTaskDialog`: `{{ task.immName }} / {{ task.moldName }} / {{ task.productFullArticle }}`.
- `TasksView`: колонка `prop="productFullArticle"` с заголовком-сортировкой по образцу `moldName` (`handleSort('productFullArticle')`), в ячейке — полный артикул и под ним вторичная подпись мелким шрифтом.
- `ShiftTimeline`: `barLabel(task) = [task.productFullArticle, task.moldName].filter(Boolean).join(' · ') || 'Задание'`; использовать в обоих местах бара и в подсказках.
- `ImmCard`: под `currentMoldName` строка `currentProductArticle` (если задан).
- Поиск в `stores/tasks.js` и `stores/dashboard.js`: добавить `productFullArticle`/`productName` и `currentProductArticle`.
- Журнал работы без задания: метки приходят готовыми с бэкенда (Task 4) — правок фронта не нужно; проверить, что спеки журнала не завязаны на старый формат метки.

- [ ] **Step 3: PASS + полный фронт-прогон + сборка, commit**

Run: `npx vitest run && npm run build` → PASS / сборка без ошибок.
```bash
git add Wintime-Control-Frontend/src
git commit -m "feat(ui): show product in tasks, mobile, timeline and IMM tile (PZP-19)"
```

---

### Task 9: ADR-0020, CLAUDE.md, доменные доки, руководства, seed-скрипты, VERSION

**Files:**
- Create: `docs/adr/0020-product-types-and-products.md`
- Modify: `docs/adr/0007-product-type-catalog.md` (только строка статуса), `docs/adr/0009-orders-module.md` (только пометка о частичной замене), `docs/adr/README.md`
- Modify: `CLAUDE.md` (раздел «Тип изделия (`ProductType`) обязателен для новых ПФ и заданий»)
- Create: `docs/domain/product-type.md` (по `docs/domain/_TEMPLATE.md`)
- Modify: `docs/domain/mold.md`, `docs/domain/shift-task.md:70`, `docs/domain/README.md` (если там индекс)
- Modify: `docs/user-guide/manager.md`, `docs/user-guide/adjuster.md`
- Modify: `seed_equipment_report.sql`, `seed_demo.sql` (если вставляет задания/заказы/типы)
- Modify: `VERSION` (`0.16.0` → `0.17.0`)

**Interfaces:** Consumes: итоговое поведение Tasks 2–8 и спека.

- [ ] **Step 1: ADR-0020** (формат MADR, как `docs/adr/0018-mold-imm-compatibility-manual-list.md`):
  - Статус Accepted, дата 2026-10-18, «Заменяет ADR-0007; заменяет п.1 и п.8 ADR-0009».
  - Контекст: пилот ПЗП — одна ПФ выпускает несколько товарных позиций (цвета), одному изделию — несколько ПФ (замена изношенной).
  - Решение: решения 1–9 спеки (двухуровневый каталог; изделие по умолчанию `0`; полный артикул `ТИП.ЧАСТЬ`; единое пространство артикулов; свободная правка с пересчётом; архив/удаление; хранимый `FullArticle` + unique; `Order.ProductId` вместо типа; автоподстановка). Отдельным пунктом: «Каталог по-прежнему тонкий и расширяется (принцип ADR-0007 сохраняется)».
  - Альтернативы: M:N ПФ↔изделие; ПФ на каждый цвет; иерархия «изделие → вариант» поверх ПФ; запрет точек в частях; склейка без разделителя; заморозка артикулов; вычисляемый полный артикул; реестр `Articles`.
  - Последствия: миграция `AddProducts`; физическое удаление неиспользованного изделия — исключение из правила мягкого удаления; рецепты BL-36 будут привязаны к `Product`; отчёты по изделиям — будущая задача.
- [ ] **Step 2:** ADR-0007: статус → `Superseded by [ADR-0020](0020-product-types-and-products.md)`. ADR-0009: под статусом строка «п.1 и п.8 заменены [ADR-0020](0020-product-types-and-products.md): заказ = изделие (`Product`) + количество, привязка задания — по совпадению изделия». README — строка индекса ADR-0020, у 0007 отметка о замене (в формате соседних строк).
- [ ] **Step 3: CLAUDE.md** — заменить раздел «Тип изделия (`ProductType`) обязателен для новых ПФ и заданий» на «Тип изделия и изделие (`ProductType` → `Product`)», 6–10 строк в стиле соседних разделов: двухуровневый каталог; ПФ → тип, заказ и задание → изделие (`ProductId` обязателен); полный артикул `ТИП.ЧАСТЬ` уникален и не совпадает с артикулом типа; у типа всегда ≥1 изделия, у активного ≥1 активного; изделие по умолчанию `0`; изделие задания — из типа ПФ, меняется только в `Planned` без заказа; привязка к заказу — `task.ProductId == order.ProductId`; удаление — только неиспользованного; тип ПФ нельзя сменить при наличии заданий (кроме `null → value`); см. ADR-0020.
- [ ] **Step 4: docs/domain** — `product-type.md` (что это, код, ключевые поля обеих сущностей, инварианты из спеки со ссылками на ADR-0020); `mold.md` — строка `ProductTypeId` («тип изделия, который выпускает ПФ; изделие выбирается в задании»), ссылка на ADR-0020 вместо/рядом с ADR-0007; `shift-task.md:70` — «изделие задания совпадает с изделием заказа».
- [ ] **Step 5: user-guide/manager.md** — словарь (стр. ~54: «Тип изделия» и «Изделие» отдельными строками, «Полный артикул»); 4.2 (поля формы заказа: тип → изделие, автоподстановка); 4.3–4.4 (колонка «Изделие», кандидаты по изделию); 5.3 таблица полей (строка «Изделие» после «Пресс-формы»; «Заказ» — после выбора изделия); 434 (изделие правится только в «Планируется» без заказа); 9.4 → «Типы изделий» (блок «Изделия»: добавить, правка, архив, удаление неиспользованного, правила уникальности с примером `123.А` / `А.0`, последнее активное изделие); таблица ошибок — новые сообщения (`Изделие в архиве`, `Изделие не относится к типу изделия пресс-формы`, `Изделие задания не совпадает с изделием заказа`, дубли артикулов, последнее изделие). `adjuster.md` — в описании карточки задания: «изделие (полный артикул и наименование) — какой материал и краситель загружать».
- [ ] **Step 6: seed-скрипты** — `seed_equipment_report.sql`: после `INSERT INTO "ProductTypes"` вставить изделия по умолчанию (`INSERT INTO "Products" … SELECT … FROM "ProductTypes"`), добавить `"Products"` в `TRUNCATE`, в `INSERT INTO "ShiftTasks"` — колонку `"ProductId"` (изделие `0` типа ПФ), в `INSERT INTO "Orders"` (если есть) — `"ProductId"` вместо `"ProductTypeId"`. То же в `seed_demo.sql`, если он трогает эти таблицы. Проверка: прогнать скрипт на локальной БД после `dotnet ef database update` (`psql -f seed_equipment_report.sql`) — без ошибок; если локальной БД нет — сообщить в отчёте, что скрипт не прогонялся.
- [ ] **Step 7: VERSION** `0.17.0`.
- [ ] **Step 8: Commit**

```bash
git add docs CLAUDE.md seed_equipment_report.sql seed_demo.sql VERSION
git commit -m "docs: ADR-0020 product types and products, guides, seeds; bump 0.17.0 (PZP-19)"
```

---

## Финальная проверка (после всех задач)

- `dotnet build` — 0 ошибок; `dotnet test Wintime.Control.Tests.Unit`; `dotnet test Wintime.Control.Tests.Integration`; `cd Wintime-Control-Frontend && npx vitest run && npm run build`.
- `grep -rn "ProductTypeArticle\|productTypeArticle" Wintime.Control.* Wintime-Control-Frontend/src` — пусто (кроме `MoldDto`/справочника ПФ, где поле осталось законно: `MoldDto.ProductTypeArticle`).
- `dotnet ef migrations has-pending-model-changes …` — нет изменений.
