# PZP-14 — Норматив наладки и буфер выпуска: план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Задание получает норматив наладки и буфер выпуска (фиксированная часть + процент от выпуска). Умолчания задаются на странице «Настройки» и копируются в новые задания. API и карточка задания показывают расчётный план: выпуск, буфер и плановое окончание.

**Architecture:** Умолчания хранятся в таблице `PlanningDefaults` из одной строки (ADR-0015), доступ — через scoped-сервис `IPlanningDefaultsService`. На `ShiftTask` три int-поля — снапшот умолчаний. Правила правки живут в доменных методах сущности (ADR-0002), пределы — в `PlanningNormLimits`. План задания не хранится: его считает чистая функция `TaskPlanCalculator` (`Core/Policies`), результат отдаётся в `TaskDto`. Фронт повторяет формулу только для живого предпросмотра в форме.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql (PostgreSQL 16), xUnit + FluentAssertions + Testcontainers; Vue 3 + Element Plus + Pinia, Vitest.

**Спека:** `docs/superpowers/specs/2026-10-12-pzp-14-setup-norm-release-buffer-design.md`

## Global Constraints

- Ветка: `feature/pzp-14-setup-norm-release-buffer` (уже создана, спека закоммичена). Прямой push в `master` запрещён — только PR.
- Единицы: `PlannedSetupMinutes`, `ReleaseBufferFixedMinutes` (и `SetupMinutes` в умолчаниях) — **минуты**; `ReleaseBufferPercent` — **целое число процентов** от плановой длительности **выпуска** (без наладки и без фиксированной части буфера).
- Пределы: минуты — 0..1440, процент — 0..100. Ноль допустим.
- Формула (бэкенд и фронт одинаково):
  - `release = ⌈planQuantity / cavities⌉ × fullCycleSeconds` секунд; если `fullCycleSeconds` пуст или `cavities ≤ 0` — `release = buffer = end = null`;
  - `buffer = fixedMinutes × 60 + ⌈release × percent / 100⌉` секунд;
  - `plannedEnd = plannedStart + setupMinutes × 60 + release + buffer`.
- Длительности в секундах — `long` (план до 10⁶ × цикл до 86 400 не влезает в `int`).
- Гнёзда для плана — текущие `Mold.Cavities`.
- Где можно править: норматив — `Draft`/`Issued`; буфер — `Draft`/`Issued`/`Setup`/`InProgress`. Неизменённое значение — no-op в любом статусе (пределы проверяются всегда).
- Права на умолчания — политика `ManagerOrAdmin`. Начальные умолчания — все 0.
- Тексты ошибок (дословно):
  - «Норматив наладки должен быть от 0 до 1440 минут»
  - «Фиксированная часть буфера должна быть от 0 до 1440 минут»
  - «Процент буфера должен быть от 0 до 100»
  - «Норматив наладки можно изменить только до начала наладки»
  - «Буфер выпуска можно изменить только до завершения выпуска»
- Все `DateTime` в EF-запросах — `Kind=Utc`.
- Команды тестов:
  - backend: `dotnet test Wintime.Control.Tests.Unit` и `dotnet test Wintime.Control.Tests.Integration` (нужен Docker для Testcontainers);
  - frontend: `cd Wintime-Control-Frontend && npx vitest run <путь>`.
- Каждый коммит заканчивается строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Карта файлов

**Backend — создать:**
- `Wintime.Control.Core/Policies/PlanningNormLimits.cs` — пределы и их проверка.
- `Wintime.Control.Core/Policies/TaskPlan.cs` — record `TaskPlan` + `TaskPlanCalculator`.
- `Wintime.Control.Core/Entities/PlanningDefaults.cs`
- `Wintime.Control.Core/Interfaces/IPlanningDefaultsService.cs`
- `Wintime.Control.Core/DTOs/Settings/PlanningDefaultsDto.cs`
- `Wintime.Control.Core/DTOs/Settings/UpdatePlanningDefaultsRequestDto.cs`
- `Wintime.Control.Infrastructure/Services/PlanningDefaultsService.cs`
- `Wintime.Control.API/Controllers/SettingsController.cs`
- `Wintime.Control.Infrastructure/Migrations/<ts>_Pzp14SetupNormReleaseBuffer.cs` (генерируется)
- Тесты:
  - `Wintime.Control.Tests.Unit/Policies/TaskPlanCalculatorTests.cs`
  - `Wintime.Control.Tests.Unit/Entities/ShiftTaskPlanningNormsTests.cs`
  - `Wintime.Control.Tests.Unit/Entities/PlanningDefaultsTests.cs`
  - `Wintime.Control.Tests.Integration/Settings/PlanningDefaultsApiTests.cs`
  - `Wintime.Control.Tests.Integration/Settings/PlanningDefaultsInitialStateTests.cs`
  - `Wintime.Control.Tests.Integration/Tasks/TaskPlanningNormsApiTests.cs`

**Backend — изменить:**
- `Wintime.Control.Core/Entities/ShiftTask.cs`
- `Wintime.Control.Core/DTOs/Tasks/{TaskDto,CreateTaskRequestDto,UpdateTaskRequestDto,TaskMappingExtensions}.cs`
- `Wintime.Control.Infrastructure/Data/ControlDbContext.cs`
- `Wintime.Control.API/Controllers/TasksController.cs`
- `Wintime.Control.API/Program.cs`

**Backend — удалить:**
- `Wintime.Control.API/Controllers/AdminController.cs`
- `Wintime.Control.Core/DTOs/Admin/` (`SystemSettingsDto.cs`, `UpdateSystemSettingsRequestDto.cs`, `MqttTestRequestDto.cs`)

**Frontend — создать:**
- `src/utils/taskPlan.js` + `src/utils/__tests__/taskPlan.spec.js`
- `src/api/settings.js`
- `src/views/settings/SettingsView.vue` + `src/views/settings/__tests__/SettingsView.spec.js`

**Frontend — изменить:**
- `src/router/index.js`, `src/layouts/DefaultLayout.vue`
- `src/views/tasks/{TaskFormModal,TaskDetailModal,TasksView}.vue`
- `src/views/tasks/__tests__/{TaskFormModal,TaskDetailModal}.spec.js`

**Frontend — удалить:**
- `src/api/admin.js`, `src/views/admin/SettingsView.vue`

**Документация:**
- `docs/adr/0015-app-settings-storage.md`, `docs/adr/README.md`
- `CLAUDE.md`, `README.md`
- `docs/user-guide/manager.md`

---

### Task 1: Политика `TaskPlanCalculator` и пределы `PlanningNormLimits`

**Files:**
- Create: `Wintime.Control.Core/Policies/PlanningNormLimits.cs`
- Create: `Wintime.Control.Core/Policies/TaskPlan.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/TaskPlanCalculatorTests.cs`

**Interfaces:**
- Produces:
  - `PlanningNormLimits.MaxMinutes = 1440`, `PlanningNormLimits.MaxPercent = 100`;
  - `PlanningNormLimits.EnsureSetupMinutes(int minutes)` и `PlanningNormLimits.EnsureBuffer(int fixedMinutes, int percent)` — кидают `DomainException` с текстами из Global Constraints;
  - `record TaskPlan(long SetupSeconds, long? ReleaseSeconds, long? BufferSeconds, DateTime? PlannedEndAt)`;
  - `TaskPlanCalculator.Calculate(DateTime plannedStartUtc, int setupMinutes, int planQuantity, int cavities, int? fullCycleSeconds, int bufferFixedMinutes, int bufferPercent) → TaskPlan`.

- [ ] **Step 1: Написать падающие тесты**

`Wintime.Control.Tests.Unit/Policies/TaskPlanCalculatorTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class TaskPlanCalculatorTests
{
    private static readonly DateTime Start = new(2026, 10, 15, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Calculate_FullData_SumsSetupReleaseBuffer()
    {
        // 100 шт / 4 гнезда = 25 смыканий × 60 с = 1500 с; буфер 10 мин + 10 % от 1500 = 600 + 150
        var plan = TaskPlanCalculator.Calculate(Start, setupMinutes: 30, planQuantity: 100, cavities: 4,
            fullCycleSeconds: 60, bufferFixedMinutes: 10, bufferPercent: 10);

        plan.SetupSeconds.Should().Be(1800);
        plan.ReleaseSeconds.Should().Be(1500);
        plan.BufferSeconds.Should().Be(750);
        plan.PlannedEndAt.Should().Be(Start.AddSeconds(1800 + 1500 + 750));
        plan.PlannedEndAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Calculate_PartialLastShot_RoundsShotsUp()
    {
        var plan = TaskPlanCalculator.Calculate(Start, 0, planQuantity: 101, cavities: 4,
            fullCycleSeconds: 60, bufferFixedMinutes: 0, bufferPercent: 0);

        plan.ReleaseSeconds.Should().Be(26 * 60);
    }

    [Fact]
    public void Calculate_PercentPart_RoundsUpToSecond()
    {
        // 10 % от 33 с = 3.3 → 4
        var plan = TaskPlanCalculator.Calculate(Start, 0, planQuantity: 1, cavities: 1,
            fullCycleSeconds: 33, bufferFixedMinutes: 0, bufferPercent: 10);

        plan.BufferSeconds.Should().Be(4);
    }

    [Fact]
    public void Calculate_PercentBase_ExcludesSetup()
    {
        var plan = TaskPlanCalculator.Calculate(Start, setupMinutes: 600, planQuantity: 1, cavities: 1,
            fullCycleSeconds: 60, bufferFixedMinutes: 0, bufferPercent: 100);

        plan.BufferSeconds.Should().Be(60);
    }

    [Fact]
    public void Calculate_NoFullCycle_OnlySetupKnown()
    {
        var plan = TaskPlanCalculator.Calculate(Start, 45, 100, 4, fullCycleSeconds: null, 10, 10);

        plan.SetupSeconds.Should().Be(2700);
        plan.ReleaseSeconds.Should().BeNull();
        plan.BufferSeconds.Should().BeNull();
        plan.PlannedEndAt.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Calculate_NoCavities_OnlySetupKnown(int cavities)
    {
        var plan = TaskPlanCalculator.Calculate(Start, 45, 100, cavities, 60, 10, 10);

        plan.ReleaseSeconds.Should().BeNull();
        plan.BufferSeconds.Should().BeNull();
        plan.PlannedEndAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_ZeroNorms_EndIsStartPlusRelease()
    {
        var plan = TaskPlanCalculator.Calculate(Start, 0, 10, 1, 60, 0, 0);

        plan.BufferSeconds.Should().Be(0);
        plan.PlannedEndAt.Should().Be(Start.AddSeconds(600));
    }

    [Fact]
    public void Calculate_LargeValues_NoOverflow()
    {
        var plan = TaskPlanCalculator.Calculate(Start, 0, 1_000_000, 1, 86_400, 0, 100);

        plan.ReleaseSeconds.Should().Be(86_400_000_000L);
        plan.BufferSeconds.Should().Be(86_400_000_000L);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void EnsureSetupMinutes_OutOfRange_Throws(int minutes)
    {
        var act = () => PlanningNormLimits.EnsureSetupMinutes(minutes);

        act.Should().Throw<DomainException>().WithMessage("Норматив наладки должен быть от 0 до 1440 минут");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void EnsureBuffer_FixedOutOfRange_Throws(int fixedMinutes)
    {
        var act = () => PlanningNormLimits.EnsureBuffer(fixedMinutes, 0);

        act.Should().Throw<DomainException>().WithMessage("Фиксированная часть буфера должна быть от 0 до 1440 минут");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void EnsureBuffer_PercentOutOfRange_Throws(int percent)
    {
        var act = () => PlanningNormLimits.EnsureBuffer(0, percent);

        act.Should().Throw<DomainException>().WithMessage("Процент буфера должен быть от 0 до 100");
    }

    [Fact]
    public void Ensure_Bounds_Allowed()
    {
        var act = () =>
        {
            PlanningNormLimits.EnsureSetupMinutes(0);
            PlanningNormLimits.EnsureSetupMinutes(1440);
            PlanningNormLimits.EnsureBuffer(1440, 100);
            PlanningNormLimits.EnsureBuffer(0, 0);
        };

        act.Should().NotThrow();
    }
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~TaskPlanCalculatorTests"`
Expected: ошибка компиляции — `TaskPlanCalculator`/`PlanningNormLimits` не существуют.

