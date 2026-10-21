# PZP-07b — Конфликты плана (ТПА и пресс-форма): план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Модуль планирования подсвечивает конфликты заданий за ТПА и пресс-форму — конфликт плана (красный) и конфликт отклонения (оранжевый) — в бейдже меню, баннере и на барах Гантта, в карточке и списке заданий; ничего не блокирует, кроме предупреждения наладчику о занятой ПФ.

**Architecture:** Чистые функции в Core: `TaskOccupancy` строит отрезки задания (план / факт / прогноз по стадиям А–Е), `PlanConflictDetector` ищет пары с пересечением дольше порога. `IPlanConflictService` (Infrastructure) грузит незавершённые задания, прогнозы (`IReleaseForecastService`) и порог (`PlanningDefaults.ConflictThresholdMinutes`), отдаёт `PlanConflictsDto`. Эндпоинт `GET /api/planning/conflicts` + поле `conflicts` в доске. Фронт: Pinia-стор `planConflicts` с опросом раз в 60 с — источник для бейджа, баннера, карточки, списка, уведомления формы.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql, xUnit + FluentAssertions + Testcontainers; Vue 3, Element Plus, Pinia, Vitest + @vue/test-utils (jsdom).

**Спека:** `docs/superpowers/specs/2026-10-20-pzp-07b-conflicts-design.md`.

## Global Constraints

- Ресурсы: ТПА (`ImmId`) и пресс-форма (`MoldId`, на любом ТПА). Делят оба — две записи (по ТПА и по ПФ).
- Участвуют только `Planned`, `Issued`, `Setup`, `InProgress`. `Completed`/`Closed` не участвуют.
- **Ничего не блокируется**: сохранение, выдача, старт/завершение наладки, завершение выпуска — без новых запретов. Единственное новое 409 — старт наладки с ПФ, которая **сейчас** в `Setup`/`InProgress` на другом ТПА, снимается флагом `confirmMoldBusy: true`.
- Отрезки полуоткрытые `[начало, конец)`. Прогноз — конец **выпуска, без буфера**. Константа ожидаемой задержки `D = 10 мин` (в коде, не настройка).
- Стадии: `Planned` — план `[PS, PE)`; А `Issued`, `now ≤ PS` — план `[PS, PE)`; Б `Issued`, `now > PS` — прогноз `[now+D, now+D+N+R)`; В `Setup`, `now − SetupStartedAt ≤ N` — факт `[SetupStartedAt, now)` + прогноз `[now, SetupStartedAt+N+R)`; Г `Setup`, `now − SetupStartedAt > N` — факт + прогноз `[now, now+D+R)`; Д/Е `InProgress` — факт + прогноз `[now, ReleaseForecast.ForecastAt)` (существующий `ReleaseForecast`, без изменений). `PS` — плановое начало, `PE` — `PlannedEndAt` (с буфером), `N` — норматив наладки, `R` — плановый выпуск.
- Конфликт: суммарное пересечение всех отрезков пары **строго больше** порога. Вид: у обоих заданий только плановые отрезки → `Plan`, иначе → `Deviation`.
- Порог — `PlanningDefaults.ConflictThresholdMinutes`, 0..1440, по умолчанию **20**, правят Admin/Manager; **не** снапшотится в задание.
- Цвета: конфликт плана `#f56c6c` (красный), конфликт отклонения `#e6a23c` (оранжевый). Подписи: «Конфликт плана», «Конфликт отклонения».
- Архив: ТПА/ПФ с заданиями `Planned`/`Issued`/`Setup`/`InProgress` в архив не переводятся (400). `Mold.Cavities ≥ 1`. `PUT /api/tasks/{id}` требует эталон полного цикла.
- Все `DateTime` в EF-запросах — `Kind=Utc`. Время на экране — в поясе браузера.
- Enum-ы в JSON — строки (`JsonStringEnumConverter` в `Program.cs`).
- ADR — **ADR-0022**, на русском (0021 занят «Offline доминирует»).
- Коммиты заканчиваются строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Ветка и PR

- Ветка `feature/pzp-07b-conflicts` (от `epic/pzp-07-planning`, master влит, спека закоммичена). Все задачи — в ней. PR → `epic/pzp-07-planning`.
- VERSION: `1.18.3` → `1.19.0` (Task 11).

## Команды

- Unit: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~<Класс>"`
- Integration (нужен Docker): `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~<Класс>"`
- Vitest: `cd Wintime-Control-Frontend; npx vitest run <путь>`
- Миграция: `dotnet ef migrations add <Name> --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`

## Файлы

**Бэкенд**
- Create `Wintime.Control.Core/Policies/OpenTaskStatuses.cs` — незавершённые статусы.
- Create `Wintime.Control.Core/Policies/TaskOccupancy.cs` — отрезки задания.
- Create `Wintime.Control.Core/Policies/PlanConflictDetector.cs` — пары конфликтов.
- Create `Wintime.Control.Core/DTOs/Planning/PlanConflictsDto.cs` — контракт.
- Create `Wintime.Control.Core/Interfaces/IPlanConflictService.cs`, `Wintime.Control.Infrastructure/Services/PlanConflictService.cs`.
- Create `Wintime.Control.Core/DTOs/Tasks/StartTaskRequestDto.cs`.
- Modify `Wintime.Control.Core/Entities/PlanningDefaults.cs`, `Core/Policies/PlanningNormLimits.cs`, `Core/DTOs/Settings/*`, `Core/Interfaces/IPlanningDefaultsService.cs`, `Infrastructure/Services/PlanningDefaultsService.cs`, `API/Controllers/SettingsController.cs`, `Infrastructure/Data/ControlDbContext.cs` + миграция `AddConflictThreshold`.
- Modify `Wintime.Control.Core/Policies/PlanningBoardCalculator.cs` — прогноз для `Issued`/`Setup`.
- Modify `Wintime.Control.Core/DTOs/Planning/PlanningBoardDto.cs` — `Conflicts`.
- Modify `Wintime.Control.API/Controllers/PlanningController.cs` — `GET conflicts`, конфликты в доске.
- Modify `Wintime.Control.API/Controllers/TasksController.cs` — `StartTask` (`confirmMoldBusy`), `UpdateTask` (`requireFullCycle: true`).
- Modify `Wintime.Control.API/Controllers/ImmController.cs`, `MoldsController.cs` — запрет архива, `Cavities ≥ 1`.
- Modify `Wintime.Control.API/Program.cs` — регистрация `IPlanConflictService`.

**Тесты бэкенда**
- Create `Wintime.Control.Tests.Unit/Policies/TaskOccupancyTests.cs`, `PlanConflictDetectorTests.cs`.
- Modify `Wintime.Control.Tests.Unit/Policies/PlanningBoardCalculatorTests.cs`, `Wintime.Control.Tests.Unit/Entities/PlanningDefaultsTests.cs`.
- Create `Wintime.Control.Tests.Integration/Planning/ArchiveAndValidationGuardsTests.cs`, `PlanConflictsApiTests.cs`, `Wintime.Control.Tests.Integration/Tasks/StartSetupMoldBusyTests.cs`.
- Modify `Wintime.Control.Tests.Integration/Tasks/TaskCycleNormsApiTests.cs`, `Settings/PlanningDefaultsApiTests.cs`, `Settings/PlanningDefaultsInitialStateTests.cs`, `Planning/PlanningBoardTests.cs`.

**Фронтенд**
- Modify `src/api/planning.js` (+`getConflicts`), `src/api/mobile.js` (`startSetup(id, confirmMoldBusy)`), `src/api/settings.js` (без изменений сигнатуры).
- Create `src/utils/planConflicts.js` — подписи, цвета, выбор худшего вида, строки.
- Create `src/stores/planConflicts.js` — опрос и общее состояние.
- Create `src/components/planning/PlanConflictsBadge.vue`, `src/components/planning/PlanConflictsBanner.vue`.
- Modify `src/layouts/DefaultLayout.vue` — старт опроса и бейджи.
- Modify `src/utils/planningBoard.js`, `src/components/planning/GanttTaskPair.vue`, `src/views/planning/PlanningGanttView.vue`.
- Modify `src/views/tasks/TaskDetailModal.vue`, `TasksView.vue`, `TaskFormModal.vue`.
- Modify `src/views/mobile/MobileTasksView.vue`, `src/views/settings/SettingsView.vue`, `src/views/dictionary/ImmDictionary.vue`, `MoldDictionary.vue`.
- Тесты: `src/utils/__tests__/planConflicts.spec.js`, `src/stores/__tests__/planConflicts.spec.js`, `src/components/planning/__tests__/PlanConflictsBadge.spec.js`, `PlanConflictsBanner.spec.js`, modify `GanttTaskPair.spec.js`, `src/utils/__tests__/planningBoard.spec.js`, `src/views/planning/__tests__/PlanningGanttView.spec.js`, `src/views/tasks/__tests__/TaskDetailModal.spec.js`, `TaskFormModal.spec.js`, `src/views/mobile/__tests__/MobileTasksView.spec.js`, `src/views/settings/__tests__/SettingsView.spec.js`.

**Документация**
- Create `docs/adr/0022-plan-conflicts.md`; Modify `docs/adr/README.md`, `CLAUDE.md`, `docs/domain/shift-task.md`, `docs/user-guide/manager.md`, `docs/user-guide/adjuster.md`, `VERSION`.

---

### Task 1: Запрет архива ТПА/ПФ с незавершёнными заданиями, гнёзда ≥ 1, эталон цикла при правке

**Files:**
- Create: `Wintime.Control.Core/Policies/OpenTaskStatuses.cs`
- Modify: `Wintime.Control.API/Controllers/ImmController.cs` (метод `UpdateImm`, ~стр. 296–318)
- Modify: `Wintime.Control.API/Controllers/MoldsController.cs` (`CreateMold` ~стр. 85–120, `UpdateMold` ~стр. 128–175)
- Modify: `Wintime.Control.API/Controllers/TasksController.cs` (`UpdateTask`, вызов `SetCycleNorms` ~стр. 357–366)
- Modify: `Wintime-Control-Frontend/src/views/dictionary/ImmDictionary.vue` (~стр. 264–279), `MoldDictionary.vue` (~стр. 415–430)
- Test: `Wintime.Control.Tests.Integration/Planning/ArchiveAndValidationGuardsTests.cs`
- Modify test: `Wintime.Control.Tests.Integration/Tasks/TaskCycleNormsApiTests.cs` (`UpdateTask_LegacyTaskWithoutFullCycle_SavesWithoutRequiringCycle`)

**Interfaces:**
- Produces: `OpenTaskStatuses.All` (`TaskStatus[]`), `OpenTaskStatuses.Contains(TaskStatus)` — используются в Task 5.

- [ ] **Step 1: Write the failing tests**

