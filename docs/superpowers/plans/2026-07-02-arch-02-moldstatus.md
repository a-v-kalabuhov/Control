# ARCH-02: MoldStatus в модели — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить в модель пресс-формы (`Mold`) nullable-поле статуса `MoldStatus` с миграцией БД, не затрагивая UI и не ломая поведение для клиента Мун.

**Architecture:** Архитектурная заглушка под РОСОМС (ROS-01), по образцу ARCH-01 (Operator-роль). Вводим enum `MoldStatus` с зафиксированными числовыми значениями, добавляем nullable-свойство на сущность `Mold`, генерируем миграцию, добавляющую nullable-колонку `integer` в таблицу `Molds`. API-контракт (DTO) и фронтенд НЕ трогаем — они появятся в ROS-01. `null` = статус не задан → Мун-логика (доступность только через `IsActive`) остаётся без изменений.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql, PostgreSQL 16, xUnit + FluentAssertions.

## Global Constraints

- Enum хранится в БД как `int` (EF-дефолт, без `HasConversion`) — как `UserRole`/`TaskStatus`. Числовые значения enum **фиксируются** и не перенумеровываются: колонка целочисленная, перенумерация сломала бы уже сохранённые значения.
- Поле **nullable** на всех уровнях (`MoldStatus?`). Мун игнорирует его; ни одна существующая доменная логика не должна начать зависеть от него в этой задаче.
- Никакого UI и никаких изменений API-контракта (`MoldDto`, `MoldsController`) — это скоуп ROS-01.
- Значения статуса (из ROS-01): «В работе», «В ремонте», «Модернизация», «ТО».
- Миграции запускать с обоими флагами:
  `--project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`
- Не удалять сущности физически; `IsActive` — отдельный архивный флаг, `MoldStatus` его не заменяет (см. CLAUDE.md).

---

## File Structure

- `Wintime.Control.Core/Enums/MoldStatus.cs` — **создать**. Новый enum, одна ответственность: перечень статусов ПФ с фиксированными значениями.
- `Wintime.Control.Core/Entities/Mold.cs` — **изменить**. Добавить nullable-свойство `MoldStatus? MoldStatus`.
- `Wintime.Control.Tests.Unit/Enums/MoldStatusStubTests.cs` — **создать**. Тест-заглушка по образцу `OperatorRoleStubTests`: фиксирует числовые значения enum.
- `Wintime.Control.Infrastructure/Migrations/<timestamp>_AddMoldStatus.cs` (+ `.Designer.cs`) и `ControlDbContextModelSnapshot.cs` — **сгенерировать** через `dotnet ef migrations add`. Вручную не писать.

---

### Task 1: Enum `MoldStatus` + тест-заглушка стабильности значений

**Files:**
- Create: `Wintime.Control.Core/Enums/MoldStatus.cs`
- Test: `Wintime.Control.Tests.Unit/Enums/MoldStatusStubTests.cs`

**Interfaces:**
- Produces: `enum Wintime.Control.Core.Enums.MoldStatus { InWork = 0, InRepair = 1, Modernization = 2, Maintenance = 3 }`

- [ ] **Step 1: Написать падающий тест**

`Wintime.Control.Tests.Unit/Enums/MoldStatusStubTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Enums;
using Xunit;

namespace Wintime.Control.Tests.Unit.Enums;

/// <summary>
/// ARCH-02: заглушка статуса пресс-формы под РОСОМС (ROS-01).
/// Enum должен существовать; его числовые значения фиксируются,
/// т.к. MoldStatus хранится в БД как nullable int.
/// </summary>
public class MoldStatusStubTests
{
    [Theory]
    [InlineData(MoldStatus.InWork, 0)]
    [InlineData(MoldStatus.InRepair, 1)]
    [InlineData(MoldStatus.Modernization, 2)]
    [InlineData(MoldStatus.Maintenance, 3)]
    public void MoldStatus_HasStableNumericValues(MoldStatus status, int expected)
    {
        // Перенумерация сломала бы уже сохранённые значения в колонке Molds.MoldStatus.
        ((int)status).Should().Be(expected);
    }

    [Fact]
    public void MoldStatus_HasExactlyFourValues()
    {
        Enum.GetValues<MoldStatus>().Should().HaveCount(4);
    }
}
```

- [ ] **Step 2: Запустить тест — убедиться, что не компилируется/падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~MoldStatusStubTests`
Expected: FAIL — ошибка компиляции «The type or namespace name 'MoldStatus' does not exist».

- [ ] **Step 3: Создать enum**

`Wintime.Control.Core/Enums/MoldStatus.cs`:

```csharp
namespace Wintime.Control.Core.Enums;

/// <summary>
/// Статус пресс-формы. Заглушка под РОСОМС (ROS-01): в фазе Мун поле nullable
/// и игнорируется (доступность ПФ определяется только флагом IsActive).
/// Значения фиксированы — хранятся в БД как int (колонка Molds.MoldStatus).
/// </summary>
public enum MoldStatus
{
    InWork = 0,        // В работе
    InRepair = 1,      // В ремонте
    Modernization = 2, // Модернизация
    Maintenance = 3    // ТО
}
```

- [ ] **Step 4: Запустить тест — убедиться, что проходит**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~MoldStatusStubTests`
Expected: PASS (5 тестов: 4 Theory + 1 Fact).

- [ ] **Step 5: Коммит**

