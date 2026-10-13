# ARCH-03 — Развязка задания и смены: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Задание, идущее через несколько смен/суток, корректно попадает в фильтры, хронологию карточки ТПА и отчёты: отнесение к периоду — по пересечению окна активности, а не по одной точке.

**Architecture:** Одно определение окна активности задания (`TaskActivityWindow`, `Core/Policies`) — чистая функция + EF-выражение пересечения. На нём строятся фильтр `activeFrom/To` списка заданий, `EffectiveStatusHistoryService`, отчёт «Рабочий день» (с нормативным планом за период — `TaskPeriodPlan`) и отчёты по наладчикам (наладка из интервала задания). Мёртвый код (`ShiftWorkCalculator`, лишние `EventType`) удаляется.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql (PostgreSQL 16), xUnit + FluentAssertions (интеграционные — через `IntegrationTestFactory` с Testcontainers), Vue 3 + Element Plus, Pinia, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-13-arch-03-task-shift-decoupling-design.md`

## Global Constraints

- **Предусловие:** смёржен PR `refactor/task-status-planned` (план `2026-10-13-task-status-draft-to-planned.md`). Здесь статус «Планируется» = `TaskStatus.Planned` / `'Planned'`.
- Окно активности = `[SetupStartedAt, CompletedAt ?? ClosedAt ?? now)`; нет `SetupStartedAt` → окна нет. Окно выпуска = `[StartedAt, CompletedAt ?? ClosedAt ?? now)`.
- Все `DateTime` в EF-запросах — `Kind=Utc`; параметры из query string приводить `DateTime.SpecifyKind(x, DateTimeKind.Utc)` (CLAUDE.md).
- Фильтры `GET /api/tasks` объединяются через **И**. Без `status`: исключать `Completed`/`Closed`, **кроме** случая, когда задан `activeFrom` или `activeTo`.
- Статусы задания сравнивать только равенством (`==`), никогда `>=`/`<=` (значения enum не в порядке жизненного цикла: `Setup = 5`).
- Гнёзда для нормативного плана — текущие `Mold.Cavities`; для факта и расхода сырья — снапшот `ImmCycle.Cavities`.
- Время на фронте — пояс браузера (не переводить в пояс завода).
- В UI: «Карточка ТПА» = `ImmDetailModal` (окно подробностей с хронологией).
- Ветка: `feature/arch-03-task-shift-decoupling` от `master`. Прямой push в `master` запрещён.
- Коммиты заканчивать строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File Structure

| Файл | Ответственность |
|---|---|
| `Wintime.Control.Core/Policies/TaskActivityWindow.cs` (new) | окно активности/выпуска задания + EF-выражение пересечения с периодом |
| `Wintime.Control.Core/Policies/TaskPeriodPlan.cs` (new) | нормативный план задания за период |
| `Wintime.Control.Infrastructure/Services/EffectiveStatusHistoryService.cs` | перевод отбора заданий на общее выражение |
| `Wintime.Control.API/Controllers/TasksController.cs` | фильтры `issued*`/`planned*`/`active*` |
| `Wintime.Control.Infrastructure/Reports/ReportService.cs` | «Рабочий день», отчёты по наладчикам, Excel |
| `Wintime.Control.Core/DTOs/Report/AssetsPersonnel*.cs` | поля отчётов по наладчикам |
| `Wintime.Control.Core/Enums/EventType.cs` | только `Downtime` |
| `Wintime-Control-Frontend/src/utils/taskListParams.js` (new) | фильтры стора → query-параметры API |
| `Wintime-Control-Frontend/src/utils/shiftTimeline.js` (new) | геометрия бара задания на хронологии |
| `Wintime-Control-Frontend/src/stores/tasks.js`, `src/components/tasks/TaskFilters.vue` | три поля периода |
| `Wintime-Control-Frontend/src/views/dashboard/ImmDetailModal.vue`, `src/components/dashboard/ShiftTimeline.vue` | карточка ТПА |
| `Wintime-Control-Frontend/src/views/reports/AssetsReportView.vue` | колонки отчётов по наладчикам |
| `docs/adr/0016-task-longer-than-shift.md` (new), `CLAUDE.md`, `docs/domain/shift-task.md` | документация |

---

### Task 1: Окно активности задания (`TaskActivityWindow`)

**Files:**
- Create: `Wintime.Control.Core/Policies/TaskActivityWindow.cs`
- Modify: `Wintime.Control.Infrastructure/Services/EffectiveStatusHistoryService.cs:29-34`
- Test: `Wintime.Control.Tests.Unit/Policies/TaskActivityWindowTests.cs`

**Interfaces:**
- Consumes: `Interval(DateTime Start, DateTime End)` из `Wintime.Control.Core/Policies/EffectiveStatusTimeline.cs:8`.
- Produces:
  - `static Interval? TaskActivityWindow.Activity(ShiftTask task, DateTime nowUtc)`
  - `static Interval? TaskActivityWindow.Release(ShiftTask task, DateTime nowUtc)`
  - `static Expression<Func<ShiftTask, bool>> TaskActivityWindow.OverlapsPeriod(DateTime fromUtc, DateTime toUtc)`

- [ ] **Step 1: Создать ветку**

```bash
git checkout master && git pull && git checkout -b feature/arch-03-task-shift-decoupling
```

- [ ] **Step 2: Write the failing test**

`Wintime.Control.Tests.Unit/Policies/TaskActivityWindowTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Policies;