- [ ] **Step 3: Реализовать**

`Wintime.Control.Core/Policies/PlanningNormLimits.cs`:

```csharp
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Пределы норматива наладки и буфера выпуска (PZP-14). Общие для умолчаний
/// (<see cref="Entities.PlanningDefaults"/>) и задания (<see cref="Entities.ShiftTask"/>).
/// </summary>
public static class PlanningNormLimits
{
    public const int MaxMinutes = 1440;
    public const int MaxPercent = 100;

    public static void EnsureSetupMinutes(int minutes)
    {
        if (minutes is < 0 or > MaxMinutes)
            throw new DomainException("Норматив наладки должен быть от 0 до 1440 минут");
    }

    public static void EnsureBuffer(int fixedMinutes, int percent)
    {
        if (fixedMinutes is < 0 or > MaxMinutes)
            throw new DomainException("Фиксированная часть буфера должна быть от 0 до 1440 минут");
        if (percent is < 0 or > MaxPercent)
            throw new DomainException("Процент буфера должен быть от 0 до 100");
    }
}
```

`Wintime.Control.Core/Policies/TaskPlan.cs`:

```csharp
namespace Wintime.Control.Core.Policies;

/// <summary>
/// Плановые длительности задания (PZP-14), секунды. <c>null</c> — выпуск не посчитать
/// (нет эталона полного цикла или гнёзд), тогда неизвестны и буфер, и окончание.
/// </summary>
public sealed record TaskPlan(long SetupSeconds, long? ReleaseSeconds, long? BufferSeconds, DateTime? PlannedEndAt);

/// <summary>
/// План задания: наладка → выпуск → буфер выпуска. Чистая функция, результат не хранится.
/// Выпуск = ⌈план / гнёзда⌉ × эталон полного цикла; буфер = фикс. часть + процент от выпуска
/// (наладка в базу процента не входит), процентная часть округляется вверх до секунды.
/// Гнёзда — текущие <c>Mold.Cavities</c>: это прогноз, а не пересчёт истории.
/// </summary>
public static class TaskPlanCalculator
{
    public static TaskPlan Calculate(DateTime plannedStartUtc, int setupMinutes,
                                     int planQuantity, int cavities, int? fullCycleSeconds,
                                     int bufferFixedMinutes, int bufferPercent)
    {
        long setupSeconds = setupMinutes * 60L;

        if (fullCycleSeconds is not int cycle || cavities <= 0)
            return new TaskPlan(setupSeconds, null, null, null);

        long shots = ((long)planQuantity + cavities - 1) / cavities;
        long releaseSeconds = shots * cycle;
        long bufferSeconds = bufferFixedMinutes * 60L + (releaseSeconds * bufferPercent + 99) / 100;
        var end = plannedStartUtc.AddSeconds(setupSeconds + releaseSeconds + bufferSeconds);

        return new TaskPlan(setupSeconds, releaseSeconds, bufferSeconds, end);
    }
}
```

- [ ] **Step 4: Убедиться, что тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~TaskPlanCalculatorTests"`
Expected: PASS (все тесты).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/PlanningNormLimits.cs Wintime.Control.Core/Policies/TaskPlan.cs Wintime.Control.Tests.Unit/Policies/TaskPlanCalculatorTests.cs
git commit -m "feat(pzp-14): TaskPlanCalculator and planning norm limits

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Домен — поля норматива и буфера в задании, сущность `PlanningDefaults`, схема и миграция

**Files:**
- Modify: `Wintime.Control.Core/Entities/ShiftTask.cs`
- Create: `Wintime.Control.Core/Entities/PlanningDefaults.cs`
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs`
- Create: `Wintime.Control.Infrastructure/Migrations/<ts>_Pzp14SetupNormReleaseBuffer.cs` (генерируется)
- Test: `Wintime.Control.Tests.Unit/Entities/ShiftTaskPlanningNormsTests.cs`
- Test: `Wintime.Control.Tests.Unit/Entities/PlanningDefaultsTests.cs`

**Interfaces:**
- Consumes: `PlanningNormLimits` (Task 1).
- Produces:
  - `ShiftTask.PlannedSetupMinutes`, `ShiftTask.ReleaseBufferFixedMinutes`, `ShiftTask.ReleaseBufferPercent` (`int`);
  - `ShiftTask.SetSetupNorm(int minutes)`, `ShiftTask.SetReleaseBuffer(int fixedMinutes, int percent)`;
  - сущность `PlanningDefaults { int Id; int SetupMinutes; int ReleaseBufferFixedMinutes; int ReleaseBufferPercent; DateTime? UpdatedAt; string? UpdatedById; const int SingletonId = 1; void Update(int setupMinutes, int bufferFixedMinutes, int bufferPercent, string? userId, DateTime nowUtc) }`;
  - `ControlDbContext.PlanningDefaults` (`DbSet<PlanningDefaults>`), строка `Id = 1` с нулями создаётся миграцией.

- [ ] **Step 1: Написать падающие тесты задания**

`Wintime.Control.Tests.Unit/Entities/ShiftTaskPlanningNormsTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Xunit;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Entities;

public class ShiftTaskPlanningNormsTests
{
    private static ShiftTask NewTask(EntityTaskStatus status) => new()
    {
        Status = status, PlanQuantity = 10,
        PlannedSetupMinutes = 30, ReleaseBufferFixedMinutes = 10, ReleaseBufferPercent = 10
    };

    [Theory]
    [InlineData(EntityTaskStatus.Draft)]
    [InlineData(EntityTaskStatus.Issued)]
    public void SetSetupNorm_BeforeSetup_Changes(EntityTaskStatus status)
    {
        var task = NewTask(status);

        task.SetSetupNorm(90);

        task.PlannedSetupMinutes.Should().Be(90);
    }

    [Theory]
    [InlineData(EntityTaskStatus.Setup)]
    [InlineData(EntityTaskStatus.InProgress)]
    [InlineData(EntityTaskStatus.Completed)]
    [InlineData(EntityTaskStatus.Closed)]
    public void SetSetupNorm_AfterSetupStarted_Throws(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.SetSetupNorm(90);

        act.Should().Throw<DomainException>().WithMessage("Норматив наладки можно изменить только до начала наладки");
        task.PlannedSetupMinutes.Should().Be(30);
    }

    [Fact]
    public void SetSetupNorm_Unchanged_InAnyStatus_NoOp()
    {
        var task = NewTask(EntityTaskStatus.Closed);

        var act = () => task.SetSetupNorm(30);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetSetupNorm_OutOfRange_Throws_EvenIfStatusAllows()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        var act = () => task.SetSetupNorm(1441);

        act.Should().Throw<DomainException>().WithMessage("Норматив наладки должен быть от 0 до 1440 минут");
    }

    [Theory]
    [InlineData(EntityTaskStatus.Draft)]
    [InlineData(EntityTaskStatus.Issued)]
    [InlineData(EntityTaskStatus.Setup)]
    [InlineData(EntityTaskStatus.InProgress)]
    public void SetReleaseBuffer_BeforeCompletion_Changes(EntityTaskStatus status)
    {
        var task = NewTask(status);

        task.SetReleaseBuffer(45, 20);

        task.ReleaseBufferFixedMinutes.Should().Be(45);
        task.ReleaseBufferPercent.Should().Be(20);
    }

    [Theory]
    [InlineData(EntityTaskStatus.Completed)]
    [InlineData(EntityTaskStatus.Closed)]
    public void SetReleaseBuffer_AfterCompletion_Throws(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.SetReleaseBuffer(45, 20);

        act.Should().Throw<DomainException>().WithMessage("Буфер выпуска можно изменить только до завершения выпуска");
    }

    [Fact]
    public void SetReleaseBuffer_Unchanged_InAnyStatus_NoOp()
    {
        var task = NewTask(EntityTaskStatus.Closed);

        var act = () => task.SetReleaseBuffer(10, 10);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetReleaseBuffer_PercentOutOfRange_Throws()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        var act = () => task.SetReleaseBuffer(10, 101);

        act.Should().Throw<DomainException>().WithMessage("Процент буфера должен быть от 0 до 100");
    }
}
```

`Wintime.Control.Tests.Unit/Entities/PlanningDefaultsTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Xunit;

namespace Wintime.Control.Tests.Unit.Entities;

public class PlanningDefaultsTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void New_IsSingletonWithZeros()
    {
        var d = new PlanningDefaults();

        d.Id.Should().Be(PlanningDefaults.SingletonId);
        d.SetupMinutes.Should().Be(0);
        d.ReleaseBufferFixedMinutes.Should().Be(0);
        d.ReleaseBufferPercent.Should().Be(0);
    }

    [Fact]
    public void Update_Valid_SetsValuesAndAudit()
    {
        var d = new PlanningDefaults();

        d.Update(90, 30, 10, "user-1", Now);

        d.SetupMinutes.Should().Be(90);
        d.ReleaseBufferFixedMinutes.Should().Be(30);
        d.ReleaseBufferPercent.Should().Be(10);
        d.UpdatedAt.Should().Be(Now);
        d.UpdatedById.Should().Be("user-1");
    }

    [Fact]
    public void Update_OutOfRange_ThrowsAndKeepsValues()
    {
        var d = new PlanningDefaults();

        var act = () => d.Update(1441, 0, 0, "user-1", Now);

        act.Should().Throw<DomainException>().WithMessage("Норматив наладки должен быть от 0 до 1440 минут");
        d.SetupMinutes.Should().Be(0);
        d.UpdatedAt.Should().BeNull();
    }
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ShiftTaskPlanningNormsTests|FullyQualifiedName~PlanningDefaultsTests"`
Expected: ошибка компиляции — нет `SetSetupNorm`, `PlannedSetupMinutes`, `PlanningDefaults`.

- [ ] **Step 3: Реализовать в `ShiftTask`**

В `Wintime.Control.Core/Entities/ShiftTask.cs`:

1. Добавить `using Wintime.Control.Core.Policies;` в начало файла.
2. После свойства `PlannedSetupStartTime` добавить:

```csharp
    /// <summary>
    /// Норматив наладки, минуты (PZP-14). Снапшот умолчания <see cref="PlanningDefaults"/>
    /// на момент создания; правится до начала наладки (дальше — мерка перерасхода, BL-12).
    /// </summary>
    public int PlannedSetupMinutes { get; set; }

    /// <summary>Буфер выпуска: фиксированная часть, минуты (PZP-14).</summary>
    public int ReleaseBufferFixedMinutes { get; set; }

    /// <summary>
    /// Буфер выпуска: процент от плановой длительности выпуска (без наладки), целое 0..100 (PZP-14).
    /// Буфер не хранится готовым — см. <see cref="TaskPlanCalculator"/>.
    /// </summary>
    public int ReleaseBufferPercent { get; set; }
```

