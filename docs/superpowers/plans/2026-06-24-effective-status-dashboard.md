# Эффективное состояние ТПА на дашборде — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Показать эффективное состояние ТПА (6 значений) на десктоп-дашборде (плитки, бейджи, фильтр, KPI-панель, метрика загрузки) и в таймлайне смены, считая состояние одной чистой функцией в Core.

**Architecture:** Чистые функции `ImmEffectiveStatus.Resolve` и `EffectiveStatusTimeline.Build` в `Wintime.Control.Core` (тестируются по матрице `Состояния_ТПА.xlsx`). `ImmController` вычисляет состояние на лету: live — по кешу статусов + порогу + открытым простоям; история — реконструкцией наложением трёх историзированных рядов (сырой статус + интервалы заданий + простои). Фронт только отображает: единый модуль палитры → бейдж, карточка, фильтр, KPI, таймлайн.

**Tech Stack:** .NET 9 / ASP.NET Core, EF Core 9 + Npgsql, xUnit + FluentAssertions; Vue 3 + Pinia + Element Plus + Tailwind, Vitest (вводится впервые).

## Global Constraints

- **6 эффективных состояний:** `Production` (Работа), `Setup` (Наладка), `Downtime` (Простой), `Unplanned` (Работа без задания), `NoTask` (Без задания), `Offline` (Нет связи). `Stopped` наружу не выходит; `Alarm` растворяется.
- **Палитра (label / Tailwind / hex):** Production «Работа» green-500 `#22c55e`; Setup «Наладка» yellow-500 `#eab308`; Downtime «Простой» red-500 `#ef4444`; Unplanned «Работа без задания» purple-500 `#a855f7`; NoTask «Без задания» blue-500 `#3b82f6`; Offline «Нет связи» gray-400 `#9ca3af`.
- **Метрика «Текущая загрузка» = (Production + Setup) / всего.**
- **DateTime → PostgreSQL:** любое `DateTime` в EF-запрос обязано иметь `Kind=Utc` (через `DateTime.SpecifyKind(..., DateTimeKind.Utc)`).
- **Сравнение режима** только через `ImmMode.Normalize(...)`, не со строковым литералом.
- **Без хранения** эффективного состояния в БД — только вычисление на лету.
- Существующие наборы тестов зелёные: backend 226 unit + 24 integration. Новые тесты добавляются к ним.

---

### Task 1: `ImmEffectiveStatus.Resolve` + константы (Core)

**Files:**
- Create: `Wintime.Control.Core/Constants/EffectiveStatus.cs`
- Create: `Wintime.Control.Core/Policies/ImmEffectiveStatus.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ImmEffectiveStatusTests.cs`