Create `Wintime.Control.Tests.Integration/Planning/ArchiveAndValidationGuardsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Planning;

/// <summary>PZP-07b: архив ТПА/ПФ только без незавершённых заданий; гнёзда ≥ 1; эталон цикла обязателен при правке.</summary>
[Collection("Integration")]
public class ArchiveAndValidationGuardsTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ArchiveAndValidationGuardsTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedMoldAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold
        {
            Name = "M-07b", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 2, IsActive = true,
            ProductTypeId = _factory.TestProductTypeId
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold.Id;
    }

    private async Task SeedTaskAsync(Guid immId, Guid moldId, TaskStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ShiftTasks.Add(new ShiftTask
        {
            ImmId = immId, MoldId = moldId, ProductId = _factory.TestProductId, PlanQuantity = 100,
            PlannedFullCycleSeconds = 60, Status = status,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3)
        });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(TaskStatus.Planned)]
    [InlineData(TaskStatus.Issued)]
    [InlineData(TaskStatus.Setup)]
    [InlineData(TaskStatus.InProgress)]
    public async Task Archiving_imm_with_open_task_is_rejected(TaskStatus status)
    {
        var immId = await _factory.CreateFreshImmAsync();
        await SeedTaskAsync(immId, await SeedMoldAsync(), status);
        var client = await ManagerAsync();

        var resp = await client.PutAsJsonAsync($"/api/imm/{immId}", new { isActive = false });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("незавершённые задания");
    }

    [Theory]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Closed)]
    public async Task Archiving_imm_with_only_finished_tasks_is_allowed(TaskStatus status)
    {
        var immId = await _factory.CreateFreshImmAsync();
        await SeedTaskAsync(immId, await SeedMoldAsync(), status);
        var client = await ManagerAsync();

        var resp = await client.PutAsJsonAsync($"/api/imm/{immId}", new { isActive = false });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Archiving_mold_with_open_task_is_rejected()
    {
        var moldId = await SeedMoldAsync();
        await SeedTaskAsync(await _factory.CreateFreshImmAsync(), moldId, TaskStatus.Planned);
        var client = await ManagerAsync();

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}", new { isActive = false });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("незавершённые задания");
    }

    [Fact]
    public async Task Archiving_mold_with_only_finished_tasks_is_allowed()
    {
        var moldId = await SeedMoldAsync();
        await SeedTaskAsync(await _factory.CreateFreshImmAsync(), moldId, TaskStatus.Completed);
        var client = await ManagerAsync();

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}", new { isActive = false });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Creating_mold_with_zero_cavities_is_rejected()
    {
        var client = await ManagerAsync();

        var resp = await client.PostAsJsonAsync("/api/molds", new
        {
            formId = $"F-{Guid.NewGuid():N}", name = "Zero", cavities = 0, productTypeId = _factory.TestProductTypeId
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Updating_mold_to_zero_cavities_is_rejected()
    {
        var moldId = await SeedMoldAsync();
        var client = await ManagerAsync();

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}", new { cavities = 0 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

> Маршрут ТПА — `api/imm` (`[Route("api/[controller]")]` у `ImmController`). Если в проекте он другой — проверь `grep -n "Route" Wintime.Control.API/Controllers/ImmController.cs` и поправь URL в тесте.

Replace the test `UpdateTask_LegacyTaskWithoutFullCycle_SavesWithoutRequiringCycle` in `Wintime.Control.Tests.Integration/Tasks/TaskCycleNormsApiTests.cs` with:

```csharp
    [Fact]
    public async Task UpdateTask_TaskWithoutFullCycle_RequiresCycle()
    {
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var task = new ShiftTask
            {
                ImmId = _factory.TestImmId,
                MoldId = _factory.TestMoldId, ProductId = _factory.TestProductId,
                PlanQuantity = 100,
                Status = TaskStatus.Planned,
                PlannedFullCycleSeconds = null,
                PlannedInjectionCycleSeconds = null
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            id = task.Id;
        }

        var client = await ManagerClientAsync();

        // PZP-07b: legacy-лазейки нет — без эталона полного цикла правка режима отклоняется.
        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { workMode = "SemiAuto" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ArchiveAndValidationGuardsTests|FullyQualifiedName~UpdateTask_TaskWithoutFullCycle_RequiresCycle"`
Expected: FAIL — архив возвращает 204 вместо 400, нулевые гнёзда проходят, правка без эталона даёт 204.

- [ ] **Step 3: Implement**

Create `Wintime.Control.Core/Policies/OpenTaskStatuses.cs`:

```csharp
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Незавершённые статусы задания (PZP-07b): задание ещё держит ТПА и пресс-форму. Такие задания участвуют
/// в конфликтах плана и не дают перевести ТПА/ПФ в архив. Сравнение — только <c>==</c> (Setup = 5 вне порядка ЖЦ).
/// </summary>
public static class OpenTaskStatuses
{
    public static readonly TaskStatus[] All = { TaskStatus.Planned, TaskStatus.Issued, TaskStatus.Setup, TaskStatus.InProgress };

    public static bool Contains(TaskStatus status) =>
        status == TaskStatus.Planned || status == TaskStatus.Issued
        || status == TaskStatus.Setup || status == TaskStatus.InProgress;
}
```

In `ImmController.UpdateImm`, before `if (request.IsActive.HasValue)` add the guard (needs `using Wintime.Control.Core.Policies;`):

```csharp
        // PZP-07b: в архив — только ТПА без незавершённых заданий.
        if (request.IsActive == false && imm.IsActive)
        {
            var open = await _context.ShiftTasks
                .Where(t => t.ImmId == id && OpenTaskStatuses.All.Contains(t.Status))
                .OrderBy(t => t.PlannedDate)
                .Select(t => new { t.PlannedDate, MoldName = t.Mold.Name })
                .Take(5)
                .ToListAsync();
            if (open.Count > 0)
                return BadRequest("Нельзя перевести ТПА в архив: есть незавершённые задания — "
                    + string.Join(", ", open.Select(o => $"ПФ «{o.MoldName}» на {o.PlannedDate:dd.MM.yyyy}")) + ".");
        }
```

In `MoldsController.CreateMold`, right after the FormId uniqueness check add:

```csharp
        if (request.Cavities < 1)
            return BadRequest("Количество гнёзд должно быть не меньше 1.");
```

In `MoldsController.UpdateMold`, right after `if (mold == null) return NotFound();` add:

```csharp
        if (request.Cavities is < 1)
            return BadRequest("Количество гнёзд должно быть не меньше 1.");

        // PZP-07b: в архив — только ПФ без незавершённых заданий.
        if (request.IsActive == false && mold.IsActive)
        {
            var open = await _context.ShiftTasks
                .Where(t => t.MoldId == id && OpenTaskStatuses.All.Contains(t.Status))
                .OrderBy(t => t.PlannedDate)
                .Select(t => new { t.PlannedDate, ImmName = t.Imm.Name })
                .Take(5)
                .ToListAsync();
            if (open.Count > 0)
                return BadRequest("Нельзя перевести пресс-форму в архив: есть незавершённые задания — "
                    + string.Join(", ", open.Select(o => $"ТПА «{o.ImmName}» на {o.PlannedDate:dd.MM.yyyy}")) + ".");
        }
```

(add `using Wintime.Control.Core.Policies;` if missing).

In `TasksController.UpdateTask` replace the cycle-norms block comment and call:

```csharp
        if (cycleFieldsTouched)
        {
            // Не переданные поля сохраняют текущее значение задания. Эталон полного цикла обязателен
            // (PZP-07b: без него не посчитать план, отрезки и конфликты).
            task.SetCycleNorms(
                request.WorkMode ?? task.WorkMode,
                request.PlannedFullCycleSeconds ?? task.PlannedFullCycleSeconds,
                request.PlannedInjectionCycleSeconds ?? task.PlannedInjectionCycleSeconds,
                requireFullCycle: true);
        }
```

In `ImmDictionary.vue` `deleteImm` catch, and `MoldDictionary.vue` `deleteMold` catch, show the server text (import `apiErrorMessage` from `@/utils/apiError`):

```js
    if (error !== 'cancel') {
      ElMessage.error(apiErrorMessage(error, 'Ошибка удаления ТПА'))   // в MoldDictionary: 'Ошибка удаления пресс-формы'
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ArchiveAndValidationGuardsTests|FullyQualifiedName~TaskCycleNormsApiTests"`
Expected: PASS. Then the whole integration suite: `dotnet test Wintime.Control.Tests.Integration` — если какой-то старый тест архивирует ТПА/ПФ с открытыми заданиями или создаёт ПФ с `cavities = 0` через API, поправь данные теста (не правило).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/OpenTaskStatuses.cs Wintime.Control.API/Controllers Wintime.Control.Tests.Integration Wintime-Control-Frontend/src/views/dictionary
git commit -m "feat(planning): no archiving of IMM/mold with open tasks, cavities >= 1, full cycle required on edit (PZP-07b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Порог конфликта в умолчаниях планирования

**Files:**
- Modify: `Wintime.Control.Core/Entities/PlanningDefaults.cs`, `Wintime.Control.Core/Policies/PlanningNormLimits.cs`
- Modify: `Wintime.Control.Core/DTOs/Settings/PlanningDefaultsDto.cs`, `UpdatePlanningDefaultsRequestDto.cs`
- Modify: `Wintime.Control.Core/Interfaces/IPlanningDefaultsService.cs`, `Wintime.Control.Infrastructure/Services/PlanningDefaultsService.cs`, `Wintime.Control.API/Controllers/SettingsController.cs`
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs` (~стр. 191–205) + migration `AddConflictThreshold`
- Modify: `Wintime-Control-Frontend/src/views/settings/SettingsView.vue`
- Test: `Wintime.Control.Tests.Unit/Entities/PlanningDefaultsTests.cs`, `Wintime.Control.Tests.Integration/Settings/PlanningDefaultsApiTests.cs`, `PlanningDefaultsInitialStateTests.cs`, `Wintime-Control-Frontend/src/views/settings/__tests__/SettingsView.spec.js`

**Interfaces:**
- Produces: `PlanningDefaults.ConflictThresholdMinutes` (int, default `PlanningDefaults.DefaultConflictThresholdMinutes = 20`); JSON `conflictThresholdMinutes` в `GET/PUT /api/settings/planning-defaults` (в PUT — необязательный, не передан → без изменений).

- [ ] **Step 1: Write the failing tests**

Append to `Wintime.Control.Tests.Unit/Entities/PlanningDefaultsTests.cs` (inside the class; match existing `using`s — `FluentAssertions`, `Wintime.Control.Core.Entities`, `Wintime.Control.Core.Exceptions`):

```csharp
    [Fact]
    public void Conflict_threshold_defaults_to_20_minutes()
    {
        new PlanningDefaults().ConflictThresholdMinutes.Should().Be(20);
    }

    [Fact]
    public void Update_sets_conflict_threshold_when_given()
    {
        var d = new PlanningDefaults();
        d.Update(0, 0, 0, "u", DateTime.UtcNow, conflictThresholdMinutes: 45);
        d.ConflictThresholdMinutes.Should().Be(45);
    }

    [Fact]
    public void Update_keeps_conflict_threshold_when_omitted()
    {
        var d = new PlanningDefaults();
        d.Update(10, 0, 0, "u", DateTime.UtcNow);
        d.ConflictThresholdMinutes.Should().Be(20);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void Update_rejects_conflict_threshold_out_of_range(int minutes)
    {
        var act = () => new PlanningDefaults().Update(0, 0, 0, "u", DateTime.UtcNow, conflictThresholdMinutes: minutes);
        act.Should().Throw<DomainException>().WithMessage("*Порог конфликта*");
    }
```

In `PlanningDefaultsInitialStateTests.GetPlanningDefaults_Initially_AllZero` add after the percent assertion:

```csharp
        d.GetProperty("conflictThresholdMinutes").GetInt32().Should().Be(20);
```

Append to `Wintime.Control.Tests.Integration/Settings/PlanningDefaultsApiTests.cs` (reuse its client helper; if the class has none, copy `ManagerAsync` from Task 1):

```csharp
    [Fact]
    public async Task Put_sets_conflict_threshold_and_omitting_it_keeps_value()
    {
        var client = await ManagerAsync();

        var r1 = await client.PutAsJsonAsync("/api/settings/planning-defaults",
            new { setupMinutes = 0, releaseBufferFixedMinutes = 0, releaseBufferPercent = 0, conflictThresholdMinutes = 35 });
        r1.StatusCode.Should().Be(HttpStatusCode.OK);

        var r2 = await client.PutAsJsonAsync("/api/settings/planning-defaults",
            new { setupMinutes = 5, releaseBufferFixedMinutes = 0, releaseBufferPercent = 0 });
        var body = await r2.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("conflictThresholdMinutes").GetInt32().Should().Be(35);
    }

    [Fact]
    public async Task Put_conflict_threshold_out_of_range_is_400()
    {
        var client = await ManagerAsync();

        var resp = await client.PutAsJsonAsync("/api/settings/planning-defaults",
            new { setupMinutes = 0, releaseBufferFixedMinutes = 0, releaseBufferPercent = 0, conflictThresholdMinutes = 2000 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
```

In `SettingsView.spec.js`: add `conflictThresholdMinutes: 25` to the mocked `getPlanningDefaults` data; add the test below; in the existing test «Сохранить» отправляет только три значения — rename to «отправляет четыре значения» and add `conflictThresholdMinutes: 25` to the expected payload.

```js
  it('показывает и сохраняет порог конфликта', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.find('[data-test="conflict-threshold"] input').element.value).toBe('25')

    wrapper.vm.defaults.conflictThresholdMinutes = 40
    await wrapper.find('[data-test="save"]').trigger('click')
    await flushPromises()

    expect(settingsApi.updatePlanningDefaults).toHaveBeenCalledWith(
      expect.objectContaining({ conflictThresholdMinutes: 40 }))
  })
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~PlanningDefaultsTests"` → FAIL (no `ConflictThresholdMinutes`).
Run: `cd Wintime-Control-Frontend; npx vitest run src/views/settings` → FAIL.

- [ ] **Step 3: Implement**

`PlanningNormLimits.cs` — add:

```csharp
    public static void EnsureConflictThreshold(int minutes)
    {
        if (minutes is < 0 or > MaxMinutes)
            throw new DomainException("Порог конфликта должен быть от 0 до 1440 минут");
    }
```

`PlanningDefaults.cs`:

```csharp
    /// <summary>
    /// Порог конфликта плана, мин (PZP-07b, ADR-0022): пересечение двух заданий длиной не больше порога — не конфликт.
    /// В отличие от норматива и буфера, не снапшотится в задание — действует глобально и сразу.
    /// </summary>
    public const int DefaultConflictThresholdMinutes = 20;

    public int ConflictThresholdMinutes { get; set; } = DefaultConflictThresholdMinutes;

    public void Update(int setupMinutes, int bufferFixedMinutes, int bufferPercent,
                       string? userId, DateTime nowUtc, int? conflictThresholdMinutes = null)
    {
        PlanningNormLimits.EnsureSetupMinutes(setupMinutes);
        PlanningNormLimits.EnsureBuffer(bufferFixedMinutes, bufferPercent);
        if (conflictThresholdMinutes is int threshold)
            PlanningNormLimits.EnsureConflictThreshold(threshold);

        SetupMinutes = setupMinutes;
        ReleaseBufferFixedMinutes = bufferFixedMinutes;
        ReleaseBufferPercent = bufferPercent;
        if (conflictThresholdMinutes is int t)
            ConflictThresholdMinutes = t;
        UpdatedAt = nowUtc;
        UpdatedById = userId;
    }
```

`PlanningDefaultsDto` — add `public int ConflictThresholdMinutes { get; set; }`.
`UpdatePlanningDefaultsRequestDto` — add `/// <summary>Не передан — без изменений.</summary> public int? ConflictThresholdMinutes { get; set; }`.

`IPlanningDefaultsService.UpdateAsync` → `Task<PlanningDefaults> UpdateAsync(int setupMinutes, int bufferFixedMinutes, int bufferPercent, string? userId, int? conflictThresholdMinutes = null, CancellationToken ct = default);` and in the service pass it: `defaults.Update(setupMinutes, bufferFixedMinutes, bufferPercent, userId, DateTime.UtcNow, conflictThresholdMinutes);`. Check other callers: `grep -rn "UpdateAsync(" Wintime.Control.* --include=*.cs | grep -i planning` — a call passing `ct` positionally must become `ct: ct`.

`SettingsController`: pass `request.ConflictThresholdMinutes` to `UpdateAsync`, map `ConflictThresholdMinutes = d.ConflictThresholdMinutes` in `ToDto`. Update class summary: «умолчания планирования (PZP-14) и порог конфликта (PZP-07b)».

`ControlDbContext` (PlanningDefaults block): add check constraint and seed value:

```csharp
                t.HasCheckConstraint("CK_PlanningDefaults_ConflictThresholdMinutes", "\"ConflictThresholdMinutes\" BETWEEN 0 AND 1440");
            ...
            entity.HasData(new PlanningDefaults { Id = 1, ConflictThresholdMinutes = PlanningDefaults.DefaultConflictThresholdMinutes });
```

Run: `dotnet ef migrations add AddConflictThreshold --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`.
Open the generated migration: it must contain `AddColumn<int>("ConflictThresholdMinutes", …)`, the check constraint, and `UpdateData` for `Id = 1` setting `20`. If `UpdateData` is missing, add at the end of `Up`:

```csharp
            migrationBuilder.Sql("UPDATE \"PlanningDefaults\" SET \"ConflictThresholdMinutes\" = 20 WHERE \"Id\" = 1;");
```

`SettingsView.vue` — add `conflictThresholdMinutes: 20` to `defaults`, load and save it (`conflictThresholdMinutes: defaults.conflictThresholdMinutes` in the PUT body), and a second card before the buttons:

```vue
    <el-card class="mb-6">
      <template #header>
        <span class="font-semibold">Конфликты плана</span>
      </template>
      <p class="text-sm text-gray-600 mb-4">
        Пересечение двух заданий по ТПА или пресс-форме не дольше порога конфликтом не считается.
        Действует сразу для всех заданий.
      </p>
      <el-form :model="defaults" label-width="340px" label-position="left">
        <el-form-item label="Порог конфликта, мин">
          <el-input-number
            v-model="defaults.conflictThresholdMinutes"
            data-test="conflict-threshold" :value-on-clear="0"
            :min="0" :max="1440" :step="5"
            controls-position="right"
          />
        </el-form-item>
      </el-form>
    </el-card>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~PlanningDefaultsTests"`; `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~PlanningDefaults"`; `cd Wintime-Control-Frontend; npx vitest run src/views/settings`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core Wintime.Control.Infrastructure Wintime.Control.API Wintime.Control.Tests.Unit Wintime.Control.Tests.Integration Wintime-Control-Frontend/src/views/settings
git commit -m "feat(settings): conflict threshold in planning defaults, 20 min by default (PZP-07b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `TaskOccupancy` — отрезки задания по стадиям

**Files:**
- Create: `Wintime.Control.Core/Policies/TaskOccupancy.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/TaskOccupancyTests.cs`

**Interfaces:**
- Consumes: `TaskPlan` (`SetupSeconds`, `ReleaseSeconds?`, `BufferSeconds?`, `PlannedEndAt?`), `ReleaseForecastResult` (`RatePerHour?`, `ForecastAt?`) — существуют.
- Produces:
  - `enum OccupancyKind { Plan, Fact, Forecast }`
  - `record OccupancySegment(OccupancyKind Kind, DateTime Start, DateTime End)`
  - `record OccupancyFacts(TaskStatus Status, DateTime? SetupStartedAt)`
  - `static class TaskOccupancy { static readonly TimeSpan ExpectedDelay; static IReadOnlyList<OccupancySegment> Build(DateTime plannedStartUtc, TaskPlan plan, OccupancyFacts facts, ReleaseForecastResult? forecast, DateTime nowUtc) }`

- [ ] **Step 1: Write the failing tests**

Create `Wintime.Control.Tests.Unit/Policies/TaskOccupancyTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Policies;

public class TaskOccupancyTests
{
    // План: начало 10:00, наладка 30 мин, выпуск 60 мин, буфер 10 мин → окончание 11:40
    private static readonly DateTime Start = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TaskPlan Plan = new(1800, 3600, 600, Start.AddMinutes(100));
    private static DateTime At(int minutes) => Start.AddMinutes(minutes);

    private static IReadOnlyList<OccupancySegment> Build(TaskStatus status, DateTime now, DateTime? setupStarted = null,
                                                         ReleaseForecastResult? forecast = null) =>
        TaskOccupancy.Build(Start, Plan, new OccupancyFacts(status, setupStarted), forecast, now);

    private static OccupancySegment Seg(OccupancyKind kind, int from, int to) => new(kind, At(from), At(to));

    [Fact]
    public void Planned_is_plan_window_with_buffer_even_when_overdue()
    {
        Build(TaskStatus.Planned, At(300)).Should().Equal(Seg(OccupancyKind.Plan, 0, 100));
    }

    [Fact]
    public void Issued_before_planned_start_is_plan_window_case_A()
    {
        Build(TaskStatus.Issued, At(-60)).Should().Equal(Seg(OccupancyKind.Plan, 0, 100));
    }

    [Fact]
    public void Issued_exactly_at_planned_start_is_still_plan_window()
    {
        Build(TaskStatus.Issued, At(0)).Should().Equal(Seg(OccupancyKind.Plan, 0, 100));
    }

    [Fact]
    public void Issued_after_planned_start_forecasts_from_now_plus_delay_without_buffer_case_B()
    {
        // now 10:20 → старт наладки 10:30, наладка 30 + выпуск 60 → 12:00
        Build(TaskStatus.Issued, At(20)).Should().Equal(Seg(OccupancyKind.Forecast, 30, 120));
    }

    [Fact]
    public void Setup_within_norm_forecasts_from_actual_setup_start_case_V()
    {
        // наладка с 10:05, сейчас 10:25 (20 ≤ 30) → выпуск кончится 10:05 + 30 + 60 = 11:35
        Build(TaskStatus.Setup, At(25), setupStarted: At(5)).Should().Equal(
            Seg(OccupancyKind.Fact, 5, 25), Seg(OccupancyKind.Forecast, 25, 95));
    }

    [Fact]
    public void Setup_exactly_at_norm_is_case_V()
    {
        Build(TaskStatus.Setup, At(35), setupStarted: At(5)).Should().Equal(
            Seg(OccupancyKind.Fact, 5, 35), Seg(OccupancyKind.Forecast, 35, 95));
    }

    [Fact]
    public void Setup_over_norm_forecasts_now_plus_delay_plus_release_case_G()
    {
        // наладка с 10:05, сейчас 10:50 (45 > 30) → 10:50 + 10 + 60 = 12:00
        Build(TaskStatus.Setup, At(50), setupStarted: At(5)).Should().Equal(
            Seg(OccupancyKind.Fact, 5, 50), Seg(OccupancyKind.Forecast, 50, 120));
    }

    [Fact]
    public void InProgress_uses_release_forecast_cases_D_E()
    {
        Build(TaskStatus.InProgress, At(60), setupStarted: At(-5), forecast: new ReleaseForecastResult(100m, At(130)))
            .Should().Equal(Seg(OccupancyKind.Fact, -5, 60), Seg(OccupancyKind.Forecast, 60, 130));
    }

    [Fact]
    public void InProgress_with_plan_met_has_only_fact()
    {
        Build(TaskStatus.InProgress, At(60), setupStarted: At(0), forecast: new ReleaseForecastResult(100m, null))
            .Should().Equal(Seg(OccupancyKind.Fact, 0, 60));
    }

    [Fact]
    public void Early_setup_start_moves_fact_left_of_plan()
    {
        Build(TaskStatus.Setup, At(-10), setupStarted: At(-40)).First().Should().Be(Seg(OccupancyKind.Fact, -40, -10));
    }

    [Theory]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Closed)]
    public void Finished_tasks_occupy_nothing(TaskStatus status)
    {
        Build(status, At(60), setupStarted: At(0)).Should().BeEmpty();
    }

    [Fact]
    public void No_release_plan_occupies_setup_only()
    {
        var plan = new TaskPlan(1800, null, null, null);
        TaskOccupancy.Build(Start, plan, new OccupancyFacts(TaskStatus.Planned, null), null, At(-60))
            .Should().Equal(Seg(OccupancyKind.Plan, 0, 30));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~TaskOccupancyTests"`
Expected: FAIL to compile — `TaskOccupancy` not defined.

- [ ] **Step 3: Implement**

Create `Wintime.Control.Core/Policies/TaskOccupancy.cs`:

```csharp
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Core.Policies;

/// <summary>Вид отрезка занятости: план, факт или прогноз (PZP-07b).</summary>
public enum OccupancyKind { Plan, Fact, Forecast }

/// <summary>Отрезок занятости ТПА/ПФ заданием, UTC, полуоткрытый <c>[Start, End)</c>.</summary>
public sealed record OccupancySegment(OccupancyKind Kind, DateTime Start, DateTime End);

/// <summary>Статус задания и фактическое начало наладки — всё, что нужно отрезкам кроме плана и прогноза.</summary>
public sealed record OccupancyFacts(TaskStatus Status, DateTime? SetupStartedAt);

/// <summary>
/// Отрезки занятости задания для детекции конфликтов (PZP-07b, ADR-0022). Чистая функция, не хранится.
/// Planned и Issued до планового начала — план <c>[PS, PE)</c> с буфером; дальше — факт <c>[SetupStartedAt, now)</c>
/// и прогноз конца выпуска без буфера по стадии: Б — не начатое просроченное, В/Г — наладка в нормативе / сверх,
/// Д/Е — выпуск (<see cref="ReleaseForecast"/>). При опоздании считаем, что наладка начнётся/завершится через
/// <see cref="ExpectedDelay"/>. Completed/Closed ничего не занимают.
/// </summary>
public static class TaskOccupancy
{
    public static readonly TimeSpan ExpectedDelay = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<OccupancySegment> Build(DateTime plannedStartUtc, TaskPlan plan, OccupancyFacts facts,
                                                        ReleaseForecastResult? forecast, DateTime nowUtc)
    {
        var setup = TimeSpan.FromSeconds(plan.SetupSeconds);
        var release = TimeSpan.FromSeconds(plan.ReleaseSeconds ?? 0);
        var planEnd = plan.PlannedEndAt ?? Add(plannedStartUtc, setup);
        var segments = new List<OccupancySegment>(2);

        if (facts.Status == TaskStatus.Planned
            || (facts.Status == TaskStatus.Issued && nowUtc <= plannedStartUtc))
        {
            Push(segments, OccupancyKind.Plan, plannedStartUtc, planEnd);                     // Planned, А
        }
        else if (facts.Status == TaskStatus.Issued)
        {
            var start = Add(nowUtc, ExpectedDelay);                                           // Б
            Push(segments, OccupancyKind.Forecast, start, Add(Add(start, setup), release));
        }
        else if (facts.Status == TaskStatus.Setup && facts.SetupStartedAt is DateTime setupStarted)
        {
            Push(segments, OccupancyKind.Fact, setupStarted, nowUtc);
            var releaseEnd = nowUtc - setupStarted <= setup
                ? Add(Add(setupStarted, setup), release)                                      // В
                : Add(Add(nowUtc, ExpectedDelay), release);                                   // Г
            Push(segments, OccupancyKind.Forecast, nowUtc, releaseEnd);
        }
        else if (facts.Status == TaskStatus.InProgress && facts.SetupStartedAt is DateTime started)
        {
            Push(segments, OccupancyKind.Fact, started, nowUtc);
            if (forecast?.ForecastAt is DateTime forecastAt)                                  // Д, Е
                Push(segments, OccupancyKind.Forecast, nowUtc, forecastAt);
        }

        return segments;
    }

    private static void Push(List<OccupancySegment> segments, OccupancyKind kind, DateTime start, DateTime end)
    {
        if (end > start)
            segments.Add(new OccupancySegment(kind, start, end));
    }

    // Защита от выхода за DateTime.MaxValue на абсурдных планах.
    private static DateTime Add(DateTime at, TimeSpan span) =>
        span >= DateTime.MaxValue - at ? DateTime.MaxValue : at + span;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~TaskOccupancyTests"`
Expected: PASS (все тесты класса).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/TaskOccupancy.cs Wintime.Control.Tests.Unit/Policies/TaskOccupancyTests.cs
git commit -m "feat(planning): task occupancy segments — plan, fact, staged forecast A–F (PZP-07b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: `PlanConflictDetector` — пары конфликтов

**Files:**
- Create: `Wintime.Control.Core/Policies/PlanConflictDetector.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/PlanConflictDetectorTests.cs`

**Interfaces:**
- Consumes: `OccupancySegment`, `OccupancyKind` (Task 3).
- Produces:
  - `enum ConflictResource { Imm, Mold }`, `enum ConflictKind { Plan, Deviation }`
  - `record ConflictTaskInput(Guid TaskId, Guid ImmId, Guid MoldId, IReadOnlyList<OccupancySegment> Segments)`
  - `record PlanConflict(Guid TaskId, Guid OtherTaskId, ConflictResource Resource, ConflictKind Kind, DateTime OverlapStart, DateTime OverlapEnd, long OverlapSeconds)`
  - `static IReadOnlyList<PlanConflict> PlanConflictDetector.Detect(IReadOnlyCollection<ConflictTaskInput> tasks, TimeSpan threshold)` — порядок: `Plan` раньше `Deviation`, затем по `OverlapStart`; `TaskId` — задание с более ранним первым отрезком.

- [ ] **Step 1: Write the failing tests**

Create `Wintime.Control.Tests.Unit/Policies/PlanConflictDetectorTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class PlanConflictDetectorTests
{
    private static readonly DateTime T0 = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(20);
    private static readonly Guid ImmA = Guid.NewGuid(), ImmB = Guid.NewGuid();
    private static readonly Guid MoldX = Guid.NewGuid(), MoldY = Guid.NewGuid();

    private static OccupancySegment S(OccupancyKind kind, int from, int to) => new(kind, T0.AddMinutes(from), T0.AddMinutes(to));
    private static ConflictTaskInput Task(Guid imm, Guid mold, params OccupancySegment[] segs) => new(Guid.NewGuid(), imm, mold, segs);

    [Fact]
    public void Plan_vs_plan_on_same_imm_over_threshold_is_plan_conflict()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmA, MoldY, S(OccupancyKind.Plan, 60, 160));

        var r = PlanConflictDetector.Detect(new[] { b, a }, Threshold);

        r.Should().ContainSingle().Which.Should().Be(new PlanConflict(a.TaskId, b.TaskId, ConflictResource.Imm,
            ConflictKind.Plan, T0.AddMinutes(60), T0.AddMinutes(100), 40 * 60));
    }

    [Fact]
    public void Overlap_equal_to_threshold_is_not_a_conflict()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmA, MoldY, S(OccupancyKind.Plan, 80, 160));

        PlanConflictDetector.Detect(new[] { a, b }, Threshold).Should().BeEmpty();
    }

    [Fact]
    public void Overlap_one_second_over_threshold_is_a_conflict()
    {
        var a = Task(ImmA, MoldX, new OccupancySegment(OccupancyKind.Plan, T0, T0.AddMinutes(100)));
        var b = Task(ImmA, MoldY, new OccupancySegment(OccupancyKind.Plan, T0.AddMinutes(80).AddSeconds(-1), T0.AddMinutes(160)));

        PlanConflictDetector.Detect(new[] { a, b }, Threshold).Should().ContainSingle();
    }

    [Fact]
    public void Touching_segments_are_not_a_conflict_even_with_zero_threshold()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmA, MoldY, S(OccupancyKind.Plan, 100, 160));

        PlanConflictDetector.Detect(new[] { a, b }, TimeSpan.Zero).Should().BeEmpty();
    }

    [Theory]
    [InlineData(OccupancyKind.Fact)]
    [InlineData(OccupancyKind.Forecast)]
    public void Any_fact_or_forecast_in_pair_makes_deviation_conflict(OccupancyKind kind)
    {
        var running = Task(ImmA, MoldX, S(OccupancyKind.Fact, 0, 50), S(OccupancyKind.Forecast, 50, 120));
        var planned = Task(ImmA, MoldY, S(OccupancyKind.Plan, kind == OccupancyKind.Fact ? 10 : 70, 200));

        PlanConflictDetector.Detect(new[] { running, planned }, Threshold)
            .Should().ContainSingle().Which.Kind.Should().Be(ConflictKind.Deviation);
    }

    [Fact]
    public void Overlap_is_summed_over_all_segment_pairs()
    {
        // факт 0..50 и прогноз 50..70 против плана 40..200 → 10 + 20 = 30 мин > 20
        var running = Task(ImmA, MoldX, S(OccupancyKind.Fact, 0, 50), S(OccupancyKind.Forecast, 50, 70));
        var planned = Task(ImmA, MoldY, S(OccupancyKind.Plan, 40, 200));

        var c = PlanConflictDetector.Detect(new[] { running, planned }, Threshold).Single();

        c.OverlapSeconds.Should().Be(30 * 60);
        c.OverlapStart.Should().Be(T0.AddMinutes(40));
        c.OverlapEnd.Should().Be(T0.AddMinutes(70));
    }

    [Fact]
    public void Same_mold_on_different_imms_is_mold_conflict()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmB, MoldX, S(OccupancyKind.Plan, 50, 150));

        PlanConflictDetector.Detect(new[] { a, b }, Threshold)
            .Should().ContainSingle().Which.Resource.Should().Be(ConflictResource.Mold);
    }

    [Fact]
    public void Same_imm_and_same_mold_gives_two_records()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmA, MoldX, S(OccupancyKind.Plan, 50, 150));

        PlanConflictDetector.Detect(new[] { a, b }, Threshold)
            .Select(c => c.Resource).Should().BeEquivalentTo(new[] { ConflictResource.Imm, ConflictResource.Mold });
    }

    [Fact]
    public void Different_imm_and_mold_never_conflict()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmB, MoldY, S(OccupancyKind.Plan, 0, 100));

        PlanConflictDetector.Detect(new[] { a, b }, Threshold).Should().BeEmpty();
    }

    [Fact]
    public void Task_without_segments_is_ignored()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var finished = Task(ImmA, MoldX);

        PlanConflictDetector.Detect(new[] { a, finished }, Threshold).Should().BeEmpty();
    }

    [Fact]
    public void Plan_conflicts_come_before_deviation_conflicts()
    {
        var running = Task(ImmA, MoldX, S(OccupancyKind.Fact, 0, 50));
        var p1 = Task(ImmA, MoldY, S(OccupancyKind.Plan, 10, 200));
        var p2 = Task(ImmB, Guid.NewGuid(), S(OccupancyKind.Plan, 300, 400));
        var p3 = Task(ImmB, Guid.NewGuid(), S(OccupancyKind.Plan, 350, 450));

        PlanConflictDetector.Detect(new[] { running, p1, p2, p3 }, Threshold)
            .Select(c => c.Kind).Should().Equal(ConflictKind.Plan, ConflictKind.Deviation);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~PlanConflictDetectorTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

Create `Wintime.Control.Core/Policies/PlanConflictDetector.cs`:

```csharp
namespace Wintime.Control.Core.Policies;

/// <summary>Ресурс, за который спорят два задания.</summary>
public enum ConflictResource { Imm, Mold }

/// <summary>
/// Вид конфликта: <c>Plan</c> — пересекаются только плановые отрезки (ошибка планировщика);
/// <c>Deviation</c> — в паре есть факт или прогноз (выполнение ушло от плана).
/// </summary>
public enum ConflictKind { Plan, Deviation }

/// <summary>Задание для детектора: ресурсы и отрезки занятости (<see cref="TaskOccupancy"/>).</summary>
public sealed record ConflictTaskInput(Guid TaskId, Guid ImmId, Guid MoldId, IReadOnlyList<OccupancySegment> Segments);

/// <summary>Пара заданий в конфликте по одному ресурсу; <c>OverlapSeconds</c> — суммарная длина пересечения.</summary>
public sealed record PlanConflict(Guid TaskId, Guid OtherTaskId, ConflictResource Resource, ConflictKind Kind,
                                  DateTime OverlapStart, DateTime OverlapEnd, long OverlapSeconds);

/// <summary>
/// Конфликты плана (PZP-07b, ADR-0022): пары заданий на одном ТПА или с одной ПФ, у которых суммарное пересечение
/// отрезков строго больше порога. Чистая функция, не хранится, ничего не блокирует. Сравнение — внутри групп
/// по ТПА и по ПФ (группы малы: задания одного станка/формы).
/// </summary>
public static class PlanConflictDetector
{
    public static IReadOnlyList<PlanConflict> Detect(IReadOnlyCollection<ConflictTaskInput> tasks, TimeSpan threshold)
    {
        var ordered = tasks
            .Where(t => t.Segments.Count > 0)
            .OrderBy(t => t.Segments.Min(s => s.Start))
            .ThenBy(t => t.TaskId)
            .ToList();

        var result = new List<PlanConflict>();
        foreach (var group in ordered.GroupBy(t => t.ImmId))
            Compare(group.ToList(), ConflictResource.Imm, threshold, result);
        foreach (var group in ordered.GroupBy(t => t.MoldId))
            Compare(group.ToList(), ConflictResource.Mold, threshold, result);

        return result
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.OverlapStart)
            .ThenBy(c => c.TaskId)
            .ThenBy(c => c.Resource)
            .ToList();
    }

    private static void Compare(List<ConflictTaskInput> group, ConflictResource resource, TimeSpan threshold,
                                List<PlanConflict> result)
    {
        for (var i = 0; i < group.Count; i++)
        for (var j = i + 1; j < group.Count; j++)
        {
            var a = group[i];
            var b = group[j];
            var total = TimeSpan.Zero;
            DateTime? first = null, last = null;

            foreach (var sa in a.Segments)
            foreach (var sb in b.Segments)
            {
                var start = sa.Start > sb.Start ? sa.Start : sb.Start;
                var end = sa.End < sb.End ? sa.End : sb.End;
                if (end <= start)
                    continue;
                total += end - start;
                if (first is null || start < first) first = start;
                if (last is null || end > last) last = end;
            }

            if (total <= threshold || first is null || last is null)
                continue;

            var kind = PlanOnly(a) && PlanOnly(b) ? ConflictKind.Plan : ConflictKind.Deviation;
            result.Add(new PlanConflict(a.TaskId, b.TaskId, resource, kind, first.Value, last.Value,
                                        (long)Math.Floor(total.TotalSeconds)));
        }
    }

    private static bool PlanOnly(ConflictTaskInput task) => task.Segments.All(s => s.Kind == OccupancyKind.Plan);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~PlanConflictDetectorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/PlanConflictDetector.cs Wintime.Control.Tests.Unit/Policies/PlanConflictDetectorTests.cs
git commit -m "feat(planning): plan conflict detector — IMM and mold, threshold, plan vs deviation (PZP-07b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: `IPlanConflictService` и `GET /api/planning/conflicts`

**Files:**
- Create: `Wintime.Control.Core/DTOs/Planning/PlanConflictsDto.cs`
- Create: `Wintime.Control.Core/Interfaces/IPlanConflictService.cs`
- Create: `Wintime.Control.Infrastructure/Services/PlanConflictService.cs`
- Modify: `Wintime.Control.API/Program.cs` (рядом со стр. 176 — `IReleaseForecastService`)
- Modify: `Wintime.Control.API/Controllers/PlanningController.cs`
- Test: `Wintime.Control.Tests.Integration/Planning/PlanConflictsApiTests.cs`

**Interfaces:**
- Consumes: `OpenTaskStatuses.All` (Task 1), `PlanningDefaults.ConflictThresholdMinutes` (Task 2), `TaskOccupancy.Build` (Task 3), `PlanConflictDetector.Detect` (Task 4), `IReleaseForecastService.CalculateAsync`, `IFactoryClock.GetScheduleAsync`, `FactorySchedule.PlannedStartUtc(DateOnly, TimeOnly?)`, `TaskPlanCalculator.Calculate` (существуют).
- Produces:
  - `PlanConflictsDto { DateTime Now; int ThresholdMinutes; int PlanCount; int DeviationCount; int TaskCount; List<PlanConflictDto> Items }`
  - `PlanConflictDto { ConflictKind Kind; ConflictResource Resource; ConflictTaskRefDto Task; ConflictTaskRefDto Other; DateTime OverlapStart; DateTime OverlapEnd; long OverlapSeconds }`
  - `ConflictTaskRefDto { Guid Id; TaskStatus Status; Guid ImmId; string? ImmName; Guid MoldId; string? MoldName; string? ProductArticle; DateTime PlannedStartAt }`
  - `IPlanConflictService.GetAllAsync(DateTime nowUtc, CancellationToken ct = default) : Task<PlanConflictsDto>`
  - JSON: `{ now, thresholdMinutes, planCount, deviationCount, taskCount, items: [{ kind: "Plan"|"Deviation", resource: "Imm"|"Mold", task: {id, status, immId, immName, moldId, moldName, productArticle, plannedStartAt}, other: {…}, overlapStart, overlapEnd, overlapSeconds }] }`

- [ ] **Step 1: Write the failing tests**

Create `Wintime.Control.Tests.Integration/Planning/PlanConflictsApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Planning;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Planning;

/// <summary>PZP-07b: GET /api/planning/conflicts — пары по ТПА и ПФ, порог, роли, ничего не блокирует.</summary>
[Collection("Integration")]
public class PlanConflictsApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public PlanConflictsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Через 5 суток — далеко от «сейчас», задания не просрочены
    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);

    private async Task<HttpClient> ClientAsync(string login = "test_manager", string password = "Manager123!")
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<PlanConflictsDto> GetConflictsAsync()
    {
        var client = await ClientAsync();
        var resp = await client.GetAsync("/api/planning/conflicts");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<PlanConflictsDto>(JsonOptions))!;
    }

    private async Task<Guid> SeedMoldAsync(int cavities = 2)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold
        {
            Name = $"M-{Guid.NewGuid():N}"[..10], FormId = $"FORM-{Guid.NewGuid():N}", Cavities = cavities,
            IsActive = true, ProductTypeId = _factory.TestProductTypeId
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold.Id;
    }

    // План 100 шт, 2 гнезда, эталон 60 с → выпуск 50 мин; наладка 30 мин; буфер 0 → окно 80 мин
    private async Task<Guid> SeedTaskAsync(Guid immId, Guid moldId, TimeOnly time, Action<ShiftTask>? setup = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = immId, MoldId = moldId, ProductId = _factory.TestProductId, PlanQuantity = 100,
            PlannedFullCycleSeconds = 60, PlannedSetupMinutes = 30, Status = TaskStatus.Planned,
            PlannedDate = Day, PlannedSetupStartTime = time
        };
        setup?.Invoke(task);
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private static IEnumerable<PlanConflictDto> Between(PlanConflictsDto r, Guid a, Guid b) =>
        r.Items.Where(i => (i.Task.Id == a && i.Other.Id == b) || (i.Task.Id == b && i.Other.Id == a));

    [Fact]
    public async Task Overlapping_plans_on_same_imm_are_plan_conflict()
    {
        var imm = await _factory.CreateFreshImmAsync();
        var a = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(10, 0));
        var b = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(10, 30));   // пересечение 50 мин

        var r = await GetConflictsAsync();

        var c = Between(r, a, b).Should().ContainSingle().Subject;
        c.Kind.Should().Be(ConflictKind.Plan);
        c.Resource.Should().Be(ConflictResource.Imm);
        c.OverlapSeconds.Should().Be(50 * 60);
        c.Task.Id.Should().Be(a);
        c.Task.ImmName.Should().NotBeNullOrEmpty();
        r.ThresholdMinutes.Should().Be(20);
        r.PlanCount.Should().BeGreaterThan(0);
        r.TaskCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Same_mold_on_two_imms_is_mold_conflict()
    {
        var mold = await SeedMoldAsync();
        var a = await SeedTaskAsync(await _factory.CreateFreshImmAsync(), mold, new TimeOnly(14, 0));
        var b = await SeedTaskAsync(await _factory.CreateFreshImmAsync(), mold, new TimeOnly(14, 30));

        var r = await GetConflictsAsync();

        Between(r, a, b).Should().ContainSingle().Which.Resource.Should().Be(ConflictResource.Mold);
    }

    [Fact]
    public async Task Overlap_within_threshold_is_not_conflict_and_cavity_change_creates_it()
    {
        var imm = await _factory.CreateFreshImmAsync();
        var mold = await SeedMoldAsync();
        var a = await SeedTaskAsync(imm, mold, new TimeOnly(18, 0));               // 18:00–19:20
        var b = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(19, 0)); // пересечение 20 мин = порог

        Between(await GetConflictsAsync(), a, b).Should().BeEmpty();

        // 1 гнездо → выпуск 100 мин, A кончается 20:10 → пересечение 70 мин
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.Molds.SingleAsync(m => m.Id == mold)).Cavities = 1;
            await db.SaveChangesAsync();
        }

        Between(await GetConflictsAsync(), a, b).Should().ContainSingle();
    }

    [Fact]
    public async Task Late_issued_task_against_running_task_is_deviation_conflict()
    {
        var imm = await _factory.CreateFreshImmAsync();
        // Идёт выпуск: циклов нет → прогноз по эталону [now, now + 50 мин)
        var running = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(0, 0), t =>
        {
            t.PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            t.Status = TaskStatus.InProgress;
            t.SetupStartedAt = DateTime.UtcNow.AddHours(-2);
            t.StartedAt = DateTime.UtcNow.AddHours(-1);
        });
        // Выдано вчера, не начато → прогноз [now + 10, now + 90 мин) — пересечение 40 мин
        var late = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(1, 0), t =>
        {
            t.PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            t.Status = TaskStatus.Issued;
            t.PersonnelId = _factory.TestAdjusterId;
        });

        var r = await GetConflictsAsync();

        Between(r, running, late).Should().ContainSingle().Which.Kind.Should().Be(ConflictKind.Deviation);
        r.DeviationCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Finished_tasks_do_not_participate()
    {
        var imm = await _factory.CreateFreshImmAsync();
        var a = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(6, 0), t => t.Status = TaskStatus.Completed);
        var b = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(6, 0));

        Between(await GetConflictsAsync(), a, b).Should().BeEmpty();
    }

    [Fact]
    public async Task Task_in_plan_conflict_can_still_be_issued()
    {
        var imm = await _factory.CreateFreshImmAsync();
        await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(12, 0));
        var b = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(12, 30));
        var client = await ClientAsync();

        var resp = await client.PostAsJsonAsync($"/api/tasks/{b}/issue", new { personnelId = _factory.TestAdjusterId });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Observer_can_read_conflicts_adjuster_cannot()
    {
        (await (await ClientAsync("test_observer", "Observer123!")).GetAsync("/api/planning/conflicts"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await ClientAsync("test_adjuster", "Adjuster123!")).GetAsync("/api/planning/conflicts"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

> `DateOnly.FromDateTime(DateTime.UtcNow)` — дата по UTC, а план читается по часам завода. В тестах используются дни далеко от «сейчас» (±1, +5), поэтому сдвиг пояса не влияет на результат. Если в тестовом окружении пояс завода сдвигает «вчера 00:00» в будущее — возьми `AddDays(-2)`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~PlanConflictsApiTests"`
Expected: FAIL to compile (`PlanConflictsDto` not defined).

- [ ] **Step 3: Implement**

Create `Wintime.Control.Core/DTOs/Planning/PlanConflictsDto.cs`:

```csharp
using Wintime.Control.Core.Policies;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Core.DTOs.Planning;

/// <summary>Конфликты плана всех незавершённых заданий (PZP-07b, ADR-0022). Вычисляются на лету.</summary>
public class PlanConflictsDto
{
    public DateTime Now { get; set; }
    public int ThresholdMinutes { get; set; }
    public int PlanCount { get; set; }
    public int DeviationCount { get; set; }
    /// <summary>Уникальных заданий хотя бы в одной паре — «N заданий требуют корректировки».</summary>
    public int TaskCount { get; set; }
    /// <summary>Конфликты плана, затем отклонения; внутри — по началу пересечения.</summary>
    public List<PlanConflictDto> Items { get; set; } = new();
}

public class PlanConflictDto
{
    public ConflictKind Kind { get; set; }
    public ConflictResource Resource { get; set; }
    /// <summary>Задание с более ранним началом занятости.</summary>
    public ConflictTaskRefDto Task { get; set; } = null!;
    public ConflictTaskRefDto Other { get; set; } = null!;
    public DateTime OverlapStart { get; set; }
    public DateTime OverlapEnd { get; set; }
    public long OverlapSeconds { get; set; }
}

public class ConflictTaskRefDto
{
    public Guid Id { get; set; }
    public TaskStatus Status { get; set; }
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public Guid MoldId { get; set; }
    public string? MoldName { get; set; }
    /// <summary>Полный артикул изделия (<c>Product.FullArticle</c>).</summary>
    public string? ProductArticle { get; set; }
    public DateTime PlannedStartAt { get; set; }
}
```

Create `Wintime.Control.Core/Interfaces/IPlanConflictService.cs`:

```csharp
using Wintime.Control.Core.DTOs.Planning;

namespace Wintime.Control.Core.Interfaces;

/// <summary>
/// Конфликты плана (PZP-07b, ADR-0022): все незавершённые задания, отрезки (<c>TaskOccupancy</c>), порог из
/// <c>PlanningDefaults</c>. Один источник для бейджа, баннера, доски, карточки и списка.
/// </summary>
public interface IPlanConflictService
{
    Task<PlanConflictsDto> GetAllAsync(DateTime nowUtc, CancellationToken ct = default);
}
```

Create `Wintime.Control.Infrastructure/Services/PlanConflictService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Planning;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Infrastructure.Services;

public class PlanConflictService : IPlanConflictService
{
    private readonly ControlDbContext _context;
    private readonly IFactoryClock _factoryClock;
    private readonly IReleaseForecastService _releaseForecast;
    private readonly IPlanningDefaultsService _planningDefaults;

    public PlanConflictService(ControlDbContext context, IFactoryClock factoryClock,
                               IReleaseForecastService releaseForecast, IPlanningDefaultsService planningDefaults)
    {
        _context = context;
        _factoryClock = factoryClock;
        _releaseForecast = releaseForecast;
        _planningDefaults = planningDefaults;
    }

    public async Task<PlanConflictsDto> GetAllAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var tasks = await _context.ShiftTasks
            .AsNoTracking()
            .Include(t => t.Imm)
            .Include(t => t.Mold)
            .Include(t => t.Product)
            .Where(t => OpenTaskStatuses.All.Contains(t.Status))
            .ToListAsync(ct);

        var schedule = await _factoryClock.GetScheduleAsync(ct);
        var defaults = await _planningDefaults.GetAsync(ct);
        var forecasts = await _releaseForecast.CalculateAsync(
            tasks.Where(t => t.Status == TaskStatus.InProgress).Select(t => t.Id).ToList(), nowUtc, ct);

        var refs = new Dictionary<Guid, ConflictTaskRefDto>(tasks.Count);
        var inputs = new List<ConflictTaskInput>(tasks.Count);
        foreach (var t in tasks)
        {
            var plannedStart = schedule.PlannedStartUtc(t.PlannedDate, t.PlannedSetupStartTime);
            var plan = TaskPlanCalculator.Calculate(plannedStart, t.PlannedSetupMinutes, t.PlanQuantity,
                t.Mold.Cavities, t.PlannedFullCycleSeconds, t.ReleaseBufferFixedMinutes, t.ReleaseBufferPercent);
            forecasts.TryGetValue(t.Id, out var forecast);
            var segments = TaskOccupancy.Build(plannedStart, plan, new OccupancyFacts(t.Status, t.SetupStartedAt),
                                               forecast, nowUtc);
            inputs.Add(new ConflictTaskInput(t.Id, t.ImmId, t.MoldId, segments));
            refs[t.Id] = new ConflictTaskRefDto
            {
                Id = t.Id, Status = t.Status, ImmId = t.ImmId, ImmName = t.Imm?.Name, MoldId = t.MoldId,
                MoldName = t.Mold.Name, ProductArticle = t.Product?.FullArticle, PlannedStartAt = plannedStart
            };
        }

        var conflicts = PlanConflictDetector.Detect(inputs, TimeSpan.FromMinutes(defaults.ConflictThresholdMinutes));
        var items = conflicts.Select(c => new PlanConflictDto
        {
            Kind = c.Kind, Resource = c.Resource, Task = refs[c.TaskId], Other = refs[c.OtherTaskId],
            OverlapStart = c.OverlapStart, OverlapEnd = c.OverlapEnd, OverlapSeconds = c.OverlapSeconds
        }).ToList();

        return new PlanConflictsDto
        {
            Now = nowUtc,
            ThresholdMinutes = defaults.ConflictThresholdMinutes,
            PlanCount = items.Count(i => i.Kind == ConflictKind.Plan),
            DeviationCount = items.Count(i => i.Kind == ConflictKind.Deviation),
            TaskCount = items.SelectMany(i => new[] { i.Task.Id, i.Other.Id }).Distinct().Count(),
            Items = items
        };
    }
}
```

> Если `IFactoryClock.GetScheduleAsync` не принимает `CancellationToken` или `PlannedStartUtc` называется иначе — сверь с `PlanningController.GetBoard` и `TaskMappingExtensions.ToDto` (там те же вызовы).

`Program.cs` — after `IReleaseForecastService` registration:

```csharp
builder.Services.AddScoped<IPlanConflictService, PlanConflictService>();
```

`PlanningController` — inject `IPlanConflictService conflicts` (field `_conflicts`), add:

```csharp
    /// <summary>
    /// Конфликты плана (PZP-07b): пары незавершённых заданий на одном ТПА или с одной ПФ, пересечение которых
    /// дольше порога. Подсвечивают, ничего не блокируют.
    /// </summary>
    [HttpGet("conflicts")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Observer}")]
    public async Task<ActionResult<PlanConflictsDto>> GetConflicts(CancellationToken ct) =>
        Ok(await _conflicts.GetAllAsync(DateTime.UtcNow, ct));
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~PlanConflictsApiTests|FullyQualifiedName~PlanningBoardTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core Wintime.Control.Infrastructure/Services/PlanConflictService.cs Wintime.Control.API Wintime.Control.Tests.Integration/Planning/PlanConflictsApiTests.cs
git commit -m "feat(planning): GET /api/planning/conflicts — computed plan conflicts service (PZP-07b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Доска — прогноз для `Issued`/`Setup` и конфликты в `BoardTaskDto`

**Files:**
- Modify: `Wintime.Control.Core/Policies/PlanningBoardCalculator.cs`
- Modify: `Wintime.Control.Core/DTOs/Planning/PlanningBoardDto.cs`
- Modify: `Wintime.Control.API/Controllers/PlanningController.cs` (`GetBoard`)
- Test: `Wintime.Control.Tests.Unit/Policies/PlanningBoardCalculatorTests.cs`, `Wintime.Control.Tests.Integration/Planning/PlanningBoardTests.cs`

**Interfaces:**
- Consumes: `TaskOccupancy.Build`, `OccupancyFacts` (Task 3), `IPlanConflictService` (Task 5).
- Produces:
  - `record BoardFact(DateTime? SetupStart, DateTime? SetupEnd, DateTime? ReleaseEnd, DateTime? ForecastEnd, DateTime? ForecastStart = null)` — `ForecastStart` заполнен только у `Issued` (случай Б); `null` — прогноз от «сейчас».
  - `ForecastEnd` теперь и у `Issued` (Б) и `Setup` (В, Г) — конец прогнозного отрезка `TaskOccupancy`. `InProgress` — без изменений.
  - `BoardTaskDto.Conflicts : List<PlanConflictDto>` — пары, где задание `Task` или `Other`. JSON `conflicts`.

- [ ] **Step 1: Write the failing tests**

In `PlanningBoardCalculatorTests.cs` replace `Setup_past_planned_setup_end_lags_without_forecast_or_remaining` with:

```csharp
    [Fact]
    public void Setup_past_norm_lags_and_forecasts_release_from_now_plus_delay()
    {
        // наладка с 10:10, сейчас 10:45 (35 > 30) → прогноз 10:45 + 10 + 60 = 11:55
        var r = PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Setup, setupStarted: At(10)),
            new ReleaseForecastResult(100m, At(200)), At(45));

        r.SetupLagSeconds.Should().Be(15 * 60);
        r.Fact.Should().Be(new BoardFact(At(10), null, null, At(115)));
        r.BufferRemainingSeconds.Should().BeNull();
        r.OverdueNotStartedSeconds.Should().BeNull();
    }
```

Add:

```csharp
    [Fact]
    public void Setup_within_norm_forecasts_release_from_actual_setup_start()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Setup, setupStarted: At(0)), null, At(20));

        r.Fact.Should().Be(new BoardFact(At(0), null, null, At(90)));
    }

    [Fact]
    public void Issued_overdue_forecast_starts_after_expected_delay()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Issued), null, At(20));

        r.Fact.Should().Be(new BoardFact(null, null, null, At(120), At(30)));
        r.OverdueNotStartedSeconds.Should().Be(20 * 60);
    }

    [Fact]
    public void Issued_before_start_and_planned_have_no_forecast()
    {
        PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Issued), null, At(-10)).Fact
            .Should().Be(new BoardFact(null, null, null, null));
        PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Planned), null, At(20)).Fact
            .Should().Be(new BoardFact(null, null, null, null));
    }
