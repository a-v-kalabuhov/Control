# PZP-05 — Модуль учёта заказов — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ввести сущность «Заказ» (`Order`) как уровень над заданиями: CRUD, привязка заданий (1:N, опциональная), прогресс по годным деталям (derive-on-read), ручной учёт брака на задании, ручное завершение заказа при достаточном выпуске.

**Architecture:** Backend ASP.NET Core 9 слоями Core → Infrastructure → API. `Order` — сущность с конечным автоматом в теле (паттерн ADR-0002, `DomainException` → HTTP 400 через существующий middleware ADR-0003). Прогресс НЕ хранится: считается `Σ(ActualQuantity − DefectQuantity)` по привязанным `ShiftTask` на чтении (Вариант A, паттерн `UnplannedRun` PZP-04). Frontend — Vue 3 + Element Plus + Tailwind, страница «Заказы» и правки формы задания / мобильного завершения.

**Tech Stack:** C# / EF Core 9 / Npgsql / PostgreSQL 16 · xUnit + FluentAssertions (backend) · Vue 3 + Vitest (frontend). Спека: `docs/superpowers/specs/2026-07-26-pzp-05-orders-module-design.md`.

## Global Constraints

- **UTC для Npgsql:** любые `DateTime` в EF-запросах/сущностях к Postgres — `Kind=Utc`. Даты из query/body сразу оборачивать `DateTime.SpecifyKind(value, DateTimeKind.Utc)` (как `PlannedDate` в `TasksController`). Колонки дат — `timestamp with time zone`.
- **Роль — только `User.Role`:** авторизация через `[Authorize(Roles = ...)]` с константами из `Wintime.Control.Shared.Constants.Roles`. По заказам — `Roles.Admin,Roles.Manager`.
- **`IsActive` — архивный флаг:** сущности не удаляем физически. Заказ «убирается» статусом `Cancelled`, не удалением.
- **Прогресс derive-on-read:** на `Order` не хранить `produced/good/percent`. Считать запросом.
- **Две семантики выполнения:** задание = выпуск `PlanQuantity` (независимо от брака); заказ = `Σ(Actual − Defect) ≥ Quantity`.
- **Миграции — оба флага:** `dotnet ef ... --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`.
- **Тесты:** backend xUnit + FluentAssertions; frontend Vitest. Новый код покрывать.
- **Роли БД (напоминание):** Identity-роли не используем.

---

## Файловая структура

**Создать (backend):**
- `Wintime.Control.Core/Enums/OrderStatus.cs` — enum статусов заказа.
- `Wintime.Control.Core/Entities/Order.cs` — сущность + конечный автомат.
- `Wintime.Control.Core/Policies/OrderTaskBinding.cs` — чистый валидатор привязки.
- `Wintime.Control.Core/DTOs/Order/OrderDto.cs`, `OrderDetailsDto.cs`, `OrderTaskSummaryDto.cs`, `CreateOrderRequestDto.cs`, `UpdateOrderRequestDto.cs`, `AttachTaskRequestDto.cs`.
- `Wintime.Control.Core/DTOs/Tasks/SetOrderRequestDto.cs`.
- `Wintime.Control.API/Controllers/OrdersController.cs`.
- `Wintime.Control.Tests.Unit/Entities/OrderStateMachineTests.cs`, `Wintime.Control.Tests.Unit/Policies/OrderTaskBindingTests.cs`.
- `Wintime.Control.Tests.Integration/Orders/OrdersCrudTests.cs`, `OrderProgressTests.cs`, `OrderTaskBindingApiTests.cs`, `TaskCompletionDefectTests.cs`.

**Изменить (backend):**
- `Wintime.Control.Core/Entities/ShiftTask.cs` — `OrderId`, `Order`, `DefectQuantity`; расширить `Complete`.
- `Wintime.Control.Core/DTOs/Tasks/TaskDto.cs`, `TaskMappingExtensions.cs`, `CreateTaskRequestDto.cs`, `CompleteTaskRequestDto.cs`.
- `Wintime.Control.Infrastructure/Data/ControlDbContext.cs` — `DbSet<Order>` + конфиг.
- `Wintime.Control.API/Controllers/TasksController.cs` — `OrderId` при создании, `set-order`, брак при завершении.
- `Wintime.Control.Tests.Unit/Entities/ShiftTaskStateMachineTests.cs` — тесты брака.

**Создать/изменить (frontend):**
- Создать: `src/api/orders.js`, `src/constants/orderStatus.js`, `src/views/OrdersView.vue`, `src/components/orders/OrderDetailModal.vue`.
- Изменить: `src/router/index.js` (роут + меню), `src/views/TasksView.vue` (+ `TaskDetailModal.vue` при наличии) — селект «Заказ»; `src/views/mobile/MobileTaskDetailView.vue` — поле «Брак».
- Vitest: `src/views/__tests__/OrdersView.spec.js`, `src/components/orders/__tests__/OrderDetailModal.spec.js` (пути — по образцу существующих `__tests__`).

> Точные имена фронт-файлов задания/мобильного завершения — проверить в репозитории на шаге задачи (Задачи 12–13); паттерн — существующие `src/views` / `src/views/mobile`.

---

## Task 1: Enum `OrderStatus` + сущность `Order` с конечным автоматом

**Files:**
- Create: `Wintime.Control.Core/Enums/OrderStatus.cs`
- Create: `Wintime.Control.Core/Entities/Order.cs`
- Test: `Wintime.Control.Tests.Unit/Entities/OrderStateMachineTests.cs`

**Interfaces:**
- Produces: `enum OrderStatus { Active, Completed, Cancelled }`; `class Order : BaseEntity` со свойствами `string Number`, `DateTime OrderDate`, `DateTime DueDate`, `Guid ProductTypeId`, `ProductType? ProductType`, `int Quantity`, `OrderStatus Status`, `string? Note`, `ICollection<ShiftTask> Tasks`; методы `void Complete(int goodQuantity)`, `void Cancel()`, `void Reopen()`.

- [ ] **Step 1: Написать падающий тест**

```csharp
// Wintime.Control.Tests.Unit/Entities/OrderStateMachineTests.cs
using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Xunit;

namespace Wintime.Control.Tests.Unit.Entities;

public class OrderStateMachineTests
{
    private static Order NewOrder(OrderStatus status, int quantity = 100) =>
        new() { Status = status, Quantity = quantity, Number = "ORD-1" };

    [Fact]
    public void Complete_WhenEnoughGood_MovesToCompleted()
    {
        var order = NewOrder(OrderStatus.Active, quantity: 100);
        order.Complete(goodQuantity: 100);
        order.Status.Should().Be(OrderStatus.Completed);
    }

    [Fact]
    public void Complete_WhenNotEnoughGood_Throws()
    {
        var order = NewOrder(OrderStatus.Active, quantity: 100);
        var act = () => order.Complete(goodQuantity: 99);
        act.Should().Throw<DomainException>();
        order.Status.Should().Be(OrderStatus.Active);
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void Complete_FromNonActive_Throws(OrderStatus status)
    {
        var order = NewOrder(status);
        var act = () => order.Complete(goodQuantity: 1000);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Cancel_FromActive_MovesToCancelled()
    {
        var order = NewOrder(OrderStatus.Active);
        order.Cancel();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void Cancel_FromNonActive_Throws(OrderStatus status)
    {
        var order = NewOrder(status);
        var act = () => order.Cancel();
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void Reopen_FromNonActive_MovesToActive(OrderStatus status)
    {
        var order = NewOrder(status);
        order.Reopen();
        order.Status.Should().Be(OrderStatus.Active);
    }

    [Fact]
    public void Reopen_FromActive_Throws()
    {
        var order = NewOrder(OrderStatus.Active);
        var act = () => order.Reopen();
        act.Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Запустить тест — убедиться, что не компилируется/падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~OrderStateMachineTests`
Expected: FAIL (типы `OrderStatus`/`Order` не существуют).

- [ ] **Step 3: Создать enum**

```csharp
// Wintime.Control.Core/Enums/OrderStatus.cs
namespace Wintime.Control.Core.Enums;

public enum OrderStatus
{
    Active,
    Completed,
    Cancelled
}
```

- [ ] **Step 4: Создать сущность `Order`**

