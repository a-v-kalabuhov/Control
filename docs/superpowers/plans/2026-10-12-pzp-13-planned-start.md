# PZP-13 — Плановая дата и время начала наладки: план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Задание получает обязательную плановую дату и необязательное время начала наладки, выдаётся только с наладчиком, а планшет наладчика показывает очередь по плановому началу.

**Architecture:** Поля `PlannedDate` (`DateOnly`) и `PlannedSetupStartTime` (`TimeOnly?`) хранятся на `ShiftTask`. Эффективное начало (`PlannedStartAt`, UTC) не хранится — его считает чистая функция `PlannedTaskStart.Resolve` по поясу завода и первой смене; данные смен даёт scoped-сервис `IFactoryClock`. Правила правки и выдачи живут в сущности (ADR-0002), контроллер проверяет только внешние данные (наладчик существует и активен).

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql (PostgreSQL 16), xUnit + FluentAssertions + Testcontainers; Vue 3 + Element Plus + Pinia, Vitest.

**Спека:** `docs/superpowers/specs/2026-10-12-pzp-13-planned-start-design.md`

## Global Constraints

- Ветка: `feature/pzp-13-planned-start` (уже создана, спека закоммичена). Прямой push в `master` запрещён — только PR.
- Enum `TaskStatus.Draft` и строка `"Draft"` в API **не меняются**; меняются только подписи: «Черновик» → «Планируется».
- Все `DateTime` в EF-запросах — `Kind=Utc`.
- Пояс завода — `TimeZoneId` самой ранней смены, по умолчанию `Europe/Moscow` (`FactoryCalendar.DefaultTimeZoneId`).
- Время в JSON: `PlannedDate` — `"yyyy-MM-dd"`, `PlannedSetupStartTime` — `"HH:mm:ss"`. Фронт всегда шлёт время с секундами.
- Допуск «не в прошлом» для времени — 5 минут.
- Тексты ошибок (дословно):
  - «Укажите плановую дату»
  - «Плановая дата не может быть в прошлом»
  - «Время начала наладки не может быть в прошлом»
  - «Плановое начало можно изменить только до начала наладки»
  - «Назначьте наладчика»
  - «У выданного задания должен быть наладчик»
  - «Наладчика можно изменить только до начала наладки»
  - «Укажите активного наладчика»
  - «Задание не в статусе «Планируется»»
- Команды тестов:
  - backend: `dotnet test Wintime.Control.Tests.Unit` и `dotnet test Wintime.Control.Tests.Integration` (нужен Docker для Testcontainers);
  - frontend: `cd Wintime-Control-Frontend && npx vitest run <путь>`.
- Каждый коммит заканчивается строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Карта файлов

**Backend — создать:**
- `Wintime.Control.Core/Policies/PlannedTaskStart.cs` — `PlannedTaskStart.Resolve` + record `FactorySchedule`.
- `Wintime.Control.Core/Interfaces/IFactoryClock.cs`
- `Wintime.Control.Infrastructure/Services/FactoryClock.cs`
- `Wintime.Control.Core/DTOs/Tasks/SetTaskPersonnelRequestDto.cs`
- `Wintime.Control.Core/DTOs/Tasks/IssueTaskRequestDto.cs`
- `Wintime.Control.Infrastructure/Migrations/<ts>_PlannedStartDateTime.cs` (генерируется)
- Тесты:
  - `Wintime.Control.Tests.Unit/Policies/PlannedTaskStartTests.cs`
  - `Wintime.Control.Tests.Unit/Entities/ShiftTaskPlanningTests.cs`
  - `Wintime.Control.Tests.Integration/Tasks/TaskPlannedStartApiTests.cs`
  - `Wintime.Control.Tests.Integration/Tasks/TaskPersonnelApiTests.cs`
  - `Wintime.Control.Tests.Integration/Tasks/MyTasksApiTests.cs`

**Backend — изменить:**
- `Wintime.Control.Core/Entities/ShiftTask.cs`
- `Wintime.Control.Core/DTOs/Tasks/{TaskDto,CreateTaskRequestDto,UpdateTaskRequestDto,TaskMappingExtensions,MyTasksDto}.cs`
- `Wintime.Control.API/Controllers/TasksController.cs`
- `Wintime.Control.API/Controllers/UnplannedRunController.cs`
- `Wintime.Control.API/Program.cs`
- Тесты, которые создают или выдают задания (список — в задачах 2 и 4).

**Frontend — создать:**
- `src/utils/plannedStart.js` + `src/utils/__tests__/plannedStart.spec.js`
- `src/views/tasks/IssueTaskDialog.vue` + `src/views/tasks/__tests__/IssueTaskDialog.spec.js`
- `src/stores/__tests__/tasks.spec.js`

**Frontend — изменить:**
- `src/api/tasks.js`
- `src/stores/tasks.js`, `src/stores/mobile.js`
- `src/constants/taskStatus.js`
- `src/components/tasks/{TaskStatusBadge,TaskFilters}.vue`
- `src/components/mobile/{MobileStatusBadge,MobileTaskCard}.vue`
- `src/views/tasks/{TasksView,TaskFormModal,TaskDetailModal}.vue`
- `src/views/mobile/MobileTasksView.vue`
- Их тесты.

**Документация:**
- `docs/adr/0014-planned-start-and-issue-requires-adjuster.md`
- `CLAUDE.md`
- `docs/user-guide/manager.md`, `docs/user-guide/adjuster.md`
- `seed_equipment_report.sql`

`seed_demo.sql` уже устарел (пишет в несуществующую таблицу `"Tasks"`) — не трогаем.

---

### Task 1: Политика `PlannedTaskStart` и `FactorySchedule`

**Files:**
- Create: `Wintime.Control.Core/Policies/PlannedTaskStart.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/PlannedTaskStartTests.cs`