```

If other existing tests in the file assert `Fact` for `Setup` (e.g. `Setup_within_norm_has_no_lag` asserting only lag) — keep them; if one asserts `ForecastEnd == null` for `Setup`/`Issued`, update it to the new value per the rules in Global Constraints.

In `PlanningBoardTests.cs` add:

```csharp
    [Fact]
    public async Task Board_items_carry_conflicts_of_the_task()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        var a = await SeedTaskAsync(immId, await SeedMoldAsync(), t => t.PlannedDate = day);
        var b = await SeedTaskAsync(immId, await SeedMoldAsync(), t =>
        {
            t.PlannedDate = day;
            t.PlannedSetupStartTime = new TimeOnly(10, 30);
        });

        var board = await GetBoardAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(5));

        var row = board.Rows.Single(r => r.Imm.Id == immId);
        row.Tasks.Single(t => t.Task.Id == a).Conflicts.Should().ContainSingle(c => c.Other.Id == b || c.Task.Id == b);
        row.Tasks.Single(t => t.Task.Id == b).Conflicts.Should().ContainSingle();
    }

    [Fact]
    public async Task Setup_task_has_forecast_on_board()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var taskId = await SeedTaskAsync(immId, await SeedMoldAsync(), t =>
        {
            t.Status = TaskStatus.Setup;
            t.SetupStartedAt = DateTime.UtcNow.AddMinutes(-10);
        });

        var board = await GetBoardAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        var item = board.Rows.Single(r => r.Imm.Id == immId).Tasks.Single(t => t.Task.Id == taskId);
        item.Fact.ForecastEnd.Should().BeCloseTo(item.Fact.SetupStart!.Value.AddMinutes(30 + 50), TimeSpan.FromSeconds(1));
    }