```csharp
// Wintime.Control.Core/Entities/Order.cs
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Entities;

/// <summary>
/// Заказ — одна номенклатурная позиция (ProductType + Quantity). Уровень над заданиями:
/// Order → ShiftTask (1:N). Прогресс НЕ хранится (derive-on-read). См. ADR-0009, PZP-05.
/// </summary>
public class Order : BaseEntity
{
    public string Number { get; set; } = string.Empty;  // человекочитаемый, НЕ уникален (CRM/1С)
    public DateTime OrderDate { get; set; }              // дата заказа (Utc)
    public DateTime DueDate { get; set; }                // крайний срок (Utc)
    public Guid ProductTypeId { get; set; }              // изделие заказа
    public ProductType? ProductType { get; set; }
    public int Quantity { get; set; }                    // требуемое кол-во годных, > 0
    public OrderStatus Status { get; set; } = OrderStatus.Active;
    public string? Note { get; set; }

    public ICollection<ShiftTask> Tasks { get; set; } = new List<ShiftTask>();

    // ── Конечный автомат (ADR-0002: логика в сущности, DomainException → 400) ──
    // goodQuantity = Σ(ActualQuantity − DefectQuantity) по привязанным заданиям —
    // считает вызывающий код (derive-on-read), сущность прогресс не хранит.
    public void Complete(int goodQuantity)
    {
        EnsureStatus(OrderStatus.Active, "Заказ не активен");
        if (goodQuantity < Quantity)
            throw new DomainException("Недостаточно годных деталей для завершения заказа");
        Status = OrderStatus.Completed;
    }

    public void Cancel()
    {
        EnsureStatus(OrderStatus.Active, "Заказ не активен");
        Status = OrderStatus.Cancelled;
    }

    public void Reopen()
    {
        if (Status == OrderStatus.Active)
            throw new DomainException("Заказ уже активен");
        Status = OrderStatus.Active;
    }

    private void EnsureStatus(OrderStatus expected, string message)
    {
        if (Status != expected)
            throw new DomainException(message);
    }
}
```

- [ ] **Step 5: Запустить тест — убедиться, что проходит**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~OrderStateMachineTests`
Expected: PASS (все факты/теории зелёные).

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Core/Enums/OrderStatus.cs Wintime.Control.Core/Entities/Order.cs Wintime.Control.Tests.Unit/Entities/OrderStateMachineTests.cs
git commit -m "feat(PZP-05): сущность Order + конечный автомат (Complete/Cancel/Reopen)"
```

---

## Task 2: Правки `ShiftTask` — `OrderId`, `DefectQuantity`, брак в `Complete`

**Files:**
- Modify: `Wintime.Control.Core/Entities/ShiftTask.cs`
- Test: `Wintime.Control.Tests.Unit/Entities/ShiftTaskStateMachineTests.cs`

**Interfaces:**
- Consumes: `Order` (Task 1).
- Produces: `ShiftTask.OrderId` (`Guid?`), `ShiftTask.Order` (`Order?`), `ShiftTask.DefectQuantity` (`int`, default 0); сигнатура `void Complete(int? actualQuantity, string? completionReason, int? defectQuantity = null)`.

- [ ] **Step 1: Написать падающие тесты (добавить в конец класса, до закрывающей `}`)**

```csharp
// Добавить в Wintime.Control.Tests.Unit/Entities/ShiftTaskStateMachineTests.cs

    // ── PZP-05: брак при завершении ──────────────────────────────────────────

    [Fact]
    public void Complete_WithDefectInRange_StoresDefect()
    {
        var task = NewTask(EntityTaskStatus.InProgress, plan: 100);

        task.Complete(actualQuantity: 100, completionReason: null, defectQuantity: 20);

        task.DefectQuantity.Should().Be(20);
        task.Status.Should().Be(EntityTaskStatus.Completed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Complete_WithDefectOutOfRange_Throws(int defect)
    {
        var task = NewTask(EntityTaskStatus.InProgress, plan: 100);

        var act = () => task.Complete(actualQuantity: 100, completionReason: null, defectQuantity: defect);

        act.Should().Throw<DomainException>();
        task.Status.Should().Be(EntityTaskStatus.InProgress); // статус не сменился
    }

    [Fact]
    public void Complete_WithoutDefect_KeepsDefectZero()
    {
        var task = NewTask(EntityTaskStatus.InProgress, plan: 100);

        task.Complete(actualQuantity: 100, completionReason: null);

        task.DefectQuantity.Should().Be(0);
    }
```

> `DomainException` уже импортирован в этом файле (см. существующий `using Wintime.Control.Core.Exceptions;`).

- [ ] **Step 2: Запустить — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftTaskStateMachineTests`
Expected: FAIL (нет `DefectQuantity`; `Complete` без 3-го параметра).

- [ ] **Step 3: Добавить поля в `ShiftTask` (после `public string? CloseReason`)**

```csharp
    // PZP-05: связь с заказом (nullable — задание может быть без заказа) + ручной брак.
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }
    public int DefectQuantity { get; set; }   // брак (ручной ввод при завершении), default 0
```

- [ ] **Step 4: Расширить `Complete` (заменить существующий метод)**

```csharp
    /// <summary>Завершить задание: InProgress → Completed.</summary>
    /// <param name="actualQuantity">Фактический выпуск; если null — остаётся накопленное значение.</param>
    /// <param name="completionReason">Причина отклонения; фиксируется только при расхождении с планом.</param>
    /// <param name="defectQuantity">Брак (PZP-05); валидируется диапазоном 0..выпущено.</param>
    public void Complete(int? actualQuantity, string? completionReason, int? defectQuantity = null)
    {
        EnsureStatus(TaskStatus.InProgress, "Задание не в работе");

        if (actualQuantity.HasValue)
            ActualQuantity = actualQuantity.Value;

        if (defectQuantity.HasValue)
        {
            if (defectQuantity.Value < 0 || defectQuantity.Value > ActualQuantity)
                throw new DomainException("Брак должен быть в диапазоне 0..выпущено");
            DefectQuantity = defectQuantity.Value;
        }

        if (ActualQuantity != PlanQuantity && completionReason != null)
            CloseReason = completionReason;

        Status = TaskStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }
