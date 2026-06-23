# Эффективное состояние ТПА и автоматические простои — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Подчинить обработку циклов/выпуска и автоматическое создание простоев «эффективному состоянию ТПА» = f(MQTT mode + статус активного задания + открытый простой).

**Architecture:** Чистые доменные функции в `Core` (политика обработки циклов, эффективное состояние, решение о простое) кодируют матрицу `docs/details/Состояния_ТПА.xlsx`. `CycleProcessingHandler` начинает гейтить обработку по статусу задания. Новый polling-воркер `DowntimeDetectionWorker` (по образцу `ImmOfflineWorker`) создаёт/закрывает `Event` типа `Downtime`. `Event` получает поля `TaskId`, `Comment`, `IsAuto`; `DowntimeController` доработан для редактирования простоев наладчиком.

**Tech Stack:** .NET 9, EF Core 9 + Npgsql, xUnit + FluentAssertions + NSubstitute, ASP.NET Core BackgroundService, Options pattern.

**Спецификация:** [docs/superpowers/specs/2026-06-23-effective-imm-state-and-auto-downtime-design.md](../specs/2026-06-23-effective-imm-state-and-auto-downtime-design.md)

## Global Constraints

- **DateTime → PostgreSQL:** все `DateTime` в EF-запросах обязаны иметь `Kind=Utc`. Параметры из query/body конвертировать через `DateTime.SpecifyKind(x, DateTimeKind.Utc)` сразу при получении (см. CLAUDE.md).
- **Роль = только `User.Role`:** авторизация через `[Authorize(Roles=…)]` по константам `Wintime.Control.Shared.Constants.Roles`. Никаких Identity-ролей.
- **Сравнение режима:** только через `ImmMode.Normalize(mode)`, не со строковым литералом напрямую.
- **Мягкое удаление:** сущности не удаляются физически. FK `Event.TaskId` использует `OnDelete(DeleteBehavior.SetNull)` — по образцу существующего `ImmCycle.TaskId`.
- **Каноны статусов/режимов:** режим — `ImmMode` (`auto/manual/idle/alarm`, нижний регистр); статус — `ImmStatus` (`Auto/Manual/Idle/Alarm/Offline`).
- **Frequent commits:** один коммит на задачу (после прохождения тестов задачи).

## File Structure

**Создаются:**
- `Wintime.Control.Core/Enums/ActiveTaskStatus.cs` — enum активности задания + маппинг из `TaskStatus`.
- `Wintime.Control.Core/Policies/CycleProcessingPolicy.cs` — pure: обрабатывать ли цикл / учитывать ли выпуск.
- `Wintime.Control.Core/Policies/ImmEffectiveStatus.cs` — pure: эффективное состояние для дашборда.
- `Wintime.Control.Core/Policies/DowntimeDecision.cs` — pure: открыть/закрыть/ничего по авто-простою.
- `Wintime.Control.Shared/Settings/DowntimeSettings.cs` — порог и период опроса.
- `Wintime.Control.Infrastructure/Workers/DowntimeDetectionWorker.cs` — фоновый воркер простоев.
- `Wintime.Control.Tests.Unit/Policies/CycleProcessingPolicyTests.cs`
- `Wintime.Control.Tests.Unit/Policies/ImmEffectiveStatusTests.cs`
- `Wintime.Control.Tests.Unit/Policies/DowntimeDecisionTests.cs`
- `Wintime.Control.Tests.Unit/Workers/DowntimeDetectionWorkerTests.cs`
- `Wintime.Control.Tests.Integration/Downtime/UpdateDowntimeEventTests.cs`

**Модифицируются:**
- `Wintime.Control.Core/Entities/Event.cs` — поля `TaskId`, `Comment`, `IsAuto`, nav `Task`.
- `Wintime.Control.Core/DTOs/Downtime/EventDto.cs` — `TaskId`, `Comment`, `IsAuto`.
- `Wintime.Control.Core/DTOs/Downtime/UpdateDowntimeEventRequestDto.cs` — `ReasonId?`, `EndTime?`, `Comment?`.
- `Wintime.Control.Infrastructure/Data/ControlDbContext.cs` — FK `Event.TaskId`.
- `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs` — гейтинг по политике.
- `Wintime.Control.Infrastructure/Services/InfrastructureServiceExtensions.cs` — регистрация воркера.
- `Wintime.Control.API/Program.cs` — `Configure<DowntimeSettings>`.
- `Wintime.Control.API/Controllers/DowntimeController.cs` — PATCH-переработка, `IsAuto`, поля DTO.
- `Wintime.Control.API/appsettings.json` — секция `Downtime`.
- `Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs` — кейсы `Setup` и открытого простоя.

---

### Task 1: Enum активности задания `ActiveTaskStatus`

**Files:**
- Create: `Wintime.Control.Core/Enums/ActiveTaskStatus.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/CycleProcessingPolicyTests.cs` (создаётся в Task 2; маппинг проверяется там же)

**Interfaces:**
- Produces: `enum ActiveTaskStatus { None, Setup, InProgress }`; `ActiveTaskStatusMap.From(TaskStatus? status) -> ActiveTaskStatus`.

- [ ] **Step 1: Создать enum и маппер**

`Wintime.Control.Core/Enums/ActiveTaskStatus.cs`:

```csharp
namespace Wintime.Control.Core.Enums;

/// <summary>
/// Проекция <see cref="TaskStatus"/> на «активность» задания для конвейера телеметрии.
/// Активным считается задание в статусе Setup (наладка) или InProgress (производство);
/// всё остальное (нет задания, Draft/Issued/Completed/Closed) — None.
/// </summary>
public enum ActiveTaskStatus
{
    None,
    Setup,
    InProgress
}

/// <summary>
/// Маппинг доменного <see cref="TaskStatus"/> в <see cref="ActiveTaskStatus"/>.
/// Единственная точка, определяющая, какое задание «активно» для обработки телеметрии.
/// </summary>
public static class ActiveTaskStatusMap
{
    public static ActiveTaskStatus From(TaskStatus? status) => status switch
    {
        TaskStatus.Setup => ActiveTaskStatus.Setup,
        TaskStatus.InProgress => ActiveTaskStatus.InProgress,
        _ => ActiveTaskStatus.None
    };
}
```

- [ ] **Step 2: Собрать проект Core**