```

(`SeedTaskAsync` in this class sets plan 100 шт, 2 гнезда, 60 с, наладка 30 мин → выпуск 50 мин.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~PlanningBoardCalculatorTests"` → FAIL.
Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~PlanningBoardTests"` → FAIL to compile (`Conflicts`).

- [ ] **Step 3: Implement**

`PlanningBoardCalculator.cs`:

```csharp
/// <summary>
/// Фактические границы бара, UTC. <c>ForecastEnd</c> — прогноз окончания выпуска: InProgress — <see cref="ReleaseForecast"/>,
/// Issued после планового начала и Setup — по стадиям <see cref="TaskOccupancy"/> (PZP-07b). <c>ForecastStart</c> —
/// начало прогноза, если не «сейчас» (только Issued: наладка ожидается через <see cref="TaskOccupancy.ExpectedDelay"/>).
/// </summary>
public sealed record BoardFact(DateTime? SetupStart, DateTime? SetupEnd, DateTime? ReleaseEnd, DateTime? ForecastEnd,
                               DateTime? ForecastStart = null);
```

In `Build`, replace the `forecastEnd`/`factBar` lines:

```csharp
        DateTime? forecastEnd = null;
        DateTime? forecastStart = null;
        if (facts.Status == TaskStatus.InProgress)
        {
            forecastEnd = ForecastEnd(forecast, nowUtc);
        }
        else if (facts.Status is TaskStatus.Issued or TaskStatus.Setup)
        {
            var projected = TaskOccupancy.Build(plannedStartUtc, plan,
                    new OccupancyFacts(facts.Status, facts.SetupStartedAt), null, nowUtc)
                .FirstOrDefault(s => s.Kind == OccupancyKind.Forecast);
            if (projected is not null)
            {
                forecastEnd = projected.End;
                if (facts.Status == TaskStatus.Issued)
                    forecastStart = projected.Start;
            }
        }
        var factBar = new BoardFact(facts.SetupStartedAt, factSetupEnd, factReleaseEnd, forecastEnd, forecastStart);
```