3. После метода `SetPlannedStart` добавить:

```csharp
    /// <summary>
    /// PZP-14: норматив наладки. Менять можно до начала наладки (Draft/Issued): после старта
    /// норматив — мерка перерасхода и задним числом не подкручивается. Неизменённое значение — no-op.
    /// </summary>
    public void SetSetupNorm(int minutes)
    {
        PlanningNormLimits.EnsureSetupMinutes(minutes);
        if (minutes == PlannedSetupMinutes)
            return;

        if (Status is not (TaskStatus.Draft or TaskStatus.Issued))
            throw new DomainException("Норматив наладки можно изменить только до начала наладки");

        PlannedSetupMinutes = minutes;
    }

    /// <summary>
    /// PZP-14: буфер выпуска (фикс. часть + процент). Менять можно до завершения выпуска —
    /// планировщик перепланирует хвост, если выпуск затягивается. Неизменённое значение — no-op.
    /// </summary>
    public void SetReleaseBuffer(int fixedMinutes, int percent)
    {
        PlanningNormLimits.EnsureBuffer(fixedMinutes, percent);
        if (fixedMinutes == ReleaseBufferFixedMinutes && percent == ReleaseBufferPercent)
            return;

        if (Status is TaskStatus.Completed or TaskStatus.Closed)
            throw new DomainException("Буфер выпуска можно изменить только до завершения выпуска");

        ReleaseBufferFixedMinutes = fixedMinutes;
        ReleaseBufferPercent = percent;
    }
```

- [ ] **Step 4: Создать сущность `PlanningDefaults`**

`Wintime.Control.Core/Entities/PlanningDefaults.cs`:

```csharp
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.Entities;

/// <summary>
/// Умолчания планирования (PZP-14, ADR-0015): одна строка, <c>Id = 1</c>. Копируются в
/// новые задания; смена умолчаний существующие задания не трогает.
/// </summary>
public class PlanningDefaults
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public int SetupMinutes { get; set; }
    public int ReleaseBufferFixedMinutes { get; set; }
    public int ReleaseBufferPercent { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedById { get; set; }

    public void Update(int setupMinutes, int bufferFixedMinutes, int bufferPercent,
                       string? userId, DateTime nowUtc)
    {
        PlanningNormLimits.EnsureSetupMinutes(setupMinutes);
        PlanningNormLimits.EnsureBuffer(bufferFixedMinutes, bufferPercent);

        SetupMinutes = setupMinutes;
        ReleaseBufferFixedMinutes = bufferFixedMinutes;
        ReleaseBufferPercent = bufferPercent;
        UpdatedAt = nowUtc;
        UpdatedById = userId;
    }
}
```

- [ ] **Step 5: Убедиться, что юнит-тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ShiftTaskPlanningNormsTests|FullyQualifiedName~PlanningDefaultsTests"`
Expected: PASS.

- [ ] **Step 6: Схема в `ControlDbContext`**

В `Wintime.Control.Infrastructure/Data/ControlDbContext.cs`:

1. После `public DbSet<Order> Orders { get; set; }` добавить:

```csharp
    public DbSet<PlanningDefaults> PlanningDefaults { get; set; }
```

2. Заменить строку

```csharp
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>().ToTable("ShiftTasks");
```

на

```csharp
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>().ToTable("ShiftTasks", t =>
        {
            // PZP-14: пределы норматива и буфера (дублируют PlanningNormLimits на уровне БД)
            t.HasCheckConstraint("CK_ShiftTasks_PlannedSetupMinutes", "\"PlannedSetupMinutes\" BETWEEN 0 AND 1440");
            t.HasCheckConstraint("CK_ShiftTasks_ReleaseBufferFixedMinutes", "\"ReleaseBufferFixedMinutes\" BETWEEN 0 AND 1440");
            t.HasCheckConstraint("CK_ShiftTasks_ReleaseBufferPercent", "\"ReleaseBufferPercent\" BETWEEN 0 AND 100");
        });
```

3. После блока конфигурации `AppHeartbeat` добавить:

```csharp
        // Умолчания планирования (PZP-14, ADR-0015): одна строка, Id = 1, создаётся миграцией
        builder.Entity<PlanningDefaults>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UpdatedById).OnDelete(DeleteBehavior.SetNull);
            entity.ToTable("PlanningDefaults", t =>
            {
                t.HasCheckConstraint("CK_PlanningDefaults_Singleton", "\"Id\" = 1");
                t.HasCheckConstraint("CK_PlanningDefaults_SetupMinutes", "\"SetupMinutes\" BETWEEN 0 AND 1440");
                t.HasCheckConstraint("CK_PlanningDefaults_ReleaseBufferFixedMinutes", "\"ReleaseBufferFixedMinutes\" BETWEEN 0 AND 1440");
                t.HasCheckConstraint("CK_PlanningDefaults_ReleaseBufferPercent", "\"ReleaseBufferPercent\" BETWEEN 0 AND 100");
            });
            entity.HasData(new PlanningDefaults { Id = 1 });
        });
```

Внутри `ControlDbContext` имя `PlanningDefaults` в выражении означает свойство `DbSet`, поэтому не обращайся к `PlanningDefaults.SingletonId` внутри этого класса — пиши литерал `1`, как выше. В позиции типа (`Entity<PlanningDefaults>`, `new PlanningDefaults`) имя разрешается в тип, конфликта нет.

- [ ] **Step 7: Сгенерировать миграцию**

Run:
```powershell
dotnet ef migrations add Pzp14SetupNormReleaseBuffer --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: создан `Wintime.Control.Infrastructure/Migrations/<ts>_Pzp14SetupNormReleaseBuffer.cs`. Открой его и проверь, что `Up` содержит:
- `AddColumn<int>` ×3 на `ShiftTasks` (`PlannedSetupMinutes`, `ReleaseBufferFixedMinutes`, `ReleaseBufferPercent`) с `nullable: false, defaultValue: 0`;
- `CreateTable("PlanningDefaults", …)` с FK на `Users` (`onDelete: ReferentialAction.SetNull`) и четырьмя check-ограничениями;
- `InsertData("PlanningDefaults", …)` со строкой `Id = 1` и нулями;
- `AddCheckConstraint` ×3 на `ShiftTasks`.

Других изменений в миграции быть не должно. Если есть — значит, модель разошлась со снапшотом ещё до этой задачи: остановись и сообщи.

Run: `dotnet ef migrations has-pending-model-changes --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`
Expected: «No changes have been made to the model since the last migration.»

- [ ] **Step 8: Сборка и полный прогон тестов**

Run: `dotnet build Wintime.Control.sln` — Expected: Build succeeded.
Run: `dotnet test Wintime.Control.Tests.Unit` — Expected: PASS.
Run: `dotnet test Wintime.Control.Tests.Integration` — Expected: PASS (миграция применяется при старте хоста в Testcontainers; существующие задания получают нули).

- [ ] **Step 9: Commit**

```bash
git add Wintime.Control.Core/Entities/ShiftTask.cs Wintime.Control.Core/Entities/PlanningDefaults.cs Wintime.Control.Infrastructure/Data/ControlDbContext.cs Wintime.Control.Infrastructure/Migrations Wintime.Control.Tests.Unit/Entities/ShiftTaskPlanningNormsTests.cs Wintime.Control.Tests.Unit/Entities/PlanningDefaultsTests.cs
git commit -m "feat(pzp-14): setup norm and release buffer on task, PlanningDefaults table

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: API умолчаний — `IPlanningDefaultsService`, `SettingsController`, удаление `AdminController`

**Files:**
- Create: `Wintime.Control.Core/Interfaces/IPlanningDefaultsService.cs`
- Create: `Wintime.Control.Infrastructure/Services/PlanningDefaultsService.cs`
- Create: `Wintime.Control.Core/DTOs/Settings/PlanningDefaultsDto.cs`
- Create: `Wintime.Control.Core/DTOs/Settings/UpdatePlanningDefaultsRequestDto.cs`
- Create: `Wintime.Control.API/Controllers/SettingsController.cs`
- Modify: `Wintime.Control.API/Program.cs` (регистрация сервиса)
- Delete: `Wintime.Control.API/Controllers/AdminController.cs`, `Wintime.Control.Core/DTOs/Admin/` (3 файла)
- Test: `Wintime.Control.Tests.Integration/Settings/PlanningDefaultsApiTests.cs`
- Test: `Wintime.Control.Tests.Integration/Settings/PlanningDefaultsInitialStateTests.cs`

**Interfaces:**
- Consumes: `PlanningDefaults`, `ControlDbContext.PlanningDefaults` (Task 2).
- Produces:
  - `IPlanningDefaultsService.GetAsync(CancellationToken ct = default) → Task<PlanningDefaults>`;
  - `IPlanningDefaultsService.UpdateAsync(int setupMinutes, int bufferFixedMinutes, int bufferPercent, string? userId, CancellationToken ct = default) → Task<PlanningDefaults>`;
  - `GET/PUT /api/settings/planning-defaults`, JSON `{ setupMinutes, releaseBufferFixedMinutes, releaseBufferPercent, updatedAt }`.

- [ ] **Step 1: Написать падающие интеграционные тесты**

`Wintime.Control.Tests.Integration/Settings/PlanningDefaultsApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Settings;

/// <summary>PZP-14: GET/PUT /api/settings/planning-defaults — сохранение, пределы, права.</summary>
[Collection("Integration")]
public class PlanningDefaultsApiTests : IClassFixture<IntegrationTestFactory>
{
    private const string Url = "/api/settings/planning-defaults";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IntegrationTestFactory _factory;
    public PlanningDefaultsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string login, string password)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Fact]
    public async Task UpdatePlanningDefaults_Valid_Persists()
    {
        var client = await ClientAsync("test_manager", "Manager123!");

        var resp = await client.PutAsJsonAsync(Url,
            new { setupMinutes = 90, releaseBufferFixedMinutes = 30, releaseBufferPercent = 10 });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await client.GetFromJsonAsync<JsonElement>(Url, JsonOptions);
        saved.GetProperty("setupMinutes").GetInt32().Should().Be(90);
        saved.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(30);
        saved.GetProperty("releaseBufferPercent").GetInt32().Should().Be(10);
        saved.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task UpdatePlanningDefaults_Admin_Allowed()
    {
        var client = await ClientAsync("admin", "Admin123!");

        var resp = await client.PutAsJsonAsync(Url,
            new { setupMinutes = 60, releaseBufferFixedMinutes = 0, releaseBufferPercent = 0 });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(1441, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 1441, 0)]
    [InlineData(0, 0, 101)]
    [InlineData(0, 0, -1)]
    public async Task UpdatePlanningDefaults_OutOfRange_Returns400(int setup, int fixedMinutes, int percent)
    {
        var client = await ClientAsync("test_manager", "Manager123!");

        var resp = await client.PutAsJsonAsync(Url,
            new { setupMinutes = setup, releaseBufferFixedMinutes = fixedMinutes, releaseBufferPercent = percent });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PlanningDefaults_Adjuster_Returns403()
    {
        var client = await ClientAsync("test_adjuster", "Adjuster123!");

        (await client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync(Url,
            new { setupMinutes = 1, releaseBufferFixedMinutes = 1, releaseBufferPercent = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

`Wintime.Control.Tests.Integration/Settings/PlanningDefaultsInitialStateTests.cs` (отдельный класс — своя БД, в ней умолчания никто не менял):

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Settings;

/// <summary>PZP-14: миграция создаёт строку умолчаний с нулями.</summary>
[Collection("Integration")]
public class PlanningDefaultsInitialStateTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public PlanningDefaultsInitialStateTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetPlanningDefaults_Initially_AllZero()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var d = await client.GetFromJsonAsync<JsonElement>("/api/settings/planning-defaults",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        d.GetProperty("setupMinutes").GetInt32().Should().Be(0);
        d.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(0);
        d.GetProperty("releaseBufferPercent").GetInt32().Should().Be(0);
        d.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~PlanningDefaults"`