Run: `dotnet build Wintime.Control.Core`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Wintime.Control.Core/Enums/ActiveTaskStatus.cs
git commit -m "feat: ActiveTaskStatus enum + маппинг из TaskStatus"
```

---

### Task 2: Политика обработки циклов `CycleProcessingPolicy` (pure)

Кодирует колонки «Обработка: цикл/смыкания» и «Обработка: выпуск» из `Состояния_ТПА.xlsx`.

**Files:**
- Create: `Wintime.Control.Core/Policies/CycleProcessingPolicy.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/CycleProcessingPolicyTests.cs`

**Interfaces:**
- Consumes: `ActiveTaskStatus` (Task 1), `ImmMode.Normalize`.
- Produces:
  - `CycleProcessingPolicy.ShouldProcessCycle(string mode, ActiveTaskStatus task) -> bool`
  - `CycleProcessingPolicy.ShouldCountOutput(string mode, ActiveTaskStatus task, bool hasOpenDowntime) -> bool`

- [ ] **Step 1: Написать падающий тест (16 сценариев документа)**

`Wintime.Control.Tests.Unit/Policies/CycleProcessingPolicyTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class CycleProcessingPolicyTests
{
    // Строки из docs/details/Состояния_ТПА.xlsx.
    // command → (task, hasOpenDowntime): Наладка→Setup; Работа→InProgress,false;
    // Простой(ручной)→InProgress,true; "-"→None,false.
    [Theory]
    // signal, task, hasOpenDowntime, expectedCycle, expectedOutput
    [InlineData("idle",   ActiveTaskStatus.Setup,      false, false, false)] // 1
    [InlineData("idle",   ActiveTaskStatus.InProgress, false, true,  false)] // 2
    [InlineData("idle",   ActiveTaskStatus.InProgress, true,  true,  false)] // 3
    [InlineData("idle",   ActiveTaskStatus.None,       false, false, false)] // 3а
    [InlineData("alarm",  ActiveTaskStatus.Setup,      false, false, false)] // 4
    [InlineData("alarm",  ActiveTaskStatus.InProgress, false, true,  false)] // 5
    [InlineData("alarm",  ActiveTaskStatus.InProgress, true,  true,  false)] // 6
    [InlineData("alarm",  ActiveTaskStatus.None,       false, false, false)] // alarm,-
    [InlineData("manual", ActiveTaskStatus.Setup,      false, false, false)] // 7
    [InlineData("manual", ActiveTaskStatus.InProgress, false, true,  false)] // 8
    [InlineData("manual", ActiveTaskStatus.InProgress, true,  true,  false)] // 9
    [InlineData("manual", ActiveTaskStatus.None,       false, false, false)] // 9а
    [InlineData("auto",   ActiveTaskStatus.Setup,      false, false, false)] // 10
    [InlineData("auto",   ActiveTaskStatus.InProgress, false, true,  true )] // 11 ← единственный выпуск
    [InlineData("auto",   ActiveTaskStatus.InProgress, true,  true,  false)] // 12
    [InlineData("auto",   ActiveTaskStatus.None,       false, true,  false)] // 12а
    public void Matrix_MatchesSpecDocument(
        string signal, ActiveTaskStatus task, bool hasOpenDowntime,
        bool expectedCycle, bool expectedOutput)
    {
        CycleProcessingPolicy.ShouldProcessCycle(signal, task)
            .Should().Be(expectedCycle);
        CycleProcessingPolicy.ShouldCountOutput(signal, task, hasOpenDowntime)
            .Should().Be(expectedOutput);
    }

    [Fact]
    public void ShouldProcessCycle_NormalizesMode_UppercaseAutoNoTask_True()
    {
        CycleProcessingPolicy.ShouldProcessCycle("AUTO", ActiveTaskStatus.None)
            .Should().BeTrue();
    }
}
```

- [ ] **Step 2: Запустить тест — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter CycleProcessingPolicyTests`
Expected: FAIL — `CycleProcessingPolicy` не существует (ошибка компиляции).

- [ ] **Step 3: Реализовать политику**

`Wintime.Control.Core/Policies/CycleProcessingPolicy.cs`:

```csharp
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Правила обработки циклов и учёта выпуска по матрице docs/details/Состояния_ТПА.xlsx.
/// Чистые функции: решают, писать ли цикл (ImmCycle) и увеличивать ли выработку задания.
/// </summary>
public static class CycleProcessingPolicy
{
    /// <summary>
    /// Обрабатывать ли смыкание (писать ImmCycle).
    /// InProgress — всегда; нет задания — только при auto; Setup (наладка) — никогда.
    /// </summary>
    public static bool ShouldProcessCycle(string mode, ActiveTaskStatus task) => task switch
    {
        ActiveTaskStatus.InProgress => true,
        ActiveTaskStatus.None => ImmMode.Normalize(mode) == ImmMode.Auto,
        _ => false // Setup
    };

    /// <summary>
    /// Учитывать ли выпуск (ActualQuantity / материал задания).
    /// Только: задание InProgress И режим auto И нет открытого простоя.
    /// </summary>
    public static bool ShouldCountOutput(string mode, ActiveTaskStatus task, bool hasOpenDowntime) =>
        task == ActiveTaskStatus.InProgress
        && ImmMode.Normalize(mode) == ImmMode.Auto
        && !hasOpenDowntime;
}
```

- [ ] **Step 4: Запустить тест — убедиться, что проходит**

Run: `dotnet test Wintime.Control.Tests.Unit --filter CycleProcessingPolicyTests`
Expected: PASS (17 тестов).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/CycleProcessingPolicy.cs Wintime.Control.Tests.Unit/Policies/CycleProcessingPolicyTests.cs
git commit -m "feat: CycleProcessingPolicy — матрица обработки циклов/выпуска"
```

---

### Task 3: Эффективное состояние `ImmEffectiveStatus` (pure)

**Files:**
- Create: `Wintime.Control.Core/Policies/ImmEffectiveStatus.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ImmEffectiveStatusTests.cs`

**Interfaces:**
- Consumes: `ActiveTaskStatus` (Task 1), `ImmStatus` константы.
- Produces: строковые константы `ImmEffectiveStatus.{Offline,Setup,Production,Downtime,Stopped,Unplanned,NoTask}` и
  `ImmEffectiveStatus.Resolve(string rawStatus, ActiveTaskStatus task, bool hasOpenDowntime, bool thresholdPassed) -> string`.

> Примечание: функция готовит почву для отображения на дашборде (отдельная фронтенд-задача, см. спецификацию). Здесь — каноническая, протестированная реализация модели состояния; в эндпоинты пока не подключается.

- [ ] **Step 1: Написать падающий тест**

`Wintime.Control.Tests.Unit/Policies/ImmEffectiveStatusTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Xunit;
using Effective = Wintime.Control.Core.Policies.ImmEffectiveStatus;

namespace Wintime.Control.Tests.Unit.Policies;