```

> `defectQuantity` — необязательный параметр со значением по умолчанию `null`, поэтому существующие вызовы `Complete(x, y)` (контроллер, старые тесты) продолжают компилироваться.

- [ ] **Step 5: Запустить — убедиться, что проходит (включая старые тесты)**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftTaskStateMachineTests`
Expected: PASS (новые + все старые факты).

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Core/Entities/ShiftTask.cs Wintime.Control.Tests.Unit/Entities/ShiftTaskStateMachineTests.cs
git commit -m "feat(PZP-05): ShiftTask.OrderId/DefectQuantity + брак в Complete"
```

---

## Task 3: Валидатор привязки `OrderTaskBinding`

**Files:**
- Create: `Wintime.Control.Core/Policies/OrderTaskBinding.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/OrderTaskBindingTests.cs`

**Interfaces:**
- Consumes: `Order`, `OrderStatus` (Task 1).
- Produces: `static class OrderTaskBinding` с `void EnsureCanBind(Order order, Guid? moldProductTypeId)` — кидает `DomainException`, если заказ не `Active`, у ПФ нет типа, или тип ПФ ≠ тип заказа.

- [ ] **Step 1: Написать падающий тест**

```csharp
// Wintime.Control.Tests.Unit/Policies/OrderTaskBindingTests.cs
using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class OrderTaskBindingTests
{
    private static Order ActiveOrder(Guid productTypeId) =>
        new() { Status = OrderStatus.Active, ProductTypeId = productTypeId, Quantity = 100, Number = "O-1" };

    [Fact]
    public void EnsureCanBind_MatchingActive_DoesNotThrow()
    {
        var pt = Guid.NewGuid();
        var act = () => OrderTaskBinding.EnsureCanBind(ActiveOrder(pt), pt);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanBind_ProductTypeMismatch_Throws()
    {
        var act = () => OrderTaskBinding.EnsureCanBind(ActiveOrder(Guid.NewGuid()), Guid.NewGuid());
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void EnsureCanBind_MoldWithoutType_Throws()
    {
        var act = () => OrderTaskBinding.EnsureCanBind(ActiveOrder(Guid.NewGuid()), null);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void EnsureCanBind_NonActiveOrder_Throws(OrderStatus status)
    {
        var pt = Guid.NewGuid();
        var order = new Order { Status = status, ProductTypeId = pt, Quantity = 100, Number = "O-1" };
        var act = () => OrderTaskBinding.EnsureCanBind(order, pt);
        act.Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Запустить — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~OrderTaskBindingTests`
Expected: FAIL (`OrderTaskBinding` не существует).

- [ ] **Step 3: Создать валидатор**

```csharp
// Wintime.Control.Core/Policies/OrderTaskBinding.cs
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Правило привязки задания к заказу (PZP-05, UC-6): заказ активен, у ПФ задания задан тип,
/// и тип ПФ совпадает с типом изделия заказа. Чистая функция — вызывающий код грузит сущности.
/// </summary>
public static class OrderTaskBinding
{
    public static void EnsureCanBind(Order order, Guid? moldProductTypeId)
    {
        if (order.Status != OrderStatus.Active)
            throw new DomainException("Привязать задание можно только к активному заказу");
        if (moldProductTypeId == null)
            throw new DomainException("У пресс-формы задания не задан тип изделия");
        if (moldProductTypeId.Value != order.ProductTypeId)
            throw new DomainException("Изделие задания не совпадает с изделием заказа");
    }
}
```

- [ ] **Step 4: Запустить — убедиться, что проходит**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~OrderTaskBindingTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/OrderTaskBinding.cs Wintime.Control.Tests.Unit/Policies/OrderTaskBindingTests.cs
git commit -m "feat(PZP-05): OrderTaskBinding — валидатор привязки задания к заказу"
```

---

## Task 4: EF Core конфиг + миграция `AddOrders`

**Files:**
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs`
- Create: миграция `Wintime.Control.Infrastructure/Migrations/*_AddOrders.cs` (генерируется)

**Interfaces:**
- Consumes: `Order` (Task 1), `ShiftTask.OrderId/DefectQuantity` (Task 2).
- Produces: `ControlDbContext.Orders` (`DbSet<Order>`); таблица `Orders`; колонки `ShiftTasks.OrderId` (nullable FK, SetNull), `ShiftTasks.DefectQuantity` (int, default 0); индексы `IX_Orders_Number`, `IX_ShiftTasks_OrderId`.

- [ ] **Step 1: Добавить `DbSet<Order>` (после строки `public DbSet<UnplannedRun> UnplannedRuns`)**

```csharp
    public DbSet<Order> Orders { get; set; }
```

- [ ] **Step 2: Добавить конфиг в `OnModelCreating` (перед закрывающей `}` метода, рядом с блоком UnplannedRun)**

```csharp
        // Конфигурация Order (PZP-05, ADR-0009)
        builder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.ProductType)
                  .WithMany()
                  .HasForeignKey(e => e.ProductTypeId)
                  .OnDelete(DeleteBehavior.Restrict);   // ProductType физически не удаляют (IsActive)
            entity.Property(e => e.OrderDate).HasColumnType("timestamp with time zone");
            entity.Property(e => e.DueDate).HasColumnType("timestamp with time zone");
            entity.HasIndex(e => e.Number);              // Number НЕ уникален (решение п.2) — обычный индекс
            entity.ToTable("Orders");
        });

        // Связь ShiftTask → Order (1:N, OrderId nullable)
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>()
            .HasOne(t => t.Order)
            .WithMany(o => o.Tasks)
            .HasForeignKey(t => t.OrderId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>()
            .HasIndex(t => t.OrderId);                   // под Σ-агрегацию прогресса
```

- [ ] **Step 3: Собрать проект — убедиться, что компилируется**

Run: `dotnet build Wintime.Control.API`
Expected: Build succeeded.

- [ ] **Step 4: Создать миграцию**

Run:
```powershell
dotnet ef migrations add AddOrders --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: создан файл `*_AddOrders.cs` в `Wintime.Control.Infrastructure/Migrations`.

- [ ] **Step 5: Проверить содержимое миграции**

Открыть сгенерированный `*_AddOrders.cs` и убедиться, что `Up()`:
- создаёт таблицу `Orders` (колонки `Id`, `Number`, `OrderDate` (`timestamp with time zone`), `DueDate`, `ProductTypeId`, `Quantity`, `Status` (int), `Note`, `CreatedAt`) с FK на `ProductTypes` (`onDelete: Restrict`);
- добавляет `ShiftTasks.OrderId` (nullable) с FK на `Orders` (`onDelete: SetNull`);
- добавляет `ShiftTasks.DefectQuantity` (`int`, `defaultValue: 0` — проверить, что для существующих строк подставляется 0; если EF не проставил default, добавить в `AddColumn<int>(... defaultValue: 0)` вручную);
- создаёт индексы `IX_Orders_Number`, `IX_Orders_ProductTypeId`, `IX_ShiftTasks_OrderId`.

- [ ] **Step 6: Применить миграцию к локальной БД (если поднята)**

Run:
```powershell
dotnet ef database update --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: `Done.` (или пропустить, если локальной БД нет — интеграционные тесты применяют миграции на Testcontainers сами).

- [ ] **Step 7: Commit**

```bash
git add Wintime.Control.Infrastructure/Data/ControlDbContext.cs Wintime.Control.Infrastructure/Migrations/
git commit -m "feat(PZP-05): EF-конфиг Order + миграция AddOrders"
```

---

## Task 5: DTO заказа и правки DTO задания

**Files:**
- Create: `Wintime.Control.Core/DTOs/Order/OrderDto.cs`, `OrderDetailsDto.cs`, `OrderTaskSummaryDto.cs`, `CreateOrderRequestDto.cs`, `UpdateOrderRequestDto.cs`, `AttachTaskRequestDto.cs`
- Create: `Wintime.Control.Core/DTOs/Tasks/SetOrderRequestDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Tasks/TaskDto.cs`, `TaskMappingExtensions.cs`, `CreateTaskRequestDto.cs`, `CompleteTaskRequestDto.cs`

**Interfaces:**
- Produces: `OrderDto` (реквизиты + агрегаты прогресса), `OrderDetailsDto : OrderDto` (+ `List<OrderTaskSummaryDto> Tasks`), `OrderTaskSummaryDto`, `CreateOrderRequestDto`, `UpdateOrderRequestDto`, `AttachTaskRequestDto { Guid TaskId }`, `SetOrderRequestDto { Guid OrderId }`; `TaskDto.OrderId/OrderNumber/DefectQuantity`; `CreateTaskRequestDto.OrderId`; `CompleteTaskRequestDto.DefectQuantity`.

- [ ] **Step 1: Создать DTO заказа**

```csharp
// Wintime.Control.Core/DTOs/Order/OrderDto.cs
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Order;

public class OrderDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime DueDate { get; set; }
    public Guid ProductTypeId { get; set; }
    public string? ProductTypeArticle { get; set; }
    public string? ProductTypeName { get; set; }
    public int Quantity { get; set; }
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
    // Агрегаты прогресса (derive-on-read)
    public int ProducedQuantity { get; set; }   // Σ ActualQuantity
    public int DefectQuantity { get; set; }      // Σ DefectQuantity
    public int GoodQuantity { get; set; }        // Σ(Actual − Defect)
    public decimal ProgressPercent { get; set; } // GoodQuantity / Quantity * 100 (не капается)
    public int TaskCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

```csharp
// Wintime.Control.Core/DTOs/Order/OrderTaskSummaryDto.cs
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Order;

public class OrderTaskSummaryDto
{
    public Guid TaskId { get; set; }
    public string? ImmName { get; set; }
    public string? MoldName { get; set; }
    public int PlanQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public int DefectQuantity { get; set; }
    public Wintime.Control.Core.Enums.TaskStatus Status { get; set; }
}
```

```csharp
// Wintime.Control.Core/DTOs/Order/OrderDetailsDto.cs
namespace Wintime.Control.Core.DTOs.Order;

public class OrderDetailsDto : OrderDto
{
    public List<OrderTaskSummaryDto> Tasks { get; set; } = new();
}
```

```csharp
// Wintime.Control.Core/DTOs/Order/CreateOrderRequestDto.cs
namespace Wintime.Control.Core.DTOs.Order;

public class CreateOrderRequestDto
{
    public string Number { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime DueDate { get; set; }
    public Guid ProductTypeId { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
}
```

```csharp
// Wintime.Control.Core/DTOs/Order/UpdateOrderRequestDto.cs
namespace Wintime.Control.Core.DTOs.Order;

public class UpdateOrderRequestDto
{
    public string? Number { get; set; }
    public DateTime? OrderDate { get; set; }
    public DateTime? DueDate { get; set; }
    public Guid? ProductTypeId { get; set; }
    public int? Quantity { get; set; }
    public string? Note { get; set; }
}
```

```csharp
// Wintime.Control.Core/DTOs/Order/AttachTaskRequestDto.cs
namespace Wintime.Control.Core.DTOs.Order;

public class AttachTaskRequestDto
{
    public Guid TaskId { get; set; }
}
```

- [ ] **Step 2: Создать `SetOrderRequestDto` (non-null OrderId — отвязка из формы задания недоступна, UC-7)**

```csharp
// Wintime.Control.Core/DTOs/Tasks/SetOrderRequestDto.cs
namespace Wintime.Control.Core.DTOs.Tasks;

public class SetOrderRequestDto
{
    public Guid OrderId { get; set; }
}
```

- [ ] **Step 3: Дополнить `TaskDto` (добавить свойства)**

```csharp
    // PZP-05
    public Guid? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public int DefectQuantity { get; set; }
```

- [ ] **Step 4: Дополнить маппинг `TaskMappingExtensions.ToDto` (добавить в инициализатор)**

```csharp
        OrderId = t.OrderId,
        OrderNumber = t.Order?.Number,
        DefectQuantity = t.DefectQuantity,
```

- [ ] **Step 5: Дополнить `CreateTaskRequestDto` и `CompleteTaskRequestDto`**

```csharp
// CreateTaskRequestDto.cs — добавить свойство
    public Guid? OrderId { get; set; }
```

```csharp
// CompleteTaskRequestDto.cs — добавить свойство
    public int? DefectQuantity { get; set; }
```

- [ ] **Step 6: Собрать — убедиться, что компилируется**

Run: `dotnet build Wintime.Control.API`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add Wintime.Control.Core/DTOs/
git commit -m "feat(PZP-05): DTO заказа + правки DTO задания (OrderId/DefectQuantity)"
```

---

## Task 6: `OrdersController` — CRUD, прогресс, переходы статуса

**Files:**
- Create: `Wintime.Control.API/Controllers/OrdersController.cs`
- Test: `Wintime.Control.Tests.Integration/Orders/OrdersCrudTests.cs`, `Wintime.Control.Tests.Integration/Orders/OrderProgressTests.cs`

**Interfaces:**
- Consumes: `Order`, `OrderStatus`, DTO заказа (Tasks 1, 5), `ControlDbContext.Orders` (Task 4).
- Produces: endpoints `GET /api/orders`, `GET /api/orders/{id}`, `POST /api/orders`, `PUT /api/orders/{id}`, `POST /api/orders/{id}/complete|cancel|reopen`. Приватный агрегатор прогресса `ComputeAggregatesAsync`.

- [ ] **Step 1: Написать интеграционные тесты CRUD**

```csharp
// Wintime.Control.Tests.Integration/Orders/OrdersCrudTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wintime.Control.Core.DTOs.Order;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Orders;

[Collection("Integration")]
public class OrdersCrudTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public OrdersCrudTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private CreateOrderRequestDto ValidOrder(int qty = 100) => new()
    {
        Number = "ORD-100",
        OrderDate = DateTime.UtcNow.Date,
        DueDate = DateTime.UtcNow.Date.AddDays(7),
        ProductTypeId = _factory.TestProductTypeId,
        Quantity = qty
    };

    [Fact]
    public async Task Create_ValidOrder_Returns201_AndActive()
    {
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/orders", ValidOrder());
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<OrderDto>();
        dto!.Status.Should().Be(Core.Enums.OrderStatus.Active);
        dto.ProductTypeArticle.Should().Be("TYPE-TEST");
    }

    [Fact]
    public async Task Create_NonPositiveQuantity_Returns400()
    {
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/orders", ValidOrder(qty: 0));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_UnknownProductType_Returns400()
    {
        var client = await ManagerClientAsync();
        var body = ValidOrder();
        body.ProductTypeId = Guid.NewGuid();
        var resp = await client.PostAsJsonAsync("/api/orders", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancel_ThenComplete_Returns400()
    {
        var client = await ManagerClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/orders", ValidOrder()))
            .Content.ReadFromJsonAsync<OrderDto>();

        (await client.PostAsync($"/api/orders/{created!.Id}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/orders/{created.Id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest); // не Active
    }

    [Fact]
    public async Task Reopen_CancelledOrder_MakesActive()
    {
        var client = await ManagerClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/orders", ValidOrder()))
            .Content.ReadFromJsonAsync<OrderDto>();
        await client.PostAsync($"/api/orders/{created!.Id}/cancel", null);

        var resp = await client.PostAsync($"/api/orders/{created.Id}/reopen", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var reloaded = await (await client.GetAsync($"/api/orders/{created.Id}"))
            .Content.ReadFromJsonAsync<OrderDetailsDto>();
        reloaded!.Status.Should().Be(Core.Enums.OrderStatus.Active);
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        var client = await ManagerClientAsync();
        await client.PostAsJsonAsync("/api/orders", ValidOrder());
        var resp = await client.GetAsync("/api/orders?status=Active");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<OrderDto>>();
        list!.Should().OnlyContain(o => o.Status == Core.Enums.OrderStatus.Active);
    }
}
```

- [ ] **Step 2: Запустить — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~OrdersCrudTests`
Expected: FAIL (нет контроллера/маршрутов → 404).

- [ ] **Step 3: Создать `OrdersController` (CRUD + переходы + агрегатор)**

```csharp
// Wintime.Control.API/Controllers/OrdersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Order;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
public class OrdersController : ControllerBase
{
    private readonly ControlDbContext _context;
    public OrdersController(ControlDbContext context) => _context = context;

    // Агрегаты прогресса по набору заказов (derive-on-read, одна GroupBy — без N+1).
    private async Task<Dictionary<Guid, (int produced, int defect, int count)>> ComputeAggregatesAsync(
        IReadOnlyCollection<Guid> orderIds)
    {
        if (orderIds.Count == 0)
            return new();
        var rows = await _context.ShiftTasks
            .Where(t => t.OrderId != null && orderIds.Contains(t.OrderId.Value))
            .GroupBy(t => t.OrderId!.Value)
            .Select(g => new
            {
                OrderId = g.Key,
                Produced = g.Sum(t => t.ActualQuantity),
                Defect = g.Sum(t => t.DefectQuantity),
                Count = g.Count()
            })
            .ToListAsync();
        return rows.ToDictionary(r => r.OrderId, r => (r.Produced, r.Defect, r.Count));
    }

    private static OrderDto ToDto(Order o, (int produced, int defect, int count) agg)
    {
        var good = agg.produced - agg.defect;
        return new OrderDto
        {
            Id = o.Id,
            Number = o.Number,
            OrderDate = o.OrderDate,
            DueDate = o.DueDate,
            ProductTypeId = o.ProductTypeId,
            ProductTypeArticle = o.ProductType?.Article,
            ProductTypeName = o.ProductType?.Name,
            Quantity = o.Quantity,
            Status = o.Status,
            Note = o.Note,
            ProducedQuantity = agg.produced,
            DefectQuantity = agg.defect,
            GoodQuantity = good,
            ProgressPercent = o.Quantity > 0 ? (decimal)good / o.Quantity * 100 : 0,
            TaskCount = agg.count,
            CreatedAt = o.CreatedAt
        };
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetList(
        [FromQuery] OrderStatus? status = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? productTypeId = null)
    {
        var query = _context.Orders.Include(o => o.ProductType).AsQueryable();
        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o => o.Number.Contains(search));
        if (productTypeId.HasValue)
            query = query.Where(o => o.ProductTypeId == productTypeId.Value);

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
        var agg = await ComputeAggregatesAsync(orders.Select(o => o.Id).ToList());
        var dtos = orders.Select(o => ToDto(o, agg.GetValueOrDefault(o.Id))).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDetailsDto>> GetById(Guid id)
    {
        var order = await _context.Orders
            .Include(o => o.ProductType)
            .Include(o => o.Tasks).ThenInclude(t => t.Imm)
            .Include(o => o.Tasks).ThenInclude(t => t.Mold)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order == null)
            return NotFound();

        var agg = (await ComputeAggregatesAsync(new[] { id })).GetValueOrDefault(id);
        var baseDto = ToDto(order, agg);
        var details = new OrderDetailsDto
        {
            Id = baseDto.Id, Number = baseDto.Number, OrderDate = baseDto.OrderDate,
            DueDate = baseDto.DueDate, ProductTypeId = baseDto.ProductTypeId,
            ProductTypeArticle = baseDto.ProductTypeArticle, ProductTypeName = baseDto.ProductTypeName,
            Quantity = baseDto.Quantity, Status = baseDto.Status, Note = baseDto.Note,
            ProducedQuantity = baseDto.ProducedQuantity, DefectQuantity = baseDto.DefectQuantity,
            GoodQuantity = baseDto.GoodQuantity, ProgressPercent = baseDto.ProgressPercent,
            TaskCount = baseDto.TaskCount, CreatedAt = baseDto.CreatedAt,
            Tasks = order.Tasks.Select(t => new OrderTaskSummaryDto
            {
                TaskId = t.Id, ImmName = t.Imm?.Name, MoldName = t.Mold?.Name,
                PlanQuantity = t.PlanQuantity, ActualQuantity = t.ActualQuantity,
                DefectQuantity = t.DefectQuantity, Status = t.Status
            }).ToList()
        };
        return Ok(details);
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create([FromBody] CreateOrderRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Number))
            return BadRequest("Номер заказа обязателен.");
        if (request.Quantity <= 0)
            return BadRequest("Количество должно быть больше нуля.");
        var pt = await _context.ProductTypes.FindAsync(request.ProductTypeId);
        if (pt == null || !pt.IsActive)
            return BadRequest("Указан неактивный или несуществующий тип изделия.");

        var order = new Order
        {
            Number = request.Number.Trim(),
            OrderDate = DateTime.SpecifyKind(request.OrderDate, DateTimeKind.Utc),
            DueDate = DateTime.SpecifyKind(request.DueDate, DateTimeKind.Utc),
            ProductTypeId = request.ProductTypeId,
            Quantity = request.Quantity,
            Note = request.Note,
            Status = OrderStatus.Active
        };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        order.ProductType = pt;
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, ToDto(order, (0, 0, 0)));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrderRequestDto request)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();

        if (request.Number != null)
        {
            if (string.IsNullOrWhiteSpace(request.Number))
                return BadRequest("Номер заказа не может быть пустым.");
            order.Number = request.Number.Trim();
        }
        if (request.Quantity.HasValue)
        {
            if (request.Quantity.Value <= 0)
                return BadRequest("Количество должно быть больше нуля.");
            order.Quantity = request.Quantity.Value;
        }
        if (request.OrderDate.HasValue)
            order.OrderDate = DateTime.SpecifyKind(request.OrderDate.Value, DateTimeKind.Utc);
        if (request.DueDate.HasValue)
            order.DueDate = DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc);
        if (request.Note != null)
            order.Note = request.Note;
        if (request.ProductTypeId.HasValue && request.ProductTypeId.Value != order.ProductTypeId)
        {
            // Смена изделия запрещена, если по заказу уже есть привязанные задания (аналогия ADR-0007).
            var hasTasks = await _context.ShiftTasks.AnyAsync(t => t.OrderId == id);
            if (hasTasks)
                return Conflict("Нельзя сменить изделие заказа: к нему привязаны задания.");
            var pt = await _context.ProductTypes.FindAsync(request.ProductTypeId.Value);
            if (pt == null || !pt.IsActive)
                return BadRequest("Указан неактивный или несуществующий тип изделия.");
            order.ProductTypeId = request.ProductTypeId.Value;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();
        var agg = (await ComputeAggregatesAsync(new[] { id })).GetValueOrDefault(id);
        order.Complete(agg.produced - agg.defect);   // DomainException → 400 при недостатке годных
        await _context.SaveChangesAsync();
        return Ok(new { message = "Заказ выполнен" });
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();
        order.Cancel();
        await _context.SaveChangesAsync();
        return Ok(new { message = "Заказ отменён" });
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();
        order.Reopen();
        await _context.SaveChangesAsync();
        return Ok(new { message = "Заказ возобновлён" });
    }
}
```

> `DomainException` → HTTP 400 обеспечивается существующим middleware (ADR-0003); отдельного `try/catch` не нужно.

- [ ] **Step 4: Запустить CRUD-тесты — убедиться, что проходят**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~OrdersCrudTests`
Expected: PASS.

- [ ] **Step 5: Написать тест прогресса (derive-on-read)**

```csharp
// Wintime.Control.Tests.Integration/Orders/OrderProgressTests.cs
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Order;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Orders;

[Collection("Integration")]
public class OrderProgressTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public OrderProgressTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Progress_CountsGoodOnly_AndNotEnough_BlocksComplete()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var order = await (await client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            Number = "ORD-P", OrderDate = DateTime.UtcNow.Date, DueDate = DateTime.UtcNow.Date.AddDays(3),
            ProductTypeId = _factory.TestProductTypeId, Quantity = 100
        })).Content.ReadFromJsonAsync<OrderDto>();

        // Два задания того же заказа: 60 годных + 50 выпущено − 10 брак = 40 годных → всего 100 годных
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftTasks.Add(new ShiftTask { ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId,
                PlanQuantity = 60, ActualQuantity = 60, DefectQuantity = 0, Status = TaskStatus.Completed, OrderId = order!.Id });
            db.ShiftTasks.Add(new ShiftTask { ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId,
                PlanQuantity = 50, ActualQuantity = 50, DefectQuantity = 10, Status = TaskStatus.Completed, OrderId = order.Id });
            await db.SaveChangesAsync();
        }

        var details = await (await client.GetAsync($"/api/orders/{order!.Id}"))
            .Content.ReadFromJsonAsync<OrderDetailsDto>();
        details!.ProducedQuantity.Should().Be(110);
        details.DefectQuantity.Should().Be(10);
        details.GoodQuantity.Should().Be(100);   // 60 + (50 − 10)
        details.ProgressPercent.Should().Be(100);
        details.Tasks.Should().HaveCount(2);

        // Годных ровно 100 = Quantity → завершение проходит
        (await client.PostAsync($"/api/orders/{order.Id}/complete", null))
            .IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Complete_WhenGoodBelowQuantity_Returns400()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var order = await (await client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            Number = "ORD-P2", OrderDate = DateTime.UtcNow.Date, DueDate = DateTime.UtcNow.Date.AddDays(3),
            ProductTypeId = _factory.TestProductTypeId, Quantity = 100
        })).Content.ReadFromJsonAsync<OrderDto>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftTasks.Add(new ShiftTask { ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId,
                PlanQuantity = 100, ActualQuantity = 100, DefectQuantity = 5, Status = TaskStatus.Completed, OrderId = order!.Id });
            await db.SaveChangesAsync();
        }

        (await client.PostAsync($"/api/orders/{order!.Id}/complete", null))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest); // 95 годных < 100
    }
}
```

- [ ] **Step 6: Запустить тесты прогресса**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~OrderProgressTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Wintime.Control.API/Controllers/OrdersController.cs Wintime.Control.Tests.Integration/Orders/OrdersCrudTests.cs Wintime.Control.Tests.Integration/Orders/OrderProgressTests.cs
git commit -m "feat(PZP-05): OrdersController — CRUD, прогресс по годным, переходы статуса"
```

---

## Task 7: Привязка/отвязка заданий из карточки заказа

**Files:**
- Modify: `Wintime.Control.API/Controllers/OrdersController.cs`
- Test: `Wintime.Control.Tests.Integration/Orders/OrderTaskBindingApiTests.cs`

**Interfaces:**
- Consumes: `OrderTaskBinding.EnsureCanBind` (Task 3), `AttachTaskRequestDto` (Task 5).
- Produces: `POST /api/orders/{id}/tasks` (привязать), `DELETE /api/orders/{id}/tasks/{taskId}` (отвязать — единственная точка отвязки, UC-7).

- [ ] **Step 1: Написать интеграционные тесты**

```csharp
// Wintime.Control.Tests.Integration/Orders/OrderTaskBindingApiTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Order;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Orders;

[Collection("Integration")]
public class OrderTaskBindingApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public OrderTaskBindingApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> CreateOrderAsync(HttpClient client) =>
        (await (await client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            Number = "ORD-B", OrderDate = DateTime.UtcNow.Date, DueDate = DateTime.UtcNow.Date.AddDays(3),
            ProductTypeId = _factory.TestProductTypeId, Quantity = 100
        })).Content.ReadFromJsonAsync<OrderDto>())!.Id;

    private async Task<Guid> CreateTaskAsync(Guid moldId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var t = new ShiftTask { ImmId = _factory.TestImmId, MoldId = moldId, PlanQuantity = 50, Status = TaskStatus.Issued, IssuedAt = DateTime.UtcNow };
        db.ShiftTasks.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    [Fact]
    public async Task Attach_CompatibleTask_Ok_AndDetach_ClearsOrderId()
    {
        var client = await ManagerClientAsync();
        var orderId = await CreateOrderAsync(client);
        var taskId = await CreateTaskAsync(_factory.TestMoldId);  // ПФ с TestProductType

        (await client.PostAsJsonAsync($"/api/orders/{orderId}/tasks", new AttachTaskRequestDto { TaskId = taskId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftTasks.FindAsync(taskId))!.OrderId.Should().Be(orderId);
        }

        (await client.DeleteAsync($"/api/orders/{orderId}/tasks/{taskId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftTasks.FindAsync(taskId))!.OrderId.Should().BeNull();
        }
    }

    [Fact]
    public async Task Attach_ProductTypeMismatch_Returns400()
    {
        var client = await ManagerClientAsync();
        var orderId = await CreateOrderAsync(client);

        // ПФ другого типа изделия
        Guid otherMoldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var otherPt = new ProductType { Article = "OTHER-TYPE", Name = "Other", IsActive = true };
            db.ProductTypes.Add(otherPt);
            var mold = new Mold { Name = "Other Mold", FormId = $"OM-{Guid.NewGuid():N}", Cavities = 1, IsActive = true, ProductTypeId = otherPt.Id };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            otherMoldId = mold.Id;
        }
        var taskId = await CreateTaskAsync(otherMoldId);

        (await client.PostAsJsonAsync($"/api/orders/{orderId}/tasks", new AttachTaskRequestDto { TaskId = taskId }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Attach_ToCancelledOrder_Returns400()
    {
        var client = await ManagerClientAsync();
        var orderId = await CreateOrderAsync(client);
        await client.PostAsync($"/api/orders/{orderId}/cancel", null);
        var taskId = await CreateTaskAsync(_factory.TestMoldId);

        (await client.PostAsJsonAsync($"/api/orders/{orderId}/tasks", new AttachTaskRequestDto { TaskId = taskId }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Запустить — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~OrderTaskBindingApiTests`
Expected: FAIL (маршрутов нет → 404/405).

- [ ] **Step 3: Добавить методы в `OrdersController` (перед закрывающей `}` класса)**

Добавить `using Wintime.Control.Core.Policies;` в шапку файла, затем методы:

```csharp
    [HttpPost("{id:guid}/tasks")]
    public async Task<IActionResult> AttachTask(Guid id, [FromBody] AttachTaskRequestDto request)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();
        var task = await _context.ShiftTasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(t => t.Id == request.TaskId);
        if (task == null)
            return NotFound("Задание не найдено.");

        OrderTaskBinding.EnsureCanBind(order, task.Mold?.ProductTypeId);  // DomainException → 400
        task.OrderId = id;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Задание привязано к заказу" });
    }

    [HttpDelete("{id:guid}/tasks/{taskId:guid}")]
    public async Task<IActionResult> DetachTask(Guid id, Guid taskId)
    {
        var task = await _context.ShiftTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.OrderId == id);
        if (task == null)
            return NotFound("Задание не привязано к этому заказу.");
        task.OrderId = null;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Задание отвязано от заказа" });
    }
```

- [ ] **Step 4: Запустить — убедиться, что проходит**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~OrderTaskBindingApiTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.API/Controllers/OrdersController.cs Wintime.Control.Tests.Integration/Orders/OrderTaskBindingApiTests.cs
git commit -m "feat(PZP-05): привязка/отвязка заданий из карточки заказа"
```

---

## Task 8: Правки `TasksController` — `OrderId` при создании, `set-order`, брак

**Files:**
- Modify: `Wintime.Control.API/Controllers/TasksController.cs`
- Test: `Wintime.Control.Tests.Integration/Orders/TaskCompletionDefectTests.cs`

**Interfaces:**
- Consumes: `OrderTaskBinding` (Task 3), `SetOrderRequestDto`, `CreateTaskRequestDto.OrderId`, `CompleteTaskRequestDto.DefectQuantity` (Task 5).
- Produces: `POST /api/tasks/{id}/set-order` (привязка/смена из формы задания, non-null); `CreateTask` учитывает `OrderId`; `CompleteTask` передаёт `DefectQuantity`.

- [ ] **Step 1: Написать интеграционный тест брака при завершении**

```csharp
// Wintime.Control.Tests.Integration/Orders/TaskCompletionDefectTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Orders;

[Collection("Integration")]
public class TaskCompletionDefectTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public TaskCompletionDefectTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<Guid> InProgressTaskAsync(int actual)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var t = new ShiftTask { ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId,
            PlanQuantity = 100, ActualQuantity = actual, Status = TaskStatus.InProgress, IssuedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow };
        db.ShiftTasks.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    [Fact]
    public async Task Complete_WithDefect_StoresDefect()
    {
        var taskId = await InProgressTaskAsync(actual: 100);
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.PostAsJsonAsync($"/api/tasks/{taskId}/complete",
            new CompleteTaskRequestDto { ActualQuantity = 100, DefectQuantity = 15 });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftTasks.FindAsync(taskId))!.DefectQuantity.Should().Be(15);
    }

    [Fact]
    public async Task Complete_WithDefectAboveActual_Returns400()
    {
        var taskId = await InProgressTaskAsync(actual: 100);
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.PostAsJsonAsync($"/api/tasks/{taskId}/complete",
            new CompleteTaskRequestDto { ActualQuantity = 100, DefectQuantity = 101 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Запустить — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~TaskCompletionDefectTests`
Expected: FAIL (брак не сохраняется — `CompleteTask` не передаёт `DefectQuantity`).

- [ ] **Step 3: Передать брак в `CompleteTask` (заменить вызов `task.Complete`)**

В `TasksController.CompleteTask` заменить:
```csharp
        task.Complete(request.ActualQuantity, request.CompletionReason);
```
на:
```csharp
        task.Complete(request.ActualQuantity, request.CompletionReason, request.DefectQuantity);
```

- [ ] **Step 4: Учесть `OrderId` в `CreateTask` (после проверки типа ПФ, до `_context.SaveChangesAsync`)**

В `TasksController.CreateTask`, после создания `task` и до `_context.ShiftTasks.Add(task)` добавить обработку привязки:
```csharp
        if (request.OrderId.HasValue)
        {
            var order = await _context.Orders.FindAsync(request.OrderId.Value);
            if (order == null)
                return BadRequest("Указан несуществующий заказ.");
            Wintime.Control.Core.Policies.OrderTaskBinding.EnsureCanBind(order, mold.ProductTypeId);
            task.OrderId = request.OrderId.Value;
        }
```
> `mold` уже загружен в начале `CreateTask`. `EnsureCanBind` кинет `DomainException` → 400 при несовпадении/неактивном заказе.

- [ ] **Step 5: Добавить endpoint `set-order` (после `AddQuantity`, перед `CloseTask`)**

```csharp
    /// <summary>
    /// PZP-05: привязать/сменить заказ задания из формы задания (UC-6).
    /// Отвязка отсюда недоступна (UC-7) — только через карточку заказа (DELETE /orders/{id}/tasks/{taskId}).
    /// </summary>
    [HttpPost("{id:guid}/set-order")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> SetOrder(Guid id, [FromBody] SetOrderRequestDto request)
    {
        var task = await _context.ShiftTasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (task == null)
            return NotFound();
        var order = await _context.Orders.FindAsync(request.OrderId);
        if (order == null)
            return BadRequest("Указан несуществующий заказ.");

        Wintime.Control.Core.Policies.OrderTaskBinding.EnsureCanBind(order, task.Mold?.ProductTypeId);
        task.OrderId = request.OrderId;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Заказ задания обновлён" });
    }
```

- [ ] **Step 6: Загрузить навигацию `Order` в списках заданий (чтобы `TaskDto.OrderNumber` заполнялся)**

В `TasksController.GetTaskList`, `GetTaskById` добавить `.Include(t => t.Order)` к цепочке `Include`. (В `GetMyTasks` — по желанию; наладчику номер заказа не обязателен, можно не трогать.)

- [ ] **Step 7: Запустить тесты брака + регрессию заданий**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~TaskCompletionDefectTests`
Затем полная регрессия задач: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~Tasks`
Expected: PASS (новые + существующие тесты заданий).

- [ ] **Step 8: Полный прогон backend-тестов**

Run: `dotnet test Wintime.Control.Tests.Unit && dotnet test Wintime.Control.Tests.Integration`
Expected: все зелёные.

- [ ] **Step 9: Commit**

```bash
git add Wintime.Control.API/Controllers/TasksController.cs Wintime.Control.Tests.Integration/Orders/TaskCompletionDefectTests.cs
git commit -m "feat(PZP-05): TasksController — OrderId при создании, set-order, брак при завершении"
```

---

## Task 9: Frontend — API-клиент заказов + константы статусов

**Files:**
- Create: `Wintime-Control-Frontend/src/api/orders.js`
- Create: `Wintime-Control-Frontend/src/constants/orderStatus.js`

**Interfaces:**
- Produces: `ordersApi` (list/get/create/update/complete/cancel/reopen/attachTask/detachTask/setOrder на задании); `ORDER_STATUS` (палитра/подписи) + `getOrderStatusMeta`.

- [ ] **Step 1: Создать API-клиент (по образцу `src/api/productTypes.js`)**

```javascript
// Wintime-Control-Frontend/src/api/orders.js
import apiClient from './client'

export const ordersApi = {
  getList(params) {
    return apiClient.get('/orders', { params })
  },
  getById(id) {
    return apiClient.get(`/orders/${id}`)
  },
  create(data) {
    return apiClient.post('/orders', data)
  },
  update(id, data) {
    return apiClient.put(`/orders/${id}`, data)
  },
  complete(id) {
    return apiClient.post(`/orders/${id}/complete`)
  },
  cancel(id) {
    return apiClient.post(`/orders/${id}/cancel`)
  },
  reopen(id) {
    return apiClient.post(`/orders/${id}/reopen`)
  },
  attachTask(id, taskId) {
    return apiClient.post(`/orders/${id}/tasks`, { taskId })
  },
  detachTask(id, taskId) {
    return apiClient.delete(`/orders/${id}/tasks/${taskId}`)
  },
  // привязка/смена заказа из формы задания (UC-6)
  setTaskOrder(taskId, orderId) {
    return apiClient.post(`/tasks/${taskId}/set-order`, { orderId })
  }
}
```

- [ ] **Step 2: Создать константы статусов (по образцу `src/constants/effectiveStatus.js`)**

```javascript
// Wintime-Control-Frontend/src/constants/orderStatus.js
export const ORDER_STATUS = {
  Active:    { label: 'Активен',  bg: 'bg-blue-100',  text: 'text-blue-800',  dot: 'bg-blue-500',  hex: '#3b82f6' },
  Completed: { label: 'Выполнен', bg: 'bg-green-100', text: 'text-green-800', dot: 'bg-green-500', hex: '#22c55e' },
  Cancelled: { label: 'Отменён',  bg: 'bg-gray-100',  text: 'text-gray-800',  dot: 'bg-gray-500',  hex: '#9ca3af' },
}

export const ORDER_STATUS_KEYS = Object.keys(ORDER_STATUS)

export function getOrderStatusMeta(key) {
  return ORDER_STATUS[key] || ORDER_STATUS.Active
}
```

- [ ] **Step 3: Проверить сборку фронта**

Run: `cd Wintime-Control-Frontend && npm run build`
Expected: сборка без ошибок.

- [ ] **Step 4: Commit**

```bash
git add Wintime-Control-Frontend/src/api/orders.js Wintime-Control-Frontend/src/constants/orderStatus.js
git commit -m "feat(PZP-05): фронт — API заказов + константы статусов"
```

---

## Task 10: Frontend — страница «Заказы» (список, фильтры, CRUD, статусы)

**Files:**
- Create: `Wintime-Control-Frontend/src/views/OrdersView.vue`
- Modify: `Wintime-Control-Frontend/src/router/index.js` (роут + пункт меню)
- Test: `Wintime-Control-Frontend/src/views/__tests__/OrdersView.spec.js`

**Interfaces:**
- Consumes: `ordersApi` (Task 9), `productTypesApi` (существующий), `ORDER_STATUS` (Task 9).
- Produces: маршрут `/orders` (роль Manager/Admin); компонент со списком, фильтром статуса, поиском, формой create/edit, кнопками Завершить/Отменить/Возобновить, прогресс-баром.

- [ ] **Step 1: Написать Vitest-тест**

```javascript
// Wintime-Control-Frontend/src/views/__tests__/OrdersView.spec.js
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import OrdersView from '../OrdersView.vue'

vi.mock('@/api/orders', () => ({
  ordersApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      { id: '1', number: 'ORD-1', productTypeArticle: 'A', quantity: 100,
        goodQuantity: 40, progressPercent: 40, status: 'Active',
        dueDate: '2026-08-01T00:00:00Z' }
    ] })),
    create: vi.fn(() => Promise.resolve({ data: {} })),
    complete: vi.fn(() => Promise.resolve({ data: {} })),
    cancel: vi.fn(() => Promise.resolve({ data: {} })),
  }
}))
vi.mock('@/api/productTypes', () => ({
  productTypesApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) }
}))

describe('OrdersView', () => {
  beforeEach(() => vi.clearAllMocks())

  it('загружает и показывает список заказов с прогрессом', async () => {
    const wrapper = mount(OrdersView, {
      global: { stubs: { 'el-progress': true, 'el-select': true, 'el-option': true } }
    })
    await flushPromises()
    expect(wrapper.text()).toContain('ORD-1')
    expect(wrapper.text()).toContain('40')
  })
})
```

> Проверить фактический путь и стиль существующих Vitest-тестов (`src/**/__tests__/*.spec.js`) и при необходимости выровнять импорты/алиасы (`@/`).

- [ ] **Step 2: Запустить — убедиться, что падает**

Run: `cd Wintime-Control-Frontend && npx vitest run src/views/__tests__/OrdersView.spec.js`
Expected: FAIL (нет `OrdersView.vue`).

- [ ] **Step 3: Создать `OrdersView.vue`**

Реализовать по паттерну существующего справочника (взять за образец `src/views/dictionary/ProductTypeDictionary.vue` — та же структура: таблица Element Plus, фильтры, модалка create/edit). Ключевые элементы:

- `onMounted` → `ordersApi.getList(params)`; хранить `orders`, `filters { status, search }`.
- Таблица (`el-table`) с колонками: Номер, Дата, Срок (`dueDate`), Изделие (`productTypeArticle`), Заказано (`quantity`), Прогресс (`el-progress :percentage="Math.min(100, Math.round(o.progressPercent))"` + подпись `o.goodQuantity/o.quantity`), Статус (бейдж из `getOrderStatusMeta(o.status)`).
- Фильтр статуса (`el-select` по `ORDER_STATUS_KEYS`), поиск по номеру (`el-input`) → перезапрос `getList`.
- Кнопка «Создать» → модалка с полями: Номер (`el-input`), Дата (`el-date-picker`), Срок (`el-date-picker`), Тип изделия (`el-select` filterable, грузит `productTypesApi.getList({ isActive: true })`), Количество (`el-input-number` min=1), Примечание. Submit → `ordersApi.create`.
- Кнопки строки: «Открыть» (переход в карточку — Task 11), «Завершить» (`ordersApi.complete`, **disabled если `o.goodQuantity < o.quantity`**), «Отменить» (`ordersApi.cancel`), для не-Active — «Возобновить» (`ordersApi.reopen`). После действия — перезагрузка списка; ошибки показывать через `ElMessage.error(e.response?.data)` (сообщение приходит с бэкенда, напр. «Недостаточно годных…»).

> Для дат при создании: отправлять ISO-строку в UTC; `el-date-picker` с `value-format="YYYY-MM-DD"` — бэкенд обернёт `SpecifyKind` в UTC.

- [ ] **Step 4: Зарегистрировать маршрут и пункт меню**

В `src/router/index.js` добавить маршрут (по образцу существующих защищённых маршрутов справочников, роль Manager/Admin через существующий `meta`-механизм):
```javascript
{
  path: '/orders',
  name: 'orders',
  component: () => import('@/views/OrdersView.vue'),
  meta: { requiresAuth: true, roles: ['Admin', 'Manager'] }
}
```
> Точный формат `meta.roles` и добавление пункта в навигационное меню — свериться с существующими маршрутами (напр. `producttypes`/справочники) и повторить их паттерн (включая место в меню-компоненте).

- [ ] **Step 5: Запустить тест — убедиться, что проходит**

Run: `cd Wintime-Control-Frontend && npx vitest run src/views/__tests__/OrdersView.spec.js`
Expected: PASS.

- [ ] **Step 6: Проверить сборку**

Run: `cd Wintime-Control-Frontend && npm run build`
Expected: без ошибок.

- [ ] **Step 7: Commit**

```bash
git add Wintime-Control-Frontend/src/views/OrdersView.vue Wintime-Control-Frontend/src/router/index.js Wintime-Control-Frontend/src/views/__tests__/OrdersView.spec.js
git commit -m "feat(PZP-05): фронт — страница «Заказы» (список, фильтры, CRUD, статусы)"
```

---

## Task 11: Frontend — карточка заказа (детали, задания, привязка/отвязка)

**Files:**
- Create: `Wintime-Control-Frontend/src/components/orders/OrderDetailModal.vue`
- Modify: `Wintime-Control-Frontend/src/views/OrdersView.vue` (открытие карточки)
- Test: `Wintime-Control-Frontend/src/components/orders/__tests__/OrderDetailModal.spec.js`

**Interfaces:**
- Consumes: `ordersApi.getById/attachTask/detachTask` (Task 9).
- Produces: модальная карточка заказа: разбивка прогресса (Заказано/Выпущено/Брак/Годных/%), список привязанных заданий, «Привязать существующее», «Отвязать».

- [ ] **Step 1: Написать Vitest-тест**

```javascript
// Wintime-Control-Frontend/src/components/orders/__tests__/OrderDetailModal.spec.js
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import OrderDetailModal from '../OrderDetailModal.vue'

vi.mock('@/api/orders', () => ({
  ordersApi: {
    getById: vi.fn(() => Promise.resolve({ data: {
      id: '1', number: 'ORD-1', quantity: 100, producedQuantity: 110,
      defectQuantity: 10, goodQuantity: 100, progressPercent: 100, status: 'Active',
      productTypeArticle: 'A',
      tasks: [
        { taskId: 't1', immName: 'ТПА-1', moldName: 'ПФ-1', planQuantity: 60, actualQuantity: 60, defectQuantity: 0, status: 'Completed' }
      ]
    } })),
    detachTask: vi.fn(() => Promise.resolve({ data: {} })),
  }
}))

describe('OrderDetailModal', () => {
  beforeEach(() => vi.clearAllMocks())

  it('показывает разбивку прогресса и список заданий', async () => {
    const wrapper = mount(OrderDetailModal, {
      props: { modelValue: true, orderId: '1' },
      global: { stubs: { 'el-dialog': { template: '<div><slot /></div>' }, 'el-progress': true } }
    })
    await flushPromises()
    expect(wrapper.text()).toContain('ТПА-1')
    expect(wrapper.text()).toContain('100') // годных
  })
})
```

- [ ] **Step 2: Запустить — убедиться, что падает**

Run: `cd Wintime-Control-Frontend && npx vitest run src/components/orders/__tests__/OrderDetailModal.spec.js`
Expected: FAIL (нет компонента).

- [ ] **Step 3: Создать `OrderDetailModal.vue`**

Компонент-модалка (`el-dialog`), props `modelValue` (bool) + `orderId`. При открытии `ordersApi.getById(orderId)` → `order`. Показать:
- шапку с номером/статусом/сроком;
- разбивку: Заказано `order.quantity`, Выпущено `order.producedQuantity`, Брак `order.defectQuantity`, **Годных** `order.goodQuantity`, Прогресс `el-progress`;
- `el-table` заданий (`order.tasks`): ТПА, ПФ, План, Факт, Брак, Статус + кнопка «Отвязать» на строке → `ordersApi.detachTask(orderId, row.taskId)` → перезагрузка;
- кнопка «Привязать существующее» → под-диалог с `el-select` заданий без заказа того же изделия. Источник списка: `tasksApi.getList()` (существующий), отфильтровать на клиенте по `t.orderId == null` и совпадению изделия; выбор → `ordersApi.attachTask(orderId, taskId)`. Ошибку привязки (400) показать `ElMessage.error(e.response?.data)`.
- `emit('updated')` после привязки/отвязки, чтобы `OrdersView` перезагрузил список.

В `OrdersView.vue`: кнопка «Открыть» строки ставит `selectedOrderId` и `showDetail=true`, монтирует `<OrderDetailModal v-model="showDetail" :order-id="selectedOrderId" @updated="loadOrders" />`.

- [ ] **Step 4: Запустить тест — убедиться, что проходит**

Run: `cd Wintime-Control-Frontend && npx vitest run src/components/orders/__tests__/OrderDetailModal.spec.js`
Expected: PASS.

- [ ] **Step 5: Проверить сборку**

Run: `cd Wintime-Control-Frontend && npm run build`
Expected: без ошибок.

- [ ] **Step 6: Commit**

```bash
git add Wintime-Control-Frontend/src/components/orders/ Wintime-Control-Frontend/src/views/OrdersView.vue
git commit -m "feat(PZP-05): фронт — карточка заказа (детали, задания, привязка/отвязка)"
```

---

## Task 12: Frontend — селект «Заказ» в форме задания

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/TasksView.vue` (и `src/components/**/TaskDetailModal.vue` — если форма создания/редактирования там)
- Test: расширить/создать Vitest-тест формы задания

**Interfaces:**
- Consumes: `ordersApi.getList` (Task 9), `ordersApi.setTaskOrder` (Task 9).
- Produces: опциональный селект «Заказ» в форме задания, отфильтрованный по `ProductType` ПФ; отвязка из формы недоступна (UC-7).

- [ ] **Step 1: Найти форму задания**

Run: `cd Wintime-Control-Frontend && grep -rl "moldId" src/views src/components | grep -i task`
Определить, где рендерится форма создания/редактирования задания (селект ПФ). Открыть этот файл.

- [ ] **Step 2: Написать/расширить Vitest-тест (фильтрация заказов по изделию ПФ)**

Добавить тест: при выбранной ПФ с `productTypeId = X` селект «Заказ» запрашивает `ordersApi.getList({ status: 'Active', productTypeId: X })` и показывает только эти заказы. (Замокать `ordersApi.getList`, смонтировать форму, установить `moldId`/`productTypeId`, проверить вызов с нужным `productTypeId`.)

```javascript
// фрагмент теста — проверка фильтра по изделию
expect(ordersApi.getList).toHaveBeenCalledWith(
  expect.objectContaining({ status: 'Active', productTypeId: 'X' })
)
```

> Точную структуру монтирования взять из существующего Vitest-теста формы задания (если есть) либо из `OrdersView.spec.js` как образца моков.

- [ ] **Step 3: Запустить — убедиться, что падает**

Run: `cd Wintime-Control-Frontend && npx vitest run <путь к тесту формы>`
Expected: FAIL.

- [ ] **Step 4: Добавить селект «Заказ» в форму**

- Добавить в модель формы поле `orderId` (может быть `null`).
- Вычислить `productTypeId` выбранной ПФ (из загруженного списка ПФ: `molds.find(m => m.id === form.moldId)?.productTypeId`).
- При изменении ПФ: если `productTypeId` задан — `ordersApi.getList({ status: 'Active', productTypeId })` → `orderOptions`; сбросить `form.orderId`, если он не входит в новый список.
- `el-select` «Заказ» (`filterable`, показывает `number` + артикул), **без опции очистки в «нет»** (`:clearable="false"`) — снять привязку можно только из карточки заказа (UC-7).
- Сохранение:
  - при **создании** задания — включить `orderId` в тело `POST /api/tasks` (бэкенд валидирует);
  - при **редактировании** существующего — если `orderId` изменился на непустой, вызвать `ordersApi.setTaskOrder(taskId, orderId)`.

- [ ] **Step 5: Запустить тест — убедиться, что проходит**

Run: `cd Wintime-Control-Frontend && npx vitest run <путь к тесту формы>`
Expected: PASS.

- [ ] **Step 6: Проверить сборку**

Run: `cd Wintime-Control-Frontend && npm run build`
Expected: без ошибок.

- [ ] **Step 7: Commit**

```bash
git add Wintime-Control-Frontend/src/
git commit -m "feat(PZP-05): фронт — селект «Заказ» в форме задания (фильтр по изделию)"
```

---

## Task 13: Frontend — поле «Брак» в мобильном завершении задания

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/mobile/MobileTaskDetailView.vue` (проверить точный путь)
- Test: Vitest-тест диалога завершения

**Interfaces:**
- Consumes: `mobileApi`/`tasksApi` complete (существующий) — тело дополняется `defectQuantity`.
- Produces: поле «Брак» (число, 0..выпущено) в диалоге «Завершить задание».

- [ ] **Step 1: Найти диалог завершения задания**

Run: `cd Wintime-Control-Frontend && grep -rln "complete" src/views/mobile src/components`
Открыть файл мобильной карточки задания с кнопкой/диалогом «Завершить задание».

- [ ] **Step 2: Написать Vitest-тест**

Тест: диалог завершения содержит поле «Брак»; при подтверждении вызывается complete с `defectQuantity` в теле; ввод брака > выпущено блокируется (клиентская валидация min=0, max=`actualQuantity`).

```javascript
// фрагмент: проверка передачи брака
expect(completeSpy).toHaveBeenCalledWith(
  taskId,
  expect.objectContaining({ defectQuantity: 5 })
)
```

- [ ] **Step 3: Запустить — убедиться, что падает**

Run: `cd Wintime-Control-Frontend && npx vitest run <путь к тесту>`
Expected: FAIL.

- [ ] **Step 4: Добавить поле «Брак» в диалог**

- В форму/диалог завершения добавить `el-input-number` «Брак» (`:min="0"`, `:max="actualQuantity текущего задания"`, default 0).
- В тело запроса `complete` добавить `defectQuantity`. Бэкенд валидирует диапазон повторно (Task 8) и вернёт 400 при нарушении — показать `ElMessage.error`.

- [ ] **Step 5: Запустить тест — убедиться, что проходит**

Run: `cd Wintime-Control-Frontend && npx vitest run <путь к тесту>`
Expected: PASS.

- [ ] **Step 6: Полный прогон Vitest + сборка**

Run: `cd Wintime-Control-Frontend && npx vitest run && npm run build`
Expected: все тесты зелёные, сборка ок.

- [ ] **Step 7: Commit**

```bash
git add Wintime-Control-Frontend/src/
git commit -m "feat(PZP-05): фронт — поле «Брак» в мобильном завершении задания"
```

---

## Task 14: ADR-0009 + финальная регрессия

**Files:**
- Create: `docs/adr/0009-orders-module.md`

- [ ] **Step 1: Написать ADR-0009 (формат MADR, по образцу `docs/adr/0008-cycle-pipeline-and-unplanned-run.md`)**

Зафиксировать контекст и решения PZP-05: новая сущность `Order`; заказ = одна номенклатурная позиция; `Number` неуникален (несколько `Order` на пару номер+дата = позиции внешнего заказа); `Order → ShiftTask` 1:N, `OrderId` nullable (модуль опционален); прогресс derive-on-read по **годным** (`Σ(Actual − Defect)`), не хранится; ручной брак `ShiftTask.DefectQuantity`; две семантики выполнения (задание = `PlanQuantity`, заказ = годных ≥ `Quantity`); ручное завершение при достаточном выпуске; жёсткая валидация изделия при привязке; отвязка только из карточки заказа. Отвергнутые альтернативы: хранимый счётчик прогресса (Вариант B), многострочный заказ, авто-завершение, брак из аварийных циклов. Связи: PZP-08/PZP-04/PZP-09/BL-27/PZP-07/ROS-05/ROS-07.

- [ ] **Step 2: Обновить индекс ADR (если есть `docs/adr/README.md` со списком)**

Добавить строку про ADR-0009 в список, если README ведёт перечень.

- [ ] **Step 3: Финальная регрессия — backend + frontend**

Run:
```powershell
dotnet test Wintime.Control.Tests.Unit
dotnet test Wintime.Control.Tests.Integration
cd Wintime-Control-Frontend; npx vitest run; npm run build
```
Expected: всё зелёное.

- [ ] **Step 4: Commit**

```bash
git add docs/adr/0009-orders-module.md docs/adr/README.md
git commit -m "docs(PZP-05): ADR-0009 — модуль учёта заказов"
```

---

## Self-Review (выполнено при написании плана)

**Покрытие спеки:**
- UC-1 создание заказа → Task 6 (CRUD-тесты create/400). ✓
- UC-2 редактирование + запрет смены изделия при заданиях → Task 6 (Update, 409). ✓
- UC-3 завершение при достатке годных → Task 1 (гард) + Task 6 (`OrderProgressTests`). ✓
- UC-4/UC-5 отмена/возобновление → Task 1 + Task 6. ✓
- UC-6 привязка (2 места) → Task 7 (карточка заказа) + Task 8 (`set-order`, `CreateTask.OrderId`) + фронт Task 11/12. ✓
- UC-7 отвязка только из карточки заказа → Task 7 (DELETE) + Task 12 (`:clearable=false`). ✓
- UC-8 задание без заказа → `OrderId` nullable (Task 2), не обязателен нигде. ✓
- UC-9 брак при завершении → Task 2 (гард) + Task 8 (integration) + Task 13 (UI). ✓
- UC-10 список с прогрессом → Task 6 (`GetList`, GroupBy) + Task 10 (UI). ✓
- UC-11 карточка заказа → Task 6 (`GetById`) + Task 11 (UI). ✓
- UC-12 прогресс после переназначения сирот → derive-on-read (Task 6); `ActualQuantity` меняет PZP-04, прогресс считается на чтении (покрыто `OrderProgressTests` семантикой суммирования по `ActualQuantity/DefectQuantity`). ✓
- Миграция/индексы/UTC → Task 4. ✓ ADR-0009 → Task 14. ✓

**Плейсхолдеры:** фронт-задачи 12–13 намеренно начинаются с поиска точного файла формы (пути не зашиты в спеку); код селекта/поля и Vitest-фрагменты приведены. Тела больших Vue-компонентов (OrdersView/OrderDetailModal) описаны через образец существующего справочника + ключевые вызовы — это осознанный уровень детализации для SFC, не плейсхолдер.

**Согласованность типов:** `OrderStatus{Active,Completed,Cancelled}`, `Order.Complete(int goodQuantity)`, `OrderTaskBinding.EnsureCanBind(Order, Guid?)`, `ShiftTask.Complete(int?, string?, int? = null)`, DTO-имена (`ProducedQuantity/DefectQuantity/GoodQuantity/ProgressPercent`) — единообразны между Tasks 1–8 и фронт-моками 9–13.