The `releaseFinish` / `remaining` lines stay as they are (остаток буфера — только InProgress и завершённые). Update the class summary: «Для Setup остаток не считается; прогноз — по стадиям TaskOccupancy (PZP-07b)».

`PlanningBoardDto.cs` — in `BoardTaskDto` add:

```csharp
    /// <summary>Конфликты плана с участием задания (PZP-07b): задание — <c>Task</c> или <c>Other</c> пары.</summary>
    public List<PlanConflictDto> Conflicts { get; set; } = new();
```

`PlanningController.GetBoard` — after `var now = DateTime.UtcNow;`:

```csharp
        var conflicts = await _conflicts.GetAllAsync(now, ct);
```

and in `new BoardTaskDto { … }` add:

```csharp
                Conflicts = conflicts.Items.Where(c => c.Task.Id == t.Id || c.Other.Id == t.Id).ToList()
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~PlanningBoardCalculatorTests|FullyQualifiedName~TaskOccupancyTests"`; `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~PlanningBoardTests"`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core Wintime.Control.API/Controllers/PlanningController.cs Wintime.Control.Tests.Unit Wintime.Control.Tests.Integration/Planning/PlanningBoardTests.cs
git commit -m "feat(planning): board forecasts Issued/Setup and carries task conflicts (PZP-07b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Предупреждение наладчику — ПФ занята на другом ТПА

**Files:**
- Create: `Wintime.Control.Core/DTOs/Tasks/StartTaskRequestDto.cs`
- Modify: `Wintime.Control.API/Controllers/TasksController.cs` (`StartTask`, ~стр. 440–475)
- Modify: `Wintime-Control-Frontend/src/api/mobile.js` (`startSetup`), `src/views/mobile/MobileTasksView.vue` (`startTask`, ~стр. 265–275)
- Test: `Wintime.Control.Tests.Integration/Tasks/StartSetupMoldBusyTests.cs`, `Wintime-Control-Frontend/src/views/mobile/__tests__/MobileTasksView.spec.js`

**Interfaces:**
- Produces: `POST /api/tasks/{id}/start` body `{ confirmMoldBusy?: bool }` (пустое тело допустимо). 409 `{ message: "Пресс-форма сейчас используется на ТПА «X»", moldBusy: { immName, taskId } }` — только для `Issued`, когда ПФ задания в `Setup`/`InProgress` на другом ТПА и флаг не поднят. `mobileApi.startSetup(id, confirmMoldBusy = false)`.

- [ ] **Step 1: Write the failing tests**

Create `Wintime.Control.Tests.Integration/Tasks/StartSetupMoldBusyTests.cs`:

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