public class ImmEffectiveStatusTests
{
    [Theory]
    // rawStatus, task, hasOpenDowntime, thresholdPassed, expected
    [InlineData(ImmStatus.Offline, ActiveTaskStatus.InProgress, false, true,  Effective.Offline)]
    [InlineData(ImmStatus.Idle,    ActiveTaskStatus.Setup,      false, true,  Effective.Setup)]
    [InlineData(ImmStatus.Auto,    ActiveTaskStatus.InProgress, false, false, Effective.Production)]
    [InlineData(ImmStatus.Auto,    ActiveTaskStatus.InProgress, true,  false, Effective.Downtime)]   // ручной простой при auto
    [InlineData(ImmStatus.Idle,    ActiveTaskStatus.InProgress, false, true,  Effective.Downtime)]   // idle дольше порога
    [InlineData(ImmStatus.Idle,    ActiveTaskStatus.InProgress, false, false, Effective.Stopped)]    // idle, порог не пройден
    [InlineData(ImmStatus.Auto,    ActiveTaskStatus.None,       false, false, Effective.Unplanned)]  // работа без задания
    [InlineData(ImmStatus.Idle,    ActiveTaskStatus.None,       false, false, Effective.NoTask)]
    public void Resolve_MatchesStateModel(
        string rawStatus, ActiveTaskStatus task, bool hasOpenDowntime, bool thresholdPassed, string expected)
    {
        Effective.Resolve(rawStatus, task, hasOpenDowntime, thresholdPassed)
            .Should().Be(expected);
    }

    [Fact]
    public void Resolve_SetupTakesPrecedenceOverOffline()
    {
        Effective.Resolve(ImmStatus.Offline, ActiveTaskStatus.Setup, false, true)
            .Should().Be(Effective.Setup);
    }
}
```

- [ ] **Step 2: Запустить тест — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter ImmEffectiveStatusTests`
Expected: FAIL — `ImmEffectiveStatus` не существует.

- [ ] **Step 3: Реализовать**

`Wintime.Control.Core/Policies/ImmEffectiveStatus.cs`:

```csharp
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Эффективное состояние ТПА = f(сырой статус + активность задания + открытый простой + порог).
/// Производное значение для дашборда/отчётов; кодирует таблицу состояний из спецификации.
/// </summary>
public static class ImmEffectiveStatus
{
    public const string Offline   = "Offline";
    public const string Setup     = "Setup";
    public const string Production = "Production";
    public const string Downtime  = "Downtime";
    public const string Stopped   = "Stopped";
    public const string Unplanned = "Unplanned";
    public const string NoTask    = "NoTask";

    public static string Resolve(
        string rawStatus, ActiveTaskStatus task, bool hasOpenDowntime, bool thresholdPassed)
    {
        if (task == ActiveTaskStatus.Setup)
            return Setup;

        if (rawStatus == ImmStatus.Offline)
            return Offline;

        if (task == ActiveTaskStatus.InProgress)
        {
            bool isAuto = rawStatus == ImmStatus.Auto;
            if (isAuto && !hasOpenDowntime)
                return Production;
            if (hasOpenDowntime || (!isAuto && thresholdPassed))
                return Downtime;
            return Stopped; // не-auto, порог ещё не пройден
        }

        // task == None
        return rawStatus == ImmStatus.Auto ? Unplanned : NoTask;
    }
}
```

- [ ] **Step 4: Запустить тест — убедиться, что проходит**

Run: `dotnet test Wintime.Control.Tests.Unit --filter ImmEffectiveStatusTests`
Expected: PASS (9 тестов).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/ImmEffectiveStatus.cs Wintime.Control.Tests.Unit/Policies/ImmEffectiveStatusTests.cs
git commit -m "feat: ImmEffectiveStatus.Resolve — эффективное состояние ТПА"
```

---

### Task 4: Решение об авто-простое `DowntimeDecision` (pure)

**Files:**
- Create: `Wintime.Control.Core/Policies/DowntimeDecision.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/DowntimeDecisionTests.cs`

**Interfaces:**
- Consumes: `ActiveTaskStatus` (Task 1), `ImmStatus`, `ImmMode`.
- Produces:
  - `enum DowntimeAction { None, Open, Close }`
  - `readonly record struct DowntimeOutcome(DowntimeAction Action, DateTime At)`
  - `DowntimeDecision.Evaluate(string rawStatus, DateTime statusSinceUtc, DateTime nowUtc, ActiveTaskStatus task, DateTime? taskStartedAtUtc, bool hasOpenAutoDowntime, int thresholdSeconds) -> DowntimeOutcome`

Семантика: `Open.At` = начало простоя (бэкдейт), `Close.At` = момент возврата в Auto.

- [ ] **Step 1: Написать падающий тест**

`Wintime.Control.Tests.Unit/Policies/DowntimeDecisionTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class DowntimeDecisionTests
{
    private static readonly DateTime Now = new(2026, 6, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Started = new(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NonAuto_InProgress_PastThreshold_NoOpen_OpensAtStatusSince()
    {
        var since = Now.AddSeconds(-200);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, since, Now, ActiveTaskStatus.InProgress, Started,
            hasOpenAutoDowntime: false, thresholdSeconds: 120);

        r.Action.Should().Be(DowntimeAction.Open);
        r.At.Should().Be(since); // бэкдейт на начало не-Auto
    }

    [Fact]
    public void NonAuto_InProgress_BeforeThreshold_None()
    {
        var since = Now.AddSeconds(-30);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, since, Now, ActiveTaskStatus.InProgress, Started,
            false, 120);

        r.Action.Should().Be(DowntimeAction.None);
    }

    [Fact]
    public void Open_StartTime_ClampedToTaskStarted_WhenStatusOlderThanTask()
    {
        // Статус ушёл в idle ещё до запуска задания (во время наладки) —
        // начало простоя не должно залезать раньше StartedAt.
        var since = Started.AddSeconds(-300);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, since, Now, ActiveTaskStatus.InProgress, Started,
            false, 120);

        r.Action.Should().Be(DowntimeAction.Open);
        r.At.Should().Be(Started);
    }

    [Fact]
    public void Offline_InProgress_PastThreshold_Opens()
    {
        var since = Now.AddSeconds(-200);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Offline, since, Now, ActiveTaskStatus.InProgress, Started,
            false, 120);

        r.Action.Should().Be(DowntimeAction.Open);
    }

    [Fact]
    public void Auto_WithOpenAutoDowntime_Closes_AtStatusSince()
    {
        var since = Now.AddSeconds(-10);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Auto, since, Now, ActiveTaskStatus.InProgress, Started,
            hasOpenAutoDowntime: true, thresholdSeconds: 120);

        r.Action.Should().Be(DowntimeAction.Close);
        r.At.Should().Be(since);
    }

    [Fact]
    public void TaskLeftInProgress_WithOpenAutoDowntime_Closes()
    {
        var since = Now.AddSeconds(-10);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, since, Now, ActiveTaskStatus.None, taskStartedAtUtc: null,
            hasOpenAutoDowntime: true, thresholdSeconds: 120);

        r.Action.Should().Be(DowntimeAction.Close);
    }

    [Fact]
    public void NonAuto_PastThreshold_AlreadyOpen_None()
    {
        var since = Now.AddSeconds(-200);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, since, Now, ActiveTaskStatus.InProgress, Started,
            hasOpenAutoDowntime: true, thresholdSeconds: 120);

        r.Action.Should().Be(DowntimeAction.None);
    }

    [Fact]
    public void NoTask_NoOpen_None()
    {
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, Now.AddSeconds(-500), Now, ActiveTaskStatus.None, null,
            false, 120);

        r.Action.Should().Be(DowntimeAction.None);
    }
}
```

- [ ] **Step 2: Запустить тест — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter DowntimeDecisionTests`
Expected: FAIL — `DowntimeDecision` не существует.