**Interfaces:**
- Consumes: `ImmMode.Normalize`, `ImmMode.Auto`, `ImmStatus.Offline` (существуют); `ActiveTaskStatus` enum `{ None, Setup, InProgress }` (существует).
- Produces: `EffectiveStatus.{Production,Setup,Downtime,Unplanned,NoTask,Offline}` (const string); `ImmEffectiveStatus.Resolve(string rawMode, ActiveTaskStatus task, bool hasOpenDowntime, bool thresholdPassed) → string`.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class ImmEffectiveStatusTests
{
    // Матрица docs/details/Состояния_ТПА.xlsx, спроецированная на 6 эффективных состояний.
    // rawMode подаётся как ImmStatus (формат кеша, с заглавной) — Resolve нормализует сам.
    [Theory]
    // rawMode,   task,                        hasOpenDt, thresholdPassed, expected
    [InlineData("Auto",    ActiveTaskStatus.Setup,      false, false, EffectiveStatus.Setup)]
    [InlineData("Idle",    ActiveTaskStatus.Setup,      false, true,  EffectiveStatus.Setup)]
    [InlineData("Offline", ActiveTaskStatus.Setup,      false, true,  EffectiveStatus.Setup)]
    [InlineData("Auto",    ActiveTaskStatus.InProgress, false, false, EffectiveStatus.Production)]
    [InlineData("Idle",    ActiveTaskStatus.InProgress, false, false, EffectiveStatus.Production)] // до порога
    [InlineData("Manual",  ActiveTaskStatus.InProgress, false, true,  EffectiveStatus.Downtime)]   // порог пройден
    [InlineData("Alarm",   ActiveTaskStatus.InProgress, true,  false, EffectiveStatus.Downtime)]    // открыт простой
    [InlineData("Offline", ActiveTaskStatus.InProgress, false, true,  EffectiveStatus.Downtime)]    // offline дольше порога
    [InlineData("Offline", ActiveTaskStatus.InProgress, false, false, EffectiveStatus.Production)]   // offline до порога (дребезг)
    [InlineData("Auto",    ActiveTaskStatus.None,        false, false, EffectiveStatus.Unplanned)]
    [InlineData("Offline", ActiveTaskStatus.None,        false, false, EffectiveStatus.Offline)]
    [InlineData("Idle",    ActiveTaskStatus.None,        false, false, EffectiveStatus.NoTask)]
    [InlineData("Manual",  ActiveTaskStatus.None,        false, false, EffectiveStatus.NoTask)]
    [InlineData("Alarm",   ActiveTaskStatus.None,        false, false, EffectiveStatus.NoTask)]
    public void Resolve_MatchesMatrix(
        string rawMode, ActiveTaskStatus task, bool hasOpenDowntime, bool thresholdPassed, string expected)
    {
        ImmEffectiveStatus.Resolve(rawMode, task, hasOpenDowntime, thresholdPassed)
            .Should().Be(expected);
    }

    [Fact]
    public void Resolve_NormalizesMode_LowercaseAutoInProgress_Production()
    {
        ImmEffectiveStatus.Resolve("auto", ActiveTaskStatus.InProgress, false, false)
            .Should().Be(EffectiveStatus.Production);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Unit --filter ImmEffectiveStatusTests`
Expected: FAIL — `EffectiveStatus`/`ImmEffectiveStatus` не существуют (ошибка компиляции).

- [ ] **Step 3: Create the constants**

`Wintime.Control.Core/Constants/EffectiveStatus.cs`:
```csharp
namespace Wintime.Control.Core.Constants;

/// <summary>
/// Эффективное состояние ТПА — производное от (сырой режим + статус активного задания +
/// наличие открытого простоя + признак прохождения порога). Вычисляется на лету, наружу
/// отдаётся фронтенду. 6 значений; «Stopped» наружу не выходит, «Alarm» растворяется.
/// </summary>
public static class EffectiveStatus
{
    public const string Production = "Production"; // Работа
    public const string Setup      = "Setup";      // Наладка
    public const string Downtime   = "Downtime";   // Простой
    public const string Unplanned  = "Unplanned";  // Работа без задания
    public const string NoTask     = "NoTask";     // Без задания
    public const string Offline    = "Offline";    // Нет связи
}
```

- [ ] **Step 4: Implement `Resolve`**

`Wintime.Control.Core/Policies/ImmEffectiveStatus.cs`:
```csharp
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Чистая функция вычисления эффективного состояния ТПА по матрице
/// docs/details/Состояния_ТПА.xlsx. Переиспользуется live-эндпоинтом дашборда и
/// реконструкцией исторического таймлайна (EffectiveStatusTimeline).
/// </summary>
public static class ImmEffectiveStatus
{
    private static readonly string OfflineMode = ImmMode.Normalize(ImmStatus.Offline);

    public static string Resolve(string rawMode, ActiveTaskStatus task,
                                 bool hasOpenDowntime, bool thresholdPassed)
    {
        var mode = ImmMode.Normalize(rawMode);
        var isAuto = mode == ImmMode.Auto;

        if (task == ActiveTaskStatus.Setup)
            return EffectiveStatus.Setup;

        if (task == ActiveTaskStatus.InProgress)
        {
            if (isAuto) return EffectiveStatus.Production;
            return (hasOpenDowntime || thresholdPassed)
                ? EffectiveStatus.Downtime
                : EffectiveStatus.Production; // до порога — «Stopped» растворён в Работу
        }

        // task == None
        if (isAuto) return EffectiveStatus.Unplanned;
        if (mode == OfflineMode) return EffectiveStatus.Offline;
        return EffectiveStatus.NoTask;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Wintime.Control.Tests.Unit --filter ImmEffectiveStatusTests`
Expected: PASS (15 кейсов).

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Core/Constants/EffectiveStatus.cs Wintime.Control.Core/Policies/ImmEffectiveStatus.cs Wintime.Control.Tests.Unit/Policies/ImmEffectiveStatusTests.cs
git commit -m "feat: ImmEffectiveStatus.Resolve — чистая функция эффективного состояния ТПА"
```

---

### Task 2: `EffectiveStatusTimeline.Build` + входные типы (Core)

**Files:**
- Create: `Wintime.Control.Core/Policies/EffectiveStatusTimeline.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/EffectiveStatusTimelineTests.cs`

**Interfaces:**
- Consumes: `ImmEffectiveStatus.Resolve` (Task 1), `ActiveTaskStatus`, `ImmStatus`.
- Produces (все record-типы в файле `EffectiveStatusTimeline.cs`, namespace `Wintime.Control.Core.Policies`):
  - `record RawSegment(string Status, DateTime Start, DateTime End)`
  - `record TaskInterval(ActiveTaskStatus Status, DateTime Start, DateTime End)`
  - `record Interval(DateTime Start, DateTime End)`
  - `record EffectiveSegment(string EffectiveStatus, DateTime Start, DateTime End)`
  - `EffectiveStatusTimeline.Build(IReadOnlyList<RawSegment> raw, IReadOnlyList<TaskInterval> tasks, IReadOnlyList<Interval> downtimes, DateTime from, DateTime to) → IReadOnlyList<EffectiveSegment>`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class EffectiveStatusTimelineTests
{
    private static readonly DateTime T0 = new(2026, 6, 24, 8, 0, 0, DateTimeKind.Utc);
    private static DateTime M(int min) => T0.AddMinutes(min);

    [Fact]
    public void Build_SetupThenProduction_NoGap()
    {
        // Наладка 0–30 (auto), затем работа 30–60 (auto). Простоев нет.
        var raw = new[] { new RawSegment(ImmStatus.Auto, M(0), M(60)) };
        var tasks = new[]
        {
            new TaskInterval(ActiveTaskStatus.Setup,      M(0),  M(30)),
            new TaskInterval(ActiveTaskStatus.InProgress, M(30), M(60)),
        };
        var result = EffectiveStatusTimeline.Build(raw, tasks, System.Array.Empty<Interval>(), M(0), M(60));

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Setup, M(0), M(30)));
        result[1].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Production, M(30), M(60)));
    }

    [Fact]
    public void Build_NonAutoBeforeDowntime_IsProduction_CoveredByEvent_IsDowntime()
    {
        // Работа всё время InProgress. Сырой: auto 0–20, idle 20–60.
        // Простой зафиксирован Event только 40–60 (порог пройден к 40-й минуте).
        var raw = new[]
        {
            new RawSegment(ImmStatus.Auto, M(0),  M(20)),
            new RawSegment(ImmStatus.Idle, M(20), M(60)),
        };
        var tasks = new[] { new TaskInterval(ActiveTaskStatus.InProgress, M(0), M(60)) };
        var downtimes = new[] { new Interval(M(40), M(60)) };

        var result = EffectiveStatusTimeline.Build(raw, tasks, downtimes, M(0), M(60));

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Production, M(0), M(40)));
        result[1].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Downtime, M(40), M(60)));
    }

    [Fact]
    public void Build_ClampsToWindow_AndMergesAdjacentEqual()
    {
        // Сырой статус выходит за окно; два смежных auto-InProgress сегмента должны слиться.
        var raw = new[]
        {
            new RawSegment(ImmStatus.Auto, M(-30), M(20)),
            new RawSegment(ImmStatus.Auto, M(20),  M(90)),
        };
        var tasks = new[] { new TaskInterval(ActiveTaskStatus.InProgress, M(-30), M(90)) };

        var result = EffectiveStatusTimeline.Build(raw, tasks, System.Array.Empty<Interval>(), M(0), M(60));

        result.Should().ContainSingle();
        result[0].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Production, M(0), M(60)));
    }

    [Fact]
    public void Build_NoTaskOffline_GivesOffline()
    {
        var raw = new[] { new RawSegment(ImmStatus.Offline, M(0), M(60)) };
        var result = EffectiveStatusTimeline.Build(
            raw, System.Array.Empty<TaskInterval>(), System.Array.Empty<Interval>(), M(0), M(60));

        result.Should().ContainSingle();
        result[0].EffectiveStatus.Should().Be(EffectiveStatus.Offline);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Unit --filter EffectiveStatusTimelineTests`
Expected: FAIL — типы/метод не существуют.

- [ ] **Step 3: Implement `Build` + типы**

`Wintime.Control.Core/Policies/EffectiveStatusTimeline.cs`:
```csharp
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Policies;

public record RawSegment(string Status, DateTime Start, DateTime End);
public record TaskInterval(ActiveTaskStatus Status, DateTime Start, DateTime End);
public record Interval(DateTime Start, DateTime End);
public record EffectiveSegment(string EffectiveStatus, DateTime Start, DateTime End);

/// <summary>
/// Реконструкция таймлайна эффективного состояния за период [from, to] наложением трёх
/// историзированных рядов: сырой статус (ImmStatusHistory), интервалы статуса задания и
/// интервалы простоев (Event). Для истории thresholdPassed не нужен — факт простоя уже
/// материализован в Event, поэтому подаётся false.
/// </summary>
public static class EffectiveStatusTimeline
{
    public static IReadOnlyList<EffectiveSegment> Build(
        IReadOnlyList<RawSegment> raw,
        IReadOnlyList<TaskInterval> tasks,
        IReadOnlyList<Interval> downtimes,
        DateTime from, DateTime to)
    {
        if (to <= from) return System.Array.Empty<EffectiveSegment>();

        // 1. Собрать все границы внутри окна.
        var points = new SortedSet<DateTime> { from, to };
        void AddBound(DateTime d) { if (d > from && d < to) points.Add(d); }
        foreach (var s in raw)       { AddBound(s.Start); AddBound(s.End); }
        foreach (var t in tasks)     { AddBound(t.Start); AddBound(t.End); }
        foreach (var d in downtimes) { AddBound(d.Start); AddBound(d.End); }

        var bounds = points.ToList();

        // 2. На каждом под-интервале вычислить эффективное состояние в его середине.
        var segments = new List<EffectiveSegment>();
        for (int i = 0; i < bounds.Count - 1; i++)
        {
            var start = bounds[i];
            var end = bounds[i + 1];
            var mid = start + (end - start) / 2;

            var rawMode = raw.FirstOrDefault(s => s.Start <= mid && mid < s.End)?.Status
                          ?? ImmStatus.Offline;
            var task = tasks.FirstOrDefault(t => t.Start <= mid && mid < t.End)?.Status
                       ?? ActiveTaskStatus.None;
            var hasDowntime = downtimes.Any(d => d.Start <= mid && mid < d.End);

            var eff = ImmEffectiveStatus.Resolve(rawMode, task, hasDowntime, thresholdPassed: false);

            // 3. Слить со смежным сегментом, если состояние то же.
            if (segments.Count > 0 && segments[^1].EffectiveStatus == eff)
                segments[^1] = segments[^1] with { End = end };
            else
                segments.Add(new EffectiveSegment(eff, start, end));
        }

        return segments;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wintime.Control.Tests.Unit --filter EffectiveStatusTimelineTests`
Expected: PASS (4 кейса).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/EffectiveStatusTimeline.cs Wintime.Control.Tests.Unit/Policies/EffectiveStatusTimelineTests.cs
git commit -m "feat: EffectiveStatusTimeline.Build — реконструкция эффективного таймлайна наложением рядов"
```

---

### Task 3: Live-контракт — `EffectiveStatus` в `GET /imm` и `GET /imm/{id}/status`

**Files:**
- Modify: `Wintime.Control.Core/DTOs/Imm/ImmDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Imm/ImmStatusDto.cs`
- Modify: `Wintime.Control.API/Controllers/ImmController.cs`
- Test: `Wintime.Control.Tests.Integration/Imm/EffectiveStatusTests.cs` (create)

**Interfaces:**
- Consumes: `ImmEffectiveStatus.Resolve` (Task 1), `ActiveTaskStatusMap.From` (существует), `IImmStatusCache.GetEntry` → `ImmStatusEntry(ImmId, Status, SinceUtc)`, `DowntimeSettings.IdleThresholdSeconds`.
- Produces: `ImmDto.EffectiveStatus` (string?), `ImmStatusDto.EffectiveStatus` (string).

- [ ] **Step 1: Write the failing integration test**

`Wintime.Control.Tests.Integration/Imm/EffectiveStatusTests.cs`:
```csharp
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Imm;

[Collection("Integration")]
public class EffectiveStatusTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public EffectiveStatusTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetImmList_NoTaskIdleCache_ReturnsNoTask()
    {
        Guid immId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = new Core.Entities.Imm
            {
                Name = $"IMM-{Guid.NewGuid():N}",
                TemplateId = _factory.TestTemplateId,
                IsActive = true
            };
            db.Imms.Add(imm);
            await db.SaveChangesAsync();
            immId = imm.Id;

            var cache = scope.ServiceProvider.GetRequiredService<IImmStatusCache>();
            cache.SetStatus(immId, ImmStatus.Idle, DateTime.UtcNow);
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var imms = await client.GetFromJsonAsync<List<ImmDto>>("/api/imm?isActive=true");

        imms.Should().NotBeNull();
        imms!.Single(i => i.Id == immId).EffectiveStatus.Should().Be(EffectiveStatus.NoTask);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Integration --filter EffectiveStatusTests`
Expected: FAIL — `ImmDto.EffectiveStatus` не существует (компиляция) либо `null`.

- [ ] **Step 3: Add DTO fields**

В `ImmDto.cs` добавить после `public string? Status { get; set; }`:
```csharp
    public string? EffectiveStatus { get; set; } // Production/Setup/Downtime/Unplanned/NoTask/Offline
```
В `ImmStatusDto.cs` добавить после `Status`:
```csharp
    public string EffectiveStatus { get; set; } = string.Empty;
```

- [ ] **Step 4: Wire DI + GetImmList**

В `ImmController` добавить зависимость `IOptions<DowntimeSettings>`:
```csharp
using Microsoft.Extensions.Options;
using Wintime.Control.Core.Policies;
using Wintime.Control.Shared.Settings;
// ...
private readonly DowntimeSettings _downtime;

public ImmController(ControlDbContext context, IImmStatusCache statusCache, IImmCache immCache,
    IOptions<DowntimeSettings> downtime, ILogger<ImmController> logger)
{
    _context = context;
    _statusCache = statusCache;
    _immCache = immCache;
    _downtime = downtime.Value;
    _logger = logger;
}
```

В `GetImmList` добавить в проекцию (внутри `Select(i => new ImmDto { ... })`) поле статуса активного задания. Поскольку `ImmDto` не должен светить его наружу — выбрать в отдельный список параллельно. Заменить блок получения `imms` так, чтобы рядом собрать словарь статусов задания:
```csharp
var activeTaskStatuses = await query
    .Select(i => new
    {
        ImmId = i.Id,
        TaskStatus = (Core.Enums.TaskStatus?)i.ShiftTasks
            .Where(t => t.Status == Core.Enums.TaskStatus.InProgress || t.Status == Core.Enums.TaskStatus.Setup)
            .Select(t => (Core.Enums.TaskStatus?)t.Status)
            .FirstOrDefault()
    })
    .ToDictionaryAsync(x => x.ImmId, x => x.TaskStatus);

var openDowntimeImmIds = (await _context.Events
    .Where(e => e.EventType == Core.Enums.EventType.Downtime && e.EndTime == null)
    .Select(e => e.ImmId)
    .Distinct()
    .ToListAsync())
    .ToHashSet();
```

В существующем `foreach (var dto in imms)` (где уже заполняются `Status`/`LastUpdate`) добавить после строки `dto.Status = ...`:
```csharp
var rawForEff = statusEntry?.Status ?? ImmStatus.Offline;
var taskForEff = Core.Enums.ActiveTaskStatusMap.From(
    activeTaskStatuses.TryGetValue(dto.Id, out var ts) ? ts : null);
var hasOpenDt = openDowntimeImmIds.Contains(dto.Id);
var thresholdPassed = statusEntry != null &&
    (DateTime.UtcNow - statusEntry.SinceUtc).TotalSeconds >= _downtime.IdleThresholdSeconds;
dto.EffectiveStatus = ImmEffectiveStatus.Resolve(rawForEff, taskForEff, hasOpenDt, thresholdPassed);
```

- [ ] **Step 5: Wire GetImmStatus (одиночный)**

В `GetImmStatus`, после получения `currentTask` и `entry`, добавить и заполнить `EffectiveStatus`:
```csharp
var hasOpenDt = await _context.Events.AnyAsync(e =>
    e.ImmId == id && e.EventType == Core.Enums.EventType.Downtime && e.EndTime == null);
var taskForEff = Core.Enums.ActiveTaskStatusMap.From(currentTask?.Status);
var rawForEff = entry?.Status ?? ImmStatus.Offline;
var thresholdPassed = entry != null &&
    (DateTime.UtcNow - entry.SinceUtc).TotalSeconds >= _downtime.IdleThresholdSeconds;
var effective = ImmEffectiveStatus.Resolve(rawForEff, taskForEff, hasOpenDt, thresholdPassed);
```
и в возвращаемый `ImmStatusDto` добавить `EffectiveStatus = effective`. (Запрос `currentTask` уже выбирает Setup/InProgress задание; его поле `Status` доступно.)

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test Wintime.Control.Tests.Integration --filter EffectiveStatusTests`
Expected: PASS.

- [ ] **Step 7: Build whole solution**

Run: `dotnet build Wintime.Control.sln --configuration Debug`
Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add Wintime.Control.Core/DTOs/Imm/ImmDto.cs Wintime.Control.Core/DTOs/Imm/ImmStatusDto.cs Wintime.Control.API/Controllers/ImmController.cs Wintime.Control.Tests.Integration/Imm/EffectiveStatusTests.cs
git commit -m "feat: EffectiveStatus в live-эндпоинтах ТПА (список + статус)"
```

---

### Task 4: История — endpoint `GET /imm/{id}/effective-status-history`

**Files:**
- Create: `Wintime.Control.Core/DTOs/Imm/EffectiveStatusSegmentDto.cs`
- Modify: `Wintime.Control.API/Controllers/ImmController.cs`
- Test: `Wintime.Control.Tests.Integration/Imm/EffectiveStatusHistoryTests.cs` (create)

**Interfaces:**
- Consumes: `EffectiveStatusTimeline.Build` + record-типы (Task 2); `ImmStatusHistory`, `ShiftTask`, `Event` (существуют).
- Produces: endpoint, возвращающий `EffectiveStatusSegmentDto[]` `{ EffectiveStatus, ChangedAt, EndedAt }`.

- [ ] **Step 1: Write the failing integration test**

`Wintime.Control.Tests.Integration/Imm/EffectiveStatusHistoryTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Imm;

[Collection("Integration")]
public class EffectiveStatusHistoryTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public EffectiveStatusHistoryTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Returns_Production_Segment_For_Auto_InProgress()
    {
        var from = new DateTime(2026, 6, 24, 8, 0, 0, DateTimeKind.Utc);
        var to   = from.AddHours(1);
        Guid immId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = new Core.Entities.Imm
            {
                Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true
            };
            db.Imms.Add(imm);

            var mold = new Mold { Name = "M1", Cavities = 1, IsActive = true };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();

            db.ImmStatusHistory.Add(new ImmStatusHistory
            { ImmId = imm.Id, Status = ImmStatus.Auto, ChangedAt = from, EndedAt = to });

            db.ShiftTasks.Add(new ShiftTask
            {
                ImmId = imm.Id, MoldId = mold.Id, PlanQuantity = 100,
                Status = TaskStatus.InProgress, StartedAt = from, CompletedAt = to
            });
            await db.SaveChangesAsync();
            immId = imm.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var url = $"/api/imm/{immId}/effective-status-history?from={from:O}&to={to:O}";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var segs = await resp.Content.ReadFromJsonAsync<List<EffectiveStatusSegmentDto>>();
        segs.Should().NotBeNull();
        segs!.Should().ContainSingle();
        segs[0].EffectiveStatus.Should().Be(EffectiveStatus.Production);
    }
}
```

(Если у `Mold` обязательны иные поля — заполнить по образцу других интеграционных тестов; пароль/логин `test_manager`/`Manager123!` — по образцу `AuthHelper` в существующих тестах.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Integration --filter EffectiveStatusHistoryTests`
Expected: FAIL — `EffectiveStatusSegmentDto` / endpoint не существуют.

- [ ] **Step 3: Create DTO**

`Wintime.Control.Core/DTOs/Imm/EffectiveStatusSegmentDto.cs`:
```csharp
namespace Wintime.Control.Core.DTOs.Imm;

public class EffectiveStatusSegmentDto
{
    public string EffectiveStatus { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
```

- [ ] **Step 4: Implement endpoint**

В `ImmController` добавить метод (рядом с `GetImmStatusHistory`):
```csharp
/// <summary>
/// История эффективного состояния ТПА за период (реконструкция наложением рядов).
/// </summary>
[HttpGet("{id:guid}/effective-status-history")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer}")]
public async Task<ActionResult<IEnumerable<EffectiveStatusSegmentDto>>> GetImmEffectiveStatusHistory(
    Guid id,
    [FromQuery] DateTime from,
    [FromQuery] DateTime to)
{
    var immExists = await _context.Imms.AnyAsync(i => i.Id == id);
    if (!immExists)
        return NotFound();

    var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
    var toUtc   = DateTime.SpecifyKind(to,   DateTimeKind.Utc);
    var nowUtc  = DateTime.UtcNow;
    DateTime ClampEnd(DateTime? end) => (end ?? toUtc) > toUtc ? toUtc : (end ?? toUtc);

    var rawRows = await _context.ImmStatusHistory
        .Where(h => h.ImmId == id && h.ChangedAt < toUtc && (h.EndedAt == null || h.EndedAt > fromUtc))
        .OrderBy(h => h.ChangedAt)
        .Select(h => new { h.Status, h.ChangedAt, h.EndedAt })
        .ToListAsync();

    var taskRows = await _context.ShiftTasks
        .Where(t => t.ImmId == id && t.SetupStartedAt != null && t.SetupStartedAt < toUtc)
        .Select(t => new { t.SetupStartedAt, t.StartedAt, t.CompletedAt, t.ClosedAt })
        .ToListAsync();

    var downtimeRows = await _context.Events
        .Where(e => e.ImmId == id && e.EventType == Core.Enums.EventType.Downtime
                    && e.StartTime < toUtc && (e.EndTime == null || e.EndTime > fromUtc))
        .Select(e => new { e.StartTime, e.EndTime })
        .ToListAsync();

    var raw = rawRows
        .Select(r => new RawSegment(r.Status, r.ChangedAt, ClampEnd(r.EndedAt)))
        .ToList();

    var tasks = new List<TaskInterval>();
    foreach (var t in taskRows)
    {
        var setupStart = t.SetupStartedAt!.Value;
        var setupEnd   = t.StartedAt ?? t.CompletedAt ?? t.ClosedAt ?? toUtc;
        tasks.Add(new TaskInterval(Core.Enums.ActiveTaskStatus.Setup, setupStart, ClampEnd(setupEnd)));
        if (t.StartedAt != null)
        {
            var workEnd = t.CompletedAt ?? t.ClosedAt ?? toUtc;
            tasks.Add(new TaskInterval(Core.Enums.ActiveTaskStatus.InProgress, t.StartedAt.Value, ClampEnd(workEnd)));
        }
    }

    var downtimes = downtimeRows
        .Select(d => new Interval(d.StartTime, ClampEnd(d.EndTime)))
        .ToList();

    var segments = EffectiveStatusTimeline.Build(raw, tasks, downtimes, fromUtc, toUtc);

    var dto = segments.Select(s => new EffectiveStatusSegmentDto
    {
        EffectiveStatus = s.EffectiveStatus,
        ChangedAt = s.Start,
        EndedAt = s.End,
    });

    return Ok(dto);
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Wintime.Control.Tests.Integration --filter EffectiveStatusHistoryTests`
Expected: PASS.

- [ ] **Step 6: Run full backend test suite**

Run: `dotnet test Wintime.Control.sln`
Expected: PASS — все unit + integration (включая ранее существовавшие 226+24).

- [ ] **Step 7: Commit**

```bash
git add Wintime.Control.Core/DTOs/Imm/EffectiveStatusSegmentDto.cs Wintime.Control.API/Controllers/ImmController.cs Wintime.Control.Tests.Integration/Imm/EffectiveStatusHistoryTests.cs
git commit -m "feat: endpoint истории эффективного состояния ТПА (effective-status-history)"
```

---

### Task 5: Vitest + модуль палитры `effectiveStatus.js`

**Files:**
- Modify: `Wintime-Control-Frontend/package.json`
- Create: `Wintime-Control-Frontend/vitest.config.js`
- Create: `Wintime-Control-Frontend/src/constants/effectiveStatus.js`
- Test: `Wintime-Control-Frontend/src/constants/__tests__/effectiveStatus.spec.js`

**Interfaces:**
- Produces: `EFFECTIVE_STATUS` (объект, ключи = 6 состояний, поля `label,bg,text,dot,border,hex`), `EFFECTIVE_STATUS_KEYS` (массив ключей), `getEffectiveStatusMeta(key)` (с fallback на `Offline`).

- [ ] **Step 1: Add devDependencies + test script**

В `package.json` в `devDependencies` добавить `"vitest": "^3.2.4"`. В `scripts` добавить:
```json
    "test": "vitest run",
    "test:watch": "vitest"
```

- [ ] **Step 2: Install**

Run: `cd Wintime-Control-Frontend && npm install`
Expected: vitest установлен, без ошибок.

- [ ] **Step 3: Create vitest config**

`Wintime-Control-Frontend/vitest.config.js`:
```js
import { defineConfig } from 'vitest/config'
import { resolve } from 'path'

export default defineConfig({
  resolve: {
    alias: { '@': resolve(__dirname, 'src') },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.spec.js'],
  },
})
```

- [ ] **Step 4: Write the failing test**

`src/constants/__tests__/effectiveStatus.spec.js`:
```js
import { describe, it, expect } from 'vitest'
import { EFFECTIVE_STATUS, EFFECTIVE_STATUS_KEYS, getEffectiveStatusMeta } from '@/constants/effectiveStatus'

describe('EFFECTIVE_STATUS', () => {
  it('содержит ровно 6 состояний', () => {
    expect(EFFECTIVE_STATUS_KEYS).toEqual(
      ['Production', 'Setup', 'Downtime', 'Unplanned', 'NoTask', 'Offline']
    )
  })

  it('у каждого состояния заполнены все поля палитры', () => {
    for (const key of EFFECTIVE_STATUS_KEYS) {
      const m = EFFECTIVE_STATUS[key]
      expect(m.label).toBeTruthy()
      expect(m.bg).toMatch(/^bg-/)
      expect(m.text).toMatch(/^text-/)
      expect(m.dot).toMatch(/^bg-/)
      expect(m.border).toMatch(/^border-/)
      expect(m.hex).toMatch(/^#[0-9a-f]{6}$/i)
    }
  })

  it('getEffectiveStatusMeta откатывается на Offline для неизвестного ключа', () => {
    expect(getEffectiveStatusMeta('???')).toBe(EFFECTIVE_STATUS.Offline)
    expect(getEffectiveStatusMeta(null)).toBe(EFFECTIVE_STATUS.Offline)
  })
})
```

- [ ] **Step 5: Run test to verify it fails**

Run: `cd Wintime-Control-Frontend && npm run test`
Expected: FAIL — модуль `@/constants/effectiveStatus` не существует.

- [ ] **Step 6: Create the palette module**

`src/constants/effectiveStatus.js`:
```js
// Единый источник палитры эффективных состояний ТПА.
// Используется бейджем, карточкой, фильтром, KPI-панелью и таймлайном смены.
export const EFFECTIVE_STATUS = {
  Production: { label: 'Работа',             bg: 'bg-green-100',  text: 'text-green-800',  dot: 'bg-green-500',  border: 'border-green-500',  hex: '#22c55e' },
  Setup:      { label: 'Наладка',            bg: 'bg-yellow-100', text: 'text-yellow-800', dot: 'bg-yellow-500', border: 'border-yellow-500', hex: '#eab308' },
  Downtime:   { label: 'Простой',            bg: 'bg-red-100',    text: 'text-red-800',    dot: 'bg-red-500',    border: 'border-red-500',    hex: '#ef4444' },
  Unplanned:  { label: 'Работа без задания', bg: 'bg-purple-100', text: 'text-purple-800', dot: 'bg-purple-500', border: 'border-purple-500', hex: '#a855f7' },
  NoTask:     { label: 'Без задания',        bg: 'bg-blue-100',   text: 'text-blue-800',   dot: 'bg-blue-500',   border: 'border-blue-500',   hex: '#3b82f6' },
  Offline:    { label: 'Нет связи',          bg: 'bg-gray-100',   text: 'text-gray-800',   dot: 'bg-gray-500',   border: 'border-gray-400',   hex: '#9ca3af' },
}

export const EFFECTIVE_STATUS_KEYS = Object.keys(EFFECTIVE_STATUS)

export function getEffectiveStatusMeta(key) {
  return EFFECTIVE_STATUS[key] || EFFECTIVE_STATUS.Offline
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `cd Wintime-Control-Frontend && npm run test`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Wintime-Control-Frontend/package.json Wintime-Control-Frontend/package-lock.json Wintime-Control-Frontend/vitest.config.js Wintime-Control-Frontend/src/constants/effectiveStatus.js Wintime-Control-Frontend/src/constants/__tests__/effectiveStatus.spec.js
git commit -m "test: настройка Vitest + единый модуль палитры эффективных состояний"
```

---

### Task 6: `ImmStatusBadge.vue` → эффективная палитра

**Files:**
- Modify: `Wintime-Control-Frontend/src/components/dashboard/ImmStatusBadge.vue`

**Interfaces:**
- Consumes: `getEffectiveStatusMeta` / `EFFECTIVE_STATUS_KEYS` (Task 5).
- Produces: бейдж принимает `status` = один из 6 эффективных ключей.

- [ ] **Step 1: Rewrite the component script**

Заменить `<script setup>` целиком:
```vue
<script setup>
import { computed } from 'vue'
import { EFFECTIVE_STATUS, EFFECTIVE_STATUS_KEYS, getEffectiveStatusMeta } from '@/constants/effectiveStatus'

const props = defineProps({
  status: {
    type: String,
    required: true,
    validator: (v) => EFFECTIVE_STATUS_KEYS.includes(v),
  },
})

const config = computed(() => getEffectiveStatusMeta(props.status))
const statusClasses = computed(() => `${config.value.bg} ${config.value.text}`)
const dotClasses = computed(() => config.value.dot)
const label = computed(() => config.value.label)
</script>
```
`<template>` оставить без изменений (использует `statusClasses`, `dotClasses`, `label`).

- [ ] **Step 2: Verify build**

Run: `cd Wintime-Control-Frontend && npm run build`
Expected: build succeeded, без ошибок про `ImmStatusBadge`.

- [ ] **Step 3: Commit**

```bash
git add Wintime-Control-Frontend/src/components/dashboard/ImmStatusBadge.vue
git commit -m "feat: ImmStatusBadge на эффективных состояниях ТПА"
```

---

### Task 7: Стор дашборда — геттеры, фильтр, метрика загрузки

**Files:**
- Modify: `Wintime-Control-Frontend/src/stores/dashboard.js`
- Test: `Wintime-Control-Frontend/src/stores/__tests__/dashboard.spec.js` (create)

**Interfaces:**
- Consumes: эффективное поле `effectiveStatus` из API (Task 3).
- Produces (геттеры стора): `workingImms`(Production), `setupImms`(Setup), `downtimeImms`(Downtime), `noTaskImms`(NoTask), `unplannedImms`(Unplanned), `offlineImms`(Offline), `overallEfficiency`=(Production+Setup)/total; `filteredImms` фильтрует по `status` (= эффективному).

- [ ] **Step 1: Write the failing store test**

`src/stores/__tests__/dashboard.spec.js`:
```js
import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useDashboardStore } from '@/stores/dashboard'

function seed(store) {
  store.imms = [
    { id: '1', name: 'A', status: 'Production' },
    { id: '2', name: 'B', status: 'Production' },
    { id: '3', name: 'C', status: 'Setup' },
    { id: '4', name: 'D', status: 'Downtime' },
    { id: '5', name: 'E', status: 'NoTask' },
    { id: '6', name: 'F', status: 'Unplanned' },
    { id: '7', name: 'G', status: 'Offline' },
  ]
}

describe('dashboard store — эффективные состояния', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('группирует ТПА по эффективным состояниям', () => {
    const store = useDashboardStore()
    seed(store)
    expect(store.workingImms).toHaveLength(2)
    expect(store.setupImms).toHaveLength(1)
    expect(store.downtimeImms).toHaveLength(1)
    expect(store.noTaskImms).toHaveLength(1)
    expect(store.unplannedImms).toHaveLength(1)
    expect(store.offlineImms).toHaveLength(1)
  })

  it('overallEfficiency = (Production + Setup) / всего', () => {
    const store = useDashboardStore()
    seed(store)
    // (2 + 1) / 7 = 42.857 → 43
    expect(store.overallEfficiency).toBe(43)
  })

  it('filteredImms фильтрует по эффективному статусу', () => {
    const store = useDashboardStore()
    seed(store)
    store.setFilter('status', 'Downtime')
    expect(store.filteredImms).toHaveLength(1)
    expect(store.filteredImms[0].id).toBe('4')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd Wintime-Control-Frontend && npm run test -- dashboard`
Expected: FAIL — `downtimeImms`/`noTaskImms`/`unplannedImms` не существуют, `overallEfficiency` считает по `Auto`/`Manual`.

- [ ] **Step 3: Update getters**

В `src/stores/dashboard.js` заменить геттеры группировки и `overallEfficiency`:
```js
    workingImms:   (state) => state.imms.filter(i => i.status === 'Production'),
    setupImms:     (state) => state.imms.filter(i => i.status === 'Setup'),
    downtimeImms:  (state) => state.imms.filter(i => i.status === 'Downtime'),
    unplannedImms: (state) => state.imms.filter(i => i.status === 'Unplanned'),
    noTaskImms:    (state) => state.imms.filter(i => i.status === 'NoTask'),
    offlineImms:   (state) => state.imms.filter(i => i.status === 'Offline'),

    // Мгновенная загрузка цеха: (Production + Setup) / все
    overallEfficiency: (state) => {
      if (state.imms.length === 0) return 0
      const active = state.imms.filter(i => i.status === 'Production' || i.status === 'Setup')
      return Math.round(active.length / state.imms.length * 100)
    },
```
Удалить старые `alarmImms`/`idleImms` геттеры (если на них больше нет ссылок — проверить grep по `alarmImms`/`idleImms`; ссылки из `DashboardView` правятся в Task 8). `hasAlarms` геттер удалить (Alarm растворён).

- [ ] **Step 4: Update loadImms mapping**

В action `loadImms`, в `.map(imm => ({...}))` заменить так, чтобы `status` брался из эффективного, а сырой сохранялся:
```js
        this.imms = response.data.map(imm => ({
          ...imm,
          rawStatus: imm.status,                       // сырой — про запас (BL-19)
          status: imm.effectiveStatus || 'Offline',    // на дашборде используем эффективный
          currentCycleTime: imm.avgCycleTime || 0
        }))
```
В action `refreshImmStatus` аналогично: `status: response.data.effectiveStatus || 'Offline'` (вместо `response.data.status`).

- [ ] **Step 5: Run test to verify it passes**

Run: `cd Wintime-Control-Frontend && npm run test -- dashboard`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Wintime-Control-Frontend/src/stores/dashboard.js Wintime-Control-Frontend/src/stores/__tests__/dashboard.spec.js
git commit -m "feat: стор дашборда на эффективных состояниях + тесты геттеров"
```

---

### Task 8: `DashboardView.vue` (KPI вариант B + фильтр) и `ImmCard.vue`

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/dashboard/DashboardView.vue`
- Modify: `Wintime-Control-Frontend/src/components/dashboard/ImmCard.vue`

**Interfaces:**
- Consumes: геттеры стора (Task 7), `EFFECTIVE_STATUS`/`getEffectiveStatusMeta` (Task 5), `imm.effectiveStatus` через `imm.status` (Task 7 маппинг).

- [ ] **Step 1: KPI-панель — вариант B (7 карточек)**

В `DashboardView.vue` заменить блок «Статистика цеха» (5 карточек) на 7: Всего · Работа · Наладка · Простой · Без задания · Нет связи · Текущая загрузка. Карточки используют геттеры: `totalImms`, `workingImms.length`, `setupImms.length`, `downtimeImms.length`, `noTaskImms.length`, `offlineImms.length`, и `instantUtilizationLabel`. В карточке «Работа» при `unplannedImms.length > 0` добавить подпись-предупреждение:
```html
<p v-if="dashboardStore.unplannedImms.length" class="text-xs text-purple-600 mt-0.5">
  + {{ dashboardStore.unplannedImms.length }} без задания
</p>
```
Цвет иконок/плашек выбрать по палитре: Работа — green, Наладка — yellow, Простой — red, Без задания — blue, Нет связи — gray. Сетку контейнера сменить на `lg:grid-cols-7`.

- [ ] **Step 2: Фильтр статуса — 6 опций**

Заменить `<el-select v-model="statusFilter">` опции на 6 эффективных:
```html
<el-option label="Работа" value="Production" />
<el-option label="Наладка" value="Setup" />
<el-option label="Простой" value="Downtime" />
<el-option label="Работа без задания" value="Unplanned" />
<el-option label="Без задания" value="NoTask" />
<el-option label="Нет связи" value="Offline" />
```

- [ ] **Step 3: Поправить `instantUtilizationLabel` и `shiftEndedLabel`**

В `DashboardView.vue` в этих computed заменить проверки `i.status === 'Auto' || i.status === 'Manual'` на `i.status === 'Production' || i.status === 'Setup'` (актуальные эффективные ключи).

- [ ] **Step 4: `ImmCard.vue` — рамка по эффективному статусу, убрать баннер аварии**

В `ImmCard.vue`:
- `borderColor` заменить на использование палитры:
```js
import { getEffectiveStatusMeta } from '@/constants/effectiveStatus'
const borderColor = computed(() => getEffectiveStatusMeta(props.imm.status).border)
```
- удалить блок `<el-alert v-if="imm.status === 'Alarm'" ...>` (раствор Alarm → BL-19);
- бейдж уже `<ImmStatusBadge :status="imm.status" />` — `imm.status` теперь эффективный, оставить.

- [ ] **Step 5: Verify build**

Run: `cd Wintime-Control-Frontend && npm run build`
Expected: build succeeded.

- [ ] **Step 6: Manual smoke check**

Запустить `./Start-DevEnv.ps1 -Dev -SkipTests`, открыть дашборд: бейджи/рамки/фильтр/счётчики показывают эффективные состояния; карточка без красного баннера аварии. (Если dev-окружение недоступно — пропустить, полагаясь на build.)

- [ ] **Step 7: Commit**

```bash
git add Wintime-Control-Frontend/src/views/dashboard/DashboardView.vue Wintime-Control-Frontend/src/components/dashboard/ImmCard.vue
git commit -m "feat: дашборд на эффективных состояниях — KPI (7 карточек), фильтр, карточка ТПА"
```

---

### Task 9: Таймлайн смены — `api`, `ImmDetailModal.vue`, `ShiftTimeline.vue`

**Files:**
- Modify: `Wintime-Control-Frontend/src/api/dashboard.js`
- Modify: `Wintime-Control-Frontend/src/views/dashboard/ImmDetailModal.vue`
- Modify: `Wintime-Control-Frontend/src/components/dashboard/ShiftTimeline.vue`

**Interfaces:**
- Consumes: endpoint `effective-status-history` (Task 4) → сегменты `{ effectiveStatus, changedAt, endedAt }`; `EFFECTIVE_STATUS`/`getEffectiveStatusMeta` (Task 5).

- [ ] **Step 1: Add api method**

В `src/api/dashboard.js` добавить (рядом с `getImmStatusHistory`):
```js
  // История эффективного состояния ТПА за период (для таймлайна смены)
  getImmEffectiveStatusHistory(id, params) {
    return apiClient.get(`/imm/${id}/effective-status-history`, { params })
  },
```

- [ ] **Step 2: `ShiftTimeline.vue` — читать `effectiveStatus`, цвет из палитры**

В `ShiftTimeline.vue`:
- импорт: `import { getEffectiveStatusMeta } from '@/constants/effectiveStatus'`;
- удалить локальные `STATUS_COLORS` и `STATUS_LABELS`;
- `statusColor` заменить на:
```js
function statusColor(seg) { return getEffectiveStatusMeta(seg).hex }
```
- в `segStyle` заменить `background: statusColor(seg.status)` на `background: statusColor(seg.effectiveStatus)`;
- в `segTitle` заменить чтение `seg.status` на `seg.effectiveStatus` и `STATUS_LABELS[...]` на `getEffectiveStatusMeta(seg.effectiveStatus).label`:
```js
function segTitle(seg) {
  const start = new Date(seg.changedAt)
  const end   = seg.endedAt ? new Date(seg.endedAt) : now.value
  return `${getEffectiveStatusMeta(seg.effectiveStatus).label}\n${formatTime(start)} — ${formatTime(end)}\n${durStr(end - start)}`
}
```
- обновить комментарий props: `statusSegments` теперь `EffectiveStatusSegmentDto[]`.

- [ ] **Step 3: `ImmDetailModal.vue` — вызвать новый endpoint, легенда и сводка на эффективных**

В `ImmDetailModal.vue`:
- в `loadData` заменить вызов:
```js
      dashboardApi.getImmEffectiveStatusHistory(props.immId, { from: fromIso, to: toIso }),
```
- заменить `STATUS_LEGEND` на построение из палитры:
```js
import { EFFECTIVE_STATUS, EFFECTIVE_STATUS_KEYS } from '@/constants/effectiveStatus'
const STATUS_LEGEND = EFFECTIVE_STATUS_KEYS.map(k => ({
  status: k, label: EFFECTIVE_STATUS[k].label, color: EFFECTIVE_STATUS[k].hex,
}))
```
- переписать `summary` (агрегаты по эффективным ключам). Заменить computed и разметку «Итоги смены» на 6 эффективных категорий:
```js
const summary = computed(() => {
  const totals = { Production: 0, Setup: 0, Downtime: 0, Unplanned: 0, NoTask: 0, Offline: 0 }
  for (const seg of statusSegments.value) {
    const ms = segDurationMs(seg)
    const key = seg.effectiveStatus
    if (key in totals) totals[key] += ms
  }
  const out = {}
  for (const k of Object.keys(totals)) out[k] = msToHm(totals[k])
  return out
})
```
- `segDurationMs` использует `seg.changedAt`/`seg.endedAt` — поля не изменились, оставить.
- В шаблоне «Итоги смены» заменить 5 жёстко заданных плашек на цикл по палитре:
```html
<div class="grid grid-cols-6 gap-3">
  <div v-for="item in STATUS_LEGEND" :key="item.status"
       class="border rounded-lg p-3 text-center" :style="{ borderColor: item.color }">
    <div class="text-xl font-bold" :style="{ color: item.color }">{{ summary[item.status] }}</div>
    <div class="text-xs text-gray-500 mt-0.5">{{ item.label }}</div>
  </div>
</div>
```

- [ ] **Step 4: Verify build**

Run: `cd Wintime-Control-Frontend && npm run build`
Expected: build succeeded.

- [ ] **Step 5: Run all frontend tests**

Run: `cd Wintime-Control-Frontend && npm run test`
Expected: PASS (палитра + стор).

- [ ] **Step 6: Commit**

```bash
git add Wintime-Control-Frontend/src/api/dashboard.js Wintime-Control-Frontend/src/views/dashboard/ImmDetailModal.vue Wintime-Control-Frontend/src/components/dashboard/ShiftTimeline.vue
git commit -m "feat: таймлайн смены на эффективных состояниях (история + легенда + итоги)"
```

---

### Task 10: `Start-DevEnv.ps1` (прогон Vitest) + `CLAUDE.md`

**Files:**
- Modify: `Start-DevEnv.ps1`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add Vitest to the test step**

В `Start-DevEnv.ps1`, в блоке «── 3. Тесты ──» внутри `if (-not $SkipTests) { ... }`, после блока `dotnet test` (после `Write-Ok "Все тесты прошли"`), добавить:
```powershell
    Write-Step "Запуск frontend-тестов (Vitest)..."
    Push-Location "$root\Wintime-Control-Frontend"
    try {
        if (-not (Test-Path 'node_modules')) { npm install }
        npm run test
        if ($LASTEXITCODE -ne 0) {
            Write-Fail "Frontend-тесты упали"
            exit 1
        }
    } finally {
        Pop-Location
    }
    Write-Ok "Frontend-тесты прошли"
```

- [ ] **Step 2: Verify script parses**

Run: `pwsh -NoProfile -Command "& { . ./Start-DevEnv.ps1 -SkipBuild -SkipTests }" ` — прерывать после проверки Docker не нужно; достаточно убедиться, что скрипт не падает с синтаксической ошибкой парсинга. (Если Docker-контейнеры не запущены, скрипт штатно выйдет на шаге 1 — это ок, синтаксис проверен.)
Альтернатива без запуска: `pwsh -NoProfile -Command "[scriptblock]::Create((Get-Content -Raw ./Start-DevEnv.ps1)) | Out-Null; 'parse ok'"`
Expected: `parse ok` (нет ошибок парсинга).

- [ ] **Step 3: Update CLAUDE.md**

В `CLAUDE.md` в раздел «Статусы ТПА» добавить подсекцию после таблицы статусов:
```markdown
#### Эффективное состояние (для дашборда)

Дашборд показывает **эффективное состояние** = `ImmEffectiveStatus.Resolve(сырой режим,
статус активного задания, открытый простой, порог пройден)` — чистая функция в
`Wintime.Control.Core/Policies`. 6 значений: `Production` (Работа), `Setup` (Наладка),
`Downtime` (Простой), `Unplanned` (Работа без задания), `NoTask` (Без задания),
`Offline` (Нет связи). Сырой `mode` по-прежнему хранится в `ImmStatusHistory`;
эффективное состояние **не хранится** — вычисляется на лету (для истории —
`EffectiveStatusTimeline.Build` наложением рядов). Палитра/подписи на фронте —
`src/constants/effectiveStatus.js`. См. дизайн
`docs/superpowers/specs/2026-06-24-effective-status-dashboard-design.md`.
```

- [ ] **Step 4: Commit**

```bash
git add Start-DevEnv.ps1 CLAUDE.md
git commit -m "chore: прогон Vitest в Start-DevEnv + правило эффективного состояния в CLAUDE.md"
```

---

## Self-Review

**Spec coverage:**
- Секция 1 (Core Resolve) → Task 1 ✓
- Секция 3 (история, Build + наложение) → Task 2 (функция) + Task 4 (endpoint) ✓
- Секция 2 (live-контракт, DTO, контроллер) → Task 3 ✓
- Секция 4 (фронт-дашборд: палитра, бейдж, стор, KPI вариант B, фильтр, карточка) → Tasks 5,6,7,8 ✓
- Секция 5 (ImmDetailModal таймлайн) → Task 9 ✓
- Секция 6 (тесты: xUnit по матрице, Vitest setup; Start-DevEnv; CLAUDE.md) → Tasks 1,2,5,7,10 ✓
- Метрика загрузки `(Production+Setup)/всего` → Task 7 ✓
- Раствор Alarm + удаление баннера → Task 8 ✓

**Placeholder scan:** код приведён во всех шагах; «manual smoke check» (Task 8 Step 6) помечен как пропускаемый при недоступности dev-окружения.

**Type consistency:** `EffectiveStatus.*` (Core, Task 1) используется в Task 2/3/4. `EFFECTIVE_STATUS`/`getEffectiveStatusMeta`/`EFFECTIVE_STATUS_KEYS` (Task 5) — в Task 6/8/9. Сегменты истории: бэкенд отдаёт `EffectiveStatus`/`ChangedAt`/`EndedAt` (Task 4) → JSON camelCase `effectiveStatus`/`changedAt`/`endedAt`, фронт читает `seg.effectiveStatus`/`seg.changedAt`/`seg.endedAt` (Task 9) ✓. Геттеры стора `downtimeImms`/`noTaskImms`/`unplannedImms` (Task 7) используются в KPI (Task 8) ✓.