```bash
git add Wintime.Control.Core/Enums/MoldStatus.cs Wintime.Control.Tests.Unit/Enums/MoldStatusStubTests.cs
git commit -m "ARCH-02: enum MoldStatus (заглушка статуса ПФ под РОСОМС)"
```

---

### Task 2: Nullable-свойство `MoldStatus` на сущности `Mold`

**Files:**
- Modify: `Wintime.Control.Core/Entities/Mold.cs`

**Interfaces:**
- Consumes: `enum MoldStatus` (Task 1)
- Produces: `Mold.MoldStatus` типа `MoldStatus?` (nullable) — EF-дефолт: колонка `integer NULL`, без доп. конфигурации в `ControlDbContext`.

- [ ] **Step 1: Добавить свойство на сущность**

В `Wintime.Control.Core/Entities/Mold.cs` добавить `using` и свойство рядом с `IsActive`:

```csharp
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Entities;

public class Mold : BaseEntity
{
    public string FormId { get; set; } = string.Empty; // Уникальный артикул для QR
    public string Name { get; set; } = string.Empty; // Наименование изделия
    public int Cavities { get; set; } // Гнёздность
    public decimal PartWeightGrams { get; set; } // Вес детали
    public decimal RunnerWeightGrams { get; set; } // Вес литника
    public int MaxResourceCycles { get; set; } // Ресурс
    public int? To1Cycles { get; set; } // Порог ТО1
    public int? To2Cycles { get; set; } // Порог ТО2
    public string? StorageLocationIndex { get; set; } // Индекс места (А-12)
    public string? DrawingPath { get; set; }
    public string? PhotoPath { get; set; }
    public bool IsActive { get; set; } = true;

    // Статус ПФ (ARCH-02). Nullable: Мун игнорирует, UI появится в ROS-01.
    // Не подменяет IsActive (архивный флаг) — см. CLAUDE.md.
    public MoldStatus? MoldStatus { get; set; }

    // Navigation
    public ICollection<ShiftTask> ShiftTasks { get; set; } = new List<ShiftTask>();
}
```

- [ ] **Step 2: Сборка — убедиться, что компилируется**

Run: `dotnet build Wintime.Control.Core`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Коммит**

```bash
git add Wintime.Control.Core/Entities/Mold.cs
git commit -m "ARCH-02: nullable-поле MoldStatus на сущности Mold"
```

---

### Task 3: Миграция `AddMoldStatus`

**Files:**
- Create (сгенерировать, не писать вручную): `Wintime.Control.Infrastructure/Migrations/<timestamp>_AddMoldStatus.cs` (+ `.Designer.cs`)
- Modify (автоген): `Wintime.Control.Infrastructure/Migrations/ControlDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: `Mold.MoldStatus` типа `MoldStatus?` (Task 2)
- Produces: колонка `MoldStatus integer NULL` в таблице `Molds`.

- [ ] **Step 1: Сгенерировать миграцию**

Run:
```powershell
dotnet ef migrations add AddMoldStatus --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: создаются `<timestamp>_AddMoldStatus.cs` и `.Designer.cs`, обновляется `ControlDbContextModelSnapshot.cs`. «Done.» без ошибок.

- [ ] **Step 2: Проверить содержимое миграции**

Открыть сгенерированный `<timestamp>_AddMoldStatus.cs` и убедиться, что `Up`/`Down` содержат **ровно** добавление/удаление одной nullable-колонки (никаких лишних изменений от рассинхрона снапшота):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<int>(
        name: "MoldStatus",
        table: "Molds",
        type: "integer",
        nullable: true);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(
        name: "MoldStatus",
        table: "Molds");
}
```

Если в `Up` есть что-то ещё, кроме этой колонки — остановиться: снапшот был рассинхронизирован до задачи; откатить (`dotnet ef migrations remove --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`), разобраться с рассинхроном, повторить.

- [ ] **Step 3: Применить миграцию к БД**

Run:
```powershell
dotnet ef database update --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: «Applying migration '<timestamp>_AddMoldStatus'. Done.» без ошибок. (Требуется поднятый PostgreSQL, напр. `docker-compose up`.)

- [ ] **Step 4: Прогнать весь solution — регрессии нет**

Run: `dotnet build` затем `dotnet test`
Expected: сборка успешна; все тесты зелёные (существующие + новые из Task 1).

- [ ] **Step 5: Коммит**

```bash
git add Wintime.Control.Infrastructure/Migrations/
git commit -m "ARCH-02: миграция AddMoldStatus (nullable колонка Molds.MoldStatus)"
```

---

## Notes / решения

- **Представление в БД — enum как int (nullable).** Выбрано по конвенции кодовой базы (`UserRole` хранится как int без `HasConversion`, см. ARCH-01) и потому, что колонка компактна и не требует конфигурации. Альтернатива — строковое хранение (как `ImmStatusHistory.Status`) — отвергнута: `MoldStatus` — закрытый управляемый перечень, а не свободный статус по MQTT. Если при реализации ROS-01 понадобится строковое представление в API — это делается маппингом enum↔строка в DTO, без изменения хранения.
- **DTO/контроллер/фронт не трогаем** намеренно: ARCH-02 — только слой персистентности. Добавление в `MoldDto`, справочник ПФ и логику доступности СЗ — скоуп ROS-01.
- **Значения статуса** заданы enum-именами (`InWork`/`InRepair`/`Modernization`/`Maintenance`); человекочитаемые подписи («В работе» и т.д.) появятся на фронте в ROS-01, как это сделано для эффективных статусов (`src/constants/effectiveStatus.js`).