- [ ] **Step 3: Реализовать**

`Wintime.Control.Core/Policies/DowntimeDecision.cs`:

```csharp
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Policies;

public enum DowntimeAction { None, Open, Close }

public readonly record struct DowntimeOutcome(DowntimeAction Action, DateTime At);

/// <summary>
/// Решение об автоматическом простое для одного ТПА на момент опроса воркера.
/// Открывает простой, если задание InProgress и сырой статус не-Auto дольше порога;
/// закрывает открытый авто-простой при возврате в Auto или выходе задания из InProgress.
/// </summary>
public static class DowntimeDecision
{
    public static DowntimeOutcome Evaluate(
        string rawStatus,
        DateTime statusSinceUtc,
        DateTime nowUtc,
        ActiveTaskStatus task,
        DateTime? taskStartedAtUtc,
        bool hasOpenAutoDowntime,
        int thresholdSeconds)
    {
        bool isAuto = rawStatus == ImmStatus.Auto;
        bool productionActive = task == ActiveTaskStatus.InProgress;

        // Закрытие: есть открытый авто-простой и производство возобновилось/прекратилось.
        if (hasOpenAutoDowntime && (isAuto || !productionActive))
            return new DowntimeOutcome(DowntimeAction.Close, statusSinceUtc);

        // Открытие: производство идёт, статус не-Auto дольше порога, открытого простоя нет.
        if (productionActive && !isAuto && !hasOpenAutoDowntime)
        {
            var start = taskStartedAtUtc.HasValue && taskStartedAtUtc.Value > statusSinceUtc
                ? taskStartedAtUtc.Value
                : statusSinceUtc;

            if ((nowUtc - start).TotalSeconds >= thresholdSeconds)
                return new DowntimeOutcome(DowntimeAction.Open, start);
        }

        return new DowntimeOutcome(DowntimeAction.None, default);
    }
}
```

- [ ] **Step 4: Запустить тест — убедиться, что проходит**

Run: `dotnet test Wintime.Control.Tests.Unit --filter DowntimeDecisionTests`
Expected: PASS (8 тестов).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/DowntimeDecision.cs Wintime.Control.Tests.Unit/Policies/DowntimeDecisionTests.cs
git commit -m "feat: DowntimeDecision — решение об авто-простое"
```

---

### Task 5: Схема `Event` (TaskId, Comment, IsAuto) + миграция

**Files:**
- Modify: `Wintime.Control.Core/Entities/Event.cs`
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs:77-82` (блок `Event`)
- Create (генерируется EF): `Wintime.Control.Infrastructure/Migrations/<timestamp>_AddEventTaskIdCommentIsAuto.cs`

**Interfaces:**
- Produces: `Event.TaskId (Guid?)`, `Event.Comment (string?)`, `Event.IsAuto (bool)`, nav `Event.Task (ShiftTask?)`.

- [ ] **Step 1: Добавить поля в сущность**

`Wintime.Control.Core/Entities/Event.cs` — добавить после `PersonnelId` и в навигацию:

```csharp
    public string? PersonnelId { get; set; }
    public Guid? TaskId { get; set; }        // связь с активным заданием (nullable: ручной простой может быть без СЗ)
    public string? Comment { get; set; }     // комментарий наладчика/менеджера
    public bool IsAuto { get; set; }         // true — создан воркером простоев; false — вручную

    // Navigation
    public Imm Imm { get; set; } = null!;
    public DowntimeReason? Reason { get; set; }
    public User? Personnel { get; set; }
    public ShiftTask? Task { get; set; }
```

- [ ] **Step 2: Настроить FK в DbContext**

`Wintime.Control.Infrastructure/Data/ControlDbContext.cs` — заменить блок `Event` (строки 77-82):

```csharp
        // Конфигурация Event
        builder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Imm).WithMany(i => i.Events).HasForeignKey(e => e.ImmId);
            entity.HasOne(e => e.Task).WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.SetNull);
        });
```

- [ ] **Step 3: Создать миграцию**

Run:
```bash
dotnet ef migrations add AddEventTaskIdCommentIsAuto --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: создан файл миграции, в `Up()` — `AddColumn` для `TaskId`, `Comment`, `IsAuto` и FK на `ShiftTasks`.

- [ ] **Step 4: Проверить компиляцию решения**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Entities/Event.cs Wintime.Control.Infrastructure/Data/ControlDbContext.cs Wintime.Control.Infrastructure/Migrations/
git commit -m "feat: Event.TaskId/Comment/IsAuto + миграция"
```

---

### Task 6: Гейтинг `CycleProcessingHandler` по политике

Применяет `CycleProcessingPolicy`: пропуск при `Setup`/«нет задания+не-auto»; учёт выпуска только при `ShouldCountOutput`. Активное задание теперь `Setup ∨ InProgress`.

**Files:**
- Modify: `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs`
- Test: `Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs`

**Interfaces:**
- Consumes: `CycleProcessingPolicy` (Task 2), `ActiveTaskStatusMap` (Task 1), `Event` поля (Task 5).

- [ ] **Step 1: Написать падающие тесты (Setup и открытый простой)**

Добавить в `Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs`. Сначала вспомогательный метод для статуса задания и открытого простоя — расширить `SeedTask` перегрузкой:

```csharp
    private static EntityTask SeedTaskWithStatus(
        ControlDbContext db, Guid immId, EntityTaskStatus status, int cavities = 2)
    {
        var mold = new Mold
        {
            Name = "Test Mold", FormId = Guid.NewGuid().ToString(),
            Cavities = cavities, PartWeightGrams = 10m, RunnerWeightGrams = 5m,
            MaxResourceCycles = 100_000
        };
        db.Molds.Add(mold);
        var imm = new Imm { Id = immId, Name = "Test IMM", IsActive = true };
        db.Imms.Add(imm);
        var task = new EntityTask
        {
            ImmId = immId, MoldId = mold.Id, Mold = mold, Imm = imm,
            PlanQuantity = 1000, Status = status
        };
        db.ShiftTasks.Add(task);
        db.SaveChanges();
        return task;
    }

    [Fact]
    public async Task SetupTask_DoesNotWriteCycle()
    {
        var immId = Guid.NewGuid();
        var db = CreateDb();
        SeedTaskWithStatus(db, immId, EntityTaskStatus.Setup);
        _tracker.Get(immId).Returns(ActiveCycleState(lastCounter: 4)); // был активный цикл
        var sut = new CycleProcessingHandler(db, _tracker, _emulator, NullLogger<CycleProcessingHandler>.Instance);

        // переход из auto в idle — при наладке цикл писаться не должен
        await sut.ProcessAsync(MakeCycleContext(immId, counter: 5, mode: "idle"));

        db.ImmCycles.Should().BeEmpty();
    }

    [Fact]
    public async Task InProgress_WithOpenDowntime_WritesCycle_ButNoOutput()
    {
        var immId = Guid.NewGuid();
        var db = CreateDb();
        var task = SeedTaskWithStatus(db, immId, EntityTaskStatus.InProgress, cavities: 2);
        db.Events.Add(new Wintime.Control.Core.Entities.Event
        {
            ImmId = immId,
            EventType = Wintime.Control.Core.Enums.EventType.Downtime,
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = null,
            IsAuto = false
        });
        db.SaveChanges();
        _tracker.Get(immId).Returns(ActiveCycleState(lastCounter: 4));
        var sut = new CycleProcessingHandler(db, _tracker, _emulator, NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeCycleContext(immId, counter: 5, mode: "auto"));

        db.ImmCycles.Should().HaveCount(1);                       // цикл записан
        db.ShiftTasks.Find(task.Id)!.ActualQuantity.Should().Be(0); // выпуск НЕ учтён
    }
```