public class TaskActivityWindowTests
{
    private static readonly DateTime T0 = new(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = T0.AddDays(3);

    private static ShiftTask Task(DateTime? setup, DateTime? started, DateTime? completed, DateTime? closed,
        TaskStatus status = TaskStatus.InProgress) => new()
    {
        Status = status, SetupStartedAt = setup, StartedAt = started, CompletedAt = completed, ClosedAt = closed
    };

    [Fact]
    public void Activity_NoSetupStarted_IsNull()
    {
        TaskActivityWindow.Activity(Task(null, null, null, null, TaskStatus.Issued), Now).Should().BeNull();
    }

    [Fact]
    public void Activity_Open_EndsAtNow()
    {
        TaskActivityWindow.Activity(Task(T0, T0.AddHours(1), null, null), Now)
            .Should().Be(new Interval(T0, Now));
    }

    [Fact]
    public void Activity_Completed_EndsAtCompletedAt()
    {
        TaskActivityWindow.Activity(Task(T0, T0.AddHours(1), T0.AddDays(2), T0.AddDays(2).AddHours(1)), Now)
            .Should().Be(new Interval(T0, T0.AddDays(2)));
    }

    [Fact]
    public void Activity_ClosedWithoutCompletion_EndsAtClosedAt()
    {
        TaskActivityWindow.Activity(Task(T0, null, null, T0.AddHours(5), TaskStatus.Closed), Now)
            .Should().Be(new Interval(T0, T0.AddHours(5)));
    }

    [Fact]
    public void Release_NoStartedAt_IsNull()
    {
        TaskActivityWindow.Release(Task(T0, null, null, T0.AddHours(5), TaskStatus.Closed), Now).Should().BeNull();
    }

    [Fact]
    public void Release_StartsAtStartedAt()
    {
        TaskActivityWindow.Release(Task(T0, T0.AddHours(1), T0.AddDays(2), null), Now)
            .Should().Be(new Interval(T0.AddHours(1), T0.AddDays(2)));
    }

    [Fact]
    public void Activity_EndBeforeStart_CollapsesToEmpty()
    {
        TaskActivityWindow.Activity(Task(T0, null, null, T0.AddMinutes(-1), TaskStatus.Closed), Now)
            .Should().Be(new Interval(T0, T0));
    }

    [Theory]
    // окно задания [T0+0h, T0+48h)
    [InlineData(-10, -1, false)] // период целиком до задания
    [InlineData(-10, 0, false)]  // период заканчивается ровно в начале — [from, to) не пересекается
    [InlineData(-1, 1, true)]    // захватывает начало
    [InlineData(10, 20, true)]   // внутри
    [InlineData(47, 60, true)]   // захватывает конец
    [InlineData(48, 60, false)]  // начинается ровно в конце
    public void OverlapsPeriod_ClosedTask(int fromH, int toH, bool expected)
    {
        var task = Task(T0, T0.AddHours(1), T0.AddHours(48), null, TaskStatus.Completed);
        var predicate = TaskActivityWindow.OverlapsPeriod(T0.AddHours(fromH), T0.AddHours(toH)).Compile();
        predicate(task).Should().Be(expected);
    }

    [Fact]
    public void OverlapsPeriod_OpenTask_MatchesAnyPeriodAfterStart()
    {
        var task = Task(T0, T0.AddHours(1), null, null);
        var predicate = TaskActivityWindow.OverlapsPeriod(T0.AddDays(5), T0.AddDays(6)).Compile();
        predicate(task).Should().BeTrue();
    }

    [Fact]
    public void OverlapsPeriod_NotStarted_False()
    {
        var predicate = TaskActivityWindow.OverlapsPeriod(T0.AddDays(-1), T0.AddDays(1)).Compile();
        predicate(Task(null, null, null, null, TaskStatus.Issued)).Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~TaskActivityWindowTests`
Expected: FAIL — build error `The name 'TaskActivityWindow' does not exist in the current context`.

- [ ] **Step 4: Write implementation**

`Wintime.Control.Core/Policies/TaskActivityWindow.cs`:

```csharp
using System.Linq.Expressions;
using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Окно активности задания (ARCH-03, ADR-0016): <c>[SetupStartedAt, CompletedAt ?? ClosedAt ?? now)</c>.
/// Задание относится к периоду, если его окно активности пересекается с периодом — а не по одной
/// точке (<c>StartedAt</c>/<c>IssuedAt</c>): задание может идти через несколько смен и суток.
/// Окно выпуска — то же, но от <c>StartedAt</c>. Не хранится, считается на лету.
/// </summary>
public static class TaskActivityWindow
{
    /// <summary>Окно активности; <c>null</c>, если наладка не начиналась.</summary>
    public static Interval? Activity(ShiftTask task, DateTime nowUtc) => Build(task.SetupStartedAt, task, nowUtc);

    /// <summary>Окно выпуска; <c>null</c>, если выпуск не начинался.</summary>
    public static Interval? Release(ShiftTask task, DateTime nowUtc) => Build(task.StartedAt, task, nowUtc);

    /// <summary>
    /// EF-выражение «окно активности пересекается с <c>[fromUtc, toUtc)</c>». Единственный способ
    /// отбирать задания по периоду в запросах — чтобы условие не расходилось с <see cref="Activity"/>.
    /// Открытое окно (не завершено и не закрыто) тянется до «сейчас» и пересекает любой период после начала.
    /// </summary>
    public static Expression<Func<ShiftTask, bool>> OverlapsPeriod(DateTime fromUtc, DateTime toUtc) =>
        t => t.SetupStartedAt != null && t.SetupStartedAt < toUtc
             && ((t.CompletedAt == null && t.ClosedAt == null) || (t.CompletedAt ?? t.ClosedAt) > fromUtc);

    private static Interval? Build(DateTime? start, ShiftTask task, DateTime nowUtc)
    {
        if (start is not DateTime s)
            return null;
        var end = task.CompletedAt ?? task.ClosedAt ?? nowUtc;
        return new Interval(s, end < s ? s : end);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~TaskActivityWindowTests`
Expected: PASS (все тесты класса).

- [ ] **Step 6: Перевести `EffectiveStatusHistoryService` на общее выражение**

В `Wintime.Control.Infrastructure/Services/EffectiveStatusHistoryService.cs` заменить запрос `taskRows` (строки 29-34):

```csharp
        var taskRows = await _context.ShiftTasks
            .Where(t => ids.Contains(t.ImmId))
            .Where(TaskActivityWindow.OverlapsPeriod(fromUtc, toUtc))
            .Select(t => new { t.ImmId, t.SetupStartedAt, t.StartedAt, t.CompletedAt, t.ClosedAt })
            .ToListAsync(ct);
```

(`using Wintime.Control.Core.Policies;` уже есть.) Логику разбиения на `Setup`/`InProgress`-интервалы ниже не трогать.

- [ ] **Step 7: Регрессия эффективного статуса**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~EffectiveStatusHistory|FullyQualifiedName~EquipmentReport"`
Expected: PASS — поведение не изменилось.

- [ ] **Step 8: Commit**

```bash
git add Wintime.Control.Core/Policies/TaskActivityWindow.cs Wintime.Control.Tests.Unit/Policies/TaskActivityWindowTests.cs Wintime.Control.Infrastructure/Services/EffectiveStatusHistoryService.cs
git commit -m "feat(arch-03): TaskActivityWindow — single definition of task activity window

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Фильтры списка заданий `issued*` / `planned*` / `active*`

**Files:**
- Modify: `Wintime.Control.API/Controllers/TasksController.cs:35-71` (`GetTaskList`)
- Test: `Wintime.Control.Tests.Integration/Tasks/TaskListFiltersApiTests.cs` (new)

**Interfaces:**
- Consumes: `TaskActivityWindow.OverlapsPeriod(DateTime fromUtc, DateTime toUtc)` (Task 1).
- Produces: `GET /api/tasks?status&immId&personnelId&issuedFrom&issuedTo&plannedFrom&plannedTo&activeFrom&activeTo`.
  `issued*`, `active*` — ISO `DateTime` (UTC); `planned*` — `yyyy-MM-dd` (`DateOnly`).
  Параметры `dateFrom`/`dateTo` удаляются.

- [ ] **Step 1: Write the failing test**

`Wintime.Control.Tests.Integration/Tasks/TaskListFiltersApiTests.cs`:

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
public class TaskListFiltersApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Окно запроса: [W0, W1)
    private static readonly DateTime W0 = new(2025, 6, 10, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime W1 = W0.AddHours(12);

    public TaskListFiltersApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<(Guid immId, Guid moldId)> CreateImmAndMoldAsync()
    {
        var immId = await _factory.CreateFreshImmAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold { Name = $"M-{Guid.NewGuid():N}", FormId = $"F-{Guid.NewGuid():N}", Cavities = 1, IsActive = true };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return (immId, mold.Id);
    }

    private async Task<Guid> AddTaskAsync(Guid immId, Guid moldId, TaskStatus status, DateOnly plannedDate,
        DateTime? issued = null, DateTime? setup = null, DateTime? started = null,
        DateTime? completed = null, DateTime? closed = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = immId, MoldId = moldId, PlanQuantity = 10, Status = status, PlannedDate = plannedDate,
            IssuedAt = issued, SetupStartedAt = setup, StartedAt = started, CompletedAt = completed, ClosedAt = closed
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task<List<Guid>> GetIdsAsync(string query)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var resp = await client.GetAsync($"/api/tasks?{query}");
        resp.EnsureSuccessStatusCode();
        var items = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return items.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
    }

    private static string Iso(DateTime d) => Uri.EscapeDataString(d.ToString("O"));

    [Fact]
    public async Task Active_ReturnsTasksOverlappingWindow_IncludingCompleted()
    {
        var (imm, mold) = await CreateImmAndMoldAsync();
        var day = DateOnly.FromDateTime(W0);
        var spanning = await AddTaskAsync(imm, mold, TaskStatus.InProgress, day.AddDays(-2),
            issued: W0.AddDays(-2), setup: W0.AddDays(-2), started: W0.AddDays(-2).AddHours(1));
        var completedInside = await AddTaskAsync(imm, mold, TaskStatus.Completed, day,
            issued: W0, setup: W0.AddHours(1), started: W0.AddHours(2), completed: W0.AddHours(5));
        var issuedNotStarted = await AddTaskAsync(imm, mold, TaskStatus.Issued, day, issued: W0.AddHours(1));
        var finishedBefore = await AddTaskAsync(imm, mold, TaskStatus.Completed, day.AddDays(-3),
            issued: W0.AddDays(-3), setup: W0.AddDays(-3), started: W0.AddDays(-3).AddHours(1), completed: W0.AddHours(-1));

        var ids = await GetIdsAsync($"immId={imm}&activeFrom={Iso(W0)}&activeTo={Iso(W1)}");

        ids.Should().BeEquivalentTo(new[] { spanning, completedInside });
        ids.Should().NotContain(issuedNotStarted).And.NotContain(finishedBefore);
    }

    [Fact]
    public async Task Issued_FiltersByIssuedAt_ExcludesPlannedAndArchiveByDefault()
    {
        var (imm, mold) = await CreateImmAndMoldAsync();
        var day = DateOnly.FromDateTime(W0);
        var issuedInside = await AddTaskAsync(imm, mold, TaskStatus.Issued, day, issued: W0.AddHours(1));
        var planned = await AddTaskAsync(imm, mold, TaskStatus.Planned, day);
        var issuedOutside = await AddTaskAsync(imm, mold, TaskStatus.Issued, day, issued: W0.AddDays(-5));
        var completedInside = await AddTaskAsync(imm, mold, TaskStatus.Completed, day,
            issued: W0.AddHours(1), setup: W0.AddHours(2), started: W0.AddHours(3), completed: W0.AddHours(4));

        var ids = await GetIdsAsync($"immId={imm}&issuedFrom={Iso(W0)}&issuedTo={Iso(W1)}");

        ids.Should().BeEquivalentTo(new[] { issuedInside });
        ids.Should().NotContain(planned).And.NotContain(issuedOutside).And.NotContain(completedInside);
    }

    [Fact]
    public async Task Planned_FiltersByPlannedDate()
    {
        var (imm, mold) = await CreateImmAndMoldAsync();
        var day = DateOnly.FromDateTime(W0);
        var onDay = await AddTaskAsync(imm, mold, TaskStatus.Planned, day);
        var nextDay = await AddTaskAsync(imm, mold, TaskStatus.Planned, day.AddDays(1));
        var before = await AddTaskAsync(imm, mold, TaskStatus.Planned, day.AddDays(-1));

        var ids = await GetIdsAsync($"immId={imm}&plannedFrom={day:yyyy-MM-dd}&plannedTo={day.AddDays(1):yyyy-MM-dd}");

        ids.Should().BeEquivalentTo(new[] { onDay, nextDay });
        ids.Should().NotContain(before);
    }

    [Fact]
    public async Task Filters_CombineWithAnd()
    {
        var (imm, mold) = await CreateImmAndMoldAsync();
        var day = DateOnly.FromDateTime(W0);
        var both = await AddTaskAsync(imm, mold, TaskStatus.Issued, day, issued: W0.AddHours(1));
        var onlyIssued = await AddTaskAsync(imm, mold, TaskStatus.Issued, day.AddDays(7), issued: W0.AddHours(1));

        var ids = await GetIdsAsync(
            $"immId={imm}&issuedFrom={Iso(W0)}&issuedTo={Iso(W1)}&plannedFrom={day:yyyy-MM-dd}&plannedTo={day:yyyy-MM-dd}");

        ids.Should().BeEquivalentTo(new[] { both });
        ids.Should().NotContain(onlyIssued);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~TaskListFiltersApiTests`
Expected: FAIL — фильтры не применяются (возвращаются лишние задания), `Active_…` не находит `completedInside`.

- [ ] **Step 3: Write implementation**

В `TasksController.GetTaskList` заменить сигнатуру и фильтрацию (весь метод до `var tasks = await query.ToListAsync();`):

```csharp
    /// <summary>
    /// Список заданий (с фильтрацией). Три диапазона дат объединяются через И (ARCH-03):
    /// <c>issued*</c> — по моменту выдачи; <c>planned*</c> — по плановой дате (даты завода);
    /// <c>active*</c> — окно активности задания пересекается с <c>[activeFrom, activeTo)</c>.
    /// Без <c>status</c> выполненные и закрытые исключаются — кроме фильтра <c>active*</c>:
    /// задание, завершённое в периоде, в нём было в работе.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<IEnumerable<TaskDto>>> GetTaskList(
        [FromQuery] Core.Enums.TaskStatus? status = null,
        [FromQuery] Guid? immId = null,
        [FromQuery] string? personnelId = null,
        [FromQuery] DateTime? issuedFrom = null,
        [FromQuery] DateTime? issuedTo = null,
        [FromQuery] DateOnly? plannedFrom = null,
        [FromQuery] DateOnly? plannedTo = null,
        [FromQuery] DateTime? activeFrom = null,
        [FromQuery] DateTime? activeTo = null)
    {
        var query = _context.ShiftTasks
            .Include(t => t.Imm)
            .Include(t => t.Mold)
            .Include(t => t.Personnel)
            .Include(t => t.Order)
            .AsQueryable();

        bool activeFilter = activeFrom.HasValue || activeTo.HasValue;

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        else if (!activeFilter)
            query = query.Where(t => t.Status != Core.Enums.TaskStatus.Completed
                                  && t.Status != Core.Enums.TaskStatus.Closed);
        if (immId.HasValue)
            query = query.Where(t => t.ImmId == immId.Value);
        if (!string.IsNullOrEmpty(personnelId))
            query = query.Where(t => t.PersonnelId == personnelId);

        if (issuedFrom.HasValue)
        {
            var from = DateTime.SpecifyKind(issuedFrom.Value, DateTimeKind.Utc);
            query = query.Where(t => t.IssuedAt >= from);
        }
        if (issuedTo.HasValue)
        {
            var to = DateTime.SpecifyKind(issuedTo.Value, DateTimeKind.Utc);
            query = query.Where(t => t.IssuedAt <= to);
        }

        if (plannedFrom.HasValue)
            query = query.Where(t => t.PlannedDate >= plannedFrom.Value);
        if (plannedTo.HasValue)
            query = query.Where(t => t.PlannedDate <= plannedTo.Value);

        if (activeFilter)
        {
            var from = activeFrom.HasValue ? DateTime.SpecifyKind(activeFrom.Value, DateTimeKind.Utc) : DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
            var to   = activeTo.HasValue   ? DateTime.SpecifyKind(activeTo.Value,   DateTimeKind.Utc) : DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
            query = query.Where(TaskActivityWindow.OverlapsPeriod(from, to));
        }
```

Ниже — без изменений (`var tasks = await query.ToListAsync(); …`). Добавить `using Wintime.Control.Core.Policies;` в начало файла, если его нет.

Note: при одной заданной границе вторая — `DateTime.MinValue`/`MaxValue` с `Kind=Utc` (Npgsql пишет их как `-infinity`/`infinity`). Тесты задают обе границы.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~TaskListFiltersApiTests`
Expected: PASS (4 теста).

- [ ] **Step 5: Регрессия списков заданий**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~Tasks`
Expected: PASS. Если какой-то существующий тест передавал `dateFrom`/`dateTo` — переименовать в `issuedFrom`/`issuedTo`.

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.API/Controllers/TasksController.cs Wintime.Control.Tests.Integration/Tasks/TaskListFiltersApiTests.cs
git commit -m "feat(arch-03): task list filters issued/planned/active (AND-combined)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Фронт — три поля периода на странице «Задания»

**Files:**
- Create: `Wintime-Control-Frontend/src/utils/taskListParams.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/taskListParams.spec.js`
- Modify: `Wintime-Control-Frontend/src/stores/tasks.js` (state `filters`, `loadTasks`, `clearFilters`)
- Modify: `Wintime-Control-Frontend/src/components/tasks/TaskFilters.vue`

**Interfaces:**
- Consumes: API из Task 2.
- Produces: `buildTaskListParams(filters) → object` — query-параметры для `tasksApi.getList`. Фильтры стора: `{ status, immId, personnelId, issuedFrom, issuedTo, plannedFrom, plannedTo, activeFrom, activeTo, search }`, даты — строки `YYYY-MM-DD` (из `el-date-picker value-format`).

- [ ] **Step 1: Write the failing test**

`Wintime-Control-Frontend/src/utils/__tests__/taskListParams.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import { buildTaskListParams } from '../taskListParams'

const localIso = (y, m, d, h = 0, min = 0, s = 0, ms = 0) => new Date(y, m - 1, d, h, min, s, ms).toISOString()

describe('buildTaskListParams', () => {
  it('убирает пустые значения', () => {
    expect(buildTaskListParams({ status: null, immId: '', personnelId: undefined, search: '' })).toEqual({})
  })

  it('«Выдано» — включительно до конца дня (пояс браузера)', () => {
    const p = buildTaskListParams({ issuedFrom: '2026-10-13', issuedTo: '2026-10-14' })
    expect(p.issuedFrom).toBe(localIso(2026, 10, 13))
    expect(p.issuedTo).toBe(localIso(2026, 10, 14, 23, 59, 59, 999))
  })

  it('«В работе» — полуинтервал до начала следующего дня', () => {
    const p = buildTaskListParams({ activeFrom: '2026-10-13', activeTo: '2026-10-14' })
    expect(p.activeFrom).toBe(localIso(2026, 10, 13))
    expect(p.activeTo).toBe(localIso(2026, 10, 15))
  })

  it('«Плановая дата» — передаётся датой как есть', () => {
    const p = buildTaskListParams({ plannedFrom: '2026-10-13', plannedTo: '2026-10-20' })
    expect(p).toEqual({ plannedFrom: '2026-10-13', plannedTo: '2026-10-20' })
  })

  it('прочие фильтры передаются без изменений', () => {
    expect(buildTaskListParams({ status: 'Issued', immId: 'imm-1', personnelId: 'u-1' }))
      .toEqual({ status: 'Issued', immId: 'imm-1', personnelId: 'u-1' })
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd Wintime-Control-Frontend && npx vitest run src/utils/__tests__/taskListParams.spec.js`
Expected: FAIL — `Failed to resolve import "../taskListParams"`.

- [ ] **Step 3: Write implementation**

`Wintime-Control-Frontend/src/utils/taskListParams.js`:

```js
// Фильтры списка заданий → query-параметры GET /api/tasks (ARCH-03).
// Даты из date-picker — 'YYYY-MM-DD' в поясе браузера.
// «Выдано» — включительно [начало from; конец to]; «В работе» — полуинтервал
// [начало from; начало дня после to); «Плановая дата» — дата завода, передаётся как есть.

function dayStart(ymd) {
  const [y, m, d] = ymd.split('-').map(Number)
  return new Date(y, m - 1, d)
}

function addDays(date, days) {
  const r = new Date(date)
  r.setDate(r.getDate() + days)
  return r
}

export function buildTaskListParams(filters) {
  const f = filters ?? {}
  const params = {
    status: f.status,
    immId: f.immId,
    personnelId: f.personnelId,
    issuedFrom: f.issuedFrom ? dayStart(f.issuedFrom).toISOString() : null,
    issuedTo: f.issuedTo ? new Date(addDays(dayStart(f.issuedTo), 1) - 1).toISOString() : null,
    plannedFrom: f.plannedFrom,
    plannedTo: f.plannedTo,
    activeFrom: f.activeFrom ? dayStart(f.activeFrom).toISOString() : null,
    activeTo: f.activeTo ? addDays(dayStart(f.activeTo), 1).toISOString() : null
  }
  Object.keys(params).forEach(key => {
    if (params[key] === null || params[key] === undefined || params[key] === '') delete params[key]
  })
  return params
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd Wintime-Control-Frontend && npx vitest run src/utils/__tests__/taskListParams.spec.js`
Expected: PASS (5 тестов).

- [ ] **Step 5: Стор**

`Wintime-Control-Frontend/src/stores/tasks.js`:
- `import { buildTaskListParams } from '@/utils/taskListParams'`.
- В `state.filters` и в `clearFilters()` заменить `dateFrom: null, dateTo: null,` на:

```js
      issuedFrom: null,
      issuedTo: null,
      plannedFrom: null,
      plannedTo: null,
      activeFrom: null,
      activeTo: null,
```

- В `loadTasks()` заменить построение `params` и блок «Очистка пустых параметров» на:

```js
        const params = buildTaskListParams(this.filters)
```

- [ ] **Step 6: `TaskFilters.vue`**

Заменить единственный `<el-form-item label="Выдано">…</el-form-item>` на три:

```vue
      <el-form-item label="Выдано">
        <el-date-picker
          v-model="issuedRange"
          type="daterange"
          range-separator="—"
          start-placeholder="Начало"
          end-placeholder="Окончание"
          value-format="YYYY-MM-DD"
          class="w-64"
        />
      </el-form-item>

      <el-form-item label="Плановая дата">
        <el-date-picker
          v-model="plannedRange"
          type="daterange"
          range-separator="—"
          start-placeholder="Начало"
          end-placeholder="Окончание"
          value-format="YYYY-MM-DD"
          class="w-64"
        />
      </el-form-item>

      <el-form-item label="В работе">
        <el-date-picker
          v-model="activeRange"
          type="daterange"
          range-separator="—"
          start-placeholder="Начало"
          end-placeholder="Окончание"
          value-format="YYYY-MM-DD"
          class="w-64"
        />
      </el-form-item>
```

В `<script setup>` заменить `const dateRange = …` и его `watch` на:

```js
// Три независимых диапазона (ARCH-03): выдача, плановая дата, работа на ТПА; объединяются через И.
function rangeRef(fromKey, toKey) {
  const range = ref(props.filters[fromKey] ? [props.filters[fromKey], props.filters[toKey]] : [])
  watch(range, (r) => {
    const set = r && r.length === 2
    localFilters[fromKey] = set ? r[0] : null
    localFilters[toKey] = set ? r[1] : null
  })
  return range
}

const issuedRange = rangeRef('issuedFrom', 'issuedTo')
const plannedRange = rangeRef('plannedFrom', 'plannedTo')
const activeRange = rangeRef('activeFrom', 'activeTo')
```

В `resetFilters` (ниже в файле) заменить сброс `dateRange.value = []` на:

```js
  issuedRange.value = []
  plannedRange.value = []
  activeRange.value = []
```

и сброс `localFilters.dateFrom/dateTo` — на шесть новых ключей `= null`. Прочитать функцию целиком перед правкой.

- [ ] **Step 7: Проверка**

Run: `cd Wintime-Control-Frontend && grep -rn "dateFrom\|dateTo\|dateRange" src/stores/tasks.js src/components/tasks/TaskFilters.vue`
Expected: пусто.
Run: `cd Wintime-Control-Frontend && npx vitest run` → Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add Wintime-Control-Frontend/src/utils/taskListParams.js Wintime-Control-Frontend/src/utils/__tests__/taskListParams.spec.js Wintime-Control-Frontend/src/stores/tasks.js Wintime-Control-Frontend/src/components/tasks/TaskFilters.vue
git commit -m "feat(arch-03): three period filters on Tasks page

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Карточка ТПА — задания по окну активности, бар от начала наладки

**Files:**
- Create: `Wintime-Control-Frontend/src/utils/shiftTimeline.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/shiftTimeline.spec.js`
- Modify: `Wintime-Control-Frontend/src/components/dashboard/ShiftTimeline.vue` (`taskStyle`, `taskTitle`, `v-for` по заданиям)
- Modify: `Wintime-Control-Frontend/src/views/dashboard/ImmDetailModal.vue:180`

**Interfaces:**
- Consumes: `GET /api/tasks?immId&activeFrom&activeTo` (Task 2); поля `TaskDto`: `setupStartedAt`, `startedAt`, `completedAt`, `closedAt` (camelCase).
- Produces: `taskBarLayout(task, shiftStart: Date, shiftEnd: Date, now: Date) → { leftPct, widthPct, setupPct } | null`.

- [ ] **Step 1: Write the failing test**

`Wintime-Control-Frontend/src/utils/__tests__/shiftTimeline.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import { taskBarLayout } from '../shiftTimeline'

const S = new Date('2026-10-13T08:00:00Z')
const E = new Date('2026-10-13T20:00:00Z') // смена 12 ч
const at = (h) => new Date(S.getTime() + h * 3_600_000).toISOString()
const NOW = new Date(S.getTime() + 6 * 3_600_000)

describe('taskBarLayout', () => {
  it('нет начала наладки — бара нет', () => {
    expect(taskBarLayout({ setupStartedAt: null }, S, E, NOW)).toBeNull()
  })

  it('начато до смены и идёт — от начала смены до «сейчас», наладки в смене нет', () => {
    const l = taskBarLayout({ setupStartedAt: at(-30), startedAt: at(-29) }, S, E, NOW)
    expect(l.leftPct).toBeCloseTo(0)
    expect(l.widthPct).toBeCloseTo(50)
    expect(l.setupPct).toBeCloseTo(0)
  })

  it('наладка и выпуск внутри смены — доля наладки', () => {
    const l = taskBarLayout({ setupStartedAt: at(1), startedAt: at(2), completedAt: at(5) }, S, E, NOW)
    expect(l.leftPct).toBeCloseTo(100 / 12)
    expect(l.widthPct).toBeCloseTo(4 / 12 * 100)
    expect(l.setupPct).toBeCloseTo(25)
  })

  it('идёт наладка — весь бар наладка', () => {
    const l = taskBarLayout({ setupStartedAt: at(4) }, S, E, NOW)
    expect(l.widthPct).toBeCloseTo(2 / 12 * 100)
    expect(l.setupPct).toBeCloseTo(100)
  })

  it('закрыто без завершения — до closedAt', () => {
    const l = taskBarLayout({ setupStartedAt: at(1), closedAt: at(3) }, S, E, NOW)
    expect(l.widthPct).toBeCloseTo(2 / 12 * 100)
  })

  it('завершилось до смены — бара нет', () => {
    expect(taskBarLayout({ setupStartedAt: at(-10), startedAt: at(-9), completedAt: at(-1) }, S, E, NOW)).toBeNull()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd Wintime-Control-Frontend && npx vitest run src/utils/__tests__/shiftTimeline.spec.js`
Expected: FAIL — `Failed to resolve import "../shiftTimeline"`.

- [ ] **Step 3: Write implementation**

`Wintime-Control-Frontend/src/utils/shiftTimeline.js`:

```js
// Геометрия бара задания на хронологии смены (карточка ТПА, ARCH-03).
// Бар = окно активности задания [setupStartedAt, completedAt ?? closedAt ?? now),
// обрезанное границами смены; setupPct — доля бара, занятая наладкой (до startedAt).

export function taskBarLayout(task, shiftStart, shiftEnd, now) {
  if (!task?.setupStartedAt) return null
  const s0 = shiftStart.getTime()
  const e0 = shiftEnd.getTime()
  const dur = e0 - s0
  if (dur <= 0) return null

  const clamp = (t) => Math.min(Math.max(t, s0), e0)
  const start = new Date(task.setupStartedAt).getTime()
  const endRaw = task.completedAt ?? task.closedAt
  const end = endRaw ? new Date(endRaw).getTime() : now.getTime()
  const releaseStart = task.startedAt ? new Date(task.startedAt).getTime() : end

  const s = clamp(start)
  const e = clamp(end)
  if (e <= s) return null
  const setupEnd = clamp(Math.min(releaseStart, end))

  return {
    leftPct: (s - s0) / dur * 100,
    widthPct: (e - s) / dur * 100,
    setupPct: Math.max(0, setupEnd - s) / (e - s) * 100
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd Wintime-Control-Frontend && npx vitest run src/utils/__tests__/shiftTimeline.spec.js`
Expected: PASS (6 тестов).

- [ ] **Step 5: `ShiftTimeline.vue`**

- Импорт: `import { taskBarLayout } from '@/utils/shiftTimeline'`.
- Добавить computed с барами и рендерить только задания с баром:

```js
// Бары заданий: только задания с окном активности, пересекающим смену.
const taskBars = computed(() => props.tasks
  .map((task, idx) => ({ task, idx, layout: taskBarLayout(task, props.shiftStart, props.shiftEnd, nowClamped.value) }))
  .filter(b => b.layout))
```

- В шаблоне заменить `v-for="task in tasks"` блок на:

```vue
          <div
            v-for="bar in taskBars"
            :key="bar.task.id"
            class="task-block"
            :style="taskStyle(bar)"
            :title="taskTitle(bar.task)"
          >
            <div class="task-name">{{ bar.task.moldName || 'Задание' }}</div>
            <div class="task-qty">{{ bar.task.actualQuantity }}&thinsp;/&thinsp;{{ bar.task.planQuantity }} шт</div>
          </div>
```

- Заменить функцию `taskStyle`:

```js
// Наладка — светлым оттенком цвета задания, выпуск — основным цветом.
function taskStyle(bar) {
  const color = TASK_COLORS[bar.idx % TASK_COLORS.length]
  const sp = bar.layout.setupPct.toFixed(3)
  return {
    left:       bar.layout.leftPct.toFixed(3) + '%',
    width:      bar.layout.widthPct.toFixed(3) + '%',
    background: `linear-gradient(to right, ${color}80 0 ${sp}%, ${color} ${sp}% 100%)`,
  }
}
```

- В `taskTitle` заменить первые две строки:

```js
  const start = new Date(task.setupStartedAt)
  const end   = new Date(task.completedAt ?? task.closedAt ?? now.value)
```

и добавить строку наладки в массив после строки времени:

```js
    task.startedAt ? `Наладка: ${formatTime(task.setupStartedAt)} — ${formatTime(task.startedAt)}` : 'Идёт наладка',
```

- [ ] **Step 6: `ImmDetailModal.vue`**

Строка 180: `tasksApi.getList({ immId: props.immId, dateFrom: fromIso, dateTo: toIso }),` →

```js
      tasksApi.getList({ immId: props.immId, activeFrom: fromIso, activeTo: toIso }),
```

- [ ] **Step 7: Проверка**

Run: `cd Wintime-Control-Frontend && npx vitest run && npm run build`
Expected: all PASS, сборка без ошибок.

Ручная проверка (`superpowers:verification-before-completion` / скилл `run`): API + фронт, задание с наладкой, начатой вчера (через БД или эмулятор) → в карточке ТПА бар от начала смены; задание, завершённое в смене, видно.

- [ ] **Step 8: Commit**

```bash
git add Wintime-Control-Frontend/src/utils/shiftTimeline.js Wintime-Control-Frontend/src/utils/__tests__/shiftTimeline.spec.js Wintime-Control-Frontend/src/components/dashboard/ShiftTimeline.vue Wintime-Control-Frontend/src/views/dashboard/ImmDetailModal.vue
git commit -m "feat(arch-03): IMM card timeline — tasks by activity window, bar from setup start

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Нормативный план задания за период (`TaskPeriodPlan`)

**Files:**
- Create: `Wintime.Control.Core/Policies/TaskPeriodPlan.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/TaskPeriodPlanTests.cs`

**Interfaces:**
- Consumes: `Interval` (`EffectiveStatusTimeline.cs:8`); окно выпуска — `TaskActivityWindow.Release` (Task 1) вызывает потребитель.
- Produces: `static int TaskPeriodPlan.Calculate(Interval? release, int planQuantity, int cavities, int? fullCycleSeconds, DateTime fromUtc, DateTime toUtc)`.

- [ ] **Step 1: Write the failing test**

`Wintime.Control.Tests.Unit/Policies/TaskPeriodPlanTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class TaskPeriodPlanTests
{
    private static readonly DateTime S = new(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc);
    private static DateTime H(double hours) => S.AddHours(hours);

    // Эталон 60 с, 2 гнезда → 120 шт/ч
    private static int Plan(Interval? release, int plan, DateTime from, DateTime to) =>
        TaskPeriodPlan.Calculate(release, plan, cavities: 2, fullCycleSeconds: 60, from, to);

    [Fact]
    public void TaskInsidePeriod_FullNormCappedByPlan()
    {
        Plan(new Interval(H(1), H(3)), 1000, H(0), H(24)).Should().Be(240);
        Plan(new Interval(H(1), H(3)), 100, H(0), H(24)).Should().Be(100);
    }

    [Fact]
    public void ThreeDayTask_SumOfDaysEqualsTotalNorm()
    {
        var release = new Interval(H(3), H(51)); // 48 ч → 5760 шт по норме
        var d1 = Plan(release, 100_000, H(-8), H(16));
        var d2 = Plan(release, 100_000, H(16), H(40));
        var d3 = Plan(release, 100_000, H(40), H(64));
        d2.Should().Be(2880);
        (d1 + d2 + d3).Should().Be(5760);
    }

    [Fact]
    public void SumOfDaysNeverExceedsPlan_TailGoesToDayWhereNormReachedPlan()
    {
        var release = new Interval(H(0), H(48));
        var d1 = Plan(release, 3000, H(0), H(24));  // норма 2880
        var d2 = Plan(release, 3000, H(24), H(48)); // остаток 120
        d1.Should().Be(2880);
        d2.Should().Be(120);
    }

    [Fact]
    public void PartialShot_RoundsDown()
    {
        // 90 с выпуска при эталоне 60 с → 1 смыкание × 2 гнезда
        Plan(new Interval(S, S.AddSeconds(90)), 1000, H(-1), H(1)).Should().Be(2);
    }

    [Fact]
    public void PeriodBeforeRelease_Zero()
    {
        Plan(new Interval(H(10), H(20)), 1000, H(0), H(5)).Should().Be(0);
    }

    [Fact]
    public void NoRelease_Zero()
    {
        Plan(null, 1000, H(0), H(24)).Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void NoCycleNorm_Zero(int? cycle)
    {
        TaskPeriodPlan.Calculate(new Interval(H(0), H(5)), 1000, 2, cycle, H(0), H(24)).Should().Be(0);
    }

    [Fact]
    public void NoCavities_Zero()
    {
        TaskPeriodPlan.Calculate(new Interval(H(0), H(5)), 1000, 0, 60, H(0), H(24)).Should().Be(0);
    }

    [Fact]
    public void HugeValues_DoNotOverflow()
    {
        TaskPeriodPlan.Calculate(new Interval(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc), DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)),
            int.MaxValue, int.MaxValue, 1, H(0), H(24)).Should().BeInRange(0, int.MaxValue);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~TaskPeriodPlanTests`
Expected: FAIL — `The name 'TaskPeriodPlan' does not exist`.

- [ ] **Step 3: Write implementation**

`Wintime.Control.Core/Policies/TaskPeriodPlan.cs`:

```csharp
namespace Wintime.Control.Core.Policies;

/// <summary>
/// Нормативный план задания за период (ARCH-03, ADR-0016): сколько штук должно было выйти
/// за время выпуска внутри <c>[from, to)</c> по эталону полного цикла.
/// <c>N(t) = ⌊(min(t, E) − S) / C⌋ × K</c> — накопленный норматив к моменту t;
/// план за период = <c>min(P, N(to)) − min(P, N(from))</c>. Поэтому сумма по дням равна плану
/// задания (не больше P), а простои внутри выпуска не вычитаются — это потери, видимые как
/// разрыв план/факт. Гнёзда K — текущие <c>Mold.Cavities</c> (прогноз, как в TaskPlanCalculator).
/// </summary>
public static class TaskPeriodPlan
{
    /// <param name="release">Окно выпуска (<see cref="TaskActivityWindow.Release"/>); <c>null</c> → 0.</param>
    public static int Calculate(Interval? release, int planQuantity, int cavities, int? fullCycleSeconds,
                                DateTime fromUtc, DateTime toUtc)
    {
        if (release is null || fullCycleSeconds is not > 0 || cavities <= 0 || planQuantity <= 0)
            return 0;

        long cap = planQuantity;
        long Cumulative(DateTime t)
        {
            var until = t < release.End ? t : release.End;
            if (until <= release.Start)
                return 0;
            long shots = (long)((until - release.Start).TotalSeconds / fullCycleSeconds.Value);
            // shots × K с насыщением до плана: переполнения быть не должно
            return shots >= (cap + cavities - 1) / cavities ? cap : Math.Min(cap, shots * cavities);
        }

        return (int)(Cumulative(toUtc) - Cumulative(fromUtc));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~TaskPeriodPlanTests`
Expected: PASS (все тесты класса).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/TaskPeriodPlan.cs Wintime.Control.Tests.Unit/Policies/TaskPeriodPlanTests.cs
git commit -m "feat(arch-03): TaskPeriodPlan — normative plan of a task for a period

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Отчёт «Рабочий день» — все задания периода, нормативный план, расход по циклам

**Files:**
- Modify: `Wintime.Control.Infrastructure/Reports/ReportService.cs:84-226` (`GenerateDailyImmItemAsync`)
- Test: `Wintime.Control.Tests.Integration/Reports/DailyReportMultiDayTaskTests.cs` (new)

**Interfaces:**
- Consumes: `TaskActivityWindow.OverlapsPeriod`, `TaskActivityWindow.Release` (Task 1), `TaskPeriodPlan.Calculate` (Task 5).
- Produces: `DailyReportImmItemDto` — контракт полей не меняется; `MoldName` = ПФ всех заданий периода через `", "`, `PlanQuantity` = сумма нормативных планов, `RawMaterialKg` — по ПФ каждого цикла.

- [ ] **Step 1: Write the failing test**

`Wintime.Control.Tests.Integration/Reports/DailyReportMultiDayTaskTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Report;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Reports;

/// <summary>
/// ARCH-03: отчёт «Рабочий день» для заданий через несколько суток и нескольких заданий за сутки.
/// Метки времени выбраны так, чтобы попадать в те же локальные сутки завода при поясе от UTC−6 до UTC+10
/// (по умолчанию — Europe/Moscow).
/// </summary>
[Collection("Integration")]
public class DailyReportMultiDayTaskTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public DailyReportMultiDayTaskTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<DailyReportImmItemDto> GetItemAsync(DateTime day, Guid immId)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var resp = await client.GetAsync($"/api/reports/daily?date={day:yyyy-MM-dd}&immId={immId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await resp.Content.ReadFromJsonAsync<DailyReportDto>();
        return report!.ImmData.Should().ContainSingle().Subject;
    }

    private static Mold NewMold(string name, int cavities, decimal part, decimal runner) => new()
    {
        Name = name, FormId = $"F-{Guid.NewGuid():N}", Cavities = cavities,
        PartWeightGrams = part, RunnerWeightGrams = runner, IsActive = true
    };

    [Fact]
    public async Task ThreeDayTask_SecondDayShowsMoldAndNormativePlan_SumEqualsTotalNorm()
    {
        var d1 = new DateTime(2025, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var immId = await _factory.CreateFreshImmAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = NewMold($"ПФ-3сут-{Guid.NewGuid():N}", 2, 10, 4);
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            // Выпуск: d1 11:00Z → d3 11:00Z (48 ч), норма 60 с × 2 гнезда = 120 шт/ч
            db.ShiftTasks.Add(new ShiftTask
            {
                ImmId = immId, MoldId = mold.Id, PlanQuantity = 100_000, PlannedFullCycleSeconds = 60,
                Status = TaskStatus.Completed, PlannedDate = DateOnly.FromDateTime(d1),
                SetupStartedAt = d1.AddHours(10), StartedAt = d1.AddHours(11), CompletedAt = d1.AddDays(2).AddHours(11)
            });
            await db.SaveChangesAsync();
        }

        var day1 = await GetItemAsync(d1, immId);
        var day2 = await GetItemAsync(d1.AddDays(1), immId);
        var day3 = await GetItemAsync(d1.AddDays(2), immId);

        day2.MoldName.Should().StartWith("ПФ-3сут-");
        day2.PlanQuantity.Should().Be(2880);
        (day1.PlanQuantity + day2.PlanQuantity + day3.PlanQuantity).Should().Be(5760);
    }

    [Fact]
    public async Task TwoTasksOneDay_MoldsListed_PlansSummed_RawMaterialPerCycleMold()
    {
        var d = new DateTime(2025, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var immId = await _factory.CreateFreshImmAsync();
        string mold1Name, mold2Name;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold1 = NewMold($"ПФ-A-{Guid.NewGuid():N}", 2, 10, 4);
            var mold2 = NewMold($"ПФ-B-{Guid.NewGuid():N}", 1, 5, 1);
            db.Molds.AddRange(mold1, mold2);
            await db.SaveChangesAsync();
            mold1Name = mold1.Name; mold2Name = mold2.Name;

            // Задание 1: выпуск 08:00–10:00, норма 120 шт/ч → 240, план 100 → 100
            var t1 = new ShiftTask
            {
                ImmId = immId, MoldId = mold1.Id, PlanQuantity = 100, PlannedFullCycleSeconds = 60,
                Status = TaskStatus.Completed, PlannedDate = DateOnly.FromDateTime(d),
                SetupStartedAt = d.AddHours(7), StartedAt = d.AddHours(8), CompletedAt = d.AddHours(10)
            };
            // Задание 2: выпуск 12:00–13:00, норма 30 с × 1 гнездо → 120
            var t2 = new ShiftTask
            {
                ImmId = immId, MoldId = mold2.Id, PlanQuantity = 1000, PlannedFullCycleSeconds = 30,
                Status = TaskStatus.Completed, PlannedDate = DateOnly.FromDateTime(d),
                SetupStartedAt = d.AddHours(11), StartedAt = d.AddHours(12), CompletedAt = d.AddHours(13)
            };
            db.ShiftTasks.AddRange(t1, t2);
            await db.SaveChangesAsync();

            ImmCycle Cycle(Guid moldId, Guid taskId, int cavities, DateTime start) => new()
            {
                ImmId = immId, MoldId = moldId, TaskId = taskId, Cavities = cavities, IsSuccessful = true,
                StartTime = start, EndTime = start.AddSeconds(60), DurationSeconds = 60
            };
            db.ImmCycles.AddRange(
                Cycle(mold1.Id, t1.Id, 2, d.AddHours(9)),
                Cycle(mold1.Id, t1.Id, 2, d.AddHours(9).AddMinutes(1)),
                Cycle(mold2.Id, t2.Id, 1, d.AddHours(12).AddMinutes(30)));
            await db.SaveChangesAsync();
        }

        var item = await GetItemAsync(d, immId);

        item.MoldName.Should().Be($"{mold1Name}, {mold2Name}");
        item.PlanQuantity.Should().Be(100 + 120);
        item.ActualQuantity.Should().Be(5);
        // ПФ-A: 2 цикла × (10 г × 2 гнезда + 4 г литник) = 48 г; ПФ-B: 1 × (5 × 1 + 1) = 6 г
        item.RawMaterialKg.Should().Be(0.054m);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~DailyReportMultiDayTaskTests`
Expected: FAIL — `day2.MoldName` пустой / `PlanQuantity` = 0; во втором тесте `MoldName` одной ПФ, `PlanQuantity` = `PlanQuantity` одного задания.

- [ ] **Step 3: Write implementation**

В `ReportService.GenerateDailyImmItemAsync`:

(а) заменить запрос «Активное задание за период» (строки 103-107):

```csharp
        // Задания, чьё окно активности пересекается с периодом (ARCH-03): задание может идти
        // через несколько смен и суток, поэтому отбор — не по одной точке StartedAt.
        var tasks = await _context.ShiftTasks
            .Include(t => t.Mold)
            .Where(t => t.ImmId == imm.Id)
            .Where(TaskActivityWindow.OverlapsPeriod(periodStart, periodEnd))
            .OrderBy(t => t.SetupStartedAt)
            .ToListAsync(ct);

        // ПФ циклов периода — для расхода сырья по ПФ каждого цикла
        var cycleMoldIds = cycles.Where(c => c.MoldId != null).Select(c => c.MoldId!.Value).Distinct().ToList();
        var cycleMolds = await _context.Molds
            .Where(m => cycleMoldIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);
```

(б) заменить блок «Выработка считается по снапшоту…» (строки 160-164):

```csharp
        // Выработка — по снапшоту гнёздности каждого цикла (ImmCycle.Cavities), не по текущему Mold.Cavities.
        var actualQuantity = cycles.Sum(c => c.Cavities);
```

(в) заменить блок «Расчёт расхода сырья» (строки 184-195):

```csharp
        // Расход сырья — по ПФ каждого цикла: деталь × гнёзда цикла + один литник на цикл.
        // Циклы без ПФ (MoldId = null) сырьё не считают.
        decimal rawGrams = 0;
        foreach (var c in cycles)
            if (c.MoldId is Guid moldId && cycleMolds.TryGetValue(moldId, out var cm))
                rawGrams += cm.PartWeightGrams * c.Cavities + cm.RunnerWeightGrams;
        var rawMaterialKg = rawGrams / 1000m;

        var moldNames = tasks.Select(t => t.Mold?.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        string? moldName = moldNames.Count > 0 ? string.Join(", ", moldNames) : null;

        // Нормативный план за период (ARCH-03): сумма по заданиям; для текущих суток — до «сейчас».
        var planQuantity = tasks.Sum(t => TaskPeriodPlan.Calculate(
            TaskActivityWindow.Release(t, now), t.PlanQuantity, t.Mold?.Cavities ?? 0,
            t.PlannedFullCycleSeconds, periodStart, aggregateEnd));
```

(г) в `return new DailyReportImmItemDto { … }` заменить `PlanQuantity = task?.PlanQuantity ?? 0,` на `PlanQuantity = planQuantity,`.

Note: переменные `now` и `aggregateEnd` объявлены выше в методе (строки 125-127) — блок (в) должен стоять после них (он и стоит: после цикла по `statusHistory`). Переменная `task` больше не используется — убедиться, что ссылок на неё не осталось.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~DailyReport`
Expected: PASS (новые и `DailyReportTimelineTests`).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Infrastructure/Reports/ReportService.cs Wintime.Control.Tests.Integration/Reports/DailyReportMultiDayTaskTests.cs
git commit -m "feat(arch-03): daily report — all tasks of the period, normative plan, raw material per cycle mold

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Отчёты по наладчикам — наладки из заданий; удалить «смены» и «время работы»

**Files:**
- Modify: `Wintime.Control.Core/DTOs/Report/AssetsPersonnelItemDto.cs`, `AssetsPersonnelByImmItemDto.cs`, `AssetsPersonnelImmBreakdownDto.cs`
- Modify: `Wintime.Control.Infrastructure/Reports/ReportService.cs:460-500` (Personnel), `:590-693` (PersonnelByImm), `:810-866` (Excel)
- Delete: `Wintime.Control.Infrastructure/Reports/ShiftWorkCalculator.cs`, `Wintime.Control.Tests.Unit/Reports/ShiftWorkCalculatorTests.cs`
- Modify: `Wintime-Control-Frontend/src/views/reports/AssetsReportView.vue:117-185`
- Test: `Wintime.Control.Tests.Integration/Reports/PersonnelReportTests.cs` (new)

**Interfaces:**
- Produces: DTO-поля `CompletedTasks`, `CompletedSetups`, `TotalSetupSeconds`, `AvgSetupTime` (секунды, `decimal`); поля `WorkedShifts`, `TotalWorkSeconds` удалены.

- [ ] **Step 1: Write the failing test**

`Wintime.Control.Tests.Integration/Reports/PersonnelReportTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Report;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Reports;

/// <summary>ARCH-03: наладки и задания наладчика — по интервалам задания, не по событиям/сменам.</summary>
[Collection("Integration")]
public class PersonnelReportTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public PersonnelReportTests(IntegrationTestFactory factory) => _factory = factory;

    // Отчётные сутки; метки времени в полдень UTC — те же локальные сутки при поясе UTC−11…UTC+11
    private static readonly DateTime Day = new(2025, 5, 12, 0, 0, 0, DateTimeKind.Utc);

    private async Task<string> CreateAdjusterAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var name = $"adj_{Guid.NewGuid():N}";
        var user = new User { UserName = name, Email = $"{name}@test.local", FullName = name, Role = UserRole.Adjuster, IsActive = true };
        (await um.CreateAsync(user, "Passw0rd!")).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task SeedAsync(string adjusterId, Guid immId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold { Name = $"M-{Guid.NewGuid():N}", FormId = $"F-{Guid.NewGuid():N}", Cavities = 1, IsActive = true };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();

        ShiftTask T(TaskStatus st, DateTime? setup, DateTime? started, DateTime? completed) => new()
        {
            ImmId = immId, MoldId = mold.Id, PersonnelId = adjusterId, PlanQuantity = 10, Status = st,
            PlannedDate = DateOnly.FromDateTime(Day), IssuedAt = Day.AddDays(-2),
            SetupStartedAt = setup, StartedAt = started, CompletedAt = completed
        };

        db.ShiftTasks.AddRange(
            // Наладка через границу суток: накануне 12:00 → сегодня 12:00 (24 ч) — относится к сегодня;
            // задание ещё в работе
            T(TaskStatus.InProgress, Day.AddHours(-12), Day.AddHours(12), null),
            // Начато позавчера, завершено сегодня — «выполнено заданий» +1, наладка не сегодня
            T(TaskStatus.Completed, Day.AddDays(-2).AddHours(10), Day.AddDays(-2).AddHours(11), Day.AddHours(13)),
            // Наладка сегодня 1 ч, задание в работе
            T(TaskStatus.InProgress, Day.AddHours(10), Day.AddHours(11), null),
            // Идёт наладка — не завершена, не считается
            T(TaskStatus.Setup, Day.AddHours(14), null, null),
            // Отменённая наладка (CancelSetup сбрасывает SetupStartedAt) — не считается
            T(TaskStatus.Issued, null, null, null));
        await db.SaveChangesAsync();
    }

    private async Task<AssetsReportDto> GetAsync(string reportType)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var resp = await client.GetAsync($"/api/reports/assets?dateFrom={Day:yyyy-MM-dd}&dateTo={Day:yyyy-MM-dd}&reportType={reportType}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<AssetsReportDto>())!;
    }

    [Fact]
    public async Task Personnel_CountsSetupsByCompletion_TasksByCompletedAt()
    {
        var adjusterId = await CreateAdjusterAsync();
        var immId = await _factory.CreateFreshImmAsync();
        await SeedAsync(adjusterId, immId);

        var report = await GetAsync("Personnel");
        var row = report.PersonnelData.Should().ContainSingle(p => p.PersonnelId == adjusterId).Subject;

        row.CompletedTasks.Should().Be(1);
        row.CompletedSetups.Should().Be(2);
        row.TotalSetupSeconds.Should().Be(24 * 3600 + 3600);
        row.AvgSetupTime.Should().Be((24 * 3600 + 3600) / 2m);
    }

    [Fact]
    public async Task PersonnelByImm_SameRulesWithImmBreakdown()
    {
        var adjusterId = await CreateAdjusterAsync();
        var immId = await _factory.CreateFreshImmAsync();
        await SeedAsync(adjusterId, immId);

        var report = await GetAsync("PersonnelByImm");
        var row = report.PersonnelByImmData.Should().ContainSingle(p => p.PersonnelId == adjusterId).Subject;

        row.CompletedTasks.Should().Be(1);
        row.CompletedSetups.Should().Be(2);
        row.TotalSetupSeconds.Should().Be(24 * 3600 + 3600);
        var imm = row.ImmBreakdown.Should().ContainSingle().Subject;
        imm.ImmId.Should().Be(immId);
        imm.CompletedSetups.Should().Be(2);
        imm.CompletedTasks.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~PersonnelReportTests`
Expected: FAIL — build error `'AssetsPersonnelItemDto' does not contain a definition for 'CompletedSetups'`.

- [ ] **Step 3: DTO**

`AssetsPersonnelItemDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Report;

public class AssetsPersonnelItemDto
{
    public string PersonnelId { get; set; } = string.Empty;
    public string? FullName { get; set; }
    /// <summary>Задания, завершённые (Completed) в периоде.</summary>
    public int CompletedTasks { get; set; }
    /// <summary>Наладки, завершённые в периоде (StartedAt ∈ период).</summary>
    public int CompletedSetups { get; set; }
    /// <summary>Среднее время наладки, секунды.</summary>
    public decimal AvgSetupTime { get; set; }
    /// <summary>Суммарное время наладок, завершённых в периоде, секунды.</summary>
    public int TotalSetupSeconds { get; set; }
}
```

`AssetsPersonnelByImmItemDto.cs` — те же поля + `public List<AssetsPersonnelImmBreakdownDto> ImmBreakdown { get; set; } = [];`.

`AssetsPersonnelImmBreakdownDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Report;

public class AssetsPersonnelImmBreakdownDto
{
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public int CompletedTasks { get; set; }
    public int CompletedSetups { get; set; }
    public decimal AvgSetupTime { get; set; }
    public int TotalSetupSeconds { get; set; }
}
```

- [ ] **Step 4: ReportService — общий сбор статистики**

Добавить в класс `ReportService` (рядом с `GetFactoryTimeZoneIdAsync`):

```csharp
    // Строка задания наладчика для отчётов по персоналу (ARCH-03): задание завершено в периоде
    // и/или наладка завершена в периоде (тогда — её длительность). Наладка относится к периоду
    // своего завершения целиком; наладчик — PersonnelId задания (после начала наладки не меняется).
    private sealed record PersonnelTaskRow(string PersonnelId, Guid ImmId, bool CompletedInPeriod, int? SetupSeconds);

    private readonly record struct PersonnelStats(int CompletedTasks, int CompletedSetups, int TotalSetupSeconds, decimal AvgSetupSeconds)
    {
        public static PersonnelStats Of(IEnumerable<PersonnelTaskRow> rows)
        {
            var list = rows.ToList();
            var setups = list.Where(r => r.SetupSeconds.HasValue).Select(r => r.SetupSeconds!.Value).ToList();
            var total = setups.Sum();
            return new PersonnelStats(list.Count(r => r.CompletedInPeriod), setups.Count, total,
                setups.Count > 0 ? (decimal)total / setups.Count : 0m);
        }
    }

    private async Task<List<PersonnelTaskRow>> LoadPersonnelTaskRowsAsync(
        List<string> personnelIds, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var completed = Wintime.Control.Core.Enums.TaskStatus.Completed;
        var rows = await _context.ShiftTasks
            .Where(t => t.PersonnelId != null && personnelIds.Contains(t.PersonnelId)
                && ((t.Status == completed && t.CompletedAt >= fromUtc && t.CompletedAt < toUtc)
                    || (t.SetupStartedAt != null && t.StartedAt >= fromUtc && t.StartedAt < toUtc)))
            .Select(t => new { t.PersonnelId, t.ImmId, t.Status, t.CompletedAt, t.SetupStartedAt, t.StartedAt })
            .ToListAsync(ct);

        return rows.Select(r => new PersonnelTaskRow(
            r.PersonnelId!,
            r.ImmId,
            r.Status == completed && r.CompletedAt >= fromUtc && r.CompletedAt < toUtc,
            r.SetupStartedAt != null && r.StartedAt >= fromUtc && r.StartedAt < toUtc
                ? Math.Max(0, (int)(r.StartedAt!.Value - r.SetupStartedAt.Value).TotalSeconds)
                : null)).ToList();
    }
```

- [ ] **Step 5: ReportService — ветка `Personnel`**

Заменить тело `else if (reportType.Equals("Personnel", …))` (от `var personnel = …` до закрывающей `}` ветки):

```csharp
            var personnel = await _context.Users
                .Where(u => u.IsActive && u.Role == UserRole.Adjuster)
                .ToListAsync(ct);

            var rows = await LoadPersonnelTaskRowsAsync(personnel.Select(p => p.Id).ToList(), dateFromUtc, periodEnd, ct);

            report.PersonnelData = [.. personnel.Select(person =>
            {
                var s = PersonnelStats.Of(rows.Where(r => r.PersonnelId == person.Id));
                return new AssetsPersonnelItemDto
                {
                    PersonnelId = person.Id,
                    FullName = person.FullName,
                    CompletedTasks = s.CompletedTasks,
                    CompletedSetups = s.CompletedSetups,
                    AvgSetupTime = s.AvgSetupSeconds,
                    TotalSetupSeconds = s.TotalSetupSeconds
                };
            })];
```

- [ ] **Step 6: ReportService — ветка `PersonnelByImm`**

Заменить тело `else if (reportType.Equals("PersonnelByImm", …))` целиком:

```csharp
            var personnel = await _context.Users
                .Where(u => u.IsActive && u.Role == UserRole.Adjuster)
                .ToListAsync(ct);

            var immNameLookup = await _context.Imms.ToDictionaryAsync(i => i.Id, i => i.Name, ct);

            var rows = await LoadPersonnelTaskRowsAsync(personnel.Select(p => p.Id).ToList(), dateFromUtc, periodEnd, ct);

            report.PersonnelByImmData = [.. personnel.Select(person =>
            {
                var personRows = rows.Where(r => r.PersonnelId == person.Id).ToList();
                var total = PersonnelStats.Of(personRows);

                var breakdown = personRows
                    .GroupBy(r => r.ImmId)
                    .Select(g =>
                    {
                        var s = PersonnelStats.Of(g);
                        return new AssetsPersonnelImmBreakdownDto
                        {
                            ImmId = g.Key,
                            ImmName = immNameLookup.GetValueOrDefault(g.Key, g.Key.ToString()),
                            CompletedTasks = s.CompletedTasks,
                            CompletedSetups = s.CompletedSetups,
                            AvgSetupTime = s.AvgSetupSeconds,
                            TotalSetupSeconds = s.TotalSetupSeconds
                        };
                    })
                    .OrderBy(r => r.ImmName)
                    .ToList();

                return new AssetsPersonnelByImmItemDto
                {
                    PersonnelId = person.Id,
                    FullName = person.FullName,
                    CompletedTasks = total.CompletedTasks,
                    CompletedSetups = total.CompletedSetups,
                    AvgSetupTime = total.AvgSetupSeconds,
                    TotalSetupSeconds = total.TotalSetupSeconds,
                    ImmBreakdown = breakdown
                };
            })];
```

(Имена ТПА — из всех ТПА, включая архивные: история наладок на архивном ТПА сохраняется — инвариант `IsActive`.)

- [ ] **Step 7: ReportService — Excel**

В `ExportToExcelAsync`, блок `PersonnelData` (строки ~810-833):

```csharp
                var personnelHeaders = new[] { "Сотрудник", "Выполнено задач", "Выполнено наладок", "Ср. время наладки (мин)", "Время наладки (ч)" };
```

и строки данных:

```csharp
                    worksheet.Cell(row, 1).Value = item.FullName;
                    worksheet.Cell(row, 2).Value = item.CompletedTasks;
                    worksheet.Cell(row, 3).Value = item.CompletedSetups;
                    worksheet.Cell(row, 4).Value = Math.Round(item.AvgSetupTime / 60m, 1);
                    worksheet.Cell(row, 5).Value = Math.Round(item.TotalSetupSeconds / 3600m, 2);
```

Блок `PersonnelByImmData` (строки ~835-866): заголовки `{ "Наладчик / ТПА", "Выполнено задач", "Выполнено наладок", "Ср. время наладки (мин)", "Время наладки (ч)" }`; строка наладчика и строка ТПА — те же 5 колонок (`person.*` / `imm.*`) по образцу выше.

- [ ] **Step 8: Удалить `ShiftWorkCalculator`**

```bash
git rm Wintime.Control.Infrastructure/Reports/ShiftWorkCalculator.cs Wintime.Control.Tests.Unit/Reports/ShiftWorkCalculatorTests.cs
```

Run: `grep -rn "ShiftWorkCalculator\|ShiftInstance\|TotalWorkSeconds\|WorkedShifts" --include=*.cs . | grep -v "/obj/\|/bin/"`
Expected: пусто.

- [ ] **Step 9: Run tests**

Run: `dotnet build` → Expected: `Build succeeded`.
Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~Reports` → Expected: PASS.
Run: `dotnet test Wintime.Control.Tests.Unit` → Expected: PASS.

- [ ] **Step 10: Фронт — `AssetsReportView.vue`**

Таблица «Наладчики» (строки 119-137) — колонки:

```vue
        <el-table-column prop="fullName" label="Наладчик" min-width="200" />
        <el-table-column prop="completedTasks" label="Выполнено заданий" width="140" align="center" />
        <el-table-column prop="completedSetups" label="Выполнено наладок" width="140" align="center" />
        <el-table-column prop="avgSetupTime" label="Ср. время наладки (мин)" width="160" align="center">
          <template #default="{ row }">
            {{ row.completedSetups ? (row.avgSetupTime / 60).toFixed(1) : '—' }}
          </template>
        </el-table-column>
        <el-table-column label="Общее время наладки (ч)" width="160" align="center">
          <template #default="{ row }">
            {{ row.totalSetupSeconds ? (row.totalSetupSeconds / 3600).toFixed(1) : '—' }}
          </template>
        </el-table-column>
```

Таблица «Наладчики по ТПА»: в развороте строки ТПА заменить
`<div class="text-center" style="width: 150px">{{ (imm.totalWorkSeconds / 3600).toFixed(2) }}</div>` на
`<div class="text-center" style="width: 150px">{{ imm.completedSetups }}</div>`;
в колонках заменить `workedShifts` («Отработано смен») и «Время работы (ч)» одной колонкой:

```vue
        <el-table-column prop="completedSetups" label="Выполнено наладок" width="150" align="center" />
```

(ширины разворота: заданий 150 / наладок 150 / ср. наладка 170 / общее 160 — совпадают с колонками).

Run: `cd Wintime-Control-Frontend && grep -n "workedShifts\|totalWorkSeconds" -r src` → Expected: пусто.
Run: `cd Wintime-Control-Frontend && npx vitest run && npm run build` → Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add -A Wintime.Control.Core/DTOs/Report Wintime.Control.Infrastructure/Reports Wintime.Control.Tests.Unit/Reports Wintime.Control.Tests.Integration/Reports/PersonnelReportTests.cs Wintime-Control-Frontend/src/views/reports/AssetsReportView.vue
git commit -m "feat(arch-03): personnel reports — setups from task intervals; drop worked shifts and work time

Setup time/avg were always 0 (read never-created Setup events).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: `EventType` — оставить только `Downtime`; комментарий `Close`

**Files:**
- Modify: `Wintime.Control.Core/Enums/EventType.cs`
- Modify: `Wintime.Control.Core/Entities/ShiftTask.cs:191` (XML-doc `Close`)

- [ ] **Step 1: Enum**

`Wintime.Control.Core/Enums/EventType.cs`:

```csharp
namespace Wintime.Control.Core.Enums;

/// <summary>
/// Тип события. Создаются только простои (ручные — DowntimeController, авто — DowntimeDetectionWorker).
/// Наладка — интервал задания (SetupStartedAt…StartedAt), авария — статус ТПА, циклы — ImmCycle.
/// Колонка хранится как int; новые типы добавлять осознанно, со значением > 0.
/// </summary>
public enum EventType
{
    Downtime = 0
}
```

- [ ] **Step 2: Комментарий `Close`**

`ShiftTask.cs:191`: `/// <summary>Закрыть задание (ручное закрытие в конце дня): любой статус → Closed.</summary>` →

```csharp
    /// <summary>Закрыть задание вручную (досрочно или после завершения): любой статус → Closed.</summary>
```

- [ ] **Step 3: Build & tests**

Run: `dotnet build` → Expected: `Build succeeded` (ссылки на `EventType.Setup` удалены в Task 7).
Run: `grep -rn "EventType\.\(Setup\|Alarm\|CycleComplete\|CycleAborted\)" --include=*.cs . | grep -v "/obj/\|/bin/\|Migrations"` → Expected: пусто.
Run: `dotnet test Wintime.Control.Tests.Unit && dotnet test Wintime.Control.Tests.Integration` → Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add Wintime.Control.Core/Enums/EventType.cs Wintime.Control.Core/Entities/ShiftTask.cs
git commit -m "refactor(arch-03): EventType keeps only Downtime; fix Close doc

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: ADR-0016, CLAUDE.md, доменная документация, PR

**Files:**
- Create: `docs/adr/0016-task-longer-than-shift.md`
- Modify: `docs/adr/README.md` (индекс, если в нём есть список ADR)
- Modify: `CLAUDE.md`
- Modify: `docs/domain/shift-task.md`

- [ ] **Step 1: ADR**

`docs/adr/0016-task-longer-than-shift.md`:

```markdown
# ADR-0016: Задание длиннее смены — окно активности и правила отнесения к периоду

- **Статус:** Accepted
- **Дата:** 2026-10-13
- **Задача:** ARCH-03

## Контекст

Код отчётов писался в предположении «задание короче смены»: задание относилось к суткам/смене по
одной точке (`StartedAt`, в карточке ТПА — `IssuedAt`). Большая партия идёт через несколько смен и
суток, а с планированием (PZP-07) это станет нормой. На вторые сутки задания «Рабочий день»
показывал пустую ПФ и нулевой план, наладчику прогон на 9 смен засчитывался одной сменой, карточка
ТПА не видела задание, выданное вчера. Выпуск, циклы и статусы уже считались по времени корректно.

## Решение

- **Модель A** (2026-07-23): задание — непрерывный прогон, смена/сутки — ось отчётности (проекция).
- **Окно активности** задания `[SetupStartedAt, CompletedAt ?? ClosedAt ?? now)`, окно выпуска —
  от `StartedAt`. Задание относится к периоду, если окно активности пересекается с ним. Одно
  определение — `TaskActivityWindow` (`Core/Policies`); в EF — только через `OverlapsPeriod`.
- **Фильтры `GET /api/tasks`** — `issued*` (момент выдачи), `planned*` (плановая дата), `active*`
  (окно активности); объединяются через И. Без `status` архив исключается, кроме `active*`.
- **План в «Рабочем дне»** — нормативный план за период `min(P, N(b)) − min(P, N(a))`,
  `N(t) = ⌊(min(t,E) − S) / эталон⌋ × гнёзда` (`TaskPeriodPlan`). Простои не вычитаются.
- **Расход сырья** — по ПФ каждого цикла (`ImmCycle.MoldId`, `ImmCycle.Cavities`).
- **Наладчики:** наладка — из интервала задания `SetupStartedAt…StartedAt`, относится к периоду
  своего завершения целиком; «Отработано смен» и «Время работы» удалены — рабочее время персонала
  ведёт СКУД, присутствие в сменах — будущий журнал входов (BL-26).
- `EventType` сокращён до `Downtime`: остальные значения никогда не создавались, а запросы к ним
  молча давали 0 (время наладки в отчётах было всегда 0).

## Отвергнутые альтернативы

- **Модель B** — цепочка сменных заданий + партия: дробит единицу работы, требует сущности партии.
- **Отнесение по одной точке** (`StartedAt`/`IssuedAt`) — исходный дефект.
- **Начало окна = `IssuedAt`** — выдача не означает работы на ТПА: задания выдают заранее.
- **План всей партии в суточной строке** — несопоставим с фактом за сутки; **убрать план** — потеря колонки.
- **Засчитывать наладчику все смены, которые пересекает задание** — приписывает смены, когда его не было.
- **Отменять умолчание «Активные» любым фильтром периода** — «Выдано за неделю» показывал бы архив;
  **параметр `includeArchived`** — лишний параметр.

## Последствия

- Любой новый отбор заданий по периоду обязан идти через `TaskActivityWindow.OverlapsPeriod`.
- Отчёт «Рабочий день» по-прежнему считает время по сырому `ImmStatus` — перевод на эффективный
  статус вынесен в BL-42.
- По-сменная нарезка выпуска уже есть («Рабочий день» с фильтром смены); по-наладчиковая
  атрибуция присутствия — BL-26.
```

Если `docs/adr/README.md` содержит список ADR — добавить строку `- [ADR-0016](0016-task-longer-than-shift.md) — Задание длиннее смены: окно активности и правила отнесения к периоду` по образцу соседних.

- [ ] **Step 2: CLAUDE.md**

В «Инварианты домена» после раздела «Плановое начало и выдача задания» добавить:

```markdown
### Задание длиннее смены — окно активности

Задание может идти через несколько смен и суток; смена/сутки — только ось отчётности (ADR-0016).
Окно активности `[SetupStartedAt, CompletedAt ?? ClosedAt ?? now)` (окно выпуска — от `StartedAt`)
— `TaskActivityWindow`. Задание относится к периоду **только** по пересечению окна
(`TaskActivityWindow.OverlapsPeriod` в EF), никогда по одной точке (`StartedAt`/`IssuedAt`).
План за период — нормативный (`TaskPeriodPlan`, сумма по дням = плану задания). Наладка относится к
периоду своего завершения. Статусы задания сравнивать только `==` (`Setup = 5` вне порядка ЖЦ).
`GET /api/tasks`: фильтры `issued*`/`planned*`/`active*` через И; без `status` архив скрыт, кроме `active*`.
```

- [ ] **Step 3: `docs/domain/shift-task.md`**

Прочитать файл. Добавить абзац «Задание длиннее смены» со ссылкой на ADR-0016 (окно активности, отнесение к периоду). Удалить/исправить формулировки, предполагающие, что задание укладывается в смену или «закрывается в конце дня».

- [ ] **Step 4: Полный прогон**

Run: `dotnet build && dotnet test Wintime.Control.Tests.Unit && dotnet test Wintime.Control.Tests.Integration`
Expected: Build succeeded, all PASS.
Run: `cd Wintime-Control-Frontend && npx vitest run && npm run build`
Expected: all PASS, сборка без ошибок.

- [ ] **Step 5: Commit**

```bash
git add docs/adr/0016-task-longer-than-shift.md docs/adr/README.md CLAUDE.md docs/domain/shift-task.md
git commit -m "docs(arch-03): ADR-0016, CLAUDE.md rule, domain doc

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 6: PR**

```bash
git push -u origin feature/arch-03-task-shift-decoupling
gh pr create --title "ARCH-03: задание длиннее смены — окно активности, фильтры, отчёты" --body "$(cat <<'EOF'
## Что сделано
- `TaskActivityWindow` — одно определение окна активности задания; `EffectiveStatusHistoryService` переведён на него.
- `GET /api/tasks`: фильтры `issued*` / `planned*` / `active*` (через И); страница «Задания» — три поля периода.
- Карточка ТПА: задания по окну активности, бар от начала наладки (наладка — светлым оттенком).
- «Рабочий день»: все задания периода, нормативный план за период (`TaskPeriodPlan`), расход сырья по ПФ циклов.
- Отчёты по наладчикам: «Выполнено наладок», время наладки из интервалов заданий (раньше было всегда 0); «Отработано смен» и «Время работы» удалены.
- `EventType` — только `Downtime`; удалён `ShiftWorkCalculator`.
- ADR-0016, правило в CLAUDE.md.

Спека: `docs/superpowers/specs/2026-10-13-arch-03-task-shift-decoupling-design.md`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```