Expected: FAIL — `/api/settings/planning-defaults` не существует (404 или HTML SPA-fallback вместо JSON).

- [ ] **Step 3: Реализовать сервис и DTO**

`Wintime.Control.Core/Interfaces/IPlanningDefaultsService.cs`:

```csharp
using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Interfaces;

/// <summary>Умолчания планирования (PZP-14, ADR-0015). Без кеша: читаются редко.</summary>
public interface IPlanningDefaultsService
{
    Task<PlanningDefaults> GetAsync(CancellationToken ct = default);

    Task<PlanningDefaults> UpdateAsync(int setupMinutes, int bufferFixedMinutes, int bufferPercent,
                                       string? userId, CancellationToken ct = default);
}
```

`Wintime.Control.Infrastructure/Services/PlanningDefaultsService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public class PlanningDefaultsService : IPlanningDefaultsService
{
    private readonly ControlDbContext _db;

    public PlanningDefaultsService(ControlDbContext db) => _db = db;

    public Task<PlanningDefaults> GetAsync(CancellationToken ct = default) =>
        _db.PlanningDefaults.SingleAsync(d => d.Id == PlanningDefaults.SingletonId, ct);

    public async Task<PlanningDefaults> UpdateAsync(int setupMinutes, int bufferFixedMinutes, int bufferPercent,
                                                    string? userId, CancellationToken ct = default)
    {
        var defaults = await GetAsync(ct);
        defaults.Update(setupMinutes, bufferFixedMinutes, bufferPercent, userId, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
        return defaults;
    }
}
```

`Wintime.Control.Core/DTOs/Settings/PlanningDefaultsDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Settings;

public class PlanningDefaultsDto
{
    public int SetupMinutes { get; set; }
    public int ReleaseBufferFixedMinutes { get; set; }
    public int ReleaseBufferPercent { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

`Wintime.Control.Core/DTOs/Settings/UpdatePlanningDefaultsRequestDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Settings;

public class UpdatePlanningDefaultsRequestDto
{
    public int SetupMinutes { get; set; }
    public int ReleaseBufferFixedMinutes { get; set; }
    public int ReleaseBufferPercent { get; set; }
}
```

- [ ] **Step 4: Контроллер и регистрация**

`Wintime.Control.API/Controllers/SettingsController.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.Settings;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.API.Controllers;

/// <summary>Настройки приложения (ADR-0015). Сейчас — только умолчания планирования (PZP-14).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ManagerOrAdmin")]
public class SettingsController : ControllerBase
{
    private readonly IPlanningDefaultsService _planningDefaults;

    public SettingsController(IPlanningDefaultsService planningDefaults) => _planningDefaults = planningDefaults;

    /// <summary>Умолчания норматива наладки и буфера выпуска для новых заданий.</summary>
    [HttpGet("planning-defaults")]
    public async Task<ActionResult<PlanningDefaultsDto>> GetPlanningDefaults() =>
        Ok(ToDto(await _planningDefaults.GetAsync()));

    /// <summary>Изменить умолчания. Существующие задания не меняются.</summary>
    [HttpPut("planning-defaults")]
    public async Task<ActionResult<PlanningDefaultsDto>> UpdatePlanningDefaults(
        [FromBody] UpdatePlanningDefaultsRequestDto request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var saved = await _planningDefaults.UpdateAsync(
            request.SetupMinutes, request.ReleaseBufferFixedMinutes, request.ReleaseBufferPercent, userId);
        return Ok(ToDto(saved));
    }

    private static PlanningDefaultsDto ToDto(PlanningDefaults d) => new()
    {
        SetupMinutes = d.SetupMinutes,
        ReleaseBufferFixedMinutes = d.ReleaseBufferFixedMinutes,
        ReleaseBufferPercent = d.ReleaseBufferPercent,
        UpdatedAt = d.UpdatedAt
    };
}
```

В `Wintime.Control.API/Program.cs` после строки `builder.Services.AddScoped<IFactoryClock, FactoryClock>();` добавить:

```csharp
builder.Services.AddScoped<IPlanningDefaultsService, PlanningDefaultsService>();
```

(`using` для `Wintime.Control.Core.Interfaces` и `Wintime.Control.Infrastructure.Services` в `Program.cs` уже есть — их использует `FactoryClock`; если сборка скажет иначе, добавь.)

- [ ] **Step 5: Удалить заглушку настроек (BL-31)**

```bash
git rm Wintime.Control.API/Controllers/AdminController.cs Wintime.Control.Core/DTOs/Admin/SystemSettingsDto.cs Wintime.Control.Core/DTOs/Admin/UpdateSystemSettingsRequestDto.cs Wintime.Control.Core/DTOs/Admin/MqttTestRequestDto.cs
```

Run: `dotnet build Wintime.Control.sln`
Expected: Build succeeded. Если что-то ссылается на `Wintime.Control.Core.DTOs.Admin` — это мёртвые ссылки на удалённые DTO, удали их.

- [ ] **Step 6: Убедиться, что тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~PlanningDefaults"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Wintime.Control.Core/Interfaces/IPlanningDefaultsService.cs Wintime.Control.Infrastructure/Services/PlanningDefaultsService.cs Wintime.Control.Core/DTOs/Settings Wintime.Control.API/Controllers/SettingsController.cs Wintime.Control.API/Program.cs Wintime.Control.Tests.Integration/Settings
git commit -m "feat(pzp-14): planning defaults API; remove fake admin settings (BL-31)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: API заданий — умолчания при создании, правка норматива и буфера, план в `TaskDto`

**Files:**
- Modify: `Wintime.Control.Core/DTOs/Tasks/CreateTaskRequestDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Tasks/UpdateTaskRequestDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Tasks/TaskDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Tasks/TaskMappingExtensions.cs`
- Modify: `Wintime.Control.API/Controllers/TasksController.cs`
- Test: `Wintime.Control.Tests.Integration/Tasks/TaskPlanningNormsApiTests.cs`

**Interfaces:**
- Consumes: `TaskPlanCalculator` (Task 1); `ShiftTask.SetSetupNorm/SetReleaseBuffer` (Task 2); `IPlanningDefaultsService` (Task 3).
- Produces (JSON задания): `plannedSetupMinutes`, `releaseBufferFixedMinutes`, `releaseBufferPercent` (int), `plannedReleaseSeconds`, `plannedReleaseBufferSeconds` (long | null), `plannedEndAt` (UTC ISO | null). Запросы `POST /api/tasks` и `PUT /api/tasks/{id}` принимают три необязательных поля с теми же именами.

- [ ] **Step 1: Написать падающие интеграционные тесты**

`Wintime.Control.Tests.Integration/Tasks/TaskPlanningNormsApiTests.cs`:

```csharp
using System.Net;
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