- [ ] **Step 2: Запустить тесты — убедиться, что падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter CycleProcessingHandlerTests`
Expected: FAIL — `SetupTask_DoesNotWriteCycle` (цикл пишется) и `InProgress_WithOpenDowntime...` (выпуск учитывается).

- [ ] **Step 3: Переработать обработчик**

`Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs` — заменить тело `ProcessAsync` начиная со строки определения `state` (после получения `currentCounter`, `currentMode`, `immId`, `currentTime`). Полная версия метода:

```csharp
    public async SystemTask ProcessAsync(MqttProcessingContext context, CancellationToken ct = default)
    {
        var data = context.Data;
        var template = context.Template;
        var device = context.Device;

        if (data is null || template is null || device is null)
            return;

        var counterSensor = template.Sensors.FirstOrDefault(s => s.ParameterType == "cycleCounter");
        if (counterSensor is null)
            return;

        if (!data.Sensors.TryGetValue(counterSensor.ParameterName, out var rawValue))
            return;

        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentCounter))
            return;

        var currentMode = ImmMode.Normalize(data.Mode);
        var immId = device.Id;
        var currentTime = DateTimeOffset.FromUnixTimeSeconds(data.Timestamp).UtcDateTime;

        // Активное задание: Setup (наладка) или InProgress (производство) — их не более одного.
        var activeTask = await _db.ShiftTasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(
                t => t.ImmId == immId
                  && (t.Status == EntityTaskStatus.Setup || t.Status == EntityTaskStatus.InProgress),
                ct);

        var taskStatus = ActiveTaskStatusMap.From(activeTask?.Status);

        // Гейтинг по матрице Состояния_ТПА.xlsx: при Setup и при «нет задания + не-auto»
        // циклы не обрабатываются. Сбрасываем активный цикл в трекере, чтобы он не
        // «склеился» через границу наладки, и сохраняем счётчик/режим.
        if (!CycleProcessingPolicy.ShouldProcessCycle(currentMode, taskStatus))
        {
            _tracker.Set(immId, new CycleState(null, currentCounter, currentMode));
            return;
        }

        var state = _tracker.Get(immId);

        if (state is null)
        {
            var startTime = currentMode == ImmMode.Auto ? currentTime : (DateTime?)null;
            _tracker.Set(immId, new CycleState(startTime, currentCounter, currentMode));
            return;
        }

        bool cycleWasActive = state.CycleStartTime.HasValue;
        bool counterChanged = state.LastCounterValue.HasValue && state.LastCounterValue.Value != currentCounter;
        bool modeChangedFromAuto = state.LastMode == ImmMode.Auto && currentMode != ImmMode.Auto;

        bool cycleEnded = cycleWasActive && (counterChanged || modeChangedFromAuto);

        if (cycleEnded)
        {
            bool isSuccessful = currentMode != ImmMode.Alarm;
            var cycleStart = state.CycleStartTime!.Value;
            var duration = (int)(currentTime - cycleStart).TotalSeconds;

            var cavities = activeTask?.Mold.Cavities ?? 0;

            var cycle = new ImmCycle
            {
                ImmId = immId,
                TaskId = activeTask?.Id,
                MoldId = activeTask?.MoldId,
                StartTime = cycleStart,
                EndTime = currentTime,
                DurationSeconds = duration,
                IsSuccessful = isSuccessful,
                Cavities = cavities
            };
            _db.ImmCycles.Add(cycle);

            // Учёт выпуска — только если политика разрешает (InProgress + auto + нет открытого простоя).
            bool hasOpenDowntime = await _db.Events.AnyAsync(
                e => e.ImmId == immId
                  && e.EventType == Core.Enums.EventType.Downtime
                  && e.EndTime == null, ct);

            if (isSuccessful
                && activeTask is not null
                && CycleProcessingPolicy.ShouldCountOutput(currentMode, taskStatus, hasOpenDowntime))
            {
                activeTask.ActualQuantity += cavities;
                activeTask.ActualMaterialWeightGrams +=
                    cavities * activeTask.Mold.PartWeightGrams
                    + activeTask.Mold.RunnerWeightGrams;
                if (activeTask.ActualQuantity >= activeTask.PlanQuantity)
                    await _emulator.SetModeAsync(immId.ToString(), "idle", ct);
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogDebug(
                "IMM {ImmId}: cycle saved — duration {Duration}s, successful={Success}",
                immId, duration, isSuccessful);
        }

        DateTime? newCycleStart = null;
        if (counterChanged && currentMode == ImmMode.Auto)
            newCycleStart = currentTime;
        else if (!cycleWasActive && currentMode == ImmMode.Auto)
            newCycleStart = currentTime;
        else if (cycleWasActive && !cycleEnded)
            newCycleStart = state.CycleStartTime;

        _tracker.Set(immId, new CycleState(newCycleStart, currentCounter, currentMode));
    }
```

Добавить `using` в начало файла:

```csharp
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
```

(`EntityTaskStatus` alias и `ImmMode` уже импортированы; `ActiveTaskStatusMap` и `ActiveTaskStatus` — в `Core.Enums`.)

- [ ] **Step 4: Запустить тесты обработчика — все зелёные**

Run: `dotnet test Wintime.Control.Tests.Unit --filter CycleProcessingHandlerTests`
Expected: PASS — существующие тесты (выпуск при InProgress+auto без простоя считается) + два новых.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs
git commit -m "feat: гейтинг обработки циклов/выпуска по CycleProcessingPolicy"
```

---

### Task 7: `DowntimeSettings` + конфигурация

**Files:**
- Create: `Wintime.Control.Shared/Settings/DowntimeSettings.cs`
- Modify: `Wintime.Control.API/Program.cs:36` (после `Configure<CorsSettings>`)
- Modify: `Wintime.Control.API/appsettings.json`

**Interfaces:**
- Produces: `DowntimeSettings { SectionName, IdleThresholdSeconds, PollingIntervalSeconds }` — потребляется воркером в Task 8.

- [ ] **Step 1: Создать класс настроек**

`Wintime.Control.Shared/Settings/DowntimeSettings.cs`:

