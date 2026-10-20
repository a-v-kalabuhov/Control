# Итоги выполнения задания — вкладка «Результаты выполнения» Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** В карточке задания вкладка «Результаты выполнения»: выпуск/брак/перевыпуск, фактические средние циклы против эталонов и время выпуска по статусам (работа / простои / нет связи), считается на лету.

**Architecture:** `TaskResultsService` (Infrastructure) считает итоги по окну выпуска `TaskActivityWindow.Release`, переиспользуя `IEffectiveStatusHistoryService` + `EffectiveStatusTimeline.Build` (те же цифры, что карточка ТПА и отчёты) и агрегат циклов как у прогноза (ADR-0017). Отдаётся `GET /api/tasks/{id}/results`. Фронт: `TaskDetailModal` получает вкладки, новая `TaskResultsTab.vue` грузит эндпоинт при открытии вкладки и обновляется раз в минуту, пока задание в работе.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql, xUnit + FluentAssertions (Integration, Testcontainers PostgreSQL); Vue 3 + Element Plus, Vitest + @vue/test-utils.

**Spec:** `docs/superpowers/specs/2026-10-15-task-results-tab-design.md`

## Global Constraints

- Окно выпуска = `[StartedAt, CompletedAt ?? ClosedAt ?? now)` — только через `TaskActivityWindow.Release`, своего определения окна не заводить.
- Итоги **не хранятся**; схема БД не меняется, миграций нет.
- Все `DateTime` в EF-запросах — `Kind=Utc` (берутся из сущности и `DateTime.UtcNow`).
- Средние циклы — только по закрытым успешным циклам задания (`TaskId = id`, `IsSuccessful`, `EndTime != null`); при `count < ReleaseForecast.MinClosedCycles` (3) средние = `null`.
- Полный цикл = `AVG(DurationSeconds) + AVG(PauseDurationMs)/1000` (`null`-паузы в среднее не входят, ADR-0017).
- Время по статусам — `EffectiveStatusTimeline.Build(..., gapAsNoData: false)` по окну выпуска; «Нет связи» — отдельной строкой.
- Количество простоев — `Event(Downtime)` на ТПА задания, пересекающие окно (авто и ручные).
- Права эндпоинта — `Admin, Manager, Adjuster` (как `GET /api/tasks/{id}`); Observer → 403.
- Проценты и отклонения считает фронт; API отдаёт секунды.
- Тесты: xUnit (Integration) для .NET, Vitest для фронта.
- Перед PR поднять **минорный** номер в `VERSION` относительно master (master сейчас 0.11.0; если PR #135 с 0.12.0 уже смёржен — ставить 0.13.0).

---

## File Structure

| Файл | Ответственность |
|---|---|
| Create `Wintime.Control.Core/DTOs/Tasks/TaskResultsDto.cs` | Контракт ответа (4 класса: корень + выпуск + циклы + время) |
| Create `Wintime.Control.Core/Interfaces/ITaskResultsService.cs` | Интерфейс расчёта итогов |
| Create `Wintime.Control.Infrastructure/Services/TaskResultsService.cs` | Расчёт итогов (одна точка; на ней потом BL-05) |
| Modify `Wintime.Control.API/Program.cs:175` | DI-регистрация |
| Modify `Wintime.Control.API/Controllers/TasksController.cs` | Эндпоинт `GET {id}/results` |
| Create `Wintime.Control.Tests.Integration/Tasks/TaskResultsApiTests.cs` | Интеграционные тесты эндпоинта |
| Modify `Wintime-Control-Frontend/src/api/tasks.js` | `tasksApi.getResults(id)` |
| Create `Wintime-Control-Frontend/src/utils/taskResults.js` | Чистые форматтеры: секунды, доля, отклонение |
| Create `Wintime-Control-Frontend/src/utils/__tests__/taskResults.spec.js` | Тесты форматтеров |
| Create `Wintime-Control-Frontend/src/components/tasks/TaskResultsTab.vue` | Вкладка «Результаты выполнения» |
| Create `Wintime-Control-Frontend/src/components/tasks/__tests__/TaskResultsTab.spec.js` | Тесты вкладки |
| Modify `Wintime-Control-Frontend/src/views/tasks/TaskDetailModal.vue` | Вкладки «Данные задания» / «Результаты выполнения» |
| Modify `Wintime-Control-Frontend/src/views/tasks/__tests__/TaskDetailModal.spec.js` | Тест вкладок |
| Modify `docs/user-guide/manager.md` | Описание вкладки |
| Modify `VERSION` | Минорный bump перед PR |

---

### Task 1: Бэкенд — сервис итогов и эндпоинт `GET /api/tasks/{id}/results`

**Files:**
- Create: `Wintime.Control.Core/DTOs/Tasks/TaskResultsDto.cs`
- Create: `Wintime.Control.Core/Interfaces/ITaskResultsService.cs`
- Create: `Wintime.Control.Infrastructure/Services/TaskResultsService.cs`
- Modify: `Wintime.Control.API/Program.cs` (строка 175, рядом с `IEffectiveStatusHistoryService`)
- Modify: `Wintime.Control.API/Controllers/TasksController.cs` (конструктор + новый action после `GetTaskById`)
- Test: `Wintime.Control.Tests.Integration/Tasks/TaskResultsApiTests.cs`

**Interfaces:**
- Consumes: `TaskActivityWindow.Release(ShiftTask, DateTime) : Interval?` (`Core/Policies/TaskActivityWindow.cs`); `IEffectiveStatusHistoryService.GatherAsync(...)`; `EffectiveStatusTimeline.Build(raw, tasks, downtimes, from, to, gapAsNoData)`; `ReleaseForecast.MinClosedCycles`; `EffectiveStatus.Production/Downtime/Offline` (`Core/Constants`).
- Produces: JSON `GET /api/tasks/{id}/results` (camelCase):
  `{ releaseStarted, releaseFrom, releaseTo, inProgress, output: { planQuantity, actualQuantity, defectQuantity, goodQuantity, overrunQuantity, deviationReason }, cycles: { count, avgInjectionSeconds, plannedInjectionSeconds, avgFullCycleSeconds, plannedFullCycleSeconds }, time: { windowSeconds, productionSeconds, downtimeSeconds, downtimeCount, offlineSeconds } }`.
  При `releaseStarted = false` — `releaseFrom/releaseTo/output/cycles/time = null`, `inProgress = false`.

- [ ] **Step 1: Write the failing tests**

Create `Wintime.Control.Tests.Integration/Tasks/TaskResultsApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskResultsApiTests : IClassFixture<IntegrationTestFactory>
{
    // Завершённое задание в прошлом: окно закрыто, расчёт детерминирован.
    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static DateTime M(int min) => T0.AddMinutes(min);

    private readonly IntegrationTestFactory _factory;
    public TaskResultsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string user = "test_manager", string password = "Manager123!")
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, user, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    /// <summary>Свой ТПА и ПФ на тест — чтобы статусы, циклы и простои не пересекались между тестами.</summary>
    private async Task<(Guid ImmId, Guid MoldId)> SeedImmAndMoldAsync(ControlDbContext db)
    {
        var imm = new Imm { Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true };
        var mold = new Mold { Name = "M-results", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 2, IsActive = true };
        db.Imms.Add(imm);
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return (imm.Id, mold.Id);
    }

    private static ImmCycle Cycle(Guid immId, Guid? taskId, DateTime start, int durationS,
        int? injectionMs, int? pauseMs, bool successful = true, bool closed = true) => new()
    {
        ImmId = immId, TaskId = taskId, StartTime = start,
        EndTime = closed ? start.AddSeconds(durationS) : null,
        DurationSeconds = closed ? durationS : 0, IsSuccessful = successful, Cavities = 2,
        InjectionDurationMs = injectionMs, PauseDurationMs = pauseMs
    };

    private static Event Downtime(Guid immId, DateTime start, DateTime? end) => new()
    {
        ImmId = immId, EventType = EventType.Downtime, StartTime = start, EndTime = end, IsAuto = true
    };

    private static ImmStatusHistory Raw(Guid immId, string status, DateTime from, DateTime to) =>
        new() { ImmId = immId, Status = status, ChangedAt = from, EndedAt = to };

    [Fact]
    public async Task UnknownTask_Returns404()
    {
        var client = await ClientAsync();

        var resp = await client.GetAsync($"/api/tasks/{Guid.NewGuid()}/results");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Observer_Returns403()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask { ImmId = immId, MoldId = moldId, PlanQuantity = 10, Status = TaskStatus.Planned };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync("test_observer", "Observer123!");

        var resp = await client.GetAsync($"/api/tasks/{taskId}/results");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReleaseNotStarted_ReturnsFlagAndNullBlocks()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, PlanQuantity = 10, Status = TaskStatus.Setup,
                SetupStartedAt = M(0)
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.ReleaseStarted.Should().BeFalse();
        result.InProgress.Should().BeFalse();
        result.ReleaseFrom.Should().BeNull();
        result.ReleaseTo.Should().BeNull();
        result.Output.Should().BeNull();
        result.Cycles.Should().BeNull();
        result.Time.Should().BeNull();
    }

    [Fact]
    public async Task CompletedTask_ComputesOutputCyclesAndTimeOverReleaseWindow()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.Completed,
                PlanQuantity = 100, ActualQuantity = 110, DefectQuantity = 4,
                CloseReason = "Перевыпуск по просьбе мастера",
                PlannedFullCycleSeconds = 60, PlannedInjectionCycleSeconds = 55,
                SetupStartedAt = M(-30), StartedAt = M(0), CompletedAt = M(60)
            };
            var other = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.Completed, PlanQuantity = 1,
                SetupStartedAt = M(-200), StartedAt = M(-190), CompletedAt = M(-100)
            };
            db.ShiftTasks.AddRange(task, other);
            await db.SaveChangesAsync();

            // Окно выпуска 0–60 мин. Сырой: idle 0–5, auto 5–10, offline 10–30, idle 30–50, auto 50–60.
            db.ImmStatusHistory.AddRange(
                Raw(immId, ImmStatus.Idle,    M(0),  M(5)),
                Raw(immId, ImmStatus.Auto,    M(5),  M(10)),
                Raw(immId, ImmStatus.Offline, M(10), M(30)),
                Raw(immId, ImmStatus.Idle,    M(30), M(50)),
                Raw(immId, ImmStatus.Auto,    M(50), M(60)));
            // Простой, начавшийся до окна (обрезается по окну), простой 35–50 и простой после окна (не считается).
            db.Events.AddRange(
                Downtime(immId, M(-20), M(5)),
                Downtime(immId, M(35),  M(50)),
                Downtime(immId, M(70),  M(80)));
            // Закрытые успешные циклы задания: длит. 10/12/14 → 12; пауза 20/null/30 с → 25; литьё 8/9/10 → 9.
            db.ImmCycles.AddRange(
                Cycle(immId, task.Id, M(51), 10, 8000,  20000),
                Cycle(immId, task.Id, M(52), 12, 9000,  null),
                Cycle(immId, task.Id, M(53), 14, 10000, 30000),
                Cycle(immId, task.Id, M(54), 99, 99000, 99000, closed: false),     // открытый
                Cycle(immId, task.Id, M(55), 99, 99000, 99000, successful: false), // неуспешный
                Cycle(immId, other.Id, M(-150), 99, 99000, 99000));                // чужое задание
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.ReleaseStarted.Should().BeTrue();
        result.InProgress.Should().BeFalse();
        result.ReleaseFrom.Should().Be(M(0));
        result.ReleaseTo.Should().Be(M(60));

        result.Output!.PlanQuantity.Should().Be(100);
        result.Output.ActualQuantity.Should().Be(110);
        result.Output.DefectQuantity.Should().Be(4);
        result.Output.GoodQuantity.Should().Be(106);
        result.Output.OverrunQuantity.Should().Be(10);
        result.Output.DeviationReason.Should().Be("Перевыпуск по просьбе мастера");

        result.Cycles!.Count.Should().Be(3);
        result.Cycles.AvgInjectionSeconds.Should().BeApproximately(9.0, 1e-6);
        result.Cycles.AvgFullCycleSeconds.Should().BeApproximately(12.0 + 25.0, 1e-6);
        result.Cycles.PlannedInjectionSeconds.Should().Be(55);
        result.Cycles.PlannedFullCycleSeconds.Should().Be(60);

        // 0–5 простой (событие) · 5–10 работа · 10–30 нет связи · 30–35 работа (до порога) ·
        // 35–50 простой · 50–60 работа → по 20 мин каждого.
        result.Time!.WindowSeconds.Should().Be(3600);
        result.Time.ProductionSeconds.Should().Be(1200);
        result.Time.DowntimeSeconds.Should().Be(1200);
        result.Time.OfflineSeconds.Should().Be(1200);
        result.Time.DowntimeCount.Should().Be(2);
    }

    [Fact]
    public async Task FewerThanThreeClosedCycles_AveragesAreNull()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.Completed, PlanQuantity = 10,
                PlannedFullCycleSeconds = 60, SetupStartedAt = M(-10), StartedAt = M(0), CompletedAt = M(10)
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            db.ImmCycles.AddRange(
                Cycle(immId, task.Id, M(1), 10, 8000, 20000),
                Cycle(immId, task.Id, M(2), 12, 9000, 20000));
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.Cycles!.Count.Should().Be(2);
        result.Cycles.AvgInjectionSeconds.Should().BeNull();
        result.Cycles.AvgFullCycleSeconds.Should().BeNull();
        result.Cycles.PlannedFullCycleSeconds.Should().Be(60);
        result.Cycles.PlannedInjectionSeconds.Should().BeNull();
    }

    [Fact]
    public async Task CyclesWithoutInjectionDuration_InjectionAverageIsNull()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.Completed, PlanQuantity = 10,
                SetupStartedAt = M(-10), StartedAt = M(0), CompletedAt = M(10)
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            db.ImmCycles.AddRange(
                Cycle(immId, task.Id, M(1), 10, null, null),
                Cycle(immId, task.Id, M(2), 10, null, null),
                Cycle(immId, task.Id, M(3), 10, null, null));
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.Cycles!.Count.Should().Be(3);
        result.Cycles.AvgInjectionSeconds.Should().BeNull();
        result.Cycles.AvgFullCycleSeconds.Should().BeApproximately(10.0, 1e-6); // пауз нет → только длительность
    }

    [Fact]
    public async Task InProgressTask_WindowRunsToNow_NoUnderrunReasonAndNoOverrun()
    {
        Guid taskId;
        var startedAt = DateTime.UtcNow.AddHours(-2);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.InProgress,
                PlanQuantity = 100, ActualQuantity = 40,
                SetupStartedAt = startedAt.AddMinutes(-15), StartedAt = startedAt
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.InProgress.Should().BeTrue();
        result.ReleaseTo.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        result.Output!.OverrunQuantity.Should().Be(0);
        result.Output.GoodQuantity.Should().Be(40);
        result.Output.DeviationReason.Should().BeNull();
        // Сырой истории нет → всё окно «Нет связи» (gapAsNoData: false).
        result.Time!.OfflineSeconds.Should().BeCloseTo(result.Time.WindowSeconds, 1);
    }

    [Fact]
    public async Task ClosedWithoutCompletion_WindowEndsAtClosedAt()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.Closed, PlanQuantity = 10,
                CloseReason = "Сломалась ПФ",
                SetupStartedAt = M(-10), StartedAt = M(0), ClosedAt = M(30)
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.InProgress.Should().BeFalse();
        result.ReleaseTo.Should().Be(M(30));
        result.Time!.WindowSeconds.Should().Be(1800);
        // Причина отклонения — только для Completed; у Closed это причина закрытия (вкладка «Данные задания»).
        result.Output!.DeviationReason.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~TaskResultsApiTests"`
Expected: build FAIL — `The type or namespace name 'TaskResultsDto' could not be found`.

- [ ] **Step 3: Write the DTO**

Create `Wintime.Control.Core/DTOs/Tasks/TaskResultsDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>
/// Итоги выполнения задания по окну выпуска <c>[StartedAt, CompletedAt ?? ClosedAt ?? now)</c>.
/// Не хранятся — считаются на лету (<c>ITaskResultsService</c>). Проценты и отклонения считает фронт.
/// </summary>
public class TaskResultsDto
{
    /// <summary>false — выпуск не начинался; остальные блоки null.</summary>
    public bool ReleaseStarted { get; set; }
    public DateTime? ReleaseFrom { get; set; }
    public DateTime? ReleaseTo { get; set; }
    /// <summary>Окно открыто (задание не завершено и не закрыто) — правый край = «сейчас».</summary>
    public bool InProgress { get; set; }
    public TaskOutputResultsDto? Output { get; set; }
    public TaskCycleResultsDto? Cycles { get; set; }
    public TaskTimeResultsDto? Time { get; set; }
}

public class TaskOutputResultsDto
{
    public int PlanQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public int DefectQuantity { get; set; }
    /// <summary>Выпущено − брак.</summary>
    public int GoodQuantity { get; set; }
    /// <summary>max(0, выпущено − план).</summary>
    public int OverrunQuantity { get; set; }
    /// <summary>«Причина отклонения от плана» — CloseReason завершённого задания.</summary>
    public string? DeviationReason { get; set; }
}

public class TaskCycleResultsDto
{
    /// <summary>Закрытые успешные циклы задания.</summary>
    public int Count { get; set; }
    /// <summary>AVG(InjectionDurationMs)/1000; null — циклов меньше 3 или литьё не измерялось.</summary>
    public double? AvgInjectionSeconds { get; set; }
    public int? PlannedInjectionSeconds { get; set; }
    /// <summary>AVG(DurationSeconds) + AVG(PauseDurationMs)/1000 (ADR-0017); null — циклов меньше 3.</summary>
    public double? AvgFullCycleSeconds { get; set; }
    public int? PlannedFullCycleSeconds { get; set; }
}

public class TaskTimeResultsDto
{
    public int WindowSeconds { get; set; }
    public int ProductionSeconds { get; set; }
    public int DowntimeSeconds { get; set; }
    /// <summary>События простоя на ТПА задания, пересекающие окно (авто и ручные).</summary>
    public int DowntimeCount { get; set; }
    public int OfflineSeconds { get; set; }
}
```

- [ ] **Step 4: Write the interface**

Create `Wintime.Control.Core/Interfaces/ITaskResultsService.cs`:

```csharp
using Wintime.Control.Core.DTOs.Tasks;

namespace Wintime.Control.Core.Interfaces;

/// <summary>
/// Итоги выполнения задания (вкладка «Результаты выполнения»). Одна точка расчёта —
/// на ней же строится отчёт по заданиям (BL-05).
/// </summary>
public interface ITaskResultsService
{
    /// <summary>Итоги на «сейчас»; null — задания нет.</summary>
    Task<TaskResultsDto?> GetAsync(Guid taskId, CancellationToken ct = default);
}
```

- [ ] **Step 5: Write the service**

Create `Wintime.Control.Infrastructure/Services/TaskResultsService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Infrastructure.Services;

public class TaskResultsService : ITaskResultsService
{
    private readonly ControlDbContext _context;
    private readonly IEffectiveStatusHistoryService _effectiveStatusHistory;

    public TaskResultsService(ControlDbContext context, IEffectiveStatusHistoryService effectiveStatusHistory)
    {
        _context = context;
        _effectiveStatusHistory = effectiveStatusHistory;
    }

    public async Task<TaskResultsDto?> GetAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await _context.ShiftTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null)
            return null;

        var now = DateTime.UtcNow;
        if (TaskActivityWindow.Release(task, now) is not Interval window)
            return new TaskResultsDto { ReleaseStarted = false };

        return new TaskResultsDto
        {
            ReleaseStarted = true,
            ReleaseFrom = window.Start,
            ReleaseTo = window.End,
            InProgress = task.CompletedAt == null && task.ClosedAt == null,
            Output = new TaskOutputResultsDto
            {
                PlanQuantity = task.PlanQuantity,
                ActualQuantity = task.ActualQuantity,
                DefectQuantity = task.DefectQuantity,
                GoodQuantity = task.ActualQuantity - task.DefectQuantity,
                OverrunQuantity = Math.Max(0, task.ActualQuantity - task.PlanQuantity),
                // У Closed CloseReason — причина закрытия, она уже на вкладке «Данные задания».
                DeviationReason = task.Status == TaskStatus.Completed ? task.CloseReason : null
            },
            Cycles = await GetCyclesAsync(task.Id, task.PlannedInjectionCycleSeconds, task.PlannedFullCycleSeconds, ct),
            Time = await GetTimeAsync(task.ImmId, window, ct)
        };
    }

    private async Task<TaskCycleResultsDto> GetCyclesAsync(Guid taskId, int? plannedInjection, int? plannedFull,
        CancellationToken ct)
    {
        var agg = await _context.ImmCycles
            .Where(c => c.TaskId == taskId && c.IsSuccessful && c.EndTime != null)
            .GroupBy(c => c.TaskId)
            .Select(g => new
            {
                Count = g.Count(),
                AvgDuration = g.Average(c => (double)c.DurationSeconds),
                // null-паузы (наладка, простой, порог) и неизмеренное литьё AVG пропускает — ADR-0017
                AvgPauseMs = g.Average(c => (double?)c.PauseDurationMs),
                AvgInjectionMs = g.Average(c => (double?)c.InjectionDurationMs)
            })
            .FirstOrDefaultAsync(ct);

        var count = agg?.Count ?? 0;
        var enough = count >= ReleaseForecast.MinClosedCycles;
        return new TaskCycleResultsDto
        {
            Count = count,
            AvgInjectionSeconds = enough && agg!.AvgInjectionMs is double inj ? inj / 1000 : null,
            AvgFullCycleSeconds = enough ? agg!.AvgDuration + (agg.AvgPauseMs ?? 0) / 1000 : null,
            PlannedInjectionSeconds = plannedInjection,
            PlannedFullCycleSeconds = plannedFull
        };
    }

    private async Task<TaskTimeResultsDto> GetTimeAsync(Guid immId, Interval window, CancellationToken ct)
    {
        var inputs = (await _effectiveStatusHistory.GatherAsync(
            new[] { immId }, window.Start, window.End, window.End, ct))[immId];
        var segments = EffectiveStatusTimeline.Build(
            inputs.Raw, inputs.Tasks, inputs.Downtimes, window.Start, window.End);

        int Sum(string status) => (int)Math.Round(segments
            .Where(s => s.EffectiveStatus == status)
            .Sum(s => (s.End - s.Start).TotalSeconds));

        var downtimeCount = await _context.Events.CountAsync(e =>
            e.ImmId == immId && e.EventType == EventType.Downtime
            && e.StartTime < window.End && (e.EndTime == null || e.EndTime > window.Start), ct);

        return new TaskTimeResultsDto
        {
            WindowSeconds = (int)Math.Round((window.End - window.Start).TotalSeconds),
            ProductionSeconds = Sum(EffectiveStatus.Production),
            DowntimeSeconds = Sum(EffectiveStatus.Downtime),
            OfflineSeconds = Sum(EffectiveStatus.Offline),
            DowntimeCount = downtimeCount
        };
    }
}
```

- [ ] **Step 6: Register in DI**

In `Wintime.Control.API/Program.cs`, after line 175 (`builder.Services.AddScoped<IEffectiveStatusHistoryService, EffectiveStatusHistoryService>();`) add:

```csharp
builder.Services.AddScoped<ITaskResultsService, TaskResultsService>();
```

(Namespaces `Wintime.Control.Core.Interfaces` и `Wintime.Control.Infrastructure.Services` уже подключены — рядом регистрируется `EffectiveStatusHistoryService`.)

- [ ] **Step 7: Add the endpoint**

In `Wintime.Control.API/Controllers/TasksController.cs` extend the constructor:

```csharp
    private readonly ControlDbContext _context;
    private readonly IEmulatorControlService _emulator;
    private readonly IFactoryClock _factoryClock;
    private readonly IPlanningDefaultsService _planningDefaults;
    private readonly ITaskResultsService _taskResults;

    public TasksController(ControlDbContext context, IEmulatorControlService emulator, IFactoryClock factoryClock,
                           IPlanningDefaultsService planningDefaults, ITaskResultsService taskResults)
    {
        _context = context;
        _emulator = emulator;
        _factoryClock = factoryClock;
        _planningDefaults = planningDefaults;
        _taskResults = taskResults;
    }
```

and right after `GetTaskById` add:

```csharp
    /// <summary>
    /// Итоги выполнения задания по окну выпуска (вкладка «Результаты выполнения»): выпуск, средние
    /// циклы против эталонов, время работы / простоев / без связи. Считается на лету.
    /// </summary>
    [HttpGet("{id:guid}/results")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<TaskResultsDto>> GetTaskResults(Guid id)
    {
        var results = await _taskResults.GetAsync(id, HttpContext.RequestAborted);
        return results == null ? NotFound() : Ok(results);
    }
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~TaskResultsApiTests"`
Expected: PASS, 8 tests.

If `CompletedTask_ComputesOutputCyclesAndTimeOverReleaseWindow` fails on time split: check that the 30–35 interval is `Production` (InProgress + Idle, no event, `thresholdPassed: false` in history) and 0–5 is `Downtime` (event −20…5 covers it). Do not change expected numbers — they follow the matrix (ADR-0019).

- [ ] **Step 9: Run full .NET suites**

Run: `dotnet test Wintime.Control.Tests.Unit` and `dotnet test Wintime.Control.Tests.Integration`
Expected: all PASS (unit 510+, integration 211+).

- [ ] **Step 10: Commit**

```bash
git add Wintime.Control.Core/DTOs/Tasks/TaskResultsDto.cs Wintime.Control.Core/Interfaces/ITaskResultsService.cs Wintime.Control.Infrastructure/Services/TaskResultsService.cs Wintime.Control.API/Program.cs Wintime.Control.API/Controllers/TasksController.cs Wintime.Control.Tests.Integration/Tasks/TaskResultsApiTests.cs
git commit -m "feat(tasks): GET /api/tasks/{id}/results — task results over release window"
```

(Сообщение коммита заканчивать строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.)

---

### Task 2: Фронт — API, форматтеры и компонент `TaskResultsTab`

**Files:**
- Modify: `Wintime-Control-Frontend/src/api/tasks.js`
- Create: `Wintime-Control-Frontend/src/utils/taskResults.js`
- Create: `Wintime-Control-Frontend/src/utils/__tests__/taskResults.spec.js`
- Create: `Wintime-Control-Frontend/src/components/tasks/TaskResultsTab.vue`
- Create: `Wintime-Control-Frontend/src/components/tasks/__tests__/TaskResultsTab.spec.js`

**Interfaces:**
- Consumes: JSON из Task 1 (camelCase, см. Produces Task 1); `EFFECTIVE_STATUS` из `src/constants/effectiveStatus.js` (`.label`, `.hex`); `formatDuration(seconds)` из `src/utils/taskPlan.js` (→ `'25 мин'`, `'1 ч 5 мин'`, `'—'` для null).
- Produces: `tasksApi.getResults(id)`; компонент `TaskResultsTab` с prop `taskId: String` (required). Сам грузит данные при монтировании, обновляет раз в минуту, пока `inProgress`, снимает таймер при размонтировании. Форматтеры: `formatSeconds(v) : string`, `sharePercent(part, whole) : number|null`, `deviationPercent(fact, plan) : number|null`, `formatDeviation(pct) : string`.

- [ ] **Step 1: Write the failing formatter tests**

Create `Wintime-Control-Frontend/src/utils/__tests__/taskResults.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import { formatSeconds, sharePercent, deviationPercent, formatDeviation } from '../taskResults'

describe('taskResults formatters', () => {
  it('formatSeconds: одна цифра после точки, null → «—»', () => {
    expect(formatSeconds(12.84)).toBe('12.8 с')
    expect(formatSeconds(60)).toBe('60.0 с')
    expect(formatSeconds(null)).toBe('—')
    expect(formatSeconds(undefined)).toBe('—')
  })

  it('sharePercent: округлённый процент, пустое окно → null', () => {
    expect(sharePercent(1200, 3600)).toBe(33)
    expect(sharePercent(0, 3600)).toBe(0)
    expect(sharePercent(10, 0)).toBeNull()
  })

  it('deviationPercent: (факт − эталон) / эталон, без факта или эталона → null', () => {
    expect(deviationPercent(12.8, 55)).toBe(-77)
    expect(deviationPercent(66, 60)).toBe(10)
    expect(deviationPercent(null, 60)).toBeNull()
    expect(deviationPercent(12, null)).toBeNull()
    expect(deviationPercent(12, 0)).toBeNull()
  })

  it('formatDeviation: знак и пробел перед %, null → «—»', () => {
    expect(formatDeviation(-77)).toBe('−77 %')
    expect(formatDeviation(10)).toBe('+10 %')
    expect(formatDeviation(0)).toBe('0 %')
    expect(formatDeviation(null)).toBe('—')
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run (из `Wintime-Control-Frontend`): `npx vitest run src/utils/__tests__/taskResults.spec.js`
Expected: FAIL — `Failed to resolve import "../taskResults"`.

- [ ] **Step 3: Implement the formatters**

Create `Wintime-Control-Frontend/src/utils/taskResults.js`:

```js
// Форматтеры вкладки «Результаты выполнения» задания. API отдаёт секунды —
// проценты и отклонения от эталона считаются здесь.

export function formatSeconds(v) {
  if (v === null || v === undefined) return '—'
  return `${v.toFixed(1)} с`
}

export function sharePercent(part, whole) {
  if (!whole) return null
  return Math.round((part / whole) * 100)
}

export function deviationPercent(fact, plan) {
  if (fact === null || fact === undefined || !plan) return null
  return Math.round(((fact - plan) / plan) * 100)
}

export function formatDeviation(pct) {
  if (pct === null || pct === undefined) return '—'
  if (pct > 0) return `+${pct} %`
  if (pct < 0) return `−${Math.abs(pct)} %`
  return '0 %'
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `npx vitest run src/utils/__tests__/taskResults.spec.js`
Expected: PASS, 4 tests.

- [ ] **Step 5: Write the failing component tests**

Create `Wintime-Control-Frontend/src/components/tasks/__tests__/TaskResultsTab.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'

vi.mock('@/api/tasks', () => ({
  tasksApi: { getResults: vi.fn() }
}))

import { tasksApi } from '@/api/tasks'
import TaskResultsTab from '../TaskResultsTab.vue'

const completed = () => ({
  releaseStarted: true,
  releaseFrom: '2026-10-14T13:09:00Z',
  releaseTo: '2026-10-15T16:41:00Z',
  inProgress: false,
  output: {
    planQuantity: 100, actualQuantity: 110, defectQuantity: 4,
    goodQuantity: 106, overrunQuantity: 10, deviationReason: 'Перевыпуск по просьбе мастера'
  },
  cycles: {
    count: 567, avgInjectionSeconds: 12.84, plannedInjectionSeconds: 55,
    avgFullCycleSeconds: 66, plannedFullCycleSeconds: 60
  },
  time: { windowSeconds: 3600, productionSeconds: 1800, downtimeSeconds: 1200, downtimeCount: 4, offlineSeconds: 600 }
})

function mountTab() {
  return mount(TaskResultsTab, {
    props: { taskId: 't1' },
    global: { plugins: [ElementPlus] }
  })
}

const rowText = (w, testId) => w.find(`[data-test="${testId}"]`).text()

describe('TaskResultsTab', () => {
  beforeEach(() => { vi.clearAllMocks() })
  afterEach(() => { vi.useRealTimers() })

  it('выпуск не начат — одна строка, без блоков', async () => {
    tasksApi.getResults.mockResolvedValue({ data: { releaseStarted: false, inProgress: false } })
    const w = mountTab()
    await flushPromises()

    expect(tasksApi.getResults).toHaveBeenCalledWith('t1')
    expect(w.text()).toContain('Выпуск ещё не начат')
    expect(w.find('[data-test="output"]').exists()).toBe(false)
  })

  it('выпуск: план, выпущено, брак, годных, перевыпуск, причина', async () => {
    tasksApi.getResults.mockResolvedValue({ data: completed() })
    const w = mountTab()
    await flushPromises()

    const out = rowText(w, 'output')
    expect(out).toContain('100')
    expect(out).toContain('110')
    expect(out).toContain('106')
    expect(out).toContain('10')
    expect(out).toContain('Перевыпуск по просьбе мастера')
  })

  it('без перевыпуска и причины — «—» и строки причины нет', async () => {
    const data = completed()
    data.output.overrunQuantity = 0
    data.output.deviationReason = null
    tasksApi.getResults.mockResolvedValue({ data })
    const w = mountTab()
    await flushPromises()

    expect(w.find('[data-test="overrun"]').text()).toBe('—')
    expect(w.find('[data-test="deviation-reason"]').exists()).toBe(false)
  })

  it('циклы: факт, эталон и отклонение со знаком и цветом', async () => {
    tasksApi.getResults.mockResolvedValue({ data: completed() })
    const w = mountTab()
    await flushPromises()

    const inj = rowText(w, 'cycle-injection')
    expect(inj).toContain('12.8 с')
    expect(inj).toContain('55')
    expect(inj).toContain('−77 %')
    expect(w.find('[data-test="cycle-injection"] [data-test="deviation"]').classes()).toContain('text-green-600')

    const full = rowText(w, 'cycle-full')
    expect(full).toContain('66.0 с')
    expect(full).toContain('+10 %')
    expect(w.find('[data-test="cycle-full"] [data-test="deviation"]').classes()).toContain('text-red-600')
    expect(rowText(w, 'cycles-header')).toContain('567')
  })

  it('нет среднего или эталона — «—»', async () => {
    const data = completed()
    data.cycles.avgInjectionSeconds = null
    data.cycles.plannedFullCycleSeconds = null
    tasksApi.getResults.mockResolvedValue({ data })
    const w = mountTab()
    await flushPromises()

    expect(w.find('[data-test="cycle-injection"] [data-test="deviation"]').text()).toBe('—')
    expect(w.find('[data-test="cycle-full"] [data-test="deviation"]').text()).toBe('—')
  })

  it('время выпуска: длительности, проценты, количество простоев', async () => {
    tasksApi.getResults.mockResolvedValue({ data: completed() })
    const w = mountTab()
    await flushPromises()

    expect(rowText(w, 'time-Production')).toContain('30 мин')
    expect(rowText(w, 'time-Production')).toContain('50 %')
    expect(rowText(w, 'time-Downtime')).toContain('Простои (4)')
    expect(rowText(w, 'time-Downtime')).toContain('33 %')
    expect(rowText(w, 'time-Offline')).toContain('Нет связи')
    expect(rowText(w, 'time-Offline')).toContain('17 %')
  })

  it('задание в работе — пометка «в работе» и обновление раз в минуту', async () => {
    // Только таймеры компонента: flushPromises из test-utils ходит через setImmediate — его не подменяем
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    tasksApi.getResults.mockResolvedValue({ data: { ...completed(), inProgress: true } })
    const w = mountTab()
    await flushPromises()

    expect(w.text()).toContain('в работе')
    expect(tasksApi.getResults).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(60_000)
    expect(tasksApi.getResults).toHaveBeenCalledTimes(2)

    w.unmount()
    await vi.advanceTimersByTimeAsync(60_000)
    expect(tasksApi.getResults).toHaveBeenCalledTimes(2)   // таймер снят
  })

  it('завершённое задание не опрашивается повторно', async () => {
    // Только таймеры компонента: flushPromises из test-utils ходит через setImmediate — его не подменяем
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    tasksApi.getResults.mockResolvedValue({ data: completed() })
    mountTab()
    await flushPromises()

    await vi.advanceTimersByTimeAsync(120_000)
    expect(tasksApi.getResults).toHaveBeenCalledTimes(1)
  })
})
```

- [ ] **Step 6: Run to verify it fails**

Run: `npx vitest run src/components/tasks/__tests__/TaskResultsTab.spec.js`
Expected: FAIL — `Failed to resolve import "../TaskResultsTab.vue"`.

- [ ] **Step 7: Add the API method**

In `Wintime-Control-Frontend/src/api/tasks.js`, after `getById`:

```js
  // Итоги выполнения по окну выпуска (вкладка «Результаты выполнения»)
  getResults(id) {
    return apiClient.get(`/tasks/${id}/results`)
  },
```

- [ ] **Step 8: Implement the component**

Create `Wintime-Control-Frontend/src/components/tasks/TaskResultsTab.vue`:

```vue
<template>
  <div v-loading="loading" class="min-h-[120px]">
    <p v-if="results && !results.releaseStarted" class="text-gray-500">Выпуск ещё не начат</p>

    <template v-else-if="results">
      <p class="text-sm text-gray-600 mb-4">
        Окно выпуска: {{ formatDate(results.releaseFrom) }} —
        {{ results.inProgress ? 'сейчас' : formatDate(results.releaseTo) }}
        ({{ formatDuration(results.time.windowSeconds) }})
        <el-tag v-if="results.inProgress" size="small" type="warning" class="ml-2">в работе</el-tag>
      </p>

      <h4 class="font-semibold text-gray-700 mb-2">Выпуск</h4>
      <div data-test="output" class="mb-6">
        <div class="grid grid-cols-5 gap-2 text-center">
          <div><div class="text-xs text-gray-500">План</div><div class="tabular-nums">{{ results.output.planQuantity }}</div></div>
          <div><div class="text-xs text-gray-500">Выпущено</div><div class="tabular-nums">{{ results.output.actualQuantity }}</div></div>
          <div><div class="text-xs text-gray-500">Брак</div><div class="tabular-nums">{{ results.output.defectQuantity }}</div></div>
          <div><div class="text-xs text-gray-500">Годных</div><div class="tabular-nums">{{ results.output.goodQuantity }}</div></div>
          <div>
            <div class="text-xs text-gray-500">Перевыпуск</div>
            <div data-test="overrun" class="tabular-nums">{{ results.output.overrunQuantity > 0 ? results.output.overrunQuantity : '—' }}</div>
          </div>
        </div>
        <p v-if="results.output.deviationReason" data-test="deviation-reason" class="text-sm mt-3">
          <span class="text-gray-500">Причина отклонения от плана:</span> {{ results.output.deviationReason }}
        </p>
      </div>

      <h4 data-test="cycles-header" class="font-semibold text-gray-700 mb-2">
        Циклы ({{ results.cycles.count }} закрытых)
      </h4>
      <table class="w-full text-sm mb-6">
        <thead>
          <tr class="text-gray-500 text-xs">
            <th class="text-left font-normal"></th>
            <th class="text-right font-normal">Факт</th>
            <th class="text-right font-normal">Эталон</th>
            <th class="text-right font-normal">Откл.</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in cycleRows" :key="row.key" :data-test="`cycle-${row.key}`">
            <td>{{ row.label }}</td>
            <td class="text-right tabular-nums">{{ formatSeconds(row.fact) }}</td>
            <td class="text-right tabular-nums">{{ row.plan != null ? `${row.plan} с` : '—' }}</td>
            <td class="text-right tabular-nums">
              <span data-test="deviation" :class="deviationClass(row.deviation)">{{ formatDeviation(row.deviation) }}</span>
            </td>
          </tr>
        </tbody>
      </table>

      <h4 class="font-semibold text-gray-700 mb-2">Время выпуска</h4>
      <div class="text-sm">
        <div v-for="row in timeRows" :key="row.key" :data-test="`time-${row.key}`" class="flex justify-between py-0.5">
          <span class="flex items-center gap-2">
            <span class="inline-block w-3 h-3 rounded-sm" :style="{ backgroundColor: row.color }" />
            {{ row.label }}
          </span>
          <span class="tabular-nums">
            {{ formatDuration(row.seconds) }}
            <span class="inline-block w-12 text-right text-gray-500">{{ row.share ?? '—' }} %</span>
          </span>
        </div>
        <!-- Полоса долей (не хронология — хронология в карточке ТПА) -->
        <div class="flex h-3 rounded overflow-hidden mt-2 bg-gray-100">
          <div
            v-for="row in timeRows"
            :key="row.key"
            :style="{ width: `${row.share ?? 0}%`, backgroundColor: row.color }"
          />
        </div>
      </div>
    </template>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import dayjs from 'dayjs'
import { tasksApi } from '@/api/tasks'
import { EFFECTIVE_STATUS } from '@/constants/effectiveStatus'
import { formatDuration } from '@/utils/taskPlan'
import { formatSeconds, sharePercent, deviationPercent, formatDeviation } from '@/utils/taskResults'

const props = defineProps({
  taskId: { type: String, required: true }
})

const REFRESH_MS = 60_000

const results = ref(null)
const loading = ref(false)
let refreshTimer = null
let unmounted = false

const cycleRows = computed(() => {
  const c = results.value?.cycles
  if (!c) return []
  return [
    { key: 'injection', label: 'Цикл литья', fact: c.avgInjectionSeconds, plan: c.plannedInjectionSeconds },
    { key: 'full', label: 'Полный цикл', fact: c.avgFullCycleSeconds, plan: c.plannedFullCycleSeconds }
  ].map(r => ({ ...r, deviation: deviationPercent(r.fact, r.plan) }))
})

const timeRows = computed(() => {
  const t = results.value?.time
  if (!t) return []
  return [
    { key: 'Production', label: EFFECTIVE_STATUS.Production.label, seconds: t.productionSeconds },
    { key: 'Downtime', label: `Простои (${t.downtimeCount})`, seconds: t.downtimeSeconds },
    { key: 'Offline', label: EFFECTIVE_STATUS.Offline.label, seconds: t.offlineSeconds }
  ].map(r => ({ ...r, color: EFFECTIVE_STATUS[r.key].hex, share: sharePercent(r.seconds, t.windowSeconds) }))
})

function deviationClass(pct) {
  if (pct === null || pct === 0) return ''
  return pct < 0 ? 'text-green-600' : 'text-red-600'
}

function formatDate(date) {
  return date ? dayjs(date).format('DD.MM.YYYY HH:mm') : '—'
}

async function load() {
  loading.value = results.value === null   // спиннер только на первой загрузке, не на тихом обновлении
  try {
    const res = await tasksApi.getResults(props.taskId)
    if (unmounted) return
    results.value = res.data
  } catch (error) {
    console.error(error)
  } finally {
    loading.value = false
  }
  clearTimeout(refreshTimer)
  if (!unmounted && results.value?.inProgress) refreshTimer = setTimeout(load, REFRESH_MS)
}

onMounted(load)
onUnmounted(() => { unmounted = true; clearTimeout(refreshTimer) })
</script>
```

- [ ] **Step 9: Run to verify it passes**

Run: `npx vitest run src/components/tasks/__tests__/TaskResultsTab.spec.js src/utils/__tests__/taskResults.spec.js`
Expected: PASS, 12 tests.

If a fake-timer test hangs on `flushPromises()`, replace that call with `await vi.advanceTimersByTimeAsync(0)` in the two timer tests only (do not change component code).

- [ ] **Step 10: Commit**

```bash
git add Wintime-Control-Frontend/src/api/tasks.js Wintime-Control-Frontend/src/utils/taskResults.js Wintime-Control-Frontend/src/utils/__tests__/taskResults.spec.js Wintime-Control-Frontend/src/components/tasks/TaskResultsTab.vue Wintime-Control-Frontend/src/components/tasks/__tests__/TaskResultsTab.spec.js
git commit -m "feat(tasks-ui): task results tab component"
```

(Сообщение коммита заканчивать строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.)

---

### Task 3: Фронт — вкладки в `TaskDetailModal`, руководство, версия

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/tasks/TaskDetailModal.vue`
- Modify: `Wintime-Control-Frontend/src/views/tasks/__tests__/TaskDetailModal.spec.js`
- Modify: `docs/user-guide/manager.md`
- Modify: `VERSION`

**Interfaces:**
- Consumes: `TaskResultsTab` (prop `taskId`) из Task 2.
- Produces: в модалке `el-tabs` со вкладками `name="data"` («Данные задания», по умолчанию) и `name="results"` («Результаты выполнения»). `TaskResultsTab` монтируется только при открытой вкладке «Результаты» и открытой модалке (`v-if`) — так таймер обновления снимается при уходе со вкладки и закрытии. При смене задания и закрытии модалки вкладка сбрасывается на «Данные задания».

- [ ] **Step 1: Write the failing test**

Append to `Wintime-Control-Frontend/src/views/tasks/__tests__/TaskDetailModal.spec.js`:

```js
describe('TaskDetailModal — вкладки', () => {
  it('по умолчанию «Данные задания»; «Результаты выполнения» монтируют вкладку итогов с id задания', async () => {
    const wrapper = mount(TaskDetailModal, {
      props: { modelValue: true, task: { id: 't1', status: 'InProgress' } },
      global: {
        plugins: [ElementPlus, createPinia()],
        stubs: { TaskResultsTab: { name: 'TaskResultsTab', props: ['taskId'], template: '<div class="results-stub" />' } }
      },
      attachTo: document.body
    })
    await flushPromises()

    const tabs = wrapper.findAll('.el-tabs__item').map(t => t.text())
    expect(tabs).toEqual(['Данные задания', 'Результаты выполнения'])
    expect(wrapper.findComponent({ name: 'TaskResultsTab' }).exists()).toBe(false)

    await wrapper.findAll('.el-tabs__item')[1].trigger('click')
    await flushPromises()

    const tab = wrapper.findComponent({ name: 'TaskResultsTab' })
    expect(tab.exists()).toBe(true)
    expect(tab.props('taskId')).toBe('t1')

    await wrapper.setProps({ task: { id: 't2', status: 'Completed' } })
    await flushPromises()
    expect(wrapper.findComponent({ name: 'TaskResultsTab' }).exists()).toBe(false)   // сброс на «Данные задания»
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/views/tasks/__tests__/TaskDetailModal.spec.js`
Expected: FAIL — `expected [] to deeply equal [ 'Данные задания', 'Результаты выполнения' ]`.

- [ ] **Step 3: Implement the tabs**

In `TaskDetailModal.vue` wrap the existing content of `<div v-loading="loading">` — from `<!-- Основная информация -->` through the end of the `<!-- Примечания -->` card — into the first tab, and add the second tab. The «Действия» block stays **outside** the tabs (кнопки нужны на обеих вкладках):

```vue
    <div v-loading="loading">
      <el-tabs v-model="activeTab">
        <el-tab-pane label="Данные задания" name="data">
          <!-- Основная информация -->
          ... (существующие el-descriptions, карточка «Выполнение», карточка «Примечания» без изменений) ...
        </el-tab-pane>
        <el-tab-pane label="Результаты выполнения" name="results">
          <TaskResultsTab v-if="visible && activeTab === 'results' && task?.id" :task-id="task.id" />
        </el-tab-pane>
      </el-tabs>

      <!-- Действия -->
      ... (без изменений) ...
    </div>
```

In `<script setup>`:

```js
import { ref, computed, watch } from 'vue'
import TaskResultsTab from '@/components/tasks/TaskResultsTab.vue'
```

```js
const activeTab = ref('data')

const visible = computed({
  get: () => props.modelValue,
  set: (value) => {
    if (!value) {
      closeReasonInput.value = ''
      activeTab.value = 'data'
    }
    emit('update:modelValue', value)
  }
})

// Другое задание — снова с «Данных задания» (итоги прошлого задания не показываем)
watch(() => props.task?.id, () => { activeTab.value = 'data' })
```

- [ ] **Step 4: Run to verify it passes**

Run: `npx vitest run src/views/tasks/__tests__/TaskDetailModal.spec.js`
Expected: PASS (all existing tests + new one).

- [ ] **Step 5: Update the user guide**

In `docs/user-guide/manager.md` find the section describing «Детали задания» (search `Детали задания`) and add after its field list:

```markdown
Карточка задания разделена на две вкладки:

- **Данные задания** — параметры задания, план, прогресс, примечания (как раньше);
- **Результаты выполнения** — итоги по окну выпуска (от «Начать выпуск» до завершения или «сейчас»,
  если задание в работе; обновляется раз в минуту):
  - **Выпуск**: план, выпущено, брак, годных, перевыпуск и причина отклонения от плана;
  - **Циклы**: средний цикл литья и средний полный цикл (цикл + пауза до следующего) против эталонов
    задания, отклонение в процентах (меньше эталона — зелёным, больше — красным). Пока закрытых
    циклов меньше трёх — «—»;
  - **Время выпуска**: работа, простои (с количеством) и «Нет связи» — длительность и доля окна выпуска.
    Цифры совпадают с хронологией карточки ТПА.
```

If there is no such section, add a subsection `#### Результаты выполнения` with the same text under the section about tasks (search `## ` headings with «задани»).

- [ ] **Step 6: Bump the minor version**

Check master: `git fetch -q origin && git show origin/master:VERSION`.
Write minor+1 of master with patch 0 and a trailing newline into `VERSION` (master `0.11.0` → `0.12.0`; master `0.12.0` → `0.13.0`).

- [ ] **Step 7: Run all suites**

Run (корень репо): `dotnet test Wintime.Control.Tests.Unit`, `dotnet test Wintime.Control.Tests.Integration`; (из `Wintime-Control-Frontend`) `npx vitest run`.
Expected: всё PASS.

- [ ] **Step 8: Commit**

```bash
git add Wintime-Control-Frontend/src/views/tasks/TaskDetailModal.vue Wintime-Control-Frontend/src/views/tasks/__tests__/TaskDetailModal.spec.js docs/user-guide/manager.md
git commit -m "feat(tasks-ui): data/results tabs in task detail modal, user guide"
git add VERSION
git commit -m "build: bump version to <X.Y.0>"
```

(Каждое сообщение коммита заканчивать строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.)