/// <summary>
/// PZP-14: норматив наладки и буфер выпуска в задании — копирование умолчаний,
/// правила правки по статусам, расчётный план в TaskDto.
/// Тесты, зависящие от умолчаний, сами выставляют их перед проверкой (общая БД класса).
/// </summary>
[Collection("Integration")]
public class TaskPlanningNormsApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    private readonly IntegrationTestFactory _factory;
    public TaskPlanningNormsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static async Task SetDefaultsAsync(HttpClient client, int setup, int fixedMinutes, int percent)
    {
        var resp = await client.PutAsJsonAsync("/api/settings/planning-defaults",
            new { setupMinutes = setup, releaseBufferFixedMinutes = fixedMinutes, releaseBufferPercent = percent });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> PostTaskAsync(HttpClient client, Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["immId"] = _factory.TestImmId,
            ["moldId"] = _factory.TestMoldId,           // Cavities = 1
            ["planQuantity"] = 100,
            ["plannedDate"] = Tomorrow.ToString("yyyy-MM-dd"),
            ["plannedFullCycleSeconds"] = 60
        };
        if (extra != null)
            foreach (var (k, v) in extra) body[k] = v;
        return await client.PostAsJsonAsync("/api/tasks", body);
    }

    private async Task<JsonElement> CreateTaskAsync(HttpClient client, Dictionary<string, object?>? extra = null)
    {
        var resp = await PostTaskAsync(client, extra);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<Guid> SeedTaskAsync(TaskStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId, PlanQuantity = 100, Note = "указание",
            Status = status, PlannedDate = Tomorrow, PlannedFullCycleSeconds = 60,
            PlannedSetupMinutes = 30, ReleaseBufferFixedMinutes = 10, ReleaseBufferPercent = 10
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);

    [Fact]
    public async Task CreateTask_WithoutNormFields_UsesDefaults()
    {
        var client = await ManagerClientAsync();
        await SetDefaultsAsync(client, 45, 20, 15);

        var task = await CreateTaskAsync(client);

        task.GetProperty("plannedSetupMinutes").GetInt32().Should().Be(45);
        task.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(20);
        task.GetProperty("releaseBufferPercent").GetInt32().Should().Be(15);
    }

    [Fact]
    public async Task CreateTask_WithNormFields_UsesProvidedValues()
    {
        var client = await ManagerClientAsync();
        await SetDefaultsAsync(client, 45, 20, 15);

        var task = await CreateTaskAsync(client, new()
        {
            ["plannedSetupMinutes"] = 120, ["releaseBufferFixedMinutes"] = 0, ["releaseBufferPercent"] = 5
        });

        task.GetProperty("plannedSetupMinutes").GetInt32().Should().Be(120);
        task.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(0);
        task.GetProperty("releaseBufferPercent").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task CreateTask_NormOutOfRange_Returns400()
    {
        var client = await ManagerClientAsync();

        var resp = await PostTaskAsync(client, new() { ["plannedSetupMinutes"] = 1441 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePlanningDefaults_DoesNotChangeExistingTasks()
    {
        var client = await ManagerClientAsync();
        await SetDefaultsAsync(client, 10, 5, 5);
        var created = await CreateTaskAsync(client);

        await SetDefaultsAsync(client, 99, 99, 50);

        var task = await GetAsync(client, created.GetProperty("id").GetGuid());
        task.GetProperty("plannedSetupMinutes").GetInt32().Should().Be(10);
        task.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(5);
        task.GetProperty("releaseBufferPercent").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task GetTask_ReturnsPlannedPlan()
    {
        var client = await ManagerClientAsync();
        var created = await CreateTaskAsync(client, new()
        {
            ["plannedSetupMinutes"] = 30, ["releaseBufferFixedMinutes"] = 10, ["releaseBufferPercent"] = 10
        });

        var task = await GetAsync(client, created.GetProperty("id").GetGuid());

        // 100 шт / 1 гнездо × 60 с = 6000; буфер 600 + 10 % от 6000 = 1200
        task.GetProperty("plannedReleaseSeconds").GetInt64().Should().Be(6000);
        task.GetProperty("plannedReleaseBufferSeconds").GetInt64().Should().Be(1200);
        var start = task.GetProperty("plannedStartAt").GetDateTime();
        var end = task.GetProperty("plannedEndAt").GetDateTime();
        (end - start).Should().Be(TimeSpan.FromSeconds(1800 + 6000 + 1200));
    }

    [Fact]
    public async Task UpdateTask_Issued_ChangesSetupNormAndBuffer()
    {
        var id = await SeedTaskAsync(TaskStatus.Issued);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}",
            new { plannedSetupMinutes = 50, releaseBufferFixedMinutes = 15 });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var task = await GetAsync(client, id);
        task.GetProperty("plannedSetupMinutes").GetInt32().Should().Be(50);
        task.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(15);
        task.GetProperty("releaseBufferPercent").GetInt32().Should().Be(10);   // не передан — не меняется
    }

    [Fact]
    public async Task UpdateTask_InProgress_ChangesBuffer()
    {
        var id = await SeedTaskAsync(TaskStatus.InProgress);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { releaseBufferPercent = 25 });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("releaseBufferPercent").GetInt32().Should().Be(25);
    }

    [Fact]
    public async Task UpdateTask_InProgress_FullFormPayloadWithBufferChange_Succeeds()
    {
        // Форма в режиме «только буфер» шлёт все поля задания без изменений + новый буфер.
        var id = await SeedTaskAsync(TaskStatus.InProgress);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new
        {
            planQuantity = 100,
            note = "указание",
            workMode = "Auto",
            plannedFullCycleSeconds = 60,
            plannedDate = Tomorrow,
            clearPlannedSetupStartTime = true,
            plannedSetupMinutes = 30,
            releaseBufferFixedMinutes = 40,
            releaseBufferPercent = 10
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(40);
    }

    [Fact]
    public async Task UpdateTask_InProgress_ChangeSetupNorm_Returns400()
    {
        var id = await SeedTaskAsync(TaskStatus.InProgress);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { plannedSetupMinutes = 90 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetAsync(client, id)).GetProperty("plannedSetupMinutes").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task UpdateTask_Completed_ChangeBuffer_Returns400()
    {
        var id = await SeedTaskAsync(TaskStatus.Completed);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { releaseBufferFixedMinutes = 99 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~TaskPlanningNormsApiTests"`
Expected: FAIL — в ответе нет `plannedSetupMinutes` (`KeyNotFoundException`), PUT игнорирует новые поля.

- [ ] **Step 3: DTO запросов**

В `CreateTaskRequestDto` после `PlannedInjectionCycleSeconds` добавить:

```csharp
    /// <summary>Норматив наладки, минуты (PZP-14). Не передан — из умолчаний.</summary>
    public int? PlannedSetupMinutes { get; set; }

    /// <summary>Буфер выпуска: фикс. часть, минуты. Не передан — из умолчаний.</summary>
    public int? ReleaseBufferFixedMinutes { get; set; }

    /// <summary>Буфер выпуска: % от плановой длительности выпуска. Не передан — из умолчаний.</summary>
    public int? ReleaseBufferPercent { get; set; }
```

В `UpdateTaskRequestDto` после `PlannedInjectionCycleSeconds` добавить:

```csharp
    /// <summary>Норматив наладки, минуты; <c>null</c> — не менять.</summary>
    public int? PlannedSetupMinutes { get; set; }

    /// <summary>Буфер выпуска: фикс. часть, минуты; <c>null</c> — не менять.</summary>
    public int? ReleaseBufferFixedMinutes { get; set; }

    /// <summary>Буфер выпуска: процент; <c>null</c> — не менять.</summary>
    public int? ReleaseBufferPercent { get; set; }
```

- [ ] **Step 4: `TaskDto` и маппинг**

В `TaskDto` после `PlannedInjectionCycleSeconds` добавить:

```csharp
    // PZP-14: норматив наладки и буфер выпуска (снапшот) + расчётный план (не хранится)
    public int PlannedSetupMinutes { get; set; }
    public int ReleaseBufferFixedMinutes { get; set; }
    public int ReleaseBufferPercent { get; set; }
    /// <summary>Плановая длительность выпуска, с (TaskPlanCalculator); null — нет эталона/гнёзд.</summary>
    public long? PlannedReleaseSeconds { get; set; }
    /// <summary>Буфер выпуска, с; null — нет эталона/гнёзд.</summary>
    public long? PlannedReleaseBufferSeconds { get; set; }
    /// <summary>Плановое окончание (начало + наладка + выпуск + буфер), UTC; null — нет эталона/гнёзд.</summary>
    public DateTime? PlannedEndAt { get; set; }
```

`TaskMappingExtensions.ToDto` переписать с выражения на тело метода:

```csharp
    public static TaskDto ToDto(this ShiftTask t, FactorySchedule schedule)
    {
        var plannedStartAt = schedule.PlannedStartUtc(t.PlannedDate, t.PlannedSetupStartTime);
        // Гнёзда — текущие Mold.Cavities (прогноз); Mold должен быть загружен (Include).
        var plan = TaskPlanCalculator.Calculate(plannedStartAt, t.PlannedSetupMinutes, t.PlanQuantity,
            t.Mold?.Cavities ?? 0, t.PlannedFullCycleSeconds, t.ReleaseBufferFixedMinutes, t.ReleaseBufferPercent);

        return new TaskDto
        {
            // … все существующие присваивания без изменений, кроме PlannedStartAt:
            PlannedStartAt = plannedStartAt,
            // … и в конец, после PlannedInjectionCycleSeconds:
            PlannedSetupMinutes = t.PlannedSetupMinutes,
            ReleaseBufferFixedMinutes = t.ReleaseBufferFixedMinutes,
            ReleaseBufferPercent = t.ReleaseBufferPercent,
            PlannedReleaseSeconds = plan.ReleaseSeconds,
            PlannedReleaseBufferSeconds = plan.BufferSeconds,
            PlannedEndAt = plan.PlannedEndAt,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        };
    }
```

Полный список существующих присваиваний — в текущем файле (Id … UpdatedAt); перенеси их все. Комментарий метода дополни: «… а план задания (PZP-14) — только если загружена `Mold`».

Все вызовы `ToDto` в `TasksController` (`GetTaskList`, `GetMyTasks`, `GetTaskById`) уже делают `Include(t => t.Mold)`. В `CreateTask` пресс-форма загружена через `_context.Molds.FindAsync`, но навигация `task.Mold` не задана — явно присвой `task.Mold = mold;` (см. Step 5), чтобы план попал и в ответ на создание.

- [ ] **Step 5: `TasksController`**

1. Внедрить сервис умолчаний:

```csharp
    private readonly IPlanningDefaultsService _planningDefaults;

    public TasksController(ControlDbContext context, IEmulatorControlService emulator, IFactoryClock factoryClock,
                           IPlanningDefaultsService planningDefaults)
    {
        _context = context;
        _emulator = emulator;
        _factoryClock = factoryClock;
        _planningDefaults = planningDefaults;
    }
```

2. В `CreateTask` после вызова `task.SetCycleNorms(...)` добавить:

```csharp
        // PZP-14: норматив и буфер — снапшот умолчаний; явно переданные значения важнее.
        var defaults = await _planningDefaults.GetAsync();
        task.SetSetupNorm(request.PlannedSetupMinutes ?? defaults.SetupMinutes);
        task.SetReleaseBuffer(request.ReleaseBufferFixedMinutes ?? defaults.ReleaseBufferFixedMinutes,
                              request.ReleaseBufferPercent ?? defaults.ReleaseBufferPercent);
```

и перед `_context.ShiftTasks.Add(task);` добавить `task.Mold = mold;` (для плана в ответе).

3. В `UpdateTask` перед `await _context.SaveChangesAsync();` добавить:

```csharp
        if (request.PlannedSetupMinutes.HasValue)
            task.SetSetupNorm(request.PlannedSetupMinutes.Value);
        if (request.ReleaseBufferFixedMinutes.HasValue || request.ReleaseBufferPercent.HasValue)
        {
            // Не переданная часть буфера сохраняет текущее значение задания.
            task.SetReleaseBuffer(request.ReleaseBufferFixedMinutes ?? task.ReleaseBufferFixedMinutes,
                                  request.ReleaseBufferPercent ?? task.ReleaseBufferPercent);
        }
```

- [ ] **Step 6: Убедиться, что тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~TaskPlanningNormsApiTests"`
Expected: PASS.

Run: `dotnet test Wintime.Control.Tests.Unit` и `dotnet test Wintime.Control.Tests.Integration`
Expected: PASS (регрессия: старые тесты заданий не сломались).

- [ ] **Step 7: Commit**

```bash
git add Wintime.Control.Core/DTOs/Tasks Wintime.Control.API/Controllers/TasksController.cs Wintime.Control.Tests.Integration/Tasks/TaskPlanningNormsApiTests.cs
git commit -m "feat(pzp-14): task API copies planning defaults, edits norms, returns plan

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Фронт — `utils/taskPlan.js`, `api/settings.js`, страница «Настройки»

**Files:**
- Create: `Wintime-Control-Frontend/src/utils/taskPlan.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/taskPlan.spec.js`
- Create: `Wintime-Control-Frontend/src/api/settings.js`
- Create: `Wintime-Control-Frontend/src/views/settings/SettingsView.vue`
- Test: `Wintime-Control-Frontend/src/views/settings/__tests__/SettingsView.spec.js`
- Modify: `Wintime-Control-Frontend/src/router/index.js`
- Modify: `Wintime-Control-Frontend/src/layouts/DefaultLayout.vue`
- Delete: `Wintime-Control-Frontend/src/api/admin.js`, `Wintime-Control-Frontend/src/views/admin/SettingsView.vue`

**Interfaces:**
- Consumes: `GET/PUT /api/settings/planning-defaults` (Task 3).
- Produces:
  - `calcTaskPlan({ planQuantity, cavities, fullCycleSeconds, setupMinutes, bufferFixedMinutes, bufferPercent }) → { setupSeconds, releaseSeconds, bufferSeconds, totalSeconds }` (последние три — `number | null`);
  - `formatDuration(seconds) → string` («25 мин», «2 ч», «1 ч 8 мин», `null` → «—»; минуты округляются вверх);
  - `settingsApi.getPlanningDefaults()`, `settingsApi.updatePlanningDefaults({ setupMinutes, releaseBufferFixedMinutes, releaseBufferPercent })`;
  - маршрут `/settings` (name `Settings`).

- [ ] **Step 1: Падающий тест утилиты**

`src/utils/__tests__/taskPlan.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import { calcTaskPlan, formatDuration } from '../taskPlan'

const base = { planQuantity: 100, cavities: 4, fullCycleSeconds: 60, setupMinutes: 30, bufferFixedMinutes: 10, bufferPercent: 10 }

describe('calcTaskPlan', () => {
  it('считает выпуск, буфер и итог как бэкенд', () => {
    expect(calcTaskPlan(base)).toEqual({ setupSeconds: 1800, releaseSeconds: 1500, bufferSeconds: 750, totalSeconds: 4050 })
  })

  it('неполное последнее смыкание округляется вверх', () => {
    expect(calcTaskPlan({ ...base, planQuantity: 101 }).releaseSeconds).toBe(26 * 60)
  })

  it('процентная часть буфера округляется вверх до секунды', () => {
    const plan = calcTaskPlan({ planQuantity: 1, cavities: 1, fullCycleSeconds: 33, setupMinutes: 0, bufferFixedMinutes: 0, bufferPercent: 10 })
    expect(plan.bufferSeconds).toBe(4)
  })

  it('процент берётся только от выпуска, без наладки', () => {
    const plan = calcTaskPlan({ planQuantity: 1, cavities: 1, fullCycleSeconds: 60, setupMinutes: 600, bufferFixedMinutes: 0, bufferPercent: 100 })
    expect(plan.bufferSeconds).toBe(60)
  })

  it.each([
    { fullCycleSeconds: null },
    { cavities: 0 },
    { cavities: undefined }
  ])('без эталона или гнёзд известна только наладка: %o', (patch) => {
    expect(calcTaskPlan({ ...base, ...patch })).toEqual({ setupSeconds: 1800, releaseSeconds: null, bufferSeconds: null, totalSeconds: null })
  })
})

describe('formatDuration', () => {
  it.each([
    [null, '—'],
    [0, '0 мин'],
    [1500, '25 мин'],
    [750, '13 мин'],
    [7200, '2 ч'],
    [4050, '1 ч 8 мин']
  ])('%s с → %s', (seconds, text) => {
    expect(formatDuration(seconds)).toBe(text)
  })
})
```

Run: `cd Wintime-Control-Frontend && npx vitest run src/utils/__tests__/taskPlan.spec.js`
Expected: FAIL — модуль `../taskPlan` не найден.

- [ ] **Step 2: Реализовать утилиту**

`src/utils/taskPlan.js`:

```js
// PZP-14: план задания — наладка → выпуск → буфер выпуска.
// Та же формула, что TaskPlanCalculator на бэкенде; здесь — только для живого предпросмотра
// в форме. В карточке задания показываем то, что посчитал сервер (TaskDto).

export function calcTaskPlan({ planQuantity, cavities, fullCycleSeconds, setupMinutes, bufferFixedMinutes, bufferPercent }) {
  const setupSeconds = (setupMinutes ?? 0) * 60
  if (!fullCycleSeconds || !cavities || cavities <= 0 || !planQuantity) {
    return { setupSeconds, releaseSeconds: null, bufferSeconds: null, totalSeconds: null }
  }
  const releaseSeconds = Math.ceil(planQuantity / cavities) * fullCycleSeconds
  const bufferSeconds = (bufferFixedMinutes ?? 0) * 60 + Math.ceil(releaseSeconds * (bufferPercent ?? 0) / 100)
  return { setupSeconds, releaseSeconds, bufferSeconds, totalSeconds: setupSeconds + releaseSeconds + bufferSeconds }
}

// Длительность для человека: минуты округляются вверх.
export function formatDuration(seconds) {
  if (seconds === null || seconds === undefined) return '—'
  const totalMinutes = Math.ceil(seconds / 60)
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  if (hours === 0) return `${minutes} мин`
  if (minutes === 0) return `${hours} ч`
  return `${hours} ч ${minutes} мин`
}
```

Run: `npx vitest run src/utils/__tests__/taskPlan.spec.js` — Expected: PASS.

- [ ] **Step 3: API-модуль**

`src/api/settings.js`:

```js
import apiClient from './client'

// Настройки приложения (ADR-0015). Сейчас — только умолчания планирования (PZP-14).
export const settingsApi = {
  getPlanningDefaults() {
    return apiClient.get('/settings/planning-defaults')
  },

  updatePlanningDefaults(data) {
    return apiClient.put('/settings/planning-defaults', data)
  }
}
```

- [ ] **Step 4: Падающий тест страницы**

`src/views/settings/__tests__/SettingsView.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import SettingsView from '../SettingsView.vue'

vi.mock('@/api/settings', () => ({
  settingsApi: {
    getPlanningDefaults: vi.fn(() => Promise.resolve({ data: {
      setupMinutes: 90, releaseBufferFixedMinutes: 30, releaseBufferPercent: 10, updatedAt: null
    } })),
    updatePlanningDefaults: vi.fn(() => Promise.resolve({ data: {} }))
  }
}))

import { settingsApi } from '@/api/settings'

function mountView() {
  return mount(SettingsView, { global: { plugins: [ElementPlus] } })
}

describe('SettingsView — умолчания планирования', () => {
  beforeEach(() => vi.clearAllMocks())

  it('загружает умолчания с сервера', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(settingsApi.getPlanningDefaults).toHaveBeenCalled()
    expect(wrapper.find('[data-test="setup-minutes"] input').element.value).toBe('90')
    expect(wrapper.find('[data-test="buffer-fixed-minutes"] input').element.value).toBe('30')
    expect(wrapper.find('[data-test="buffer-percent"] input').element.value).toBe('10')
  })

  it('«Сохранить» отправляет только три значения', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.defaults.setupMinutes = 120

    await wrapper.find('[data-test="save"]').trigger('click')
    await flushPromises()

    expect(settingsApi.updatePlanningDefaults).toHaveBeenCalledWith({
      setupMinutes: 120, releaseBufferFixedMinutes: 30, releaseBufferPercent: 10
    })
  })

  it('поясняет, что уже созданные задания не меняются', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Уже созданные задания не меняются')
  })
})
```

Run: `npx vitest run src/views/settings/__tests__/SettingsView.spec.js`
Expected: FAIL — `../SettingsView.vue` не найден.

- [ ] **Step 5: Страница**

`src/views/settings/SettingsView.vue`:

```vue
<template>
  <div>
    <div class="mb-6">
      <h2 class="text-2xl font-bold text-gray-800">Настройки</h2>
    </div>

    <el-card class="mb-6">
      <template #header>
        <span class="font-semibold">Умолчания планирования</span>
      </template>

      <p class="text-sm text-gray-600 mb-4">
        Значения подставляются в новые задания. Уже созданные задания не меняются.
      </p>

      <el-form v-loading="loading" :model="defaults" label-width="340px" label-position="left">
        <el-form-item label="Норматив наладки, мин">
          <el-input-number
            v-model="defaults.setupMinutes"
            data-test="setup-minutes"
            :min="0" :max="1440" :step="5"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item label="Буфер выпуска: фиксированная часть, мин">
          <el-input-number
            v-model="defaults.releaseBufferFixedMinutes"
            data-test="buffer-fixed-minutes"
            :min="0" :max="1440" :step="5"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item label="Буфер выпуска: % от времени выпуска">
          <el-input-number
            v-model="defaults.releaseBufferPercent"
            data-test="buffer-percent"
            :min="0" :max="100"
            controls-position="right"
          />
        </el-form-item>
      </el-form>
    </el-card>

    <div class="flex justify-end gap-4">
      <el-button @click="load">Отмена</el-button>
      <el-button type="primary" data-test="save" :loading="saving" @click="save">Сохранить</el-button>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { settingsApi } from '@/api/settings'
import { apiErrorMessage } from '@/utils/apiError'

const loading = ref(false)
const saving = ref(false)

const defaults = reactive({
  setupMinutes: 0,
  releaseBufferFixedMinutes: 0,
  releaseBufferPercent: 0
})

const load = async () => {
  loading.value = true
  try {
    const { data } = await settingsApi.getPlanningDefaults()
    defaults.setupMinutes = data.setupMinutes
    defaults.releaseBufferFixedMinutes = data.releaseBufferFixedMinutes
    defaults.releaseBufferPercent = data.releaseBufferPercent
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка загрузки настроек'))
  } finally {
    loading.value = false
  }
}

const save = async () => {
  saving.value = true
  try {
    await settingsApi.updatePlanningDefaults({
      setupMinutes: defaults.setupMinutes,
      releaseBufferFixedMinutes: defaults.releaseBufferFixedMinutes,
      releaseBufferPercent: defaults.releaseBufferPercent
    })
    ElMessage.success('Настройки сохранены')
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка сохранения настроек'))
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>
```

Run: `npx vitest run src/views/settings/__tests__/SettingsView.spec.js` — Expected: PASS.

- [ ] **Step 6: Маршрут, меню, удаление старого**

В `src/router/index.js` в дочернем массиве корневого маршрута удалить из блока `path: 'admin'` элемент

```js
          {
            path: 'settings',
            name: 'AdminSettings',
            component: () => import('@/views/admin/SettingsView.vue'),
            meta: { roles: ['Admin'], title: 'Настройки' }
          },
```

и добавить новый элемент на том же уровне, что и `path: 'admin'` (непосредственно перед ним):

```js
      {
        path: 'settings',
        name: 'Settings',
        component: () => import('@/views/settings/SettingsView.vue'),
        meta: { roles: ['Admin', 'Manager'], title: 'Настройки' }
      },
```

В `src/layouts/DefaultLayout.vue`:
- удалить строку `<el-menu-item index="/admin/settings">Настройки</el-menu-item>`;
- сразу после блока `<!-- Смены отдельно для Observer … -->` (перед `<el-sub-menu v-if="canAccess(['Admin'])" index="admin">`) добавить:

```vue
          <!-- Настройки приложения (PZP-14): умолчания планирования -->
          <el-menu-item v-if="canAccess(['Admin', 'Manager'])" index="/settings">
            <el-icon><Tools /></el-icon>
            <span>Настройки</span>
          </el-menu-item>
```

(Иконки Element Plus зарегистрированы глобально в `main.js`.)

Удалить старое:

```bash
git rm Wintime-Control-Frontend/src/api/admin.js Wintime-Control-Frontend/src/views/admin/SettingsView.vue
```

Run: `git grep -n -E "admin/settings|api/admin|AdminSettings" -- Wintime-Control-Frontend/src`
Expected: совпадений нет.

- [ ] **Step 7: Полный прогон фронта и сборка**

Run: `npx vitest run` — Expected: PASS.
Run: `npm run build` — Expected: сборка без ошибок.

- [ ] **Step 8: Commit**

```bash
git add Wintime-Control-Frontend/src/utils/taskPlan.js Wintime-Control-Frontend/src/utils/__tests__/taskPlan.spec.js Wintime-Control-Frontend/src/api/settings.js Wintime-Control-Frontend/src/views/settings Wintime-Control-Frontend/src/router/index.js Wintime-Control-Frontend/src/layouts/DefaultLayout.vue
git commit -m "feat(pzp-14): settings page with planning defaults; drop fake MQTT settings

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Фронт — форма задания (поля, предзаполнение, предпросмотр, режим «только буфер»), карточка и список

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/tasks/TaskFormModal.vue`
- Modify: `Wintime-Control-Frontend/src/views/tasks/TaskDetailModal.vue`
- Modify: `Wintime-Control-Frontend/src/views/tasks/TasksView.vue`
- Test: `Wintime-Control-Frontend/src/views/tasks/__tests__/TaskFormModal.spec.js`
- Test: `Wintime-Control-Frontend/src/views/tasks/__tests__/TaskDetailModal.spec.js`

**Interfaces:**
- Consumes: `calcTaskPlan`, `formatDuration` (`@/utils/taskPlan`, Task 5); `settingsApi.getPlanningDefaults` (Task 5); поля `TaskDto` из Task 4.
- Produces: форма шлёт `plannedSetupMinutes`, `releaseBufferFixedMinutes`, `releaseBufferPercent` в `tasksApi.create`/`tasksApi.update`.

- [ ] **Step 1: Падающие тесты формы**

В `src/views/tasks/__tests__/TaskFormModal.spec.js`:

1. Рядом с остальными `vi.mock` добавить мок настроек (нужен и старым тестам — иначе форма при создании идёт в сеть):

```js
vi.mock('@/api/settings', () => ({
  settingsApi: {
    getPlanningDefaults: vi.fn(() => Promise.resolve({ data: {
      setupMinutes: 30, releaseBufferFixedMinutes: 10, releaseBufferPercent: 10, updatedAt: null
    } }))
  }
}))
```

и импорт `import { settingsApi } from '@/api/settings'` рядом с остальными импортами моков.

2. В конец файла добавить:

```js
describe('TaskFormModal — норматив наладки и буфер выпуска (PZP-14)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('при создании предзаполняет поля умолчаниями', async () => {
    const wrapper = mountModal()
    await flushPromises()

    expect(settingsApi.getPlanningDefaults).toHaveBeenCalled()
    expect(wrapper.vm.form.plannedSetupMinutes).toBe(30)
    expect(wrapper.vm.form.releaseBufferFixedMinutes).toBe(10)
    expect(wrapper.vm.form.releaseBufferPercent).toBe(10)
  })

  it('при редактировании умолчания не запрашивает и берёт значения задания', async () => {
    const wrapper = mountModal({ task: {
      id: 't1', status: 'Draft', immId: 'imm-1', moldId: 'mold-1', planQuantity: 100,
      plannedDate: '2026-10-20', plannedFullCycleSeconds: 60,
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 5, releaseBufferPercent: 20
    } })
    await flushPromises()

    expect(settingsApi.getPlanningDefaults).not.toHaveBeenCalled()
    expect(wrapper.vm.form.plannedSetupMinutes).toBe(45)
    expect(wrapper.vm.form.releaseBufferFixedMinutes).toBe(5)
    expect(wrapper.vm.form.releaseBufferPercent).toBe(20)
  })

  it('показывает предпросмотр выпуска, буфера и итога', async () => {
    const wrapper = mountModal()
    await flushPromises()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.moldId = 'mold-1'          // 4 гнезда
    wrapper.vm.form.planQuantity = 100
    wrapper.vm.form.plannedFullCycleSeconds = 60
    await flushPromises()

    const preview = wrapper.find('[data-test="plan-preview"]').text()
    expect(preview).toContain('Выпуск ≈ 25 мин')
    expect(preview).toContain('буфер ≈ 13 мин')
    expect(preview).toContain('всего с наладкой ≈ 1 ч 8 мин')
  })

  it('в «В работе» доступен только буфер', async () => {
    const wrapper = mountModal({ task: {
      id: 't1', status: 'InProgress', immId: 'imm-1', moldId: 'mold-1', planQuantity: 100,
      plannedDate: '2026-10-20', plannedFullCycleSeconds: 60,
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 5, releaseBufferPercent: 20
    } })
    await flushPromises()

    expect(wrapper.find('[data-test="plan-quantity"]').classes()).toContain('is-disabled')
    expect(wrapper.find('[data-test="setup-minutes"]').classes()).toContain('is-disabled')
    expect(wrapper.find('[data-test="buffer-fixed-minutes"]').classes()).not.toContain('is-disabled')
    expect(wrapper.find('[data-test="buffer-percent"]').classes()).not.toContain('is-disabled')
  })

  it('в «Выдано» доступны и норматив, и буфер', async () => {
    const wrapper = mountModal({ task: {
      id: 't1', status: 'Issued', immId: 'imm-1', moldId: 'mold-1', planQuantity: 100,
      plannedDate: '2026-10-20', plannedFullCycleSeconds: 60, personnelId: 'p1',
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 5, releaseBufferPercent: 20
    } })
    await flushPromises()

    expect(wrapper.find('[data-test="plan-quantity"]').classes()).toContain('is-disabled')
    expect(wrapper.find('[data-test="setup-minutes"]').classes()).not.toContain('is-disabled')
    expect(wrapper.find('[data-test="buffer-fixed-minutes"]').classes()).not.toContain('is-disabled')
  })

  it('при сохранении шлёт норматив и буфер', async () => {
    const wrapper = mountModal({ task: {
      id: 't1', status: 'InProgress', immId: 'imm-1', moldId: 'mold-1', planQuantity: 100,
      plannedDate: '2026-10-20', plannedFullCycleSeconds: 60,
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 5, releaseBufferPercent: 20
    } })
    await flushPromises()
    wrapper.vm.form.releaseBufferFixedMinutes = 40

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.update).toHaveBeenCalledWith('t1', expect.objectContaining({
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 40, releaseBufferPercent: 20
    }))
  })
})
```

Run: `npx vitest run src/views/tasks/__tests__/TaskFormModal.spec.js`
Expected: новые тесты FAIL (полей и `data-test` нет); старые — PASS.

- [ ] **Step 2: Реализовать в `TaskFormModal.vue`**

Скрипт:

1. Импорты:

```js
import { settingsApi } from '@/api/settings'
import { calcTaskPlan, formatDuration } from '@/utils/taskPlan'
```

2. В `reactive form` и в `resetForm` (оба места) добавить после `plannedInjectionCycleSeconds`:

```js
  plannedSetupMinutes: 0,
  releaseBufferFixedMinutes: 0,
  releaseBufferPercent: 0
```

3. В `populateForm` добавить после `plannedInjectionCycleSeconds`:

```js
    plannedSetupMinutes: task.plannedSetupMinutes ?? 0,
    releaseBufferFixedMinutes: task.releaseBufferFixedMinutes ?? 0,
    releaseBufferPercent: task.releaseBufferPercent ?? 0
```

4. Заменить

```js
// PZP-13: в «Выдано» правятся только плановое начало и наладчик
const isLimitedEdit = computed(() => editingTask.value?.status === 'Issued')
```

на

```js
// PZP-13/14: в «Выдано» правятся плановое начало, наладчик, норматив наладки и буфер
const isLimitedEdit = computed(() => editingTask.value?.status === 'Issued')
// PZP-14: после начала наладки и до завершения выпуска правится только буфер выпуска
const isBufferOnlyEdit = computed(() => ['Setup', 'InProgress'].includes(editingTask.value?.status))
// Поля, закрытые и в «Выдано», и в режиме «только буфер»
const isRestricted = computed(() => isLimitedEdit.value || isBufferOnlyEdit.value)

// PZP-14: живой предпросмотр плана (гнёзда — из выбранной ПФ)
const planPreview = computed(() => {
  const plan = calcTaskPlan({
    planQuantity: form.planQuantity,
    cavities: selectedMoldInfo.value?.cavities,
    fullCycleSeconds: form.plannedFullCycleSeconds,
    setupMinutes: form.plannedSetupMinutes,
    bufferFixedMinutes: form.releaseBufferFixedMinutes,
    bufferPercent: form.releaseBufferPercent
  })
  return `Выпуск ≈ ${formatDuration(plan.releaseSeconds)} · буфер ≈ ${formatDuration(plan.bufferSeconds)}`
    + ` · всего с наладкой ≈ ${formatDuration(plan.totalSeconds)}`
})

// PZP-14: новое задание получает умолчания из настроек; менеджер может их поправить
const loadPlanningDefaults = async () => {
  try {
    const { data } = await settingsApi.getPlanningDefaults()
    form.plannedSetupMinutes = data.setupMinutes
    form.releaseBufferFixedMinutes = data.releaseBufferFixedMinutes
    form.releaseBufferPercent = data.releaseBufferPercent
  } catch (error) {
    ElMessage.error('Ошибка загрузки умолчаний планирования')
  }
}
```

`selectedMoldInfo` объявлен ниже `isLimitedEdit` — перенеси объявление `planPreview` после `selectedMoldInfo` (computed ленивый, но держим порядок чтения сверху вниз).

5. После `watch(() => props.task, …)` добавить:

```js
watch(() => props.modelValue && !props.task, (creating) => {
  if (creating) loadPlanningDefaults()
}, { immediate: true })
```

Шаблон:

6. Заменить `:disabled="isLimitedEdit"` на `:disabled="isRestricted"` у полей: «Заказ» (`isLimitedEdit || !!lockedOrder || !selectedProductTypeId` → `isRestricted || !!lockedOrder || !selectedProductTypeId`), «План (шт.)», «Рабочий режим», «Полный цикл», «Цикл литья», «Примечание».
7. У «План (шт.)» добавить `data-test="plan-quantity"`.
8. У селекта «Наладчик» добавить `:disabled="isBufferOnlyEdit"`; у `el-date-picker` «Плановая дата» и `el-time-picker` «Время начала наладки» — `:disabled="isBufferOnlyEdit"`.
9. После `el-form-item` «Цикл литья (сек.)» добавить:

```vue
      <el-form-item label="Норматив наладки, мин" prop="plannedSetupMinutes">
        <el-input-number
          v-model="form.plannedSetupMinutes"
          data-test="setup-minutes"
          :min="0" :max="1440" :step="5"
          :disabled="isBufferOnlyEdit"
          class="w-full"
          controls-position="right"
        />
      </el-form-item>

      <el-form-item label="Буфер выпуска, мин" prop="releaseBufferFixedMinutes">
        <el-input-number
          v-model="form.releaseBufferFixedMinutes"
          data-test="buffer-fixed-minutes"
          :min="0" :max="1440" :step="5"
          class="w-full"
          controls-position="right"
        />
      </el-form-item>

      <el-form-item label="Буфер выпуска, % от выпуска" prop="releaseBufferPercent">
        <el-input-number
          v-model="form.releaseBufferPercent"
          data-test="buffer-percent"
          :min="0" :max="100"
          class="w-full"
          controls-position="right"
        />
        <div class="text-xs text-gray-500 mt-1" data-test="plan-preview">{{ planPreview }}</div>
      </el-form-item>
```

`handleSubmit` не меняется: новые поля входят в `form` и уходят в `create`/`update` вместе с остальными. Неизменённые поля бэкенд принимает как no-op в любом статусе (покрыто `UpdateTask_InProgress_FullFormPayloadWithBufferChange_Succeeds`).

Run: `npx vitest run src/views/tasks/__tests__/TaskFormModal.spec.js` — Expected: PASS (все, включая старые).

- [ ] **Step 3: Падающий тест карточки**

В `src/views/tasks/__tests__/TaskDetailModal.spec.js` добавить в начало импорт `import dayjs from 'dayjs'` и в конец файла:

```js
describe('TaskDetailModal — план задания (PZP-14)', () => {
  const cellText = (wrapper, label) => {
    const cells = wrapper.findAll('.el-descriptions__cell')
    const idx = cells.findIndex(c => c.text() === label)
    return cells[idx + 1].text()
  }

  it('показывает норматив, выпуск, буфер с расшифровкой и плановое окончание', async () => {
    const wrapper = mountModal({
      id: 't1', status: 'Issued',
      plannedSetupMinutes: 30, releaseBufferFixedMinutes: 10, releaseBufferPercent: 10,
      plannedReleaseSeconds: 1500, plannedReleaseBufferSeconds: 750,
      plannedEndAt: '2026-10-15T06:07:30Z'
    })
    await flushPromises()

    expect(cellText(wrapper, 'Норматив наладки')).toBe('30 мин')
    expect(cellText(wrapper, 'Выпуск (расчёт)')).toBe('25 мин')
    expect(cellText(wrapper, 'Буфер выпуска')).toBe('13 мин (10 мин + 10 %)')
    expect(cellText(wrapper, 'Плановое окончание'))
      .toBe(dayjs('2026-10-15T06:07:30Z').format('DD.MM.YYYY HH:mm'))
  })

  it('без расчёта выпуска показывает прочерки, норматив — всегда', async () => {
    const wrapper = mountModal({
      id: 't1', status: 'Draft',
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 10, releaseBufferPercent: 10,
      plannedReleaseSeconds: null, plannedReleaseBufferSeconds: null, plannedEndAt: null
    })
    await flushPromises()

    expect(cellText(wrapper, 'Норматив наладки')).toBe('45 мин')
    expect(cellText(wrapper, 'Выпуск (расчёт)')).toBe('—')
    expect(cellText(wrapper, 'Буфер выпуска')).toBe('—')
    expect(cellText(wrapper, 'Плановое окончание')).toBe('—')
  })
})
```

Run: `npx vitest run src/views/tasks/__tests__/TaskDetailModal.spec.js`
Expected: новые тесты FAIL.

- [ ] **Step 4: Реализовать в `TaskDetailModal.vue`**

1. Импорт: `import { formatDuration } from '@/utils/taskPlan'`.
2. В `<el-descriptions>` после элемента «Цикл литья» добавить:

```vue
        <el-descriptions-item label="Норматив наладки">
          {{ task ? formatDuration((task.plannedSetupMinutes ?? 0) * 60) : '—' }}
        </el-descriptions-item>
        <el-descriptions-item label="Выпуск (расчёт)">
          {{ formatDuration(task?.plannedReleaseSeconds) }}
        </el-descriptions-item>
        <el-descriptions-item label="Буфер выпуска">
          {{ bufferLabel }}
        </el-descriptions-item>
        <el-descriptions-item label="Плановое окончание">
          {{ formatDate(task?.plannedEndAt) }}
        </el-descriptions-item>
```

3. В скрипт после `workModeLabel` добавить:

```js
// PZP-14: буфер с расшифровкой формулы «фикс. мин + % от выпуска»
const bufferLabel = computed(() => {
  const seconds = props.task?.plannedReleaseBufferSeconds
  if (seconds === null || seconds === undefined) return '—'
  return `${formatDuration(seconds)} (${props.task.releaseBufferFixedMinutes} мин + ${props.task.releaseBufferPercent} %)`
})
```

4. Заменить в `canEdit`

```js
  return ['Draft', 'Issued'].includes(props.task.status) && authStore.isManager
```

на

```js
  // PZP-14: в «Наладке»/«В работе» форма открывается в режиме «только буфер»
  return ['Draft', 'Issued', 'Setup', 'InProgress'].includes(props.task.status) && authStore.isManager
```

Run: `npx vitest run src/views/tasks/__tests__/TaskDetailModal.spec.js` — Expected: PASS.

- [ ] **Step 5: Кнопка «Ред.» в списке**

В `src/views/tasks/TasksView.vue` заменить

```js
const canEditTask = (task) => {
  return ['Draft', 'Issued'].includes(task.status) && canCreate.value
}
```

на

```js
// PZP-14: в «Наладке»/«В работе» правится только буфер выпуска
const canEditTask = (task) => {
  return ['Draft', 'Issued', 'Setup', 'InProgress'].includes(task.status) && canCreate.value
}
```

- [ ] **Step 6: Полный прогон фронта и сборка**

Run: `npx vitest run` — Expected: PASS.
Run: `npm run build` — Expected: без ошибок.

- [ ] **Step 7: Commit**

```bash
git add Wintime-Control-Frontend/src/views/tasks
git commit -m "feat(pzp-14): task form norms with defaults and preview, buffer-only edit, plan in card

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Документация и финальная проверка

**Files:**
- Create: `docs/adr/0015-app-settings-storage.md`
- Modify: `docs/adr/README.md`
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `docs/user-guide/manager.md`

**Interfaces:**
- Consumes: итоговые имена из задач 1–6.

- [ ] **Step 1: ADR-0015**

`docs/adr/0015-app-settings-storage.md`:

```markdown
# ADR-0015: Хранилище настроек приложения — типизированная таблица из одной строки на группу

- **Статус:** Accepted
- **Дата:** 2026-10-12
- **Задача:** PZP-14

## Контекст

Норматив наладки и буфер выпуска задания (PZP-14) должны по умолчанию подставляться из
настроек, которые интегратор задаёт после установки, а менеджер меняет из UI. Хранилища
настроек не было: `GET /api/admin/settings` отдавал захардкоженные значения, `PUT` был
пустышкой, карточка MQTT на странице «Настройки» ничего не сохраняла. Инфраструктурные
параметры (MQTT, JWT, порог простоя) живут в `appsettings.json`/env.

## Решение

- В БД храним только **бизнес-настройки**. Инфраструктура остаётся в `appsettings`/env.
- **Группа настроек — отдельная типизированная таблица из одной строки**: колонки нужных типов,
  `CHECK ("Id" = 1)`, check-ограничения пределов; строка создаётся миграцией (`HasData`).
  Первая группа — `PlanningDefaults` (`SetupMinutes`, `ReleaseBufferFixedMinutes`,
  `ReleaseBufferPercent`, `UpdatedAt`, `UpdatedById`).
- Доступ — только через сервис группы (`IPlanningDefaultsService`); потребители не знают о
  способе хранения. Без кеша, пока настройки читаются редко.
- API — `/api/settings/<группа>`, права задаются на группу (умолчания планирования —
  `ManagerOrAdmin`).
- Фейковые настройки удалены: `AdminController`, `SystemSettingsDto`,
  `UpdateSystemSettingsRequestDto`, карточка MQTT (BL-31).

## Альтернативы

- **Key-value `AppSetting(Key, Value)`** — отвергнуто: нет типизации, ключи-строки, валидация и
  умолчания размазаны по коду.
- **Секция как JSON (`jsonb`)** — отвергнуто: поля добавляются молча, целостность держит только
  код, нужна аккуратная десериализация старого JSON. Миграции на новое поле ничего не стоят.
- **Перенос инфраструктуры (MQTT, порог простоя) в БД** — отвергнуто: нужно переподключение и
  чтение на лету с инвалидацией, пароль брокера в БД; потребности нет.

## Последствия

- Новая настройка = миграция + поле в сущности группы; новая группа = новая таблица и сервис.
- Схема сама документирует настройки, БД держит пределы.
- Если понадобится чтение в горячем пути (воркеры, хендлеры), сервис группы получает кеш без
  изменения потребителей.
```

В `docs/adr/README.md` после строки ADR-0014 добавить:

```markdown
- [ADR-0015](0015-app-settings-storage.md) — Хранилище настроек приложения: типизированная таблица из одной строки на группу
```

- [ ] **Step 2: CLAUDE.md**

В разделе «### Плановое начало и выдача задания» после абзаца, заканчивающегося «См. ADR-0014.», добавить абзац:

```markdown
**Норматив наладки и буфер выпуска** (PZP-14) — снапшот умолчаний `PlanningDefaults` в задании:
`PlannedSetupMinutes`, `ReleaseBufferFixedMinutes` (минуты 0..1440), `ReleaseBufferPercent`
(целое 0..100, % от плановой длительности **выпуска**, без наладки). Смена умолчаний задания не
трогает. Норматив правится в `Draft`/`Issued`, буфер — до завершения выпуска
(`Draft`…`InProgress`). План задания (выпуск `⌈План/Гнёзда⌉ × PlannedFullCycleSeconds`, буфер,
`TaskDto.PlannedEndAt`) считает `TaskPlanCalculator`, **не хранится**; гнёзда — текущие
`Mold.Cavities` (это прогноз, не история). Настройки приложения — по ADR-0015.
```

- [ ] **Step 3: README.md**

В таблице контроллеров заменить строку

```markdown
| `AdminController` | `/api/admin` | Admin-only operations |
```

на

```markdown
| `SettingsController` | `/api/settings` | App settings (planning defaults), Manager/Admin |
```

- [ ] **Step 4: Руководство менеджера**

В `docs/user-guide/manager.md`:

1. В таблицу раздела «5.3. Создание задания» после строки «**Цикл литья (сек.)**» добавить:

```markdown
| **Норматив наладки, мин** | Сколько времени отводится на наладку. Подставляется из настроек, можно изменить |
| **Буфер выпуска, мин** | Фиксированный запас после выпуска: демонтаж и промывка ПФ, уборка, очистка цилиндра. Подставляется из настроек |
| **Буфер выпуска, % от выпуска** | Запас на риск затяжки выпуска — процент от расчётного времени выпуска (без наладки). Подставляется из настроек |
```

и после пункта 3 списка («Под формой показана справка …») добавить пункт:

```markdown
   Под полями буфера форма показывает расчёт: время выпуска, буфер и итог вместе с наладкой.
   Плановое окончание задания видно в окне деталей после сохранения.
```

2. Раздел «5.4. Редактирование задания» — первое предложение заменить на:

```markdown
Изменять можно задания в статусах **«Планируется»**, **«Выдано»**, **«Наладка»** и **«В работе»**. В статусе **«Планируется»** правятся все поля. В статусе **«Выдано»** — плановая дата, время начала наладки, наладчик, норматив наладки и буфер выпуска. Наладчика можно переназначить, но нельзя снять. В статусах **«Наладка»** и **«В работе»** правится только буфер выпуска — например, если выпуск затягивается.
```

(Остальной текст раздела — без изменений.)

3. В раздел «5.6. Детали задания …» добавить в конец абзац:

```markdown
В окне деталей показан план задания: норматив наладки, расчётное время выпуска, буфер выпуска
(с расшифровкой «минуты + процент») и плановое окончание. Если у задания нет эталона полного
цикла, выпуск, буфер и окончание не рассчитываются — показан прочерк.
```

4. Перед разделом «## Приложение. Частые сообщения» добавить раздел и строку в «Содержание» (`- [11. Настройки](#11-настройки)` после пункта 10):

```markdown
## 11. Настройки

**Меню:** **Настройки**.

Здесь задаются умолчания планирования — значения, которые подставляются в каждое новое задание:

- **Норматив наладки, мин**;
- **Буфер выпуска: фиксированная часть, мин**;
- **Буфер выпуска: % от времени выпуска**.

Сразу после установки все значения равны 0 — их задаёт интегратор по данным вашего производства.
Изменение умолчаний не меняет уже созданные задания. Допустимые значения: минуты — от 0 до 1440,
процент — от 0 до 100.
```

- [ ] **Step 5: Финальная проверка**

Run: `dotnet build Wintime.Control.sln` — Expected: Build succeeded, без новых предупреждений.
Run: `dotnet test Wintime.Control.Tests.Unit` — Expected: PASS.
Run: `dotnet test Wintime.Control.Tests.Integration` — Expected: PASS.
Run: `cd Wintime-Control-Frontend && npx vitest run && npm run build` — Expected: PASS, сборка без ошибок.

- [ ] **Step 6: Commit**

```bash
git add docs/adr/0015-app-settings-storage.md docs/adr/README.md CLAUDE.md README.md docs/user-guide/manager.md
git commit -m "docs(pzp-14): ADR-0015 settings storage, CLAUDE.md invariant, user guide

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