```csharp
namespace Wintime.Control.Shared.Settings;

public class DowntimeSettings
{
    public const string SectionName = "Downtime";

    /// <summary>Порог: сколько секунд не-Auto при активном задании считать простоем.</summary>
    public int IdleThresholdSeconds { get; set; } = 120;

    /// <summary>Период опроса воркера простоев, секунды.</summary>
    public int PollingIntervalSeconds { get; set; } = 10;
}
```

- [ ] **Step 2: Зарегистрировать в Program.cs**

`Wintime.Control.API/Program.cs` — добавить после строки `Configure<CorsSettings>` (строка 36):

```csharp
builder.Services.Configure<DowntimeSettings>(builder.Configuration.GetSection(DowntimeSettings.SectionName));
```

Убедиться, что в начале файла есть `using Wintime.Control.Shared.Settings;` (он уже используется для других Settings).

- [ ] **Step 3: Добавить секцию в appsettings.json**

`Wintime.Control.API/appsettings.json` — добавить секцию верхнего уровня:

```json
  "Downtime": {
    "IdleThresholdSeconds": 120,
    "PollingIntervalSeconds": 10
  }
```

- [ ] **Step 4: Сборка**

Run: `dotnet build Wintime.Control.API`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Shared/Settings/DowntimeSettings.cs Wintime.Control.API/Program.cs Wintime.Control.API/appsettings.json
git commit -m "feat: DowntimeSettings (порог + период опроса)"
```

---

### Task 8: Воркер `DowntimeDetectionWorker`

**Files:**
- Create: `Wintime.Control.Infrastructure/Workers/DowntimeDetectionWorker.cs`
- Modify: `Wintime.Control.Infrastructure/Services/InfrastructureServiceExtensions.cs:100` (в `AddImmStatusWorkers`)
- Test: `Wintime.Control.Tests.Unit/Workers/DowntimeDetectionWorkerTests.cs`

**Interfaces:**
- Consumes: `DowntimeDecision` (Task 4), `ActiveTaskStatusMap` (Task 1), `IImmStatusCache`, `DowntimeSettings` (Task 7), `Event` (Task 5).
- Produces: `internal Task RunOnceAsync(CancellationToken ct)` — один проход, вызывается из тестов.

- [ ] **Step 1: Написать падающий тест прохода воркера**

`Wintime.Control.Tests.Unit/Workers/DowntimeDetectionWorkerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Workers;
using Wintime.Control.Shared.Settings;
using Xunit;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Workers;