/// <summary>PZP-07b: старт наладки с ПФ, занятой на другом ТПА, — предупреждение с подтверждением, не запрет.</summary>
[Collection("Integration")]
public class StartSetupMoldBusyTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public StartSetupMoldBusyTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> AdjusterAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedMoldAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold
        {
            Name = "M-busy", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 2, IsActive = true,
            ProductTypeId = _factory.TestProductTypeId
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold.Id;
    }

    private async Task<Guid> SeedTaskAsync(Guid immId, Guid moldId, TaskStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = immId, MoldId = moldId, ProductId = _factory.TestProductId, PlanQuantity = 100,
            PlannedFullCycleSeconds = 60, Status = status, PersonnelId = _factory.TestAdjusterId,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            SetupStartedAt = status == TaskStatus.Issued ? null : DateTime.UtcNow.AddMinutes(-30)
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    [Theory]
    [InlineData(TaskStatus.Setup)]
    [InlineData(TaskStatus.InProgress)]
    public async Task Start_with_mold_busy_on_other_imm_warns_then_starts_with_confirmation(TaskStatus busyStatus)
    {
        var mold = await SeedMoldAsync();
        var busyImm = await _factory.CreateFreshImmAsync();
        await SeedTaskAsync(busyImm, mold, busyStatus);
        var taskId = await SeedTaskAsync(await _factory.CreateFreshImmAsync(), mold, TaskStatus.Issued);
        var client = await AdjusterAsync();

        var warned = await client.PostAsync($"/api/tasks/{taskId}/start", null);
        warned.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await warned.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Contain("Пресс-форма сейчас используется");
        body.GetProperty("moldBusy").GetProperty("immName").GetString().Should().Be($"ТПА-{busyImm:N}");

        var started = await client.PostAsJsonAsync($"/api/tasks/{taskId}/start", new { confirmMoldBusy = true });
        started.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Start_with_mold_free_needs_no_confirmation()
    {
        var mold = await SeedMoldAsync();
        await SeedTaskAsync(await _factory.CreateFreshImmAsync(), mold, TaskStatus.Planned);
        var taskId = await SeedTaskAsync(await _factory.CreateFreshImmAsync(), mold, TaskStatus.Issued);
        var client = await AdjusterAsync();

        (await client.PostAsync($"/api/tasks/{taskId}/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

In `MobileTasksView.spec.js` add a new `describe` (top of file already mocks `@/api/mobile`; import `ElMessageBox` from `element-plus`):

```js
describe('MobileTasksView — пресс-форма занята на другом ТПА', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  const mountView = () => mount(MobileTasksView, {
    global: { plugins: [createPinia(), ElementPlus], stubs: { QrScanner: true } }
  })

  it('409 moldBusy → подтверждение → повтор с confirmMoldBusy', async () => {
    mobileApi.startSetup
      .mockRejectedValueOnce({ response: { status: 409, data: {
        message: 'Пресс-форма сейчас используется на ТПА «ТПА-2»', moldBusy: { immName: 'ТПА-2', taskId: 'x' } } } })
      .mockResolvedValueOnce({ data: {} })
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm')
    const wrapper = mountView()
    await flushPromises()

    await wrapper.vm.startTask({ id: 't1' })
    await flushPromises()

    expect(confirm.mock.calls[0][0]).toContain('ТПА «ТПА-2»')
    expect(mobileApi.startSetup).toHaveBeenNthCalledWith(1, 't1')
    expect(mobileApi.startSetup).toHaveBeenNthCalledWith(2, 't1', true)
    confirm.mockRestore()
  })

  it('отмена подтверждения — наладка не начинается', async () => {
    mobileApi.startSetup.mockRejectedValueOnce({ response: { status: 409, data: {
      message: 'Пресс-форма сейчас используется на ТПА «ТПА-2»', moldBusy: { immName: 'ТПА-2', taskId: 'x' } } } })
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockRejectedValue('cancel')
    const wrapper = mountView()
    await flushPromises()

    await wrapper.vm.startTask({ id: 't1' })
    await flushPromises()

    expect(mobileApi.startSetup).toHaveBeenCalledTimes(1)
    confirm.mockRestore()
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~StartSetupMoldBusyTests"` → FAIL (200 instead of 409).
Run: `cd Wintime-Control-Frontend; npx vitest run src/views/mobile/__tests__/MobileTasksView.spec.js` → FAIL.

- [ ] **Step 3: Implement**

Create `Wintime.Control.Core/DTOs/Tasks/StartTaskRequestDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>Начать наладку (PZP-07b): <c>ConfirmMoldBusy</c> — наладчик подтвердил, что ПФ занята на другом ТПА.</summary>
public class StartTaskRequestDto
{
    public bool ConfirmMoldBusy { get; set; }
}
```

`TasksController.StartTask` — signature and the check after the active-task guard:

```csharp
    public async Task<IActionResult> StartTask(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]
        StartTaskRequestDto? request)
    {
        …existing find + hasActiveTask check…

        // PZP-07b: ПФ сейчас в наладке/выпуске на другом ТПА — не запрет, а предупреждение с подтверждением.
        // Журнал таких событий для руководителя — BL-47.
        if (task.Status == Core.Enums.TaskStatus.Issued && request?.ConfirmMoldBusy != true)
        {
            var busy = await _context.ShiftTasks
                .Where(t => t.Id != task.Id && t.MoldId == task.MoldId && t.ImmId != task.ImmId &&
                            (t.Status == Core.Enums.TaskStatus.Setup || t.Status == Core.Enums.TaskStatus.InProgress))
                .Select(t => new { t.Id, ImmName = t.Imm.Name })
                .FirstOrDefaultAsync();
            if (busy != null)
                return Conflict(new
                {
                    message = $"Пресс-форма сейчас используется на ТПА «{busy.ImmName}»",
                    moldBusy = new { immName = busy.ImmName, taskId = busy.Id }
                });
        }

        task.StartSetup();
        …rest unchanged…
```

`src/api/mobile.js`:

```js
  // Начать наладку; confirmMoldBusy — наладчик подтвердил, что ПФ занята на другом ТПА (PZP-07b)
  startSetup(id, confirmMoldBusy = false) {
    return confirmMoldBusy
      ? apiClient.post(`/tasks/${id}/start`, { confirmMoldBusy: true })
      : apiClient.post(`/tasks/${id}/start`)
  },
```

`MobileTasksView.vue` — add `import { apiErrorMessage } from '@/utils/apiError'` and replace `startTask`:

```js
// PZP-07b: ПФ занята на другом ТПА — предупреждение с подтверждением, не запрет
const startSetupConfirmingMold = async (taskId) => {
  try {
    await mobileApi.startSetup(taskId)
  } catch (error) {
    const data = error?.response?.status === 409 ? error.response.data : null
    if (!data?.moldBusy) throw error
    await ElMessageBox.confirm(`${data.message}. Всё равно начать наладку?`, 'Пресс-форма занята', {
      type: 'warning', confirmButtonText: 'Начать', cancelButtonText: 'Отмена'
    })
    await mobileApi.startSetup(taskId, true)
  }
}

const startTask = async (task) => {
  try {
    await startSetupConfirmingMold(task.id)
    ElMessage.success('Наладка начата')
    await loadTasks()
    detailVisible.value = false
  } catch (error) {
    if (error === 'cancel') return
    ElMessage.error(apiErrorMessage(error, 'Ошибка начала наладки'))
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~StartSetupMoldBusyTests|FullyQualifiedName~TasksControllerTests"`; `cd Wintime-Control-Frontend; npx vitest run src/views/mobile`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/DTOs/Tasks/StartTaskRequestDto.cs Wintime.Control.API/Controllers/TasksController.cs Wintime.Control.Tests.Integration/Tasks/StartSetupMoldBusyTests.cs Wintime-Control-Frontend/src/api/mobile.js Wintime-Control-Frontend/src/views/mobile
git commit -m "feat(tasks): warn adjuster when mold is busy on another IMM, confirm to start (PZP-07b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Фронт — стор конфликтов, утилиты, бейдж в меню

**Files:**
- Modify: `Wintime-Control-Frontend/src/api/planning.js`
- Create: `Wintime-Control-Frontend/src/utils/planConflicts.js`
- Create: `Wintime-Control-Frontend/src/stores/planConflicts.js`
- Create: `Wintime-Control-Frontend/src/components/planning/PlanConflictsBadge.vue`
- Modify: `Wintime-Control-Frontend/src/layouts/DefaultLayout.vue`
- Test: `src/utils/__tests__/planConflicts.spec.js`, `src/stores/__tests__/planConflicts.spec.js`, `src/components/planning/__tests__/PlanConflictsBadge.spec.js`

**Interfaces:**
- Consumes: JSON `GET /api/planning/conflicts` (Task 5).
- Produces:
  - `planningApi.getConflicts()`
  - `utils/planConflicts.js`: `CONFLICT_KIND_LABELS`, `CONFLICT_KIND_COLORS`, `conflictsOf(items, taskId)`, `worstKind(conflicts) → 'Plan'|'Deviation'|null`, `otherSide(conflict, taskId) → ref`, `taskRefLabel(ref)`, `conflictLine(conflict, taskId) → string`
  - `usePlanConflictsStore()`: state `data`; getters `items`, `taskCount`, `planCount`, `deviationCount`, `forTask(taskId)`; actions `load()`, `start()`, `stop()`
  - `<PlanConflictsBadge />` — без props, читает стор.

- [ ] **Step 1: Write the failing tests**

Create `src/utils/__tests__/planConflicts.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import { conflictsOf, worstKind, otherSide, taskRefLabel, conflictLine, CONFLICT_KIND_COLORS } from '../planConflicts'

const ref = (id, extra = {}) => ({ id, immName: `ТПА-${id}`, moldName: `ПФ-${id}`, productArticle: `A${id}.0`, ...extra })
const conflict = (kind, resource, a, b, overlapSeconds = 4800) => ({ kind, resource, task: ref(a), other: ref(b), overlapSeconds })

describe('planConflicts utils', () => {
  it('conflictsOf — пары с заданием с любой стороны', () => {
    const items = [conflict('Plan', 'Imm', '1', '2'), conflict('Deviation', 'Mold', '3', '1'), conflict('Plan', 'Imm', '4', '5')]
    expect(conflictsOf(items, '1')).toHaveLength(2)
    expect(conflictsOf(items, '9')).toEqual([])
  })

  it('worstKind — конфликт плана важнее отклонения', () => {
    expect(worstKind([conflict('Deviation', 'Imm', '1', '2'), conflict('Plan', 'Imm', '1', '3')])).toBe('Plan')
    expect(worstKind([conflict('Deviation', 'Imm', '1', '2')])).toBe('Deviation')
    expect(worstKind([])).toBeNull()
  })

  it('otherSide — вторая сторона пары', () => {
    const c = conflict('Plan', 'Imm', '1', '2')
    expect(otherSide(c, '1').id).toBe('2')
    expect(otherSide(c, '2').id).toBe('1')
  })

  it('taskRefLabel — артикул · ПФ · ТПА', () => {
    expect(taskRefLabel(ref('1'))).toBe('A1.0 · ПФ-1 · ТПА-1')
  })

  it('conflictLine — вид, ресурс, другое задание, пересечение', () => {
    expect(conflictLine(conflict('Plan', 'Imm', '1', '2'), '1'))
      .toBe('Конфликт плана по ТПА с «A2.0 · ПФ-2 · ТПА-2», пересечение 1ч 20м')
    expect(conflictLine(conflict('Deviation', 'Mold', '1', '2', 1800), '2'))
      .toBe('Конфликт отклонения по ПФ «ПФ-1» с «A1.0 · ПФ-1 · ТПА-1», пересечение 30м')
  })

  it('цвета видов', () => {
    expect(CONFLICT_KIND_COLORS).toEqual({ Plan: '#f56c6c', Deviation: '#e6a23c' })
  })
})
```

Create `src/stores/__tests__/planConflicts.spec.js`:

```js
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('@/api/planning', () => ({
  planningApi: { getConflicts: vi.fn() }
}))

import { planningApi } from '@/api/planning'
import { usePlanConflictsStore, PLAN_CONFLICTS_REFRESH_MS } from '../planConflicts'

const data = {
  taskCount: 2, planCount: 1, deviationCount: 0,
  items: [{ kind: 'Plan', resource: 'Imm', task: { id: 'a' }, other: { id: 'b' }, overlapSeconds: 600 }]
}

describe('planConflicts store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.useFakeTimers()
    planningApi.getConflicts.mockResolvedValue({ data })
  })
  afterEach(() => vi.useRealTimers())

  it('load — кладёт ответ; forTask — пары задания', async () => {
    const store = usePlanConflictsStore()
    await store.load()
    expect(store.taskCount).toBe(2)
    expect(store.forTask('b')).toHaveLength(1)
    expect(store.forTask('z')).toEqual([])
  })

  it('ошибка загрузки — остаётся прошлое состояние', async () => {
    const store = usePlanConflictsStore()
    await store.load()
    planningApi.getConflicts.mockRejectedValueOnce(new Error('net'))
    await store.load()
    expect(store.taskCount).toBe(2)
  })

  it('start опрашивает раз в минуту, повторный start не дублирует, stop останавливает', async () => {
    const store = usePlanConflictsStore()
    store.start()
    store.start()
    expect(planningApi.getConflicts).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(PLAN_CONFLICTS_REFRESH_MS)
    expect(planningApi.getConflicts).toHaveBeenCalledTimes(2)
    store.stop()
    await vi.advanceTimersByTimeAsync(PLAN_CONFLICTS_REFRESH_MS)
    expect(planningApi.getConflicts).toHaveBeenCalledTimes(2)
  })
})
```

Create `src/components/planning/__tests__/PlanConflictsBadge.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import PlanConflictsBadge from '../PlanConflictsBadge.vue'
import { usePlanConflictsStore } from '@/stores/planConflicts'

const mountBadge = () => mount(PlanConflictsBadge, { global: { plugins: [ElementPlus] } })

describe('PlanConflictsBadge', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('скрыт, когда проблемных заданий нет', () => {
    expect(mountBadge().find('[data-test="plan-conflicts-badge"]').exists()).toBe(false)
  })

  it('красный при конфликте плана', async () => {
    usePlanConflictsStore().data = { taskCount: 3, planCount: 1, deviationCount: 2, items: [] }
    const w = mountBadge()
    const badge = w.find('[data-test="plan-conflicts-badge"]')
    expect(badge.text()).toContain('3')
    expect(badge.find('.el-badge__content--danger').exists()).toBe(true)
  })

  it('оранжевый, если только отклонения', () => {
    usePlanConflictsStore().data = { taskCount: 2, planCount: 0, deviationCount: 1, items: [] }
    expect(mountBadge().find('.el-badge__content--warning').exists()).toBe(true)
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd Wintime-Control-Frontend; npx vitest run src/utils/__tests__/planConflicts.spec.js src/stores/__tests__/planConflicts.spec.js src/components/planning/__tests__/PlanConflictsBadge.spec.js`
Expected: FAIL (modules not found).

- [ ] **Step 3: Implement**

`src/api/planning.js` — add to `planningApi`:

```js
  // Конфликты плана всех незавершённых заданий (PZP-07b)
  getConflicts() {
    return apiClient.get('/planning/conflicts')
  }
```

Create `src/utils/planConflicts.js`:

```js
// Конфликты плана (PZP-07b): подписи, цвета, строки для баннера, тултипа и карточки.
import { formatDuration } from './shiftTimeline'

export const CONFLICT_KIND_LABELS = { Plan: 'Конфликт плана', Deviation: 'Конфликт отклонения' }
export const CONFLICT_KIND_COLORS = { Plan: '#f56c6c', Deviation: '#e6a23c' }

export const conflictsOf = (items, taskId) =>
  (items ?? []).filter(c => c.task.id === taskId || c.other.id === taskId)

// Конфликт плана важнее отклонения
export function worstKind(conflicts) {
  if (!conflicts?.length) return null
  return conflicts.some(c => c.kind === 'Plan') ? 'Plan' : 'Deviation'
}

export const otherSide = (conflict, taskId) => (conflict.task.id === taskId ? conflict.other : conflict.task)

export const taskRefLabel = (ref) => [ref.productArticle, ref.moldName, ref.immName].filter(Boolean).join(' · ')

function resourceText(conflict) {
  return conflict.resource === 'Imm' ? 'по ТПА' : `по ПФ «${conflict.task.moldName}»`
}

export function conflictLine(conflict, taskId) {
  const label = CONFLICT_KIND_LABELS[conflict.kind] ?? conflict.kind
  return `${label} ${resourceText(conflict)} с «${taskRefLabel(otherSide(conflict, taskId))}», ` +
    `пересечение ${formatDuration(conflict.overlapSeconds * 1000)}`
}
```

Create `src/stores/planConflicts.js`:

```js
import { defineStore } from 'pinia'
import { planningApi } from '@/api/planning'
import { conflictsOf } from '@/utils/planConflicts'

export const PLAN_CONFLICTS_REFRESH_MS = 60_000

// Конфликты плана (PZP-07b): один опрос на всё приложение — бейдж, баннер, карточка, список, форма.
export const usePlanConflictsStore = defineStore('planConflicts', {
  state: () => ({
    data: null,
    timer: null
  }),

  getters: {
    items: (state) => state.data?.items ?? [],
    taskCount: (state) => state.data?.taskCount ?? 0,
    planCount: (state) => state.data?.planCount ?? 0,
    deviationCount: (state) => state.data?.deviationCount ?? 0,
    forTask() {
      return (taskId) => conflictsOf(this.items, taskId)
    }
  },

  actions: {
    async load() {
      try {
        const { data } = await planningApi.getConflicts()
        this.data = data
      } catch {
        // Ошибка — остаётся последнее известное состояние; опрос продолжается, без сообщения раз в минуту
      }
    },

    start() {
      if (this.timer) return
      this.load()
      this.timer = setInterval(() => this.load(), PLAN_CONFLICTS_REFRESH_MS)
    },

    stop() {
      clearInterval(this.timer)
      this.timer = null
    }
  }
})
```

Create `src/components/planning/PlanConflictsBadge.vue`:

```vue
<template>
  <el-badge
    v-if="store.taskCount > 0"
    :value="store.taskCount"
    :type="store.planCount > 0 ? 'danger' : 'warning'"
    class="ml-2"
    data-test="plan-conflicts-badge"
    :title="`Требуют корректировки: ${store.taskCount}`"
  />
</template>

<script setup>
import { usePlanConflictsStore } from '@/stores/planConflicts'

// Бейдж «N заданий требуют корректировки» (PZP-07b): красный — есть конфликт плана, иначе оранжевый.
const store = usePlanConflictsStore()
</script>
```

`DefaultLayout.vue`:
- import `PlanConflictsBadge` and `usePlanConflictsStore`;
- in the «Планирование» submenu title after `<span>Планирование</span>` add `<PlanConflictsBadge />`; the Гантт item becomes `<el-menu-item index="/planning/gantt">План-график цеха <PlanConflictsBadge /></el-menu-item>`;
- script:

```js
const planConflicts = usePlanConflictsStore()
const watchesConflicts = canAccess(['Admin', 'Manager', 'Observer'])

onMounted(() => {
  appInfo.load()
  if (watchesConflicts) planConflicts.start()
  timer = setInterval(() => {
    currentTime.value = dayjs().format('DD.MM.YYYY HH:mm')
  }, 1000)
})

onUnmounted(() => {
  if (timer) clearInterval(timer)
  planConflicts.stop()
})
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd Wintime-Control-Frontend; npx vitest run src/utils/__tests__/planConflicts.spec.js src/stores/__tests__/planConflicts.spec.js src/components/planning/__tests__/PlanConflictsBadge.spec.js`
Expected: PASS. Then `npx vitest run` — whole suite green.

- [ ] **Step 5: Commit**

```bash
git add Wintime-Control-Frontend/src
git commit -m "feat(planning-ui): plan conflicts store, utils and menu badge (PZP-07b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Гантт — рамки пар, тултип, прогноз `Issued`/`Setup`, баннер

**Files:**
- Modify: `Wintime-Control-Frontend/src/utils/planningBoard.js` (`boardSegments`, `tooltipLines`)
- Modify: `Wintime-Control-Frontend/src/components/planning/GanttTaskPair.vue`
- Create: `Wintime-Control-Frontend/src/components/planning/PlanConflictsBanner.vue`
- Modify: `Wintime-Control-Frontend/src/views/planning/PlanningGanttView.vue`
- Test: `src/utils/__tests__/planningBoard.spec.js`, `src/components/planning/__tests__/GanttTaskPair.spec.js`, `src/components/planning/__tests__/PlanConflictsBanner.spec.js`, `src/views/planning/__tests__/PlanningGanttView.spec.js`

**Interfaces:**
- Consumes: `item.conflicts`, `item.fact.forecastStart` (Task 6); `worstKind`, `conflictLine`, `CONFLICT_KIND_LABELS`, `CONFLICT_KIND_COLORS`, `taskRefLabel`, `usePlanConflictsStore` (Task 8).
- Produces: `<PlanConflictsBanner @select="(taskRef) => …" />` — `taskRef` = `conflict.task` строки (`{ id, plannedStartAt, … }`). `GanttTaskPair` — класс `pair--conflict-plan` / `pair--conflict-deviation`, атрибут `data-conflict="Plan|Deviation"`.

- [ ] **Step 1: Write the failing tests**

Append to `src/utils/__tests__/planningBoard.spec.js` (reuse the file's existing time helpers; if it has none, define `const MIN = 60_000; const T0 = new Date(2026, 9, 15, 10, 0).getTime(); const at = (m) => new Date(T0 + m * MIN).toISOString()`):

```js
describe('PZP-07b: прогноз Issued и строки конфликтов', () => {
  const issued = {
    task: { id: 't1', status: 'Issued', moldName: 'ПФ-1', immName: 'ТПА-1', planQuantity: 100, plannedSetupMinutes: 30 },
    productArticle: 'A1.0',
    plan: { setupStart: at(0), setupEnd: at(30), releaseEnd: at(90), bufferEnd: at(100) },
    fact: { setupStart: null, setupEnd: null, releaseEnd: null, forecastEnd: at(120), forecastStart: at(30) },
    bufferRemainingSeconds: null, overdueNotStartedSeconds: 1200, setupLagSeconds: null,
    conflicts: [{ kind: 'Deviation', resource: 'Imm', overlapSeconds: 2400,
      task: { id: 't0', productArticle: 'A0.0', moldName: 'ПФ-0', immName: 'ТПА-1' },
      other: { id: 't1', productArticle: 'A1.0', moldName: 'ПФ-1', immName: 'ТПА-1' } }]
  }

  it('прогноз Issued начинается с forecastStart, а не с «сейчас»', () => {
    const { fact } = boardSegments(issued, new Date(T0 + 20 * MIN))
    expect(fact).toEqual([{ kind: 'forecast', start: T0 + 30 * MIN, end: T0 + 120 * MIN }])
  })

  it('тултип содержит строку конфликта', () => {
    expect(tooltipLines(issued)).toContain('Конфликт отклонения по ТПА с «A0.0 · ПФ-0 · ТПА-1», пересечение 40м')
  })
})
```

Append to `GanttTaskPair.spec.js`:

```js
  it('рамка конфликта плана и отклонения', () => {
    const conflict = (kind) => ({ kind, resource: 'Imm', overlapSeconds: 600,
      task: { id: 't1', moldName: 'ПФ-1' }, other: { id: 't2', moldName: 'ПФ-2' } })
    const plan = mountPair({ ...overrunItem, conflicts: [conflict('Deviation'), conflict('Plan')] })
    expect(plan.find('.pair').attributes('data-conflict')).toBe('Plan')
    expect(plan.find('.pair').classes()).toContain('pair--conflict-plan')

    const dev = mountPair({ ...overrunItem, conflicts: [conflict('Deviation')] })
    expect(dev.find('.pair').classes()).toContain('pair--conflict-deviation')

    const none = mountPair(overrunItem)
    expect(none.find('.pair').attributes('data-conflict')).toBeUndefined()
  })
```

Create `src/components/planning/__tests__/PlanConflictsBanner.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import PlanConflictsBanner from '../PlanConflictsBanner.vue'
import { usePlanConflictsStore } from '@/stores/planConflicts'

const ref = (id) => ({ id, immName: `ТПА-${id}`, moldName: `ПФ-${id}`, productArticle: `A${id}.0`, plannedStartAt: '2026-10-21T07:00:00Z' })
const mountBanner = () => mount(PlanConflictsBanner, { global: { plugins: [ElementPlus] } })

describe('PlanConflictsBanner', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('нет проблем — баннера нет', () => {
    expect(mountBanner().find('[data-test="plan-conflicts-banner"]').exists()).toBe(false)
  })

  it('счётчики, раскрытие списка, клик по строке — select с заданием', async () => {
    usePlanConflictsStore().data = {
      taskCount: 3, planCount: 1, deviationCount: 1,
      items: [
        { kind: 'Plan', resource: 'Imm', task: ref('1'), other: ref('2'), overlapSeconds: 3000 },
        { kind: 'Deviation', resource: 'Mold', task: ref('3'), other: ref('1'), overlapSeconds: 1500 }
      ]
    }
    const w = mountBanner()
    expect(w.text()).toContain('3 задания требуют корректировки: 1 конфликт плана, 1 конфликт отклонения')

    await w.find('[data-test="plan-conflicts-toggle"]').trigger('click')
    const rows = w.findAll('[data-test="plan-conflict-row"]')
    expect(rows).toHaveLength(2)
    expect(rows[0].text()).toContain('A1.0 · ПФ-1 · ТПА-1')

    await rows[1].trigger('click')
    expect(w.emitted('select')[0][0].id).toBe('3')
  })
})
```

In `PlanningGanttView.spec.js` — the file mocks `@/api/planning`; add `getConflicts: vi.fn(() => Promise.resolve({ data: { taskCount: 0, planCount: 0, deviationCount: 0, items: [] } }))` to that mock and add a test:

```js
  it('загрузка доски обновляет и конфликты', async () => {
    const w = await mountView()   // используй существующий хелпер монтирования из файла
    await flushPromises()
    expect(planningApi.getConflicts).toHaveBeenCalled()
  })
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd Wintime-Control-Frontend; npx vitest run src/utils/__tests__/planningBoard.spec.js src/components/planning src/views/planning`
Expected: FAIL.

- [ ] **Step 3: Implement**

`planningBoard.js`:
- import `{ conflictLine }` from `./planConflicts`;
- in `boardSegments` replace the forecast line with:

```js
  if (f.forecastEnd) fact.push(seg('forecast', f.forecastStart ?? n, f.forecastEnd))
```

- at the end of `tooltipLines`, before `return lines`:

```js
  for (const c of item.conflicts ?? []) lines.push(conflictLine(c, task.id))
```

`GanttTaskPair.vue`:
- template root: add `:class="['pair', conflictKind && `pair--conflict-${conflictKind.toLowerCase()}`]"` (replace `class="pair"`) and `:data-conflict="conflictKind || undefined"`;
- script: `import { worstKind } from '@/utils/planConflicts'` and `const conflictKind = computed(() => worstKind(props.item.conflicts))`;
- style:

```css
/* Конфликты плана (PZP-07b): красная рамка — конфликт плана, оранжевая — отклонения */
.pair--conflict-plan { outline: 2px solid #f56c6c; outline-offset: 1px; border-radius: 2px; }
.pair--conflict-deviation { outline: 2px solid #e6a23c; outline-offset: 1px; border-radius: 2px; }
```

Create `src/components/planning/PlanConflictsBanner.vue`:

```vue
<template>
  <el-alert
    v-if="store.taskCount > 0"
    :type="store.planCount > 0 ? 'error' : 'warning'"
    :closable="false"
    show-icon
    class="mb-4"
    data-test="plan-conflicts-banner"
  >
    <template #title>
      <span>{{ title }}</span>
      <el-button link type="primary" class="ml-2" data-test="plan-conflicts-toggle" @click="expanded = !expanded">
        {{ expanded ? 'Скрыть' : 'Показать' }}
      </el-button>
    </template>
    <ul v-if="expanded" class="mt-2 space-y-1">
      <li
        v-for="(c, i) in store.items"
        :key="i"
        class="cursor-pointer hover:underline"
        :style="{ color: CONFLICT_KIND_COLORS[c.kind] }"
        data-test="plan-conflict-row"
        @click="$emit('select', c.task)"
      >
        {{ taskRefLabel(c.task) }} — {{ conflictLine(c, c.task.id) }}
      </li>
    </ul>
  </el-alert>
</template>

<script setup>
import { ref, computed } from 'vue'
import { usePlanConflictsStore } from '@/stores/planConflicts'
import { CONFLICT_KIND_COLORS, taskRefLabel, conflictLine } from '@/utils/planConflicts'

// Баннер «N заданий требуют корректировки» на Гантте (PZP-07b); клик по строке — задание на доске и его карточка.
defineEmits(['select'])

const store = usePlanConflictsStore()
const expanded = ref(false)

// 1 задание / 2 задания / 5 заданий
function plural(n, one, few, many) {
  const m10 = n % 10
  const m100 = n % 100
  if (m10 === 1 && m100 !== 11) return one
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few
  return many
}

const title = computed(() => {
  const n = store.taskCount
  const parts = []
  if (store.planCount) parts.push(`${store.planCount} ${plural(store.planCount, 'конфликт плана', 'конфликта плана', 'конфликтов плана')}`)
  if (store.deviationCount) parts.push(`${store.deviationCount} ${plural(store.deviationCount, 'конфликт отклонения', 'конфликта отклонения', 'конфликтов отклонения')}`)
  const verb = plural(n, 'требует', 'требуют', 'требуют')
  return `${n} ${plural(n, 'задание', 'задания', 'заданий')} ${verb} корректировки: ${parts.join(', ')}`
})
</script>
```

`PlanningGanttView.vue`:
- import `PlanConflictsBanner`, `usePlanConflictsStore`, `tasksApi` from `@/api/tasks`;
- template: right after the header `div`, before `<el-card>`: `<PlanConflictsBanner @select="openConflictTask" />`;
- pass `@open-task="openTaskById"` to `<TaskDetailModal>` (handled in Task 10; add the handler here now);
- script:

```js
const planConflicts = usePlanConflictsStore()

// в load(), сразу после `refreshSelectedTask(data)`:
//    planConflicts.load()

// Строка баннера → период планового начала задания (масштаб тот же) и его карточка
async function openConflictTask(taskRef) {
  anchor.value = new Date(taskRef.plannedStartAt)
  await openTaskById(taskRef.id)
}

async function openTaskById(id) {
  try {
    const { data } = await tasksApi.getById(id)
    openDetail(data)
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Не удалось открыть задание'))
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd Wintime-Control-Frontend; npx vitest run src/utils src/components/planning src/views/planning`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime-Control-Frontend/src
git commit -m "feat(planning-ui): conflict outlines, tooltip lines, Issued/Setup forecast and banner on Gantt (PZP-07b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Карточка, список и форма задания

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/tasks/TaskDetailModal.vue`
- Modify: `Wintime-Control-Frontend/src/views/tasks/TasksView.vue`
- Modify: `Wintime-Control-Frontend/src/views/tasks/TaskFormModal.vue` (`handleSubmit`, ~стр. 683–720)
- Test: `src/views/tasks/__tests__/TaskDetailModal.spec.js`, `src/views/tasks/__tests__/TaskFormModal.spec.js`

**Interfaces:**
- Consumes: `usePlanConflictsStore().forTask(id)`, `.load()`; `conflictLine`, `otherSide`, `worstKind`, `CONFLICT_KIND_COLORS`, `CONFLICT_KIND_LABELS` (Task 8).
- Produces: `TaskDetailModal` emits `open-task(taskId)` (не в `readonly`).

- [ ] **Step 1: Write the failing tests**

Append to `TaskDetailModal.spec.js` (`mountModal` there calls `setActivePinia(createPinia())` — fill the store after mounting and `await nextTick()`; import `nextTick` from `vue` and `usePlanConflictsStore`):

```js
describe('TaskDetailModal — конфликты (PZP-07b)', () => {
  const items = [{ kind: 'Plan', resource: 'Imm', overlapSeconds: 3000,
    task: { id: 't1', productArticle: 'A1.0', moldName: 'ПФ-1', immName: 'ТПА-1' },
    other: { id: 't2', productArticle: 'A2.0', moldName: 'ПФ-2', immName: 'ТПА-1' } }]

  it('показывает блок конфликтов, клик по строке — open-task с другим заданием', async () => {
    const wrapper = mountModal({ id: 't1', status: 'Planned' })
    usePlanConflictsStore().data = { taskCount: 2, planCount: 1, deviationCount: 0, items }
    await nextTick()

    const block = wrapper.find('[data-test="task-conflicts"]')
    expect(block.text()).toContain('Конфликт плана по ТПА с «A2.0 · ПФ-2 · ТПА-1», пересечение 50м')
    await block.find('[data-test="task-conflict-row"]').trigger('click')
    expect(wrapper.emitted('open-task')[0]).toEqual(['t2'])
  })

  it('нет конфликтов — нет блока', async () => {
    const wrapper = mountModal({ id: 't9', status: 'Planned' })
    usePlanConflictsStore().data = { taskCount: 2, planCount: 1, deviationCount: 0, items }
    await nextTick()
    expect(wrapper.find('[data-test="task-conflicts"]').exists()).toBe(false)
  })
})
```

Append to `TaskFormModal.spec.js` — add a mock at the top, next to the other `vi.mock` calls:

```js
vi.mock('@/api/planning', () => ({
  planningApi: { getConflicts: vi.fn(() => Promise.resolve({ data: { taskCount: 0, planCount: 0, deviationCount: 0, items: [] } })) }
}))
```

and a test, following the file's existing create-flow test (`при создании задания передаёт productId в tasksApi.create` — copy its setup of form fields and `wrapper.vm.handleSubmit()`):

```js
  it('после создания с конфликтами — предупреждение с числом конфликтов', async () => {
    const { planningApi } = await import('@/api/planning')
    const { tasksApi } = await import('@/api/tasks')
    tasksApi.create.mockResolvedValueOnce({ data: { id: 'new-1' } })
    planningApi.getConflicts.mockResolvedValueOnce({ data: { taskCount: 2, planCount: 1, deviationCount: 0, items: [
      { kind: 'Plan', resource: 'Imm', overlapSeconds: 600, task: { id: 'new-1' }, other: { id: 'x' } }] } })
    const warning = vi.spyOn(ElMessage, 'warning')

    // …заполнить форму как в тесте «передаёт productId в tasksApi.create»…
    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(warning).toHaveBeenCalledWith(expect.stringContaining('Конфликтов: 1'))
    warning.mockRestore()
  })
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd Wintime-Control-Frontend; npx vitest run src/views/tasks`
Expected: FAIL.

- [ ] **Step 3: Implement**

`TaskDetailModal.vue`:
- script: `import { usePlanConflictsStore } from '@/stores/planConflicts'`, `import { conflictLine, otherSide, CONFLICT_KIND_COLORS } from '@/utils/planConflicts'`; add `'open-task'` to `defineEmits`;

```js
const planConflicts = usePlanConflictsStore()
const taskConflicts = computed(() => (props.task?.id ? planConflicts.forTask(props.task.id) : []))
```

- template: in the «Данные задания» tab, right before the «Примечания» card:

```vue
          <!-- Конфликты плана (PZP-07b): подсветка, ничего не блокирует -->
          <el-card v-if="taskConflicts.length" class="mb-6" data-test="task-conflicts">
            <template #header>
              <span class="font-semibold">Конфликты</span>
            </template>
            <ul class="space-y-1">
              <li
                v-for="(c, i) in taskConflicts"
                :key="i"
                data-test="task-conflict-row"
                :class="readonly ? '' : 'cursor-pointer hover:underline'"
                :style="{ color: CONFLICT_KIND_COLORS[c.kind] }"
                @click="!readonly && $emit('open-task', otherSide(c, task.id).id)"
              >
                {{ conflictLine(c, task.id) }}
              </li>
            </ul>
          </el-card>
```

`TasksView.vue`:
- script: `import { usePlanConflictsStore } from '@/stores/planConflicts'`, `import { worstKind, CONFLICT_KIND_COLORS, CONFLICT_KIND_LABELS } from '@/utils/planConflicts'`, `import { tasksApi } from '@/api/tasks'` (if not imported);

```js
const planConflicts = usePlanConflictsStore()
const conflictKindOf = (task) => worstKind(planConflicts.forTask(task.id))

const openTaskById = async (id) => {
  try {
    const { data } = await tasksApi.getById(id)
    selectedTask.value = data
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Не удалось открыть задание'))
  }
}
```

(import `apiErrorMessage` from `@/utils/apiError` if missing).
- template: first column of `el-table`:

```vue
        <el-table-column width="40" align="center">
          <template #default="{ row }">
            <el-icon
              v-if="conflictKindOf(row)"
              :color="CONFLICT_KIND_COLORS[conflictKindOf(row)]"
              :title="CONFLICT_KIND_LABELS[conflictKindOf(row)]"
              data-test="task-conflict-icon"
            ><WarningFilled /></el-icon>
          </template>
        </el-table-column>
```

- on `<TaskDetailModal …>` add `@open-task="openTaskById"`.

`TaskFormModal.vue`:
- script: `import { usePlanConflictsStore } from '@/stores/planConflicts'`; `const planConflicts = usePlanConflictsStore()`;
- add helper and use it in both branches of `handleSubmit` instead of `ElMessage.success(...)`:

```js
// PZP-07b: сохранение не блокируется; если у задания есть конфликты — предупредить и отправить на план-график
const notifySaved = async (taskId, successText) => {
  await planConflicts.load()
  const count = taskId ? planConflicts.forTask(taskId).length : 0
  if (count > 0) ElMessage.warning(`Задание сохранено. Конфликтов: ${count} — см. план-график цеха`)
  else ElMessage.success(successText)
}
```

Edit branch: `await notifySaved(editingTask.value.id, 'Задание обновлено')`.
Create branch:

```js
        const { data: created } = await tasksApi.create({ ...form, personnelId: form.personnelId || null })
        await notifySaved(created?.id,
          'Задание создано в статусе «Планируется». Чтобы наладчик его увидел, нажмите «Выдать»')
```

(`planConflicts.load()` never throws — ошибка загрузки оставит прошлое состояние, покажется обычное «сохранено».)

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd Wintime-Control-Frontend; npx vitest run src/views/tasks src/views/planning`; then `npx vitest run` (whole suite).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime-Control-Frontend/src
git commit -m "feat(tasks-ui): conflicts in task card and list, warning after save (PZP-07b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: ADR-0022, документация, версия, финальная проверка

**Files:**
- Create: `docs/adr/0022-plan-conflicts.md`
- Modify: `docs/adr/README.md`, `CLAUDE.md`, `docs/domain/shift-task.md`, `docs/user-guide/manager.md`, `docs/user-guide/adjuster.md`, `VERSION`

- [ ] **Step 1: ADR**

Create `docs/adr/0022-plan-conflicts.md`:

```markdown
# ADR-0022: Конфликты плана — вычисляемые, без блокировок, прогноз выполнения по стадиям

- **Статус:** Accepted
- **Дата:** 2026-10-20
- **Связано:** [ADR-0014](0014-planned-start-date-time.md), [ADR-0015](0015-application-settings.md), [ADR-0017](0017-release-forecast-mean-cycle-period.md); эпик PZP-07, этап 07b (поглотил 07f)

## Контекст

На план-графике цеха (PZP-07a) видно опоздание задания и перерасход буфера, но не видно, что из-за этого
другое задание не может быть выполнено в своё время: ТПА или пресс-форма заняты. Пересечения плана с
планом не замечались, одну ПФ можно было запланировать на два ТПА одновременно и начать на ней наладку,
пока она в работе на другом ТПА.

В цехе две роли руководителя: планировщик (план на неделю/месяц, стыковка заданий по ТПА и ПФ) и мастер
(выполнение в смену, наладчики, оперативное устранение проблем). Отсюда два этапа обнаружения: ошибка
плана и отклонение выполнения от плана. В ЧП некогда править план — работают не по плану и приводят план
в порядок позже, поэтому система не должна мешать движению задания.

## Решение

1. **Ресурсы — ТПА и пресс-форма** (ПФ — на любом ТПА). Участвуют задания `Planned`, `Issued`, `Setup`,
   `InProgress`.
2. **Отрезки задания** (`TaskOccupancy`, не хранятся): `Planned` и `Issued` до планового начала — план
   `[начало наладки, конец выпуска + буфер)`; дальше — факт `[начало наладки, сейчас)` и прогноз конца
   выпуска **без буфера** по стадии: не начатое просроченное — наладка через 10 мин + норматив + выпуск;
   наладка в нормативе — от фактического начала; наладка сверх норматива — через 10 мин + выпуск; выпуск —
   `ReleaseForecast` (ADR-0017). 10 минут — константа.
3. **Конфликт** — суммарное пересечение отрезков двух заданий строго больше порога. Порог — глобальная
   настройка `PlanningDefaults.ConflictThresholdMinutes` (по умолчанию 20 мин), не снапшотится в задание.
4. **Два вида:** конфликт плана (оба задания только с планом, красный) и конфликт отклонения (есть факт или
   прогноз, оранжевый).
5. **Ничего не блокируется.** Конфликты подсвечиваются: бейдж в меню, баннер и рамки на Гантте, карточка,
   список, уведомление после сохранения. Единственное новое подтверждение — старт наладки с ПФ, которая
   сейчас в работе на другом ТПА: 409 с `moldBusy`, снимается флагом `confirmMoldBusy`.
6. **Расчёт на лету на сервере** (`PlanConflictService`) по всем незавершённым заданиям — `GET
   /api/planning/conflicts` и поле `conflicts` доски.
7. **Архив ТПА/ПФ** с незавершёнными заданиями запрещён; гнёзд ПФ ≥ 1; эталон полного цикла обязателен и
   при правке задания — у любого незавершённого задания есть план и активные ресурсы.

## Рассмотренные альтернативы

- **Блокировать выдачу задания в конфликте и требовать подтверждения при сохранении** — отвергнуто: мешает
  работе в ЧП и многошаговому перепланированию; решение о плане остаётся за человеком.
- **Хранимая таблица конфликтов, пересчёт по событиям** — отвергнуто: прогноз меняется с каждым циклом,
  конфликты возникают от хода времени и смены гнёздности — таблица была бы вечно устаревшей; плюс схема БД.
- **Детекция на фронте по данным доски** — отвергнуто: доска видит только свой период, бейджу нужна вся
  картина, а прогноз — серверная логика.
- **Буфер в прогнозе** — отвергнуто: буфер — запас плана на отклонения, прогноз их уже учитывает.
- **Без порога** — отвергнуто: пользователь разбирал бы пересечения в несколько секунд.

## Последствия

- Отдельный этап 07f (занятость ПФ) не нужен.
- Стоимость: один запрос незавершённых заданий, агрегат циклов только по `InProgress`, строка настроек;
  детекция в памяти внутри групп ТПА/ПФ. При десятках клиентов и сотнях заданий — без кэша.
- Запись события «наладка с занятой ПФ» в журнал руководителя — отдельная задача BL-47.
```

> Если ссылки на ADR-0014/0015 называются иначе — сверь `ls docs/adr` и поправь имена файлов.

Add to `docs/adr/README.md` after the ADR-0021 line:

```markdown
- [ADR-0022](0022-plan-conflicts.md) — Конфликты плана: вычисляемые, без блокировок, прогноз выполнения по стадиям
```

- [ ] **Step 2: CLAUDE.md**

After the section «Задание длиннее смены — окно активности» add:

```markdown
### Конфликты плана — подсвечивают, не блокируют

Ресурсы — ТПА и ПФ (на любом ТПА); участвуют `Planned`/`Issued`/`Setup`/`InProgress`. Отрезки —
`TaskOccupancy` (план с буфером до начала; дальше факт + прогноз конца выпуска без буфера по стадиям,
задержка 10 мин — константа), пары — `PlanConflictDetector`: пересечение > `PlanningDefaults.ConflictThresholdMinutes`
(20 мин, не снапшот). Вид: только план — конфликт плана, иначе — конфликт отклонения. Не хранятся, считает
`IPlanConflictService` (`GET /api/planning/conflicts`). Сохранение, выдача и статусы **не блокируются**; старт
наладки с ПФ, занятой на другом ТПА, — 409 `moldBusy` + `confirmMoldBusy`. ТПА/ПФ с незавершёнными заданиями в
архив не переводятся. См. ADR-0022.
```

In the `IsActive` section append the sentence: «ТПА/ПФ с незавершёнными заданиями (`Planned`…`InProgress`) в архив перевести нельзя (PZP-07b).»

- [ ] **Step 3: Domain doc and user guides**

`docs/domain/shift-task.md` — after the subsection «План-график: остаток буфера, перерасход, отставание (PZP-07a)» add a subsection «Конфликты плана (PZP-07b)»: таблица стадий отрезков (Planned, А–Е из Global Constraints), определение конфликта (порог, два вида), что ничего не блокируется, ссылка на ADR-0022.

`docs/user-guide/manager.md` — in the «План-график цеха» section add «Конфликты»: бейдж у «Планирования» (красный — есть конфликт плана, оранжевый — только отклонения), баннер «N заданий требуют корректировки» и переход по строке, рамки на барах и строки в подсказке, блок «Конфликты» в карточке и значок в списке заданий, уведомление после сохранения, порог в «Настройки → Конфликты плана», запрет архивировать ТПА/ПФ с незавершёнными заданиями. Пояснить, что конфликт ничего не запрещает: решение — сдвинуть задание в карточке или оставить как есть.

`docs/user-guide/adjuster.md` — in the section about starting setup add: если пресс-форма сейчас используется на другом ТПА, приложение предупредит «Пресс-форма сейчас используется на ТПА …» и спросит, начать ли наладку; можно продолжить.

- [ ] **Step 4: VERSION**

Replace `1.18.3` with `1.19.0` in `VERSION`.

- [ ] **Step 5: Final verification**

Run, from repo root:

```bash
dotnet build
dotnet test Wintime.Control.Tests.Unit
dotnet test Wintime.Control.Tests.Integration
cd Wintime-Control-Frontend
npx vitest run
npm run build
```

Expected: build OK, all tests green, frontend build OK. Record the counts in the commit body.

- [ ] **Step 6: Commit**

```bash
git add docs CLAUDE.md VERSION
git commit -m "docs(planning): ADR-0022 plan conflicts, CLAUDE.md, domain and user guides, v1.19.0 (PZP-07b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 7: PR**

```bash
git push -u origin feature/pzp-07b-conflicts
gh pr create --base epic/pzp-07-planning --title "PZP-07b: конфликты плана — ТПА и пресс-форма (v1.19.0)" --body "<summary of tasks, test counts, ADR-0022; ends with: 🤖 Generated with [Claude Code](https://claude.com/claude-code)>"
```