**Interfaces:**
- Produces:
  - `PlannedTaskStart.Resolve(DateOnly date, TimeOnly? time, int? firstShiftStartMinutes, TimeZoneInfo factoryTz) → DateTime (Kind=Utc)`;
  - `record FactorySchedule(TimeZoneInfo TimeZone, int? FirstShiftStartMinutes)` с методами `DateTime PlannedStartUtc(DateOnly date, TimeOnly? time)` и `DateTime ToLocal(DateTime utc)`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class PlannedTaskStartTests
{
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
    private static readonly DateOnly Date = new(2026, 10, 15);

    [Fact]
    public void Resolve_WithTime_IsDatePlusTimeInFactoryZone()
    {
        var result = PlannedTaskStart.Resolve(Date, new TimeOnly(14, 0), 480, Moscow);

        result.Should().Be(new DateTime(2026, 10, 15, 11, 0, 0, DateTimeKind.Utc));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Resolve_WithoutTime_IsFirstShiftStart()
    {
        var result = PlannedTaskStart.Resolve(Date, null, 480, Moscow); // 08:00 МСК

        result.Should().Be(new DateTime(2026, 10, 15, 5, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_WithoutTimeAndShifts_IsLocalMidnight()
    {
        var result = PlannedTaskStart.Resolve(Date, null, null, Moscow);

        result.Should().Be(new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_TimeInDstGap_MovesForwardToFirstValidTime()
    {
        // 29.03.2026 в Берлине 02:00–03:00 не существует (переход на летнее время).
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        var result = PlannedTaskStart.Resolve(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), null, berlin);

        result.Should().Be(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc)); // 03:00 CEST
    }

    [Fact]
    public void FactorySchedule_PlannedStartUtc_DelegatesToResolve()
    {
        var schedule = new FactorySchedule(Moscow, 480);

        schedule.PlannedStartUtc(Date, null)
            .Should().Be(PlannedTaskStart.Resolve(Date, null, 480, Moscow));
    }

    [Fact]
    public void FactorySchedule_ToLocal_ConvertsUtcToFactoryWallClock()
    {
        var schedule = new FactorySchedule(Moscow, 480);

        schedule.ToLocal(new DateTime(2026, 10, 15, 21, 30, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 10, 16, 0, 30, 0));
    }
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~PlannedTaskStartTests`
Expected: ошибка компиляции — `PlannedTaskStart` не найден.

- [ ] **Step 3: Реализовать**

```csharp
namespace Wintime.Control.Core.Policies;

/// <summary>
/// Эффективное плановое начало задания (PZP-13): плановая дата + время начала наладки
/// по часам завода; без времени — начало самой ранней смены этого дня; без смен — 00:00.
/// Чистая функция, результат не хранится.
/// </summary>
public static class PlannedTaskStart
{
    public static DateTime Resolve(DateOnly date, TimeOnly? time,
                                   int? firstShiftStartMinutes, TimeZoneInfo factoryTz)
    {
        var localTime = time
            ?? (firstShiftStartMinutes is int minutes
                ? TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes))
                : TimeOnly.MinValue);

        var local = date.ToDateTime(localTime, DateTimeKind.Unspecified);

        // Время из «дыры» перехода на летнее время не существует — сдвигаем вперёд до валидного.
        while (factoryTz.IsInvalidTime(local))
            local = local.AddMinutes(15);

        return TimeZoneInfo.ConvertTimeToUtc(local, factoryTz);
    }
}

/// <summary>Расписание завода, нужное для планового начала: пояс и начало самой ранней смены.</summary>
public sealed record FactorySchedule(TimeZoneInfo TimeZone, int? FirstShiftStartMinutes)
{
    public DateTime PlannedStartUtc(DateOnly date, TimeOnly? time) =>
        PlannedTaskStart.Resolve(date, time, FirstShiftStartMinutes, TimeZone);

    /// <summary>UTC → время по часам завода (Kind=Unspecified).</summary>
    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);
}
```

- [ ] **Step 4: Тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~PlannedTaskStartTests`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/PlannedTaskStart.cs Wintime.Control.Tests.Unit/Policies/PlannedTaskStartTests.cs
git commit -m "feat(tasks): PlannedTaskStart policy for effective planned start"
```

---

### Task 2: Схема — `PlannedDate: DateOnly`, `PlannedSetupStartTime`, миграция

Механический перевод типов. Поведение API пока не меняется: дата становится обязательной при создании, но строгие правила правки появятся в Task 3. После задачи весь набор тестов должен быть зелёным.

**Files:**
- Modify:
  - `Wintime.Control.Core/Entities/ShiftTask.cs`;
  - `Wintime.Control.Core/DTOs/Tasks/{TaskDto,CreateTaskRequestDto,UpdateTaskRequestDto,TaskMappingExtensions}.cs`;
  - `Wintime.Control.API/Controllers/TasksController.cs` (CreateTask, UpdateTask);
  - `Wintime.Control.API/Controllers/UnplannedRunController.cs` (строки с `taskDate`).
- Create: миграция `PlannedStartDateTime`.
- Modify tests:
  - `Tasks/TasksControllerTests.cs`, `Tasks/TaskCycleNormsApiTests.cs`, `Tasks/TaskProductTypeGuardTests.cs`, `Orders/TaskOrderBindingApiTests.cs`;
  - все `UnplannedRuns/*Tests.cs`, где у `ShiftTask` задан `IssuedAt`.

**Interfaces:**
- Produces:
  - `ShiftTask.PlannedDate : DateOnly`, `ShiftTask.PlannedSetupStartTime : TimeOnly?`;
  - DTO: `CreateTaskRequestDto.PlannedDate : DateOnly?`, `.PlannedSetupStartTime : TimeOnly?`;
  - `UpdateTaskRequestDto.PlannedDate : DateOnly?`, `.PlannedSetupStartTime : TimeOnly?`, `.ClearPlannedSetupStartTime : bool`;
  - `TaskDto.PlannedDate : DateOnly`, `.PlannedSetupStartTime : TimeOnly?`.

- [ ] **Step 1: Сущность**

В `ShiftTask.cs` заменить `public DateTime? PlannedDate { get; set; }` на:

```csharp
    /// <summary>Плановая дата выполнения — календарная дата завода (PZP-13). Обязательна.</summary>
    public DateOnly PlannedDate { get; set; }

    /// <summary>
    /// Плановое время начала наладки по часам завода (PZP-13). Необязательно:
    /// <c>null</c> — задание начинается с начала первой смены дня (см. PlannedTaskStart).
    /// </summary>
    public TimeOnly? PlannedSetupStartTime { get; set; }
```

- [ ] **Step 2: DTO**

`TaskDto.cs`: заменить `public DateTime? PlannedDate { get; set; }` на

```csharp
    public DateOnly PlannedDate { get; set; }
    public TimeOnly? PlannedSetupStartTime { get; set; }
```

`CreateTaskRequestDto.cs`: заменить `public DateTime? PlannedDate { get; set; }` на

```csharp
    /// <summary>Плановая дата. Обязательна (null → 400).</summary>
    public DateOnly? PlannedDate { get; set; }

    /// <summary>Плановое время начала наладки по часам завода. Необязательно.</summary>
    public TimeOnly? PlannedSetupStartTime { get; set; }
```

`UpdateTaskRequestDto.cs`: заменить `public DateTime? PlannedDate { get; set; }` на

```csharp
    /// <summary>Плановая дата; <c>null</c> — не менять.</summary>
    public DateOnly? PlannedDate { get; set; }

    /// <summary>Плановое время начала наладки; <c>null</c> — не менять.</summary>
    public TimeOnly? PlannedSetupStartTime { get; set; }

    /// <summary>Явно очистить время начала наладки (в PUT <c>null</c> означает «не менять»).</summary>
    public bool ClearPlannedSetupStartTime { get; set; }
```

`TaskMappingExtensions.cs`: после `PlannedDate = t.PlannedDate,` добавить `PlannedSetupStartTime = t.PlannedSetupStartTime,`.

- [ ] **Step 3: Контроллеры — минимально, чтобы собралось**

`TasksController.CreateTask`. В начало метода, перед поиском ПФ:

```csharp
        if (request.PlannedDate is null)
            return BadRequest("Укажите плановую дату");
```

В инициализаторе `new Core.Entities.ShiftTask { ... }`:
- заменить блок `PlannedDate = request.PlannedDate.HasValue ? ... : null,` на

```csharp
            PlannedDate = request.PlannedDate.Value,
            PlannedSetupStartTime = request.PlannedSetupStartTime,
```

- **удалить** строку `IssuedAt = DateTime.UtcNow` (черновику `IssuedAt` не положен, его ставит `Issue()`).

`TasksController.UpdateTask`: заменить блок `if (request.PlannedDate.HasValue) { ... }` на временный (в Task 3 его заменит доменный метод):

```csharp
        if (request.PlannedDate.HasValue)
            task.PlannedDate = request.PlannedDate.Value;
        if (request.ClearPlannedSetupStartTime)
            task.PlannedSetupStartTime = null;
        else if (request.PlannedSetupStartTime.HasValue)
            task.PlannedSetupStartTime = request.PlannedSetupStartTime.Value;
```

`UnplannedRunController.cs`: во всех трёх местах заменить
`var taskDate = X.PlannedDate ?? X.IssuedAt ?? X.CreatedAt;` (X = `t`, `e.Task`, `task`) на

```csharp
var taskDate = X.PlannedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
```

- [ ] **Step 4: Сборка**

Run: `dotnet build Wintime.Control.sln`
Expected: Build succeeded. Если есть ошибки — это прочие обращения к `PlannedDate` как к `DateTime?`; их нужно переписать так же.

- [ ] **Step 5: Миграция**

Run:

```powershell
dotnet ef migrations add PlannedStartDateTime --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```

Заменить тела `Up`/`Down` сгенерированного файла. Тип меняем SQL-ом, чтобы перевести UTC-полночь в дату завода и заполнить пустые значения. Snapshot оставить сгенерированным.

```csharp
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
```

Проверить, что в snapshot у `PlannedDate` тип `date` и `IsRequired()`, а у `PlannedSetupStartTime` — `time without time zone`.

- [ ] **Step 6: Починить тесты, создающие задания через API**

Добавить плановую дату **завтрашнего дня** (с запасом от разницы UTC/МСК).

- `TasksControllerTests.MakeCreateRequest`: в анонимный объект добавить `plannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),`.
- `TaskCycleNormsApiTests`: то же поле в `CreateBody(...)` и в анонимный объект теста `CreateTask_DefaultWorkMode_IsAuto`.
- `TaskProductTypeGuardTests`: то же поле в оба анонимных объекта `PostAsJsonAsync("/api/tasks", new { ... })`.
- `TaskOrderBindingApiTests`: в три `new CreateTaskRequestDto { ... }` добавить `PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),`.

- [ ] **Step 7: Починить тесты журнала работы без задания**

Правило «задание задним числом» (`UnplannedRunAdjacency.SameDate`) раньше брало `IssuedAt`, теперь — `PlannedDate`. В каждом `new EntityTask { ... }` в `Wintime.Control.Tests.Integration/UnplannedRuns/*.cs`, где задан `IssuedAt = <expr>`, добавить рядом `PlannedDate = DateOnly.FromDateTime(<expr>),`, чтобы смысл теста сохранился. Пример (`AssignTaskTests.cs:33`):

```csharp
var task = new EntityTask { ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 50, Status = TaskStatus.Setup,
    IssuedAt = start, PlannedDate = DateOnly.FromDateTime(start), SetupStartedAt = start.AddMinutes(2) };
```

- [ ] **Step 8: Полный прогон**

Run: `dotnet test Wintime.Control.Tests.Unit; dotnet test Wintime.Control.Tests.Integration`
Expected: все зелёные. Если упал тест, который проверял `IssuedAt` у черновика сразу после создания, — поправить ожидание на `null`: это и есть фикс из спеки.

- [ ] **Step 9: Commit**

```bash
git add -A Wintime.Control.Core Wintime.Control.API Wintime.Control.Infrastructure Wintime.Control.Tests.Integration
git commit -m "feat(tasks): PlannedDate as DateOnly + optional PlannedSetupStartTime"
```

---

### Task 3: Правила планового начала — домен, `IFactoryClock`, `PlannedStartAt` в API

**Files:**
- Modify:
  - `Wintime.Control.Core/Entities/ShiftTask.cs`;
  - `Wintime.Control.Core/DTOs/Tasks/TaskDto.cs`, `TaskMappingExtensions.cs`;
  - `Wintime.Control.API/Controllers/TasksController.cs`;
  - `Wintime.Control.API/Program.cs`.
- Create:
  - `Wintime.Control.Core/Interfaces/IFactoryClock.cs`;
  - `Wintime.Control.Infrastructure/Services/FactoryClock.cs`.
- Test:
  - `Wintime.Control.Tests.Unit/Entities/ShiftTaskPlanningTests.cs`;
  - `Wintime.Control.Tests.Integration/Tasks/TaskPlannedStartApiTests.cs`.

**Interfaces:**
- Consumes: `FactorySchedule` (Task 1); поля из Task 2.
- Produces:
  - `ShiftTask.SetPlannedStart(DateOnly date, TimeOnly? time, DateTime nowLocal)`;
  - `IFactoryClock { DateTime UtcNow { get; } Task<FactorySchedule> GetScheduleAsync(CancellationToken ct = default); }`;
  - `TaskMappingExtensions.ToDto(this ShiftTask t, FactorySchedule schedule)` — **параметр обязателен**;
  - `TaskDto.PlannedStartAt : DateTime` (UTC).

- [ ] **Step 1: Падающие доменные тесты**

```csharp
using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Xunit;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Entities;

public class ShiftTaskPlanningTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 10, 0, 0); // время завода
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static ShiftTask NewTask(EntityTaskStatus status) => new() { Status = status, PlanQuantity = 10 };

    [Fact]
    public void SetPlannedStart_Draft_SetsDateAndTime()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        task.SetPlannedStart(Today.AddDays(1), new TimeOnly(14, 0), Now);

        task.PlannedDate.Should().Be(Today.AddDays(1));
        task.PlannedSetupStartTime.Should().Be(new TimeOnly(14, 0));
    }

    [Fact]
    public void SetPlannedStart_Issued_Allowed()
    {
        var task = NewTask(EntityTaskStatus.Issued);

        task.SetPlannedStart(Today, null, Now);

        task.PlannedDate.Should().Be(Today);
    }

    [Theory]
    [InlineData(EntityTaskStatus.Setup)]
    [InlineData(EntityTaskStatus.InProgress)]
    [InlineData(EntityTaskStatus.Completed)]
    [InlineData(EntityTaskStatus.Closed)]
    public void SetPlannedStart_AfterSetupStarted_Throws(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.SetPlannedStart(Today.AddDays(1), null, Now);

        act.Should().Throw<DomainException>().WithMessage("Плановое начало можно изменить только до начала наладки");
    }

    [Fact]
    public void SetPlannedStart_UnchangedValue_InAnyStatus_NoOp()
    {
        var task = NewTask(EntityTaskStatus.InProgress);
        task.PlannedDate = Today.AddDays(-3);

        var act = () => task.SetPlannedStart(Today.AddDays(-3), null, Now);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetPlannedStart_PastDate_Throws()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        var act = () => task.SetPlannedStart(Today.AddDays(-1), null, Now);

        act.Should().Throw<DomainException>().WithMessage("Плановая дата не может быть в прошлом");
    }

    [Fact]
    public void SetPlannedStart_TodayPastTime_Throws()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        var act = () => task.SetPlannedStart(Today, new TimeOnly(9, 0), Now);

        act.Should().Throw<DomainException>().WithMessage("Время начала наладки не может быть в прошлом");
    }

    [Fact]
    public void SetPlannedStart_TodayTimeWithinTolerance_Allowed()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        task.SetPlannedStart(Today, new TimeOnly(9, 56), Now);

        task.PlannedSetupStartTime.Should().Be(new TimeOnly(9, 56));
    }

    [Fact]
    public void SetPlannedStart_JustAfterMidnight_FutureTimeAllowed()
    {
        // Регрессия: допуск не должен «заворачиваться» через полночь.
        var task = NewTask(EntityTaskStatus.Draft);
        var now = new DateTime(2026, 10, 15, 0, 2, 0);

        task.SetPlannedStart(Today, new TimeOnly(8, 0), now);

        task.PlannedSetupStartTime.Should().Be(new TimeOnly(8, 0));
    }

    [Fact]
    public void SetPlannedStart_ClearTimeOnPastDate_Allowed()
    {
        var task = NewTask(EntityTaskStatus.Issued);
        task.PlannedDate = Today.AddDays(-1);
        task.PlannedSetupStartTime = new TimeOnly(8, 0);

        task.SetPlannedStart(Today.AddDays(-1), null, Now);

        task.PlannedSetupStartTime.Should().BeNull();
    }
}
```

- [ ] **Step 2: Убедиться, что падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftTaskPlanningTests`
Expected: ошибка компиляции — `SetPlannedStart` не найден.

- [ ] **Step 3: Доменный метод**

В `ShiftTask.cs` перед `SetCycleNorms`:

```csharp
    /// <summary>
    /// PZP-13: задать плановую дату и (необязательно) время начала наладки.
    /// Менять можно до начала наладки (Draft/Issued). Проверяется только изменённое значение:
    /// дата — не раньше сегодняшней, дата+время — не раньше «сейчас − 5 минут».
    /// </summary>
    /// <param name="nowLocal">Текущее время по часам завода (считает вызывающий).</param>
    public void SetPlannedStart(DateOnly date, TimeOnly? time, DateTime nowLocal)
    {
        bool dateChanged = date != PlannedDate;
        bool timeChanged = time != PlannedSetupStartTime;
        if (!dateChanged && !timeChanged)
            return;

        if (Status is not (TaskStatus.Draft or TaskStatus.Issued))
            throw new DomainException("Плановое начало можно изменить только до начала наладки");

        if (dateChanged && date < DateOnly.FromDateTime(nowLocal))
            throw new DomainException("Плановая дата не может быть в прошлом");

        if (time is TimeOnly t && date.ToDateTime(t) < nowLocal.AddMinutes(-5))
            throw new DomainException("Время начала наладки не может быть в прошлом");

        PlannedDate = date;
        PlannedSetupStartTime = time;
    }
```

- [ ] **Step 4: Доменные тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftTaskPlanningTests`
Expected: все passed.

- [ ] **Step 5: `IFactoryClock`**

`Wintime.Control.Core/Interfaces/IFactoryClock.cs`:

```csharp
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.Interfaces;

/// <summary>Часы и расписание завода: текущее время и данные для планового начала заданий.</summary>
public interface IFactoryClock
{
    DateTime UtcNow { get; }

    /// <summary>Пояс завода и начало самой ранней смены. Кешируется на время запроса (scoped).</summary>
    Task<FactorySchedule> GetScheduleAsync(CancellationToken ct = default);
}
```

`Wintime.Control.Infrastructure/Services/FactoryClock.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Reports;

namespace Wintime.Control.Infrastructure.Services;

public class FactoryClock : IFactoryClock
{
    private readonly ControlDbContext _context;
    private FactorySchedule? _schedule;

    public FactoryClock(ControlDbContext context) => _context = context;

    public DateTime UtcNow => DateTime.UtcNow;

    public async Task<FactorySchedule> GetScheduleAsync(CancellationToken ct = default)
    {
        if (_schedule != null)
            return _schedule;

        var first = await _context.Shifts
            .OrderBy(s => s.StartMinutes)
            .Select(s => new { s.StartMinutes, s.TimeZoneId })
            .FirstOrDefaultAsync(ct);

        var tz = TimeZoneInfo.FindSystemTimeZoneById(first?.TimeZoneId ?? FactoryCalendar.DefaultTimeZoneId);
        return _schedule = new FactorySchedule(tz, first?.StartMinutes);
    }
}
```

`Program.cs`: рядом с `builder.Services.AddScoped<IShiftService, ShiftService>();` добавить

```csharp
builder.Services.AddScoped<IFactoryClock, FactoryClock>();
```

- [ ] **Step 6: `PlannedStartAt` в DTO**

`TaskDto.cs`, после `PlannedSetupStartTime`:

```csharp
    /// <summary>Эффективное плановое начало, UTC (PlannedTaskStart). Вычисляется, не хранится.</summary>
    public DateTime PlannedStartAt { get; set; }
```

`TaskMappingExtensions.cs`: сигнатура → `public static TaskDto ToDto(this ShiftTask t, FactorySchedule schedule) => new()`. Добавить `using Wintime.Control.Core.Policies;` и строку

```csharp
        PlannedStartAt = schedule.PlannedStartUtc(t.PlannedDate, t.PlannedSetupStartTime),
```

- [ ] **Step 7: Контроллер**

`TasksController`: поле `private readonly IFactoryClock _factoryClock;`, параметр конструктора `IFactoryClock factoryClock`, присваивание.

Во всех методах с `.ToDto()` (`GetTaskList`, `GetMyTasks`, `GetTaskById`, `CreateTask`) получить `var schedule = await _factoryClock.GetScheduleAsync();` и вызывать `.ToDto(schedule)`.

`CreateTask`: в инициализаторе **убрать** строки `PlannedDate = ...` и `PlannedSetupStartTime = ...` из Task 2. Сразу после инициализатора (перед `SetCycleNorms`):

```csharp
        var schedule = await _factoryClock.GetScheduleAsync();
        task.SetPlannedStart(request.PlannedDate.Value, request.PlannedSetupStartTime,
                             schedule.ToLocal(_factoryClock.UtcNow));
```

`UpdateTask`: заменить временный блок из Task 2 на

```csharp
        bool planTouched = request.PlannedDate.HasValue
                        || request.PlannedSetupStartTime.HasValue
                        || request.ClearPlannedSetupStartTime;
        if (planTouched)
        {
            var schedule = await _factoryClock.GetScheduleAsync();
            var date = request.PlannedDate ?? task.PlannedDate;
            var time = request.ClearPlannedSetupStartTime
                ? null
                : request.PlannedSetupStartTime ?? task.PlannedSetupStartTime;
            task.SetPlannedStart(date, time, schedule.ToLocal(_factoryClock.UtcNow));
        }
```

`DomainException` превращается в 400 глобальным хендлером (ADR-0003).

- [ ] **Step 8: Интеграционные тесты UC-1…UC-3**

`Wintime.Control.Tests.Integration/Tasks/TaskPlannedStartApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskPlannedStartApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    private readonly IntegrationTestFactory _factory;
    public TaskPlannedStartApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private object Body(DateOnly? date, string? time) => new
    {
        immId = _factory.TestImmId,
        moldId = _factory.TestMoldId,
        planQuantity = 10,
        plannedFullCycleSeconds = 30,
        plannedDate = date,
        plannedSetupStartTime = time
    };

    private async Task<DateTime> ExpectedStartAsync(DateOnly date, TimeOnly? time)
    {
        using var scope = _factory.Services.CreateScope();
        var schedule = await scope.ServiceProvider.GetRequiredService<IFactoryClock>().GetScheduleAsync();
        return schedule.PlannedStartUtc(date, time);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage resp) =>
        await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

    [Fact]
    public async Task CreateTask_WithDateOnly_PlannedStartIsFirstShiftStart()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", Body(Tomorrow, null));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync(resp);
        body.GetProperty("plannedDate").GetString().Should().Be(Tomorrow.ToString("yyyy-MM-dd"));
        body.GetProperty("plannedSetupStartTime").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("plannedStartAt").GetDateTime().ToUniversalTime()
            .Should().Be(await ExpectedStartAsync(Tomorrow, null));
        body.GetProperty("issuedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CreateTask_WithTime_PlannedStartIsDatePlusTime()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", Body(Tomorrow, "14:00:00"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync(resp);
        body.GetProperty("plannedSetupStartTime").GetString().Should().Be("14:00:00");
        body.GetProperty("plannedStartAt").GetDateTime().ToUniversalTime()
            .Should().Be(await ExpectedStartAsync(Tomorrow, new TimeOnly(14, 0)));
    }

    [Fact]
    public async Task CreateTask_WithoutPlannedDate_Returns400()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", Body(null, null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTask_PastDate_Returns400()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", Body(Tomorrow.AddDays(-3), null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> SeedTaskAsync(TaskStatus status, DateOnly date, TimeOnly? time = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId, PlanQuantity = 10,
            Status = status, PlannedDate = date, PlannedSetupStartTime = time, PlannedFullCycleSeconds = 30
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    [Fact]
    public async Task UpdateTask_IssuedTask_ChangesPlannedDate()
    {
        var id = await SeedTaskAsync(TaskStatus.Issued, Tomorrow);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}",
            new { plannedDate = Tomorrow.AddDays(2), plannedSetupStartTime = "08:30:00" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);
        after.GetProperty("plannedDate").GetString().Should().Be(Tomorrow.AddDays(2).ToString("yyyy-MM-dd"));
        after.GetProperty("plannedSetupStartTime").GetString().Should().Be("08:30:00");
    }

    [Fact]
    public async Task UpdateTask_ClearTime_SetsNull()
    {
        var id = await SeedTaskAsync(TaskStatus.Draft, Tomorrow, new TimeOnly(9, 0));
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { clearPlannedSetupStartTime = true });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);
        after.GetProperty("plannedSetupStartTime").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task UpdateTask_InProgress_ChangePlan_Returns400()
    {
        var id = await SeedTaskAsync(TaskStatus.InProgress, Tomorrow);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { plannedDate = Tomorrow.AddDays(1) });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTask_UnchangedPastDate_Succeeds()
    {
        var past = Tomorrow.AddDays(-5);
        var id = await SeedTaskAsync(TaskStatus.Issued, past);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { plannedDate = past, note = "правка" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
```

- [ ] **Step 9: Прогон**

Run: `dotnet test Wintime.Control.Tests.Unit; dotnet test Wintime.Control.Tests.Integration`
Expected: всё зелёное.

- [ ] **Step 10: Commit**

```bash
git add -A Wintime.Control.Core Wintime.Control.Infrastructure Wintime.Control.API Wintime.Control.Tests.Unit Wintime.Control.Tests.Integration
git commit -m "feat(tasks): planned start rules, IFactoryClock, PlannedStartAt in TaskDto"
```

---

### Task 4: Наладчик — назначение, переназначение, обязателен при выдаче

**Files:**
- Modify:
  - `Wintime.Control.Core/Entities/ShiftTask.cs`;
  - `Wintime.Control.API/Controllers/TasksController.cs`;
  - `Wintime.Control.Tests.Unit/Entities/ShiftTaskStateMachineTests.cs`;
  - `Wintime.Control.Tests.Unit/Entities/ShiftTaskPlanningTests.cs`;
  - `Wintime.Control.Tests.Integration/Infrastructure/IntegrationTestFactory.cs`;
  - `Wintime.Control.Tests.Integration/Tasks/TasksControllerTests.cs`.
- Create:
  - `Wintime.Control.Core/DTOs/Tasks/SetTaskPersonnelRequestDto.cs`;
  - `Wintime.Control.Core/DTOs/Tasks/IssueTaskRequestDto.cs`;
  - `Wintime.Control.Tests.Integration/Tasks/TaskPersonnelApiTests.cs`.

**Interfaces:**
- Produces:
  - `ShiftTask.AssignPersonnel(string? personnelId)`; `ShiftTask.Issue()` бросает «Назначьте наладчика» без `PersonnelId`;
  - `PUT /api/tasks/{id}/personnel` `{ personnelId: string|null }` → 204;
  - `POST /api/tasks/{id}/issue` с телом `{ personnelId?: string }`;
  - `IntegrationTestFactory.TestAdjusterId : string`.

- [ ] **Step 1: Падающие доменные тесты**

В `ShiftTaskPlanningTests.cs` добавить:

```csharp
    [Fact]
    public void AssignPersonnel_Draft_AssignsAndClears()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        task.AssignPersonnel("adj-1");
        task.PersonnelId.Should().Be("adj-1");

        task.AssignPersonnel(null);
        task.PersonnelId.Should().BeNull();
    }

    [Fact]
    public void AssignPersonnel_Issued_Reassigns()
    {
        var task = NewTask(EntityTaskStatus.Issued);
        task.PersonnelId = "adj-1";

        task.AssignPersonnel("adj-2");

        task.PersonnelId.Should().Be("adj-2");
    }

    [Fact]
    public void AssignPersonnel_Issued_Clear_Throws()
    {
        var task = NewTask(EntityTaskStatus.Issued);
        task.PersonnelId = "adj-1";

        var act = () => task.AssignPersonnel(null);

        act.Should().Throw<DomainException>().WithMessage("У выданного задания должен быть наладчик");
    }

    [Theory]
    [InlineData(EntityTaskStatus.Setup)]
    [InlineData(EntityTaskStatus.InProgress)]
    [InlineData(EntityTaskStatus.Completed)]
    [InlineData(EntityTaskStatus.Closed)]
    public void AssignPersonnel_AfterSetupStarted_Throws(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.AssignPersonnel("adj-2");

        act.Should().Throw<DomainException>().WithMessage("Наладчика можно изменить только до начала наладки");
    }

    [Fact]
    public void Issue_WithoutPersonnel_Throws()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        var act = () => task.Issue();

        act.Should().Throw<DomainException>().WithMessage("Назначьте наладчика");
        task.Status.Should().Be(EntityTaskStatus.Draft);
    }
```

В `ShiftTaskStateMachineTests.NewTask` назначить наладчика, чтобы переходы выдачи оставались валидными:

```csharp
    private static ShiftTask NewTask(EntityTaskStatus status, int plan = 100) =>
        new() { Status = status, PlanQuantity = plan, PersonnelId = "adj-1" };
```

- [ ] **Step 2: Убедиться, что падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftTask`
Expected: ошибка компиляции (`AssignPersonnel` не найден).

- [ ] **Step 3: Домен**

В `ShiftTask.cs` заменить `Issue()`:

```csharp
    /// <summary>Выдать задание: Draft → Issued. Без наладчика выдать нельзя (PZP-13).</summary>
    public void Issue()
    {
        EnsureStatus(TaskStatus.Draft, "Задание не в статусе «Планируется»");
        if (string.IsNullOrEmpty(PersonnelId))
            throw new DomainException("Назначьте наладчика");
        Status = TaskStatus.Issued;
        IssuedAt = DateTime.UtcNow;
    }
```

После `Issue()` добавить:

```csharp
    /// <summary>
    /// PZP-13: назначить/снять наладчика. «Планируется» — можно снять; «Выдано» — только
    /// переназначить (снять нельзя); после начала наладки — запрещено.
    /// Существование и активность наладчика проверяет вызывающий.
    /// </summary>
    public void AssignPersonnel(string? personnelId)
    {
        var id = string.IsNullOrWhiteSpace(personnelId) ? null : personnelId;
        switch (Status)
        {
            case TaskStatus.Draft:
                break;
            case TaskStatus.Issued when id is null:
                throw new DomainException("У выданного задания должен быть наладчик");
            case TaskStatus.Issued:
                break;
            default:
                throw new DomainException("Наладчика можно изменить только до начала наладки");
        }
        PersonnelId = id;
    }
```

- [ ] **Step 4: Доменные тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftTask`
Expected: все passed.

- [ ] **Step 5: DTO и контроллер**

`SetTaskPersonnelRequestDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>Назначить наладчика; <c>null</c> — снять (только в «Планируется»).</summary>
public class SetTaskPersonnelRequestDto
{
    public string? PersonnelId { get; set; }
}
```

`IssueTaskRequestDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>Выдача задания; <see cref="PersonnelId"/> — назначить наладчика в той же операции.</summary>
public class IssueTaskRequestDto
{
    public string? PersonnelId { get; set; }
}
```

В `TasksController` добавить приватный хелпер (рядом с `ResolveOrderForBindingAsync`):

```csharp
    /// <summary>Наладчик существует, активен и имеет роль Adjuster (PZP-13).</summary>
    private Task<bool> IsActiveAdjusterAsync(string personnelId) =>
        _context.Users.AnyAsync(u => u.Id == personnelId && u.IsActive && u.Role == UserRole.Adjuster);
```

`CreateTask`, перед `_context.ShiftTasks.Add(task)`:

```csharp
        if (!string.IsNullOrWhiteSpace(request.PersonnelId) && !await IsActiveAdjusterAsync(request.PersonnelId))
            return BadRequest("Укажите активного наладчика");
```

и в инициализаторе: `PersonnelId = string.IsNullOrWhiteSpace(request.PersonnelId) ? null : request.PersonnelId,`.

Новый endpoint после `UpdateTask`:

```csharp
    /// <summary>Назначить, переназначить или снять наладчика (PZP-13).</summary>
    [HttpPut("{id:guid}/personnel")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> SetPersonnel(Guid id, [FromBody] SetTaskPersonnelRequestDto request)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.PersonnelId) && !await IsActiveAdjusterAsync(request.PersonnelId))
            return BadRequest("Укажите активного наладчика");

        task.AssignPersonnel(request.PersonnelId);
        await _context.SaveChangesAsync();
        return NoContent();
    }
```

`IssueTask`: сигнатура и тело:

```csharp
    public async Task<IActionResult> IssueTask(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]
        IssueTaskRequestDto? request)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request?.PersonnelId))
        {
            if (!await IsActiveAdjusterAsync(request.PersonnelId))
                return BadRequest("Укажите активного наладчика");
            task.AssignPersonnel(request.PersonnelId);
        }

        task.Issue();

        await _context.SaveChangesAsync();

        return Ok(new { message = "Задание выдано" });
    }
```

- [ ] **Step 6: Тестовая инфраструктура и существующие тесты**

`IntegrationTestFactory`:
- свойство `public string TestAdjusterId { get; private set; } = string.Empty;`;
- в конце `SeedTestUsersAsync`:

```csharp
        TestAdjusterId = (await userManager.FindByNameAsync("test_adjuster"))!.Id;
```

`TasksControllerTests`:
- в `MakeCreateRequest` добавить `personnelId = _factory.TestAdjusterId,`;
- хелпер `IssueTaskAsync` → `var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/issue", new { });`.

Найти остальные выдачи: `grep -rn "/issue" Wintime.Control.Tests.Integration --include=*.cs`. Каждой выдаче нужен наладчик в задании или в теле.

- [ ] **Step 7: Интеграционные тесты UC-4, UC-5**

`Wintime.Control.Tests.Integration/Tasks/TaskPersonnelApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskPersonnelApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IntegrationTestFactory _factory;
    public TaskPersonnelApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedTaskAsync(TaskStatus status, string? personnelId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId, PlanQuantity = 10,
            Status = status, PersonnelId = personnelId, PlannedFullCycleSeconds = 30,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task<string> CreateUserAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var name = $"u_{Guid.NewGuid():N}";
        var user = new User { UserName = name, Email = $"{name}@test.local", FullName = name, Role = role, IsActive = true };
        (await um.CreateAsync(user, "Passw0rd!")).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task<JsonElement> GetAsync(HttpClient c, Guid id) =>
        await c.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);

    [Fact]
    public async Task SetPersonnel_Draft_AssignsAndClears()
    {
        var id = await SeedTaskAsync(TaskStatus.Draft, null);
        var client = await ManagerClientAsync();

        (await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = _factory.TestAdjusterId }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("personnelId").GetString().Should().Be(_factory.TestAdjusterId);

        (await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("personnelId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task SetPersonnel_Issued_Reassigns()
    {
        var other = await CreateUserAsync(UserRole.Adjuster);
        var id = await SeedTaskAsync(TaskStatus.Issued, _factory.TestAdjusterId);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = other });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("personnelId").GetString().Should().Be(other);
    }

    [Fact]
    public async Task SetPersonnel_Issued_Clear_Returns400()
    {
        var id = await SeedTaskAsync(TaskStatus.Issued, _factory.TestAdjusterId);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetPersonnel_InProgress_Returns400()
    {
        var other = await CreateUserAsync(UserRole.Adjuster); // валидный наладчик — отказ именно из-за статуса
        var id = await SeedTaskAsync(TaskStatus.InProgress, _factory.TestAdjusterId);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = other });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetPersonnel_NotAdjuster_Returns400()
    {
        var manager = await CreateUserAsync(UserRole.Manager);
        var id = await SeedTaskAsync(TaskStatus.Draft, null);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = manager });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IssueTask_WithPersonnelInBody_AssignsAndIssues()
    {
        var id = await SeedTaskAsync(TaskStatus.Draft, null);
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync($"/api/tasks/{id}/issue", new { personnelId = _factory.TestAdjusterId });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await GetAsync(client, id);
        after.GetProperty("status").GetString().Should().Be("Issued");
        after.GetProperty("personnelId").GetString().Should().Be(_factory.TestAdjusterId);
    }

    [Fact]
    public async Task IssueTask_AlreadyAssigned_NoBody_Issues()
    {
        var id = await SeedTaskAsync(TaskStatus.Draft, _factory.TestAdjusterId);
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync($"/api/tasks/{id}/issue", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IssueTask_NoPersonnel_Returns400()
    {
        var id = await SeedTaskAsync(TaskStatus.Draft, null);
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync($"/api/tasks/{id}/issue", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("Draft");
    }
}
```

- [ ] **Step 8: Прогон**

Run: `dotnet test Wintime.Control.Tests.Unit; dotnet test Wintime.Control.Tests.Integration`
Expected: всё зелёное.

- [ ] **Step 9: Commit**

```bash
git add -A Wintime.Control.Core Wintime.Control.API Wintime.Control.Tests.Unit Wintime.Control.Tests.Integration
git commit -m "feat(tasks): adjuster required to issue; assign/reassign personnel endpoint"
```

---

### Task 5: `GET /api/tasks/my` — разделы «В работе / Очередь / Архив»

**Files:**
- Modify:
  - `Wintime.Control.Core/DTOs/Tasks/MyTasksDto.cs`;
  - `Wintime.Control.API/Controllers/TasksController.cs` (`GetMyTasks`).
- Test: `Wintime.Control.Tests.Integration/Tasks/MyTasksApiTests.cs`.

**Interfaces:**
- Produces: `MyTasksDto { List<TaskDto> Active; List<TaskDto> Queue; PagedTasksDto Archive }`. JSON: `active`, `queue`, `archive`. Параметр `boundary` удалён.

- [ ] **Step 1: Падающие тесты**

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class MyTasksApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
    private readonly IntegrationTestFactory _factory;
    public MyTasksApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> AdjusterClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    // Каждый тест — на своём ТПА; search по имени ТПА изолирует тесты в общей БД класса.
    private async Task<(Guid immId, string search)> FreshImmAsync()
    {
        var immId = await _factory.CreateFreshImmAsync();
        return (immId, immId.ToString("N"));
    }

    private async Task<Guid> SeedAsync(Guid immId, TaskStatus status, DateOnly date, TimeOnly? time = null,
                                       DateTime? completedAt = null, string? personnelId = "self")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 10, Status = status,
            PersonnelId = personnelId == "self" ? _factory.TestAdjusterId : personnelId,
            PlannedDate = date, PlannedSetupStartTime = time, PlannedFullCycleSeconds = 30,
            IssuedAt = status == TaskStatus.Draft ? null : DateTime.UtcNow.AddDays(-20),
            CompletedAt = completedAt
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task<JsonElement> GetMyAsync(string search) =>
        await (await AdjusterClientAsync())
            .GetFromJsonAsync<JsonElement>($"/api/tasks/my?search={search}", JsonOptions);

    private static List<string> Ids(JsonElement arr) =>
        arr.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();

    [Fact]
    public async Task GetMyTasks_QueueSortedByPlannedStart()
    {
        var (imm, search) = await FreshImmAsync();
        var late  = await SeedAsync(imm, TaskStatus.Issued, Tomorrow.AddDays(5));
        var early = await SeedAsync(imm, TaskStatus.Issued, Tomorrow, new TimeOnly(6, 0));
        var mid   = await SeedAsync(imm, TaskStatus.Issued, Tomorrow, new TimeOnly(15, 0));

        var my = await GetMyAsync(search);

        Ids(my.GetProperty("queue")).Should().Equal(early.ToString(), mid.ToString(), late.ToString());
    }

    [Fact]
    public async Task GetMyTasks_ActiveSectionHasSetupAndInProgress()
    {
        var (imm, search) = await FreshImmAsync();
        var setup = await SeedAsync(imm, TaskStatus.Setup, Tomorrow);
        var work  = await SeedAsync(imm, TaskStatus.InProgress, Tomorrow);
        await SeedAsync(imm, TaskStatus.Issued, Tomorrow);

        var my = await GetMyAsync(search);

        Ids(my.GetProperty("active")).Should().BeEquivalentTo(new[] { setup.ToString(), work.ToString() });
        my.GetProperty("queue").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetMyTasks_ArchivePagedNewestFirst()
    {
        var (imm, search) = await FreshImmAsync();
        var older = await SeedAsync(imm, TaskStatus.Completed, Tomorrow.AddDays(-10), completedAt: DateTime.UtcNow.AddDays(-3));
        var newer = await SeedAsync(imm, TaskStatus.Completed, Tomorrow.AddDays(-10), completedAt: DateTime.UtcNow.AddDays(-1));

        var my = await GetMyAsync(search);

        var archive = my.GetProperty("archive");
        archive.GetProperty("total").GetInt32().Should().Be(2);
        Ids(archive.GetProperty("items")).Should().Equal(newer.ToString(), older.ToString());
    }

    [Fact]
    public async Task GetMyTasks_DraftHidden()
    {
        var (imm, search) = await FreshImmAsync();
        await SeedAsync(imm, TaskStatus.Draft, Tomorrow);

        var my = await GetMyAsync(search);

        my.GetProperty("active").GetArrayLength().Should().Be(0);
        my.GetProperty("queue").GetArrayLength().Should().Be(0);
        my.GetProperty("archive").GetProperty("total").GetInt32().Should().Be(0);
    }
}
```

- [ ] **Step 2: Убедиться, что падают**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~MyTasksApiTests`
Expected: FAIL — в ответе нет свойства `queue`/`active` (`KeyNotFoundException`).

- [ ] **Step 3: DTO**

`MyTasksDto.cs` — заменить класс `MyTasksDto` (класс `PagedTasksDto` не трогать):

```csharp
/// <summary>
/// Задания наладчика по разделам планшета (PZP-13):
/// <see cref="Active"/> — в наладке и в работе; <see cref="Queue"/> — выданные, по возрастанию
/// планового начала; <see cref="Archive"/> — завершённые/закрытые, новые сверху, с пагинацией.
/// </summary>
public class MyTasksDto
{
    public List<TaskDto> Active { get; set; } = new();
    public List<TaskDto> Queue { get; set; } = new();
    public PagedTasksDto Archive { get; set; } = new();
}
```

- [ ] **Step 4: Контроллер**

`GetMyTasks`:
- удалить параметр `boundary`, его разбор и XML-комментарий `<param name="boundary">`;
- обновить `<summary>`: «Мои задания — для текущего наладчика (по JWT): в работе, очередь по плановому началу, архив»;
- `baseQuery` и фильтр `search` оставить как есть;
- всё начиная с `// Текущая смена` и до `return` заменить на:

```csharp
        var schedule = await _factoryClock.GetScheduleAsync();

        var active = await baseQuery
            .Where(t => t.Status == Core.Enums.TaskStatus.Setup
                     || t.Status == Core.Enums.TaskStatus.InProgress)
            .OrderBy(t => t.SetupStartedAt)
            .ToListAsync();

        // Плановое начало вычисляется (PlannedTaskStart), поэтому сортируем в памяти:
        // выданных заданий у одного наладчика — единицы.
        var queue = (await baseQuery
                .Where(t => t.Status == Core.Enums.TaskStatus.Issued)
                .ToListAsync())
            .Select(t => t.ToDto(schedule))
            .OrderBy(d => d.PlannedStartAt)
            .ToList();

        var archiveQuery = baseQuery.Where(t => t.Status == Core.Enums.TaskStatus.Completed
                                             || t.Status == Core.Enums.TaskStatus.Closed);
        var archiveTotal = await archiveQuery.CountAsync();
        var archiveItems = await archiveQuery
            .OrderByDescending(t => t.CompletedAt ?? t.ClosedAt)
            .Skip((archivePage - 1) * archivePageSize)
            .Take(archivePageSize)
            .ToListAsync();

        return Ok(new MyTasksDto
        {
            Active = active.Select(t => t.ToDto(schedule)).ToList(),
            Queue = queue,
            Archive = new PagedTasksDto
            {
                Items = archiveItems.Select(t => t.ToDto(schedule)).ToList(),
                Total = archiveTotal,
                Page = archivePage,
                PageSize = archivePageSize
            }
        });
```

- [ ] **Step 5: Тесты проходят + полный прогон**

Run: `dotnet test Wintime.Control.Tests.Integration`
Expected: всё зелёное.

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Core/DTOs/Tasks/MyTasksDto.cs Wintime.Control.API/Controllers/TasksController.cs Wintime.Control.Tests.Integration/Tasks/MyTasksApiTests.cs
git commit -m "feat(tasks): adjuster tasks grouped as active / queue by planned start / archive"
```

---

### Task 6: Фронт — утилиты планового начала, «Планируется», список заданий

**Files:**
- Create: `Wintime-Control-Frontend/src/utils/plannedStart.js`, `src/utils/__tests__/plannedStart.spec.js`, `src/stores/__tests__/tasks.spec.js`.
- Modify:
  - `src/api/tasks.js`;
  - `src/stores/tasks.js`;
  - `src/constants/taskStatus.js`;
  - `src/components/tasks/TaskStatusBadge.vue`, `src/components/tasks/TaskFilters.vue`;
  - `src/components/mobile/MobileStatusBadge.vue`;
  - `src/views/tasks/TasksView.vue`, `src/views/tasks/TaskDetailModal.vue`.

**Interfaces:**
- Produces:
  - `formatPlannedStart(task) → string` («15.10» / «15.10 14:00»);
  - `isPlannedStartOverdue(task, now = new Date()) → boolean`;
  - `isPlannedStartToday(task, now = new Date()) → boolean`;
  - `tasksApi.setPersonnel(id, personnelId)`;
  - `tasksApi.issue(id, personnelId)`;
  - `tasksStore.issueTask(id, personnelId)`.

- [ ] **Step 1: Падающие тесты утилит**

`src/utils/__tests__/plannedStart.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import { formatPlannedStart, isPlannedStartOverdue, isPlannedStartToday } from '../plannedStart'

describe('plannedStart', () => {
  it('formatPlannedStart: только дата', () => {
    expect(formatPlannedStart({ plannedDate: '2026-10-15', plannedSetupStartTime: null })).toBe('15.10')
  })

  it('formatPlannedStart: дата и время без секунд', () => {
    expect(formatPlannedStart({ plannedDate: '2026-10-15', plannedSetupStartTime: '14:00:00' })).toBe('15.10 14:00')
  })

  it('formatPlannedStart: нет даты → прочерк', () => {
    expect(formatPlannedStart({})).toBe('—')
  })

  it('isPlannedStartOverdue: выдано и плановое начало прошло', () => {
    const now = new Date('2026-10-15T12:00:00Z')
    expect(isPlannedStartOverdue({ status: 'Issued', plannedStartAt: '2026-10-15T11:00:00Z' }, now)).toBe(true)
    expect(isPlannedStartOverdue({ status: 'Draft', plannedStartAt: '2026-10-15T11:00:00Z' }, now)).toBe(true)
  })

  it('isPlannedStartOverdue: в работе — не просрочено', () => {
    const now = new Date('2026-10-15T12:00:00Z')
    expect(isPlannedStartOverdue({ status: 'InProgress', plannedStartAt: '2026-10-15T11:00:00Z' }, now)).toBe(false)
  })

  it('isPlannedStartOverdue: начало впереди — не просрочено', () => {
    const now = new Date('2026-10-15T12:00:00Z')
    expect(isPlannedStartOverdue({ status: 'Issued', plannedStartAt: '2026-10-15T13:00:00Z' }, now)).toBe(false)
  })

  it('isPlannedStartToday: та же локальная дата', () => {
    const now = new Date(2026, 9, 15, 9, 0)
    expect(isPlannedStartToday({ plannedStartAt: new Date(2026, 9, 15, 18, 0).toISOString() }, now)).toBe(true)
    expect(isPlannedStartToday({ plannedStartAt: new Date(2026, 9, 16, 8, 0).toISOString() }, now)).toBe(false)
  })
})
```

- [ ] **Step 2: Убедиться, что падают**

Run: `cd Wintime-Control-Frontend; npx vitest run src/utils/__tests__/plannedStart.spec.js`
Expected: FAIL — модуль `../plannedStart` не найден.

- [ ] **Step 3: Утилиты**

`src/utils/plannedStart.js`:

```js
// PZP-13: плановое начало задания.
// plannedDate ('YYYY-MM-DD') и plannedSetupStartTime ('HH:mm:ss') показываются как ввёл
// менеджер — по часам завода. plannedStartAt (UTC, вычислен сервером) — для сравнения с «сейчас».

const NOT_STARTED = new Set(['Draft', 'Issued'])

export function formatPlannedStart(task) {
  if (!task?.plannedDate) return '—'
  const [, month, day] = task.plannedDate.split('-')
  const date = `${day}.${month}`
  return task.plannedSetupStartTime ? `${date} ${task.plannedSetupStartTime.slice(0, 5)}` : date
}

export function isPlannedStartOverdue(task, now = new Date()) {
  if (!NOT_STARTED.has(task?.status) || !task.plannedStartAt) return false
  return now.getTime() > new Date(task.plannedStartAt).getTime()
}

export function isPlannedStartToday(task, now = new Date()) {
  if (!task?.plannedStartAt) return false
  const start = new Date(task.plannedStartAt)
  return start.getFullYear() === now.getFullYear()
    && start.getMonth() === now.getMonth()
    && start.getDate() === now.getDate()
}
```

- [ ] **Step 4: Тесты утилит проходят**

Run: `npx vitest run src/utils/__tests__/plannedStart.spec.js`
Expected: 7 passed.

- [ ] **Step 5: Падающий тест стора**

`src/stores/__tests__/tasks.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useTasksStore } from '../tasks'

vi.mock('@/api/tasks', () => ({
  tasksApi: { getList: vi.fn(), issue: vi.fn(() => Promise.resolve({ data: {} })) }
}))

import { tasksApi } from '@/api/tasks'

describe('tasks store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-10-15T12:00:00Z'))
  })

  it('overdueTasks — не начатые задания с прошедшим плановым началом', () => {
    const store = useTasksStore()
    store.tasks = [
      { id: 'a', status: 'Issued', plannedStartAt: '2026-10-15T11:00:00Z' },
      { id: 'b', status: 'Draft', plannedStartAt: '2026-10-15T13:00:00Z' },
      { id: 'c', status: 'InProgress', plannedStartAt: '2026-10-14T05:00:00Z' }
    ]
    expect(store.overdueTasks.map(t => t.id)).toEqual(['a'])
  })

  it('issueTask передаёт наладчика в API', async () => {
    tasksApi.getList.mockResolvedValue({ data: [] })
    const store = useTasksStore()
    await store.issueTask('t1', 'adj-1')
    expect(tasksApi.issue).toHaveBeenCalledWith('t1', 'adj-1')
  })
})
```

Run: `npx vitest run src/stores/__tests__/tasks.spec.js`
Expected: FAIL (просрочка считается по-старому, `issue` вызывается без наладчика).

- [ ] **Step 6: API и стор**

`src/api/tasks.js` — заменить `issue(id)`, добавить `setPersonnel`:

```js
  // Выдать задание (Draft → Issued); personnelId — назначить наладчика в той же операции
  issue(id, personnelId) {
    return apiClient.post(`/tasks/${id}/issue`, { personnelId: personnelId ?? null })
  },

  // Назначить/переназначить/снять наладчика (null — снять, только в «Планируется»)
  setPersonnel(id, personnelId) {
    return apiClient.put(`/tasks/${id}/personnel`, { personnelId: personnelId || null })
  },
```

`src/stores/tasks.js`:
- добавить `import { isPlannedStartOverdue } from '@/utils/plannedStart'`;
- геттер `overdueTasks` заменить на

```js
    // Просрочено: задание не начато, а плановое начало прошло (PZP-13)
    overdueTasks: (state) => state.tasks.filter(t => isPlannedStartOverdue(t)),
```

- `issueTask(id)` → `issueTask(id, personnelId)`, вызов `await tasksApi.issue(id, personnelId)`.

Run: `npx vitest run src/stores/__tests__/tasks.spec.js`
Expected: 2 passed.

- [ ] **Step 7: «Черновик» → «Планируется»**

Заменить подписи (значение `'Draft'` не трогать):
- `src/constants/taskStatus.js`: `[TASK_STATUS.DRAFT]: 'Планируется',`;
- `src/components/tasks/TaskStatusBadge.vue`, `src/components/mobile/MobileStatusBadge.vue`: `label: 'Черновик'` → `label: 'Планируется'`;
- `src/components/tasks/TaskFilters.vue`: `<el-option label="Черновик" value="Draft" />` → `<el-option label="Планируется" value="Draft" />`;
- `src/views/tasks/TasksView.vue`: подпись карточки `Черновики` → `Планируются`.

Проверка: `grep -rn "Черновик" src --include=*.vue --include=*.js | grep -v __tests__` — пусто. Совпадения в тестах заменить на «Планируется».

- [ ] **Step 8: Колонка «Плановое начало» и строка в деталях**

`TasksView.vue`:
- импорт `import { formatPlannedStart } from '@/utils/plannedStart'`;
- перед колонкой `prop="issuedAt"` добавить:

```vue
        <el-table-column prop="plannedStartAt" width="150">
          <template #header>
            <span class="col-header" @click="handleSort('plannedStartAt')">
              Плановое начало <span class="sort-icon">{{ sortIcon('plannedStartAt') }}</span>
            </span>
          </template>
          <template #default="{ row }">
            {{ formatPlannedStart(row) }}
          </template>
        </el-table-column>
```

Сортировка `sortedTasks` сравнивает значения поля. ISO-строки `plannedStartAt` сортируются лексикографически корректно — проверить, что компаратор обрабатывает строки (как `updatedAt`).

`TaskDetailModal.vue`:
- импорт `formatPlannedStart`;
- перед строкой «Выдано»:

```vue
        <el-descriptions-item label="Плановое начало">
          {{ task ? formatPlannedStart(task) : '—' }}
        </el-descriptions-item>
```

- [ ] **Step 9: Прогон фронтовых тестов**

Run: `npx vitest run`
Expected: зелёные, кроме тестов, завязанных на старые подписи или старый ответ `/tasks/my`. Подписи чинятся здесь, планшет — в Task 9 (`MobileTasksView.spec.js` может падать до Task 9 — допустимо, отметить в коммите).

- [ ] **Step 10: Commit**

```bash
git add Wintime-Control-Frontend/src
git commit -m "feat(tasks-ui): planned start column, overdue by planned start, Draft labelled «Планируется»"
```

---

### Task 7: Фронт — форма задания (дата, время, наладчик, правка в «Выдано»)

**Files:**
- Modify: `src/views/tasks/TaskFormModal.vue`, `src/views/tasks/TasksView.vue` (`canEditTask`), `src/views/tasks/TaskDetailModal.vue` (`canEdit`), `src/views/tasks/__tests__/TaskFormModal.spec.js`.

**Interfaces:**
- Consumes: `tasksApi.update`, `tasksApi.setPersonnel`, `tasksApi.create` (Task 6).
- Produces: в payload формы `plannedDate: 'YYYY-MM-DD'`, `plannedSetupStartTime: 'HH:mm:ss' | null`, `clearPlannedSetupStartTime: boolean` (только в update).

- [ ] **Step 1: Падающие тесты**

Добавить в `TaskFormModal.spec.js` мок `setPersonnel: vi.fn(() => Promise.resolve({ data: {} }))` в `tasksApi`. Во всех существующих объектах `task` (`plannedDate: null`) заменить на `plannedDate: '2026-10-20', plannedSetupStartTime: null`. Новый блок:

```js
describe('TaskFormModal — плановое начало и наладчик (PZP-13)', () => {
  beforeEach(() => vi.clearAllMocks())

  const draft = {
    id: 't1', status: 'Draft', immId: 'imm-1', moldId: 'mold-1', personnelId: null,
    planQuantity: 10, plannedDate: '2026-10-20', plannedSetupStartTime: '14:00:00',
    note: '', orderId: null, workMode: 'Auto', plannedFullCycleSeconds: 30, plannedInjectionCycleSeconds: null
  }

  it('без плановой даты форма не отправляется', async () => {
    const wrapper = mountModal()
    await flushPromises()
    Object.assign(wrapper.vm.form, { immId: 'imm-1', moldId: 'mold-1', planQuantity: 5, plannedFullCycleSeconds: 30, plannedDate: null })
    await wrapper.vm.handleSubmit()
    await flushPromises()
    expect(tasksApi.create).not.toHaveBeenCalled()
  })

  it('создание без времени и без наладчика проходит', async () => {
    const wrapper = mountModal()
    await flushPromises()
    Object.assign(wrapper.vm.form, { immId: 'imm-1', moldId: 'mold-1', planQuantity: 5, plannedFullCycleSeconds: 30, plannedDate: '2099-01-01' })
    await wrapper.vm.handleSubmit()
    await flushPromises()
    expect(tasksApi.create).toHaveBeenCalledWith(expect.objectContaining({
      plannedDate: '2099-01-01', plannedSetupStartTime: null, personnelId: null
    }))
  })

  it('очистка времени при правке шлёт clearPlannedSetupStartTime', async () => {
    const wrapper = mountModal({ task: draft })
    await flushPromises()
    wrapper.vm.form.plannedSetupStartTime = null
    await wrapper.vm.handleSubmit()
    await flushPromises()
    expect(tasksApi.update).toHaveBeenCalledWith('t1', expect.objectContaining({ clearPlannedSetupStartTime: true }))
  })

  it('смена наладчика при правке идёт через setPersonnel', async () => {
    const wrapper = mountModal({ task: draft })
    await flushPromises()
    wrapper.vm.form.personnelId = 'adj-9'
    await wrapper.vm.handleSubmit()
    await flushPromises()
    expect(tasksApi.setPersonnel).toHaveBeenCalledWith('t1', 'adj-9')
    expect(tasksApi.update.mock.calls[0][1]).not.toHaveProperty('personnelId')
  })

  it('в «Выдано» заблокированы все поля, кроме даты, времени и наладчика', async () => {
    const wrapper = mountModal({ task: { ...draft, status: 'Issued', personnelId: 'adj-1' } })
    await flushPromises()
    expect(wrapper.vm.isLimitedEdit).toBe(true)
    const planInput = wrapper.findAllComponents({ name: 'ElInputNumber' })[0]
    expect(planInput.props('disabled')).toBe(true)
  })
})
```

Run: `npx vitest run src/views/tasks/__tests__/TaskFormModal.spec.js`
Expected: новые тесты FAIL.

- [ ] **Step 2: Форма — поля и валидация**

В `TaskFormModal.vue`.

Блок `<el-form-item label="Плановая дата" ...>` заменить на:

```vue
      <el-form-item label="Плановая дата" prop="plannedDate">
        <el-date-picker
          v-model="form.plannedDate"
          type="date"
          placeholder="Выберите дату"
          format="DD.MM.YYYY"
          value-format="YYYY-MM-DD"
          :disabled-date="isPastDate"
          class="w-full"
        />
      </el-form-item>

      <el-form-item label="Время начала наладки" prop="plannedSetupStartTime">
        <el-time-picker
          v-model="form.plannedSetupStartTime"
          placeholder="Не задано"
          format="HH:mm"
          value-format="HH:mm:ss"
          clearable
          class="w-full"
        />
        <div class="text-xs text-gray-500 mt-1">Необязательно. Пусто — задание начинается с начала смены</div>
      </el-form-item>
```

У селекта наладчика добавить `clearable`, `placeholder="Не назначен"`.

В `rules` добавить:

```js
  plannedDate: [{ required: true, message: 'Укажите плановую дату', trigger: 'change' }],
```

В исходное состояние `form` (оба места: объявление `reactive` и `resetForm`):
- `personnelId: ''` → `personnelId: null`;
- после `plannedDate: null,` добавить `plannedSetupStartTime: null,`.

В `populateForm`:
- `plannedDate: task.plannedDate ? task.plannedDate.slice(0, 10) : null,` → `plannedDate: task.plannedDate,`;
- добавить `plannedSetupStartTime: task.plannedSetupStartTime ?? null,`;
- `personnelId: task.personnelId ?? null,`.

- [ ] **Step 3: Ограниченная правка в «Выдано»**

В `<script setup>` (без `defineExpose`: тесты читают состояние `<script setup>` через `wrapper.vm`, как уже делают с `form`/`loadMolds`):

```js
// PZP-13: в «Выдано» правятся только плановое начало и наладчик
const isLimitedEdit = computed(() => editingTask.value?.status === 'Issued')
```

Добавить `:disabled="isLimitedEdit"` на селект «Заказ» (объединить с существующим условием: `:disabled="isLimitedEdit || !!lockedOrder || !selectedProductTypeId"`), `el-input-number` «План», селект «Рабочий режим», оба `el-input-number` эталонов и textarea «Примечание». ТПА и ПФ уже `:disabled="editingTask"`.

- [ ] **Step 4: Отправка**

В `handleSubmit` ветку `if (editingTask.value)` заменить на:

```js
      if (editingTask.value) {
        // orderId и personnelId не входят в тело update — у них отдельные endpoints
        // (ordersApi.setTaskOrder, tasksApi.setPersonnel).
        const { orderId, personnelId, ...updatePayload } = form
        updatePayload.clearPlannedSetupStartTime = !form.plannedSetupStartTime
        await tasksApi.update(editingTask.value.id, updatePayload)
        if ((personnelId || null) !== (editingTask.value.personnelId || null)) {
          await tasksApi.setPersonnel(editingTask.value.id, personnelId || null)
        }
        if (form.orderId && form.orderId !== initialOrderId.value) {
          await ordersApi.setTaskOrder(editingTask.value.id, form.orderId)
        }
        ElMessage.success('Задание обновлено')
      } else {
        await tasksApi.create({ ...form, personnelId: form.personnelId || null })
        ElMessage.success('Задание создано')
      }
```

- [ ] **Step 5: Разрешить правку в «Выдано»**

- `TasksView.vue` `canEditTask`: `return ['Draft', 'Issued'].includes(task.status) && canCreate.value`.
- `TaskDetailModal.vue` `canEdit`: `return ['Draft', 'Issued'].includes(props.task.status) && authStore.isManager`.

- [ ] **Step 6: Тесты проходят**

Run: `npx vitest run src/views/tasks`
Expected: все passed.

- [ ] **Step 7: Commit**

```bash
git add Wintime-Control-Frontend/src/views/tasks
git commit -m "feat(tasks-ui): planned date + optional setup time, optional adjuster, edit in Issued"
```

---

### Task 8: Фронт — диалог выдачи с выбором наладчика

**Files:**
- Create: `src/views/tasks/IssueTaskDialog.vue`, `src/views/tasks/__tests__/IssueTaskDialog.spec.js`.
- Modify: `src/views/tasks/TasksView.vue`, `src/views/tasks/TaskDetailModal.vue`.

**Interfaces:**
- Consumes: `tasksStore.issueTask(id, personnelId)` (Task 6), `personnelApi.getList({ isActive: true, role: 'Adjuster' })`.
- Produces: `<IssueTaskDialog v-model="visible" :task="task" @issued="..." />`.

- [ ] **Step 1: Падающий тест**

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import IssueTaskDialog from '../IssueTaskDialog.vue'

vi.mock('@/api/personnel', () => ({
  personnelApi: { getList: vi.fn(() => Promise.resolve({ data: [
    { id: 'adj-1', fullName: 'Иванов', isActive: true },
    { id: 'adj-2', fullName: 'Петров', isActive: true }
  ] })) }
}))
vi.mock('@/api/tasks', () => ({
  tasksApi: { issue: vi.fn(() => Promise.resolve({ data: {} })), getList: vi.fn(() => Promise.resolve({ data: [] })) }
}))

import { tasksApi } from '@/api/tasks'

function mountDialog(task) {
  return mount(IssueTaskDialog, {
    props: { modelValue: true, task },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
}

describe('IssueTaskDialog', () => {
  beforeEach(() => { vi.clearAllMocks(); setActivePinia(createPinia()) })

  it('предзаполняет назначенного наладчика', async () => {
    const wrapper = mountDialog({ id: 't1', personnelId: 'adj-2', immName: 'ТПА-1', moldName: 'ПФ' })
    await flushPromises()
    expect(wrapper.vm.personnelId).toBe('adj-2')
  })

  it('без наладчика кнопка «Выдать» неактивна', async () => {
    const wrapper = mountDialog({ id: 't1', personnelId: null })
    await flushPromises()
    expect(wrapper.vm.canSubmit).toBe(false)
  })

  it('архивный наладчик не предзаполняется', async () => {
    const wrapper = mountDialog({ id: 't1', personnelId: 'adj-archived' })
    await flushPromises()
    expect(wrapper.vm.personnelId).toBe(null)
  })

  it('выдаёт с выбранным наладчиком и сообщает родителю', async () => {
    const wrapper = mountDialog({ id: 't1', personnelId: null })
    await flushPromises()
    wrapper.vm.personnelId = 'adj-1'
    await wrapper.vm.submit()
    await flushPromises()
    expect(tasksApi.issue).toHaveBeenCalledWith('t1', 'adj-1')
    expect(wrapper.emitted('issued')).toBeTruthy()
  })
})
```

Run: `npx vitest run src/views/tasks/__tests__/IssueTaskDialog.spec.js`
Expected: FAIL — компонент не найден.

- [ ] **Step 2: Компонент**

`src/views/tasks/IssueTaskDialog.vue`:

```vue
<template>
  <el-dialog v-model="visible" title="Выдать задание" width="420px" :close-on-click-modal="false">
    <p v-if="task" class="mb-4 text-gray-600">{{ task.immName }} / {{ task.moldName }}</p>
    <el-form label-position="top">
      <el-form-item label="Наладчик" required>
        <el-select v-model="personnelId" placeholder="Выберите наладчика" filterable class="w-full">
          <el-option v-for="p in personnel" :key="p.id" :label="p.fullName" :value="p.id" />
        </el-select>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="visible = false">Отмена</el-button>
      <el-button type="warning" :disabled="!canSubmit" :loading="loading" @click="submit">Выдать</el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { personnelApi } from '@/api/personnel'
import { useTasksStore } from '@/stores/tasks'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  task: { type: Object, default: null }
})
const emit = defineEmits(['update:modelValue', 'issued'])

const tasksStore = useTasksStore()
const personnel = ref([])
const personnelId = ref(null)
const loading = ref(false)

const visible = computed({
  get: () => props.modelValue,
  set: (v) => emit('update:modelValue', v)
})
const canSubmit = computed(() => !!personnelId.value && !loading.value)

async function open() {
  personnelId.value = null
  try {
    const res = await personnelApi.getList({ isActive: true, role: 'Adjuster' })
    personnel.value = res.data ?? []
  } catch {
    personnel.value = []
  }
  // Предзаполняем только активным наладчиком: архивного в списке нет.
  const assigned = props.task?.personnelId
  personnelId.value = personnel.value.some(p => p.id === assigned) ? assigned : null
}

watch(() => props.modelValue, (v) => { if (v) open() }, { immediate: true })

async function submit() {
  if (!props.task || !personnelId.value) return
  loading.value = true
  try {
    const result = await tasksStore.issueTask(props.task.id, personnelId.value)
    if (result?.success) {
      visible.value = false
      emit('issued')
    }
  } finally {
    loading.value = false
  }
}

defineExpose({ personnelId, canSubmit, submit })
</script>
```

- [ ] **Step 3: Тест проходит**

Run: `npx vitest run src/views/tasks/__tests__/IssueTaskDialog.spec.js`
Expected: 4 passed.

- [ ] **Step 4: Подключить в `TasksView` и `TaskDetailModal`**

`TaskDetailModal.vue`: вместо `<el-popconfirm v-if="canIssue" ...>…</el-popconfirm>` оставить

```vue
          <el-button v-if="canIssue" type="warning" @click="$emit('issue', task)">Выдать</el-button>
```

`TasksView.vue`:
- popconfirm в строке таблицы заменить на `<el-button v-if="canIssueTask(row)" size="small" type="warning" @click.stop="issueTask(row)">Выдать</el-button>`;
- импорт `import IssueTaskDialog from './IssueTaskDialog.vue'`, состояние `const issueDialogVisible = ref(false); const issuingTask = ref(null)`;
- рядом с `TaskDetailModal` разместить

```vue
    <IssueTaskDialog v-model="issueDialogVisible" :task="issuingTask" />
```

- в `issueTask(task)` убрать проверку наладчика из `checks` — наладчик выбирается в диалоге из активных:

```js
  const checks = [immApi.getById(task.immId), moldsApi.getById(task.moldId)]
```

  `const [immRes, moldRes] = results`; строку про `personnelRes` удалить;
- последние две строки (`await tasksStore.issueTask(task.id)` и закрытие детали) заменить на

```js
  detailModalVisible.value = false
  issuingTask.value = task
  issueDialogVisible.value = true
```

`tasksStore.issueTask` сам перезагружает список, поэтому `@issued` слушать не нужно.

- [ ] **Step 5: Прогон**

Run: `npx vitest run src/views/tasks`
Expected: все passed. Если `TaskDetailModal.spec.js` проверял popconfirm — обновить на клик по кнопке и `emitted('issue')`.

- [ ] **Step 6: Commit**

```bash
git add Wintime-Control-Frontend/src/views/tasks
git commit -m "feat(tasks-ui): issue dialog with mandatory adjuster selection"
```

---

### Task 9: Фронт — планшет наладчика: «В работе / Очередь / Архив»

**Files:**
- Modify: `src/stores/mobile.js`, `src/views/mobile/MobileTasksView.vue`, `src/components/mobile/MobileTaskCard.vue`, `src/views/mobile/__tests__/MobileTasksView.spec.js`.

**Interfaces:**
- Consumes: ответ `GET /tasks/my` — `{ active, queue, archive }` (Task 5); `formatPlannedStart`, `isPlannedStartOverdue`, `isPlannedStartToday` (Task 6).
- Produces: стор `activeTasks`, `queueTasks`, `archiveTasks`.

- [ ] **Step 1: Обновить мок и написать падающие тесты**

В `MobileTasksView.spec.js` мок `getMyTasks` → `{ active: [task], queue: [], archive: {...} }`. Мок `@/api/shifts` удалить. Новый блок:

```js
describe('MobileTasksView — очередь по плановому началу (PZP-13)', () => {
  beforeEach(() => { vi.clearAllMocks(); setActivePinia(createPinia()) })

  it('показывает разделы «В работе», «Очередь» и метки', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2026, 9, 15, 12, 0))
    mobileApi.getMyTasks.mockResolvedValueOnce({ data: {
      active: [task],
      queue: [
        { ...task, id: 'q1', status: 'Issued', immName: 'ТПА-2', plannedDate: '2026-10-15', plannedSetupStartTime: '08:00:00',
          plannedStartAt: new Date(2026, 9, 15, 8, 0).toISOString() },
        { ...task, id: 'q2', status: 'Issued', immName: 'ТПА-3', plannedDate: '2026-10-15', plannedSetupStartTime: '18:00:00',
          plannedStartAt: new Date(2026, 9, 15, 18, 0).toISOString() }
      ],
      archive: { items: [], total: 0, page: 1, pageSize: 20 }
    } })

    const wrapper = mount(MobileTasksView, {
      global: { plugins: [createPinia(), ElementPlus], stubs: { QrScanner: true } }
    })
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('В работе')
    expect(text).toContain('Очередь')
    expect(text).toContain('15.10 08:00')
    expect(text).toContain('опаздывает')
    expect(text).toContain('сегодня')
    expect(text).not.toContain('Текущая смена')
    expect(mobileApi.getMyTasks).toHaveBeenCalledWith(expect.not.objectContaining({ boundary: expect.anything() }))
    vi.useRealTimers()
  })
})
```

Run: `npx vitest run src/views/mobile`
Expected: FAIL.

- [ ] **Step 2: Стор**

`src/stores/mobile.js`:
- удалить импорты `shiftsApi` и `computeShiftBoundary`, поле `shiftSchedule`, action `loadShifts`;
- `currentShiftTasks`/`unfinishedTasks` → `activeTasks: []` (наладка и в работе) и `queueTasks: []` (выданные, по плановому началу);
- геттер `tasks` → `[...state.activeTasks, ...state.queueTasks, ...state.archiveTasks]`;
- геттер `setupTask` → `state.activeTasks.find(t => t.status === 'Setup') ?? null`;
- в `loadMyTasks` убрать `boundary` и заполнять так:

```js
        const response = await mobileApi.getMyTasks({
          search: this.search || undefined,
          archivePage: this.archivePage,
          archivePageSize: this.archivePageSize
        })
        const data = response.data ?? {}
        this.activeTasks = data.active ?? []
        this.queueTasks = data.queue ?? []
```

Строки для `archive*` оставить. Найти другие обращения: `grep -rn "currentShiftTasks\|unfinishedTasks\|loadShifts" src/views/mobile src/components/mobile src/stores/mobile.js` — заменить на новые имена. `computeShiftBoundary` в `constants/shift.js`: если `grep -rn computeShiftBoundary src` больше ничего не находит (кроме его тестов), удалить функцию и её тесты.

- [ ] **Step 3: Вью**

`MobileTasksView.vue`:
- раздел «Текущая смена» → «В работе» по `mobileStore.activeTasks` (`el-empty description="Нет заданий в работе"`);
- раздел «Незавершённые» → «Очередь» по `mobileStore.queueTasks`. Обычный стиль `section-title`, без `--warning`; виден всегда, `el-empty description="Очередь пуста"`;
- в `onMounted` удалить `await mobileStore.loadShifts()`.

- [ ] **Step 4: Карточка**

`MobileTaskCard.vue`, после блока «Пресс-форма»:

```vue
    <!-- Плановое начало (PZP-13) -->
    <div v-if="task.status === 'Issued'" class="mb-3 flex items-center gap-2">
      <span class="text-sm text-gray-500">Начало:</span>
      <span class="text-base font-semibold text-gray-800">{{ formatPlannedStart(task) }}</span>
      <el-tag v-if="isPlannedStartOverdue(task)" type="danger" size="small">опаздывает</el-tag>
      <el-tag v-else-if="isPlannedStartToday(task)" type="warning" size="small">сегодня</el-tag>
    </div>
```

В `<script setup>`: `import { formatPlannedStart, isPlannedStartOverdue, isPlannedStartToday } from '@/utils/plannedStart'`.

- [ ] **Step 5: Прогон всех фронтовых тестов**

Run: `npx vitest run`
Expected: всё зелёное.

- [ ] **Step 6: Commit**

```bash
git add Wintime-Control-Frontend/src
git commit -m "feat(mobile): adjuster queue ordered by planned start with late/today marks"
```

---

### Task 10: Документация, сиды, финальная проверка

**Files:**
- Create: `docs/adr/0014-planned-start-and-issue-requires-adjuster.md`.
- Modify: `CLAUDE.md`, `docs/user-guide/manager.md`, `docs/user-guide/adjuster.md`, `seed_equipment_report.sql`, `docs/adr/README.md` (если там есть список ADR).

- [ ] **Step 1: ADR-0014** (формат — как `docs/adr/0013-*.md`)

```markdown
# ADR-0014: Плановая дата и необязательное время начала наладки; наладчик обязателен при выдаче

- **Статус:** Accepted
- **Дата:** 2026-10-12
- **Задача:** PZP-13

## Контекст

Планировщик заводит задания на месяц вперёд. Пользователи решают две задачи: контроль выполнения
и простоев (точное время не нужно — мощности в избытке) и повышение загрузки ТПА через точное
планирование (нужно время начала наладки — опорная точка бара на будущем Гантте, PZP-07).
Старое `PlannedDate` (UTC-полночь, необязательное, правка только в черновике) не обслуживало ни
одну из задач. Наладчик был необязателен даже для выданного задания — такое задание не видел ни
один планшет.

## Решение

- `ShiftTask.PlannedDate` — `DateOnly`, обязательное (дата завода);
  `PlannedSetupStartTime` — `TimeOnly?` по часам завода.
- Эффективное начало не хранится: `PlannedTaskStart.Resolve` — дата + время, без времени —
  начало самой ранней смены, без смен — 00:00; пояс — пояс смен. Отдаётся как `TaskDto.PlannedStartAt`.
- План правится в «Планируется» и «Выдано»; после начала наладки заморожен (план/факт).
- Выдать задание без наладчика нельзя; при выдаче наладчик выбирается в диалоге. В «Выдано»
  наладчика можно переназначить, но не снять.
- Планшет: «В работе» / «Очередь» (по плановому началу) / «Архив» вместо разбивки по `IssuedAt`
  и границе смены. Контракт `GET /api/tasks/my` изменён, параметр `boundary` удалён.

## Альтернативы

- **Одно поле «дата+время» (`timestamptz`)** — отклонено: «время не задано» неотличимо от
  полуночи; базовым пользователям пришлось бы вводить время.
- **Хранить `PlannedStartAt`** — отклонено: устаревает при смене расписания смен; второй
  источник правды.
- **Выдача без наладчика, назначение позже** — отклонено: выданное задание без наладчика не
  видно на планшетах; назначение наладчика перед сменой — и есть момент выдачи.
- **Новое поле рядом со старым `PlannedDate`** — отклонено: два похожих источника правды;
  legacy-данных нет.

## Последствия

- Миграция переводит старые значения в дату завода (`AT TIME ZONE 'Europe/Moscow'`), пустые
  заполняет `COALESCE(PlannedDate, IssuedAt, CreatedAt)`.
- Журнал работы без задания сравнивает дату эпизода с `PlannedDate` (без fallback).
- Ввод времени — по часам завода, отображение `PlannedStartAt` — в поясе браузера; пока пояса
  совпадают, расхождение не решаем.
- Плановое окончание задания — PZP-14.
```

- [ ] **Step 2: CLAUDE.md**

В «Инварианты домена» после блока «Тип изделия» добавить:

```markdown
### Плановое начало и выдача задания

`ShiftTask.PlannedDate` (`DateOnly`, обязательна) + `PlannedSetupStartTime` (`TimeOnly?`, по часам
завода). Эффективное начало — `PlannedTaskStart.Resolve` (без времени — начало первой смены),
**не хранится**, в API — `TaskDto.PlannedStartAt`. Менять план и наладчика можно только в
`Draft`/`Issued`. Выдать задание (`Issue`) без наладчика нельзя; в `Issued` наладчика можно
переназначить, но не снять. В UI `Draft` подписан «Планируется». См. ADR-0014.
```

- [ ] **Step 3: Руководства пользователя**

`docs/user-guide/manager.md`:
- везде «Черновик» → «Планируется» (схема статусов около строки 313, таблица статусов, карточка «Черновики» → «Планируются», раздел «Изменять можно только задание в статусе…», таблица ошибок около строки 813);
- в разделе создания задания описать поля «Плановая дата» (обязательно) и «Время начала наладки» (необязательно; пусто — с начала первой смены); «Наладчик» — необязателен при планировании;
- раздел правки: в «Планируется» правятся все поля; в «Выдано» — только плановая дата, время и наладчик (переназначить можно, снять нельзя);
- раздел выдачи: «Выдать» открывает окно выбора наладчика; без наладчика выдать нельзя;
- «Просрочено» — не начатые задания, у которых прошло плановое начало;
- колонка «Плановое начало» в списке.

`docs/user-guide/adjuster.md`, таблица разделов около строки 81:

```markdown
| **В работе** | Задания, по которым вы начали наладку или идёт выпуск |
| **Очередь** | Выданные вам задания в порядке планового начала. Метка «опаздывает» — плановое начало уже прошло, «сегодня» — начало сегодня. Берите задания сверху вниз |
| **Архив заданий** | Ваши выполненные и закрытые задания. Если их много, внизу есть переход по страницам |
```

Подпись скриншота 2.1 привести к новым разделам: «раздел «В работе» с одной карточкой; раздел «Очередь» с двумя карточками и метками; начало раздела «Архив заданий»».

- [ ] **Step 4: Сид**

`seed_equipment_report.sql`, `INSERT INTO "ShiftTasks"` (около строки 212): в `VALUES` значение для `"PlannedDate"` `date_trunc('day', cur)` → `(cur AT TIME ZONE 'Europe/Moscow')::date`. Колонку `"PlannedSetupStartTime"` не указывать (NULL).

- [ ] **Step 5: Финальная проверка**

```powershell
dotnet build Wintime.Control.sln
dotnet test Wintime.Control.Tests.Unit
dotnet test Wintime.Control.Tests.Integration
cd Wintime-Control-Frontend; npx vitest run; npm run build
```

Expected: сборка без ошибок, все тесты зелёные, фронт собирается.

Ручной смоук (`dotnet run --project Wintime.Control.API` + `npm run dev`):
1. Создать задание с датой без времени и без наладчика — статус «Планируется», плановое начало «ДД.ММ».
2. «Выдать» → окно выбора наладчика → выбрать → «Выдано».
3. Отредактировать выданное — доступны только дата, время, наладчик; задать время → колонка «ДД.ММ ЧЧ:мм».
4. Войти наладчиком — задание в «Очереди» с меткой; «Начать» → задание в «В работе».

- [ ] **Step 6: Commit**

```bash
git add docs CLAUDE.md seed_equipment_report.sql
git commit -m "docs(pzp-13): ADR-0014, CLAUDE.md invariant, user guides, seed"
```

- [ ] **Step 7: PR**

```bash
git push -u origin feature/pzp-13-planned-start
gh pr create --title "PZP-13: плановая дата и время начала наладки, наладчик при выдаче" --body "<summary + test plan; закончить строкой 🤖 Generated with [Claude Code](https://claude.com/claude-code)>"
```