public class DowntimeDetectionWorkerTests
{
    private static ControlDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DowntimeDetectionWorker BuildWorker(
        ControlDbContext db, IImmStatusCache statusCache, int threshold = 120)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddScoped<ControlDbContext>(sp => sp.GetRequiredService<ControlDbContext>());
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var opts = Options.Create(new DowntimeSettings
        {
            IdleThresholdSeconds = threshold,
            PollingIntervalSeconds = 10
        });
        return new DowntimeDetectionWorker(
            scopeFactory, statusCache, opts, NullLogger<DowntimeDetectionWorker>.Instance);
    }

    [Fact]
    public async Task RunOnce_InProgress_IdlePastThreshold_CreatesAutoDowntime()
    {
        var immId = Guid.NewGuid();
        var db = CreateDb();
        var imm = new Wintime.Control.Core.Entities.Imm { Id = immId, Name = "T", IsActive = true };
        db.Imms.Add(imm);
        db.ShiftTasks.Add(new EntityTask
        {
            ImmId = immId, Status = EntityTaskStatus.InProgress,
            StartedAt = DateTime.UtcNow.AddHours(-1), PlanQuantity = 100
        });
        db.SaveChanges();

        var statusCache = Substitute.For<IImmStatusCache>();
        statusCache.GetAll().Returns(new List<ImmStatusEntry>
        {
            new(immId, ImmStatus.Idle, DateTime.UtcNow.AddSeconds(-200))
        });

        var worker = BuildWorker(db, statusCache);
        await worker.RunOnceAsync(CancellationToken.None);

        var evt = db.Events.Single();
        evt.EventType.Should().Be(Wintime.Control.Core.Enums.EventType.Downtime);
        evt.IsAuto.Should().BeTrue();
        evt.EndTime.Should().BeNull();
        evt.TaskId.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnce_BackToAuto_ClosesOpenAutoDowntime()
    {
        var immId = Guid.NewGuid();
        var db = CreateDb();
        db.Imms.Add(new Wintime.Control.Core.Entities.Imm { Id = immId, Name = "T", IsActive = true });
        db.ShiftTasks.Add(new EntityTask
        {
            ImmId = immId, Status = EntityTaskStatus.InProgress,
            StartedAt = DateTime.UtcNow.AddHours(-1), PlanQuantity = 100
        });
        db.Events.Add(new Wintime.Control.Core.Entities.Event
        {
            ImmId = immId, EventType = Wintime.Control.Core.Enums.EventType.Downtime,
            StartTime = DateTime.UtcNow.AddMinutes(-5), EndTime = null, IsAuto = true
        });
        db.SaveChanges();

        var statusCache = Substitute.For<IImmStatusCache>();
        statusCache.GetAll().Returns(new List<ImmStatusEntry>
        {
            new(immId, ImmStatus.Auto, DateTime.UtcNow.AddSeconds(-5))
        });

        var worker = BuildWorker(db, statusCache);
        await worker.RunOnceAsync(CancellationToken.None);

        db.Events.Single().EndTime.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnce_ManualOpenDowntime_NotClosedByWorker()
    {
        var immId = Guid.NewGuid();
        var db = CreateDb();
        db.Imms.Add(new Wintime.Control.Core.Entities.Imm { Id = immId, Name = "T", IsActive = true });
        db.ShiftTasks.Add(new EntityTask
        {
            ImmId = immId, Status = EntityTaskStatus.InProgress,
            StartedAt = DateTime.UtcNow.AddHours(-1), PlanQuantity = 100
        });
        db.Events.Add(new Wintime.Control.Core.Entities.Event
        {
            ImmId = immId, EventType = Wintime.Control.Core.Enums.EventType.Downtime,
            StartTime = DateTime.UtcNow.AddMinutes(-5), EndTime = null, IsAuto = false // ручной
        });
        db.SaveChanges();

        var statusCache = Substitute.For<IImmStatusCache>();
        statusCache.GetAll().Returns(new List<ImmStatusEntry>
        {
            new(immId, ImmStatus.Auto, DateTime.UtcNow.AddSeconds(-5))
        });

        var worker = BuildWorker(db, statusCache);
        await worker.RunOnceAsync(CancellationToken.None);

        db.Events.Single().EndTime.Should().BeNull(); // воркер не трогает ручной простой
    }
}
```

- [ ] **Step 2: Запустить тест — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter DowntimeDetectionWorkerTests`
Expected: FAIL — `DowntimeDetectionWorker` не существует.

- [ ] **Step 3: Реализовать воркер**

`Wintime.Control.Infrastructure/Workers/DowntimeDetectionWorker.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Settings;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Workers;

/// <summary>
/// Фоновый сервис, который при активном задании InProgress автоматически создаёт
/// запись простоя (Event типа Downtime), если ТПА дольше порога находится не в Auto,
/// и закрывает её при возврате в Auto. По образцу <see cref="ImmOfflineWorker"/>.
/// </summary>
public class DowntimeDetectionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IImmStatusCache _statusCache;
    private readonly DowntimeSettings _settings;
    private readonly ILogger<DowntimeDetectionWorker> _logger;

    public DowntimeDetectionWorker(
        IServiceScopeFactory scopeFactory,
        IImmStatusCache statusCache,
        IOptions<DowntimeSettings> settings,
        ILogger<DowntimeDetectionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _statusCache = statusCache;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DowntimeDetectionWorker iteration failed");
            }
        }
    }

    /// <summary>Один проход опроса. Выделен для тестируемости.</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entries = _statusCache.GetAll();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        foreach (var entry in entries)
        {
            var task = await db.ShiftTasks
                .Where(t => t.ImmId == entry.ImmId
                         && (t.Status == EntityTaskStatus.Setup || t.Status == EntityTaskStatus.InProgress))
                .FirstOrDefaultAsync(ct);

            var taskStatus = ActiveTaskStatusMap.From(task?.Status);

            var openAuto = await db.Events
                .Where(e => e.ImmId == entry.ImmId
                         && e.EventType == EventType.Downtime
                         && e.EndTime == null
                         && e.IsAuto)
                .FirstOrDefaultAsync(ct);

            var outcome = DowntimeDecision.Evaluate(
                entry.Status, entry.SinceUtc, now,
                taskStatus, task?.StartedAt,
                hasOpenAutoDowntime: openAuto is not null,
                thresholdSeconds: _settings.IdleThresholdSeconds);

            if (outcome.Action == DowntimeAction.Open)
            {
                db.Events.Add(new Event
                {
                    ImmId = entry.ImmId,
                    EventType = EventType.Downtime,
                    TaskId = task?.Id,
                    StartTime = outcome.At,
                    EndTime = null,
                    ReasonId = null,
                    IsAuto = true
                });
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("IMM {ImmId}: авто-простой открыт с {Start}", entry.ImmId, outcome.At);
            }
            else if (outcome.Action == DowntimeAction.Close && openAuto is not null)
            {
                openAuto.EndTime = outcome.At;
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("IMM {ImmId}: авто-простой закрыт в {End}", entry.ImmId, outcome.At);
            }
        }
    }
}
```

- [ ] **Step 4: Зарегистрировать воркер**

`Wintime.Control.Infrastructure/Services/InfrastructureServiceExtensions.cs` — в методе `AddImmStatusWorkers`, после `AddHostedService<ImmOfflineWorker>()` (строка 100):

```csharp
        services.AddHostedService<ImmOfflineWorker>();
        services.AddHostedService<DowntimeDetectionWorker>();
        services.AddHostedService<AppHeartbeatWorker>();
```

- [ ] **Step 5: Запустить тест — убедиться, что проходит**

Run: `dotnet test Wintime.Control.Tests.Unit --filter DowntimeDetectionWorkerTests`
Expected: PASS (3 теста).

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Infrastructure/Workers/DowntimeDetectionWorker.cs Wintime.Control.Infrastructure/Services/InfrastructureServiceExtensions.cs Wintime.Control.Tests.Unit/Workers/DowntimeDetectionWorkerTests.cs
git commit -m "feat: DowntimeDetectionWorker — авто-создание/закрытие простоев"
```

---

### Task 9: `DowntimeController` — редактирование простоя наладчиком

PATCH меняет причину + время окончания + комментарий и доступен наладчику; `IsAuto`, `TaskId`, `Comment` в DTO; ручной `StartDowntime` явно `IsAuto=false`.

**Files:**
- Modify: `Wintime.Control.Core/DTOs/Downtime/UpdateDowntimeEventRequestDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Downtime/EventDto.cs`
- Modify: `Wintime.Control.API/Controllers/DowntimeController.cs`
- Test: `Wintime.Control.Tests.Integration/Downtime/UpdateDowntimeEventTests.cs`

**Interfaces:**
- Consumes: `Event` поля (Task 5).
- Produces: `PATCH /api/downtime/events/{id}` принимает `{ reasonId?, endTime?, comment? }`, роли `Adjuster,Manager,Admin`.

- [ ] **Step 1: Расширить DTO**

`Wintime.Control.Core/DTOs/Downtime/UpdateDowntimeEventRequestDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Downtime;

public class UpdateDowntimeEventRequestDto
{
    public Guid? ReasonId { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Comment { get; set; }
}
```

`Wintime.Control.Core/DTOs/Downtime/EventDto.cs` — добавить поля:

```csharp
    public string? PersonnelId { get; set; }
    public Guid? TaskId { get; set; }
    public string? Comment { get; set; }
    public bool IsAuto { get; set; }
```

- [ ] **Step 2: Написать падающий интеграционный тест**

`Wintime.Control.Tests.Integration/Downtime/UpdateDowntimeEventTests.cs`. Использовать существующую инфраструктуру интеграционных тестов (`IntegrationTestFactory`) и хелперы аутентификации по образцу других тестов в `Wintime.Control.Tests.Integration`. Если в проекте уже есть базовый класс/фикстура с авторизацией под роль — переиспользовать его; ниже — целевое поведение:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Downtime;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Downtime;

public class UpdateDowntimeEventTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public UpdateDowntimeEventTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Adjuster_CanUpdate_Reason_EndTime_Comment()
    {
        // Arrange: ТПА + причина + открытый авто-простой в БД.
        Guid eventId;
        Guid reasonId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = new Imm { Name = "IMM-1", IsActive = true };
            db.Imms.Add(imm);
            var reason = new DowntimeReason { Name = "Нет материала", Type = "downtime", IsActive = true };
            db.DowntimeReasons.Add(reason);
            var evt = new Event
            {
                ImmId = imm.Id, EventType = EventType.Downtime,
                StartTime = DateTime.UtcNow.AddMinutes(-10), EndTime = null, IsAuto = true
            };
            db.Events.Add(evt);
            await db.SaveChangesAsync();
            eventId = evt.Id;
            reasonId = reason.Id;
        }

        var client = await _factory.CreateClientAsRoleAsync("Adjuster"); // хелпер фикстуры
        var body = new UpdateDowntimeEventRequestDto
        {
            ReasonId = reasonId,
            EndTime = DateTime.UtcNow,
            Comment = "Ждали поставку сырья"
        };

        // Act
        var resp = await client.PatchAsJsonAsync($"/api/downtime/events/{eventId}", body);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var evt = db.Events.Find(eventId)!;
            evt.ReasonId.Should().Be(reasonId);
            evt.ReasonName.Should().Be("Нет материала");
            evt.EndTime.Should().NotBeNull();
            evt.Comment.Should().Be("Ждали поставку сырья");
        }
    }
}
```

> Если у `IntegrationTestFactory` нет метода `CreateClientAsRoleAsync`, добавить его по образцу существующих интеграционных тестов (выдать JWT с нужной ролью). Это часть данной задачи.

- [ ] **Step 3: Запустить тест — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter UpdateDowntimeEventTests`
Expected: FAIL — PATCH не принимает `EndTime`/`Comment` и/или роль `Adjuster` запрещена.

- [ ] **Step 4: Переработать `UpdateDowntimeEvent` и `StartDowntime`**

`Wintime.Control.API/Controllers/DowntimeController.cs`:

Заменить атрибут и тело `UpdateDowntimeEvent`:

```csharp
    /// <summary>
    /// Редактировать простой: причина, время окончания, комментарий.
    /// </summary>
    [HttpPatch("events/{id}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<EventDto>> UpdateDowntimeEvent(Guid id, [FromBody] UpdateDowntimeEventRequestDto request)
    {
        var evt = await _context.Events
            .Include(e => e.Imm)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evt == null)
            return NotFound("Событие не найдено");

        if (request.ReasonId.HasValue)
        {
            var reason = await _context.DowntimeReasons.FindAsync(request.ReasonId.Value);
            if (reason == null)
                return NotFound("Причина простоя не найдена");
            evt.ReasonId = reason.Id;
            evt.ReasonName = reason.Name;
        }

        if (request.EndTime.HasValue)
            evt.EndTime = DateTime.SpecifyKind(request.EndTime.Value, DateTimeKind.Utc);

        if (request.Comment != null)
            evt.Comment = request.Comment;

        await _context.SaveChangesAsync();

        return Ok(new EventDto
        {
            Id = evt.Id,
            ImmId = evt.ImmId,
            ImmName = evt.Imm.Name,
            EventType = evt.EventType.ToString(),
            ReasonId = evt.ReasonId,
            ReasonName = evt.ReasonName,
            ErrorCode = evt.ErrorCode,
            ErrorMessage = evt.ErrorMessage,
            StartTime = evt.StartTime,
            EndTime = evt.EndTime,
            DurationSeconds = evt.DurationSeconds,
            PersonnelId = evt.PersonnelId,
            TaskId = evt.TaskId,
            Comment = evt.Comment,
            IsAuto = evt.IsAuto
        });
    }
```

В `StartDowntime` — при создании `Event` добавить `IsAuto = false`:

```csharp
        var evt = new Event
        {
            ImmId = request.ImmId,
            EventType = Core.Enums.EventType.Downtime,
            ReasonId = request.ReasonId,
            ReasonName = reason.Name,
            StartTime = request.StartTime ?? DateTime.UtcNow,
            PersonnelId = request.PersonnelId,
            IsAuto = false
        };
```

В `GetEvents` — в проекцию `EventDto` добавить новые поля (после `PersonnelId = e.PersonnelId`):

```csharp
            PersonnelId = e.PersonnelId,
            TaskId = e.TaskId,
            Comment = e.Comment,
            IsAuto = e.IsAuto
```

- [ ] **Step 5: Запустить тест — убедиться, что проходит**

Run: `dotnet test Wintime.Control.Tests.Integration --filter UpdateDowntimeEventTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Core/DTOs/Downtime/ Wintime.Control.API/Controllers/DowntimeController.cs Wintime.Control.Tests.Integration/Downtime/UpdateDowntimeEventTests.cs
git commit -m "feat: редактирование простоя наладчиком (причина+время+комментарий)"
```

---

### Task 10: Полный прогон и применение миграции

**Files:** —

- [ ] **Step 1: Прогнать весь набор тестов**

Run: `dotnet test`
Expected: PASS — весь набor (существующие 125 unit + 21 integration плюс новые) зелёный.

- [ ] **Step 2: Применить миграцию к dev-БД**

Run:
```bash
dotnet ef database update --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: миграция `AddEventTaskIdCommentIsAuto` применена.

- [ ] **Step 3: Завести ADR о смене модели определения состояния**

Создать `docs/adr/0006-effective-imm-state.md` (формат MADR, см. `docs/adr/README.md`): решение — определять обработку и простои по эффективному состоянию (mode + статус задания + открытый простой), а не только по MQTT `mode`; отвергнутые альтернативы — событийный подход в конвейере, историзация эффективного состояния в таблицу. Закоммитить.

```bash
git add docs/adr/0006-effective-imm-state.md
git commit -m "docs: ADR-0006 эффективное состояние ТПА"
```

---

## Self-Review

**Spec coverage:**
- Эффективное состояние (два слоя, вычисление на лету) → Task 3 (`ImmEffectiveStatus`); сырой слой не меняется.
- Матрица обработки циклов/выпуска → Task 2 (`CycleProcessingPolicy`) + Task 6 (применение в хендлере).
- Активное задание = Setup ∨ InProgress → Task 1 + Task 6.
- Авто-простой: триггер «любой не-Auto + Offline при InProgress дольше порога» → Task 4 (`DowntimeDecision`) + Task 8 (воркер).
- Бэкдейт `StartTime` → Task 4 (тесты `Open...AtStatusSince`, `ClampedToTaskStarted`).
- `Event.TaskId/Comment/IsAuto` → Task 5.
- `DowntimeSettings` (порог + период) → Task 7.
- Дискриминатор авто/ручной, воркер не трогает ручной простой → Task 8 (тест `ManualOpenDowntime_NotClosedByWorker`).
- PATCH (причина+время+комментарий, роль Adjuster), `StartDowntime` IsAuto=false, поля DTO → Task 9.
- Тесты как исполняемая спецификация (16 строк xlsx) → Task 2.
- ADR → Task 10.
- Frontend (дашборд/журнал) — вне плана (следующий шаг, как в спецификации).

**Placeholder scan:** код приведён полностью в каждом шаге; единственная условность — генерируемая EF миграция (Task 5) и возможный хелпер `CreateClientAsRoleAsync` (Task 9), для которого дана инструкция «добавить по образцу существующих интеграционных тестов».

**Type consistency:** `ActiveTaskStatus`/`ActiveTaskStatusMap.From` (Task 1) используются единообразно в Task 2/6/8; `CycleProcessingPolicy.ShouldProcessCycle/ShouldCountOutput` (Task 2) — в Task 6; `DowntimeDecision.Evaluate`/`DowntimeOutcome`/`DowntimeAction` (Task 4) — в Task 8; поля `Event` (Task 5) — в Task 6/8/9.

## Открытые детали (решены в плане)
- **CycleTracker на границе Setup→InProgress:** при гейтинге (`ShouldProcessCycle == false`) трекер сбрасывается в `CycleState(null, currentCounter, currentMode)` — цикл не склеивается через наладку (Task 6, Step 3).
- **FK `Event.TaskId` OnDelete:** `SetNull` — по образцу `ImmCycle.TaskId` (Task 5).
- **Секция конфига:** `Downtime`, дефолты 120/10 (Task 7).
