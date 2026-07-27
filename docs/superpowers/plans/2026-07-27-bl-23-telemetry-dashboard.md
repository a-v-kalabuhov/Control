# BL-23 — Дашборд сырой телеметрии и циклов: план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Десктоп-экран одного ТПА, показывающий сырую телеметрию (ступенчатые тренды по сигналам) и границы циклов на общей оси времени, с фоном эффективного статуса и синхронным crosshair, в двух режимах — история (≤4 ч) и живые данные (polling).

**Architecture:** Один агрегирующий read-эндпоинт `GET /api/imm/{id}/telemetry-dashboard` собирает окно `[from, to]` из таблиц `Telemetry`, `ImmCycle` и рядов эффективного статуса (`EffectiveStatusTimeline.Build`). Логика сбора статус-рядов выносится в приватный хелпер и переиспользуется существующим `effective-status-history`. Фронт (Vue 3 + echarts) рисует столбик диаграмм «один сигнал = одна диаграмма», связанных общим axisPointer. Неиспользуемый старый `GET .../telemetry` удаляется.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql, xUnit + FluentAssertions; Vue 3 + Pinia + Element Plus + Tailwind, echarts 6, Vitest.

## Global Constraints

- **UTC:** все `DateTime` в EF-запросах к Postgres обязаны иметь `Kind=Utc`. Параметры из query string биндятся как `Unspecified` — конвертировать сразу через `DateTime.SpecifyKind(x, DateTimeKind.Utc)`. Никогда не передавать `Unspecified`-дату в `.Where()`.
- **Доступ:** новый эндпоинт — `[Authorize(Policy = "ManagerOrAdmin")]`. Наладчик (Adjuster) на десктоп-телеметрию не заходит.
- **Роль — только `User.Role`.** Не трогать Identity-роли.
- **`IsActive`** — архивный флаг, физически ничего не удалять (к этой фиче не относится напрямую, но соблюдать).
- **Тесты:** xUnit для .NET (Unit/Integration), Vitest для фронта. TDD: сначала падающий тест.
- **Максимум окна:** 4 часа (оба режима). Значение — из `TelemetryDashboardSettings.MaxWindowHours`.
- **Спека:** `docs/superpowers/specs/2026-07-27-bl-23-telemetry-dashboard-design.md`.

---

## Обзор файлов

**Backend (создать):**
- `Wintime.Control.Shared/Settings/TelemetryDashboardSettings.cs` — настройки гардов (окно, число точек).
- `Wintime.Control.Core/DTOs/Imm/TelemetryDashboardDto.cs` — DTO ответа + вложенные (`TelemetrySignalDto`, `TelemetryPointDto`, `TelemetryCycleDto`, `TelemetrySignalMetaDto`).

**Backend (изменить):**
- `Wintime.Control.Infrastructure/Data/ControlDbContext.cs` — составной индекс `IX_Telemetry_Imm_Param_Time`.
- `Wintime.Control.API/Program.cs` — регистрация `TelemetryDashboardSettings`.
- `Wintime.Control.API/appsettings.json` + `appsettings.Development.json` — секция `TelemetryDashboard`.
- `Wintime.Control.API/Controllers/ImmController.cs` — DI (`ITemplateCache`, `IOptions<TelemetryDashboardSettings>`); приватный хелпер `GatherEffectiveStatusInputsAsync`; рефактор `effective-status-history` на хелпер; новые эндпоинты `/signals` и `/telemetry-dashboard`; **удаление** `/telemetry`.

**Backend (удалить):**
- `Wintime.Control.Core/DTOs/Imm/TelemetryDto.cs`.

**Frontend (создать):**
- `Wintime-Control-Frontend/src/utils/telemetryChart.js` — чистые хелперы (step-серия, тип оси, слияние live-точек).
- `Wintime-Control-Frontend/src/utils/__tests__/telemetryChart.spec.js` — Vitest.
- `Wintime-Control-Frontend/src/api/telemetry.js` — api-обёртки.
- `Wintime-Control-Frontend/src/components/telemetry/SignalChart.vue` — одна диаграмма = один сигнал.
- `Wintime-Control-Frontend/src/views/telemetry/TelemetryDashboardView.vue` — страница.

**Frontend (изменить):**
- `Wintime-Control-Frontend/src/router/index.js` — route `imm/:id/telemetry`.
- `Wintime-Control-Frontend/src/views/dashboard/ImmDetailModal.vue` — кнопка «Телеметрия» (drill-in).
- `Wintime-Control-Frontend/src/api/imm.js` — удалить мёртвую `getTelemetry`.
- `Wintime-Control-Frontend/src/api/dashboard.js` — удалить мёртвую `getImmTelemetry`.

**Порядок:** Task 1 (индекс) → Task 2 (рефактор-хелпер) → Task 3 (эндпоинты + удаление старого) → Task 4 (фронт-утилиты) → Task 5 (api + SignalChart) → Task 6 (страница + route + drill-in).

---

### Task 1: Составной индекс телеметрии + миграция

**Files:**
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs:83-90`
- Create: `Wintime.Control.Infrastructure/Migrations/<timestamp>_AddTelemetryDashboardIndex.cs` (генерируется EF)

**Interfaces:**
- Consumes: —
- Produces: индекс `IX_Telemetry_Imm_Param_Time` на `Telemetry(ImmId, ParameterName, Timestamp)`.

- [ ] **Step 1: Добавить индекс в конфигурацию Telemetry**

В `ControlDbContext.cs`, в блоке `builder.Entity<Telemetry>(...)` (сейчас строки 84-90), добавить составной индекс. Итоговый блок:

```csharp
// Конфигурация Telemetry (Оптимизация)
builder.Entity<Telemetry>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.HasIndex(e => new { e.ImmId, e.Timestamp });
    entity.HasIndex(e => e.ParameterName);
    // BL-23: покрывающий индекс под «окно + выбранные сигналы одного ТПА».
    entity.HasIndex(e => new { e.ImmId, e.ParameterName, e.Timestamp })
          .HasDatabaseName("IX_Telemetry_Imm_Param_Time");
});
```

- [ ] **Step 2: Сгенерировать миграцию**

Run:
```powershell
dotnet ef migrations add AddTelemetryDashboardIndex --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: создан файл миграции; в методе `Up` — `migrationBuilder.CreateIndex(name: "IX_Telemetry_Imm_Param_Time", table: "Telemetry", columns: new[] { "ImmId", "ParameterName", "Timestamp" });`

- [ ] **Step 3: Проверить сборку**

Run: `dotnet build Wintime.Control.API`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```powershell
git add Wintime.Control.Infrastructure/Data/ControlDbContext.cs Wintime.Control.Infrastructure/Migrations
git commit -m "feat(BL-23): составной индекс IX_Telemetry_Imm_Param_Time"
```

---

### Task 2: Вынести сбор статус-рядов в общий хелпер (рефактор без изменения поведения)

Существующий `GET .../effective-status-history` сам собирает `raw/tasks/downtimes`. Новый эндпоинт (Task 3) должен переиспользовать эту логику. Выносим её в приватный метод; существующие интеграционные тесты `EffectiveStatusHistoryTests` должны остаться зелёными.

**Files:**
- Modify: `Wintime.Control.API/Controllers/ImmController.cs:396-461`
- Test (существует, не менять): `Wintime.Control.Tests.Integration/Imm/EffectiveStatusHistoryTests.cs`

**Interfaces:**
- Consumes: `EffectiveStatusTimeline.Build`, `RawSegment`, `TaskInterval`, `Interval` (Core.Policies).
- Produces: приватный метод
  `Task<(List<RawSegment> raw, List<TaskInterval> tasks, List<Interval> downtimes)> GatherEffectiveStatusInputsAsync(Guid id, DateTime fromUtc, DateTime toUtc, DateTime effectiveTo)`.

- [ ] **Step 1: Убедиться, что существующие тесты зелёные (базовая линия)**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~EffectiveStatusHistoryTests`
Expected: PASS (2 теста).

- [ ] **Step 2: Добавить приватный хелпер в ImmController**

Вставить метод в `ImmController` (например, сразу после `GetImmEffectiveStatusHistory`). Это дословный перенос текущей логики сбора рядов (строки ~413-449):

```csharp
private async Task<(List<RawSegment> raw, List<TaskInterval> tasks, List<Interval> downtimes)>
    GatherEffectiveStatusInputsAsync(Guid id, DateTime fromUtc, DateTime toUtc, DateTime effectiveTo)
{
    DateTime ClampEnd(DateTime? end) => (end ?? effectiveTo) > effectiveTo ? effectiveTo : (end ?? effectiveTo);

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

    return (raw, tasks, downtimes);
}
```

- [ ] **Step 3: Переписать тело `GetImmEffectiveStatusHistory` на хелпер**

Заменить блок сбора рядов (строки ~413-449) вызовом хелпера. Тело метода после `effectiveTo` становится:

```csharp
var nowUtc = DateTime.UtcNow;
var effectiveTo = toUtc < nowUtc ? toUtc : nowUtc;

var (raw, tasks, downtimes) = await GatherEffectiveStatusInputsAsync(id, fromUtc, toUtc, effectiveTo);

var segments = EffectiveStatusTimeline.Build(raw, tasks, downtimes, fromUtc, effectiveTo);

var dto = segments.Select(s => new EffectiveStatusSegmentDto
{
    EffectiveStatus = s.EffectiveStatus,
    ChangedAt = s.Start,
    EndedAt = s.End,
});

return Ok(dto);
```

(Строки `fromUtc`/`toUtc` через `SpecifyKind` и проверка `immExists` остаются как были, до этого блока.)

- [ ] **Step 4: Проверить, что тесты по-прежнему зелёные**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~EffectiveStatusHistoryTests`
Expected: PASS (2 теста) — поведение не изменилось.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.API/Controllers/ImmController.cs
git commit -m "refactor(BL-23): вынести сбор статус-рядов в GatherEffectiveStatusInputsAsync"
```

---

### Task 3: Эндпоинты `/signals` и `/telemetry-dashboard` + удаление старого `/telemetry`

**Files:**
- Create: `Wintime.Control.Shared/Settings/TelemetryDashboardSettings.cs`
- Create: `Wintime.Control.Core/DTOs/Imm/TelemetryDashboardDto.cs`
- Delete: `Wintime.Control.Core/DTOs/Imm/TelemetryDto.cs`
- Modify: `Wintime.Control.API/Program.cs:37`
- Modify: `Wintime.Control.API/appsettings.json`, `Wintime.Control.API/appsettings.Development.json`
- Modify: `Wintime.Control.API/Controllers/ImmController.cs` (DI + 2 новых эндпоинта, удаление `/telemetry`)
- Test: `Wintime.Control.Tests.Integration/Imm/TelemetryDashboardTests.cs`

**Interfaces:**
- Consumes: `GatherEffectiveStatusInputsAsync` (Task 2); `ITemplateCache.GetById(Guid) → CachedTemplate?` с `Sensors: IReadOnlyList<SensorTemplate>` (`ParameterName`, `ParameterType`); `_context.Telemetry`, `_context.ImmCycles`, `_context.Imms`.
- Produces:
  - `GET /api/imm/{id}/signals` → `List<TelemetrySignalMetaDto> { string ParameterName, string Name, string Type }`.
  - `GET /api/imm/{id}/telemetry-dashboard?from&to&parameters[&pointsFrom]` → `TelemetryDashboardDto`. Необязательный `pointsFrom` сужает выборку **точек телеметрии** (дельта live); циклы и статус-сегменты всегда за полное окно `from..to`.
  - Типы: `TelemetryDashboardDto { List<TelemetrySignalDto> Signals; List<TelemetryCycleDto> Cycles; List<EffectiveStatusSegmentDto> StatusSegments; bool Truncated }`; `TelemetrySignalDto { string ParameterName; string Type; List<TelemetryPointDto> Points }`; `TelemetryPointDto { DateTime T; decimal? Num; string? Txt }`; `TelemetryCycleDto { DateTime Start; DateTime End; bool IsSuccessful }`.

- [ ] **Step 1: Создать `TelemetryDashboardSettings`**

Create `Wintime.Control.Shared/Settings/TelemetryDashboardSettings.cs`:

```csharp
namespace Wintime.Control.Shared.Settings;

public class TelemetryDashboardSettings
{
    public const string SectionName = "TelemetryDashboard";

    /// <summary>Максимальная ширина окна выборки, часы (гард).</summary>
    public int MaxWindowHours { get; set; } = 4;

    /// <summary>Максимум строк телеметрии на ответ; при превышении — Truncated=true.</summary>
    public int MaxPoints { get; set; } = 50000;
}
```

- [ ] **Step 2: Создать `TelemetryDashboardDto` (+ вложенные типы)**

Create `Wintime.Control.Core/DTOs/Imm/TelemetryDashboardDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Imm;

public class TelemetryDashboardDto
{
    public List<TelemetrySignalDto> Signals { get; set; } = new();
    public List<TelemetryCycleDto> Cycles { get; set; } = new();
    public List<EffectiveStatusSegmentDto> StatusSegments { get; set; } = new();
    public bool Truncated { get; set; }
}

public class TelemetrySignalDto
{
    public string ParameterName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // float|int|cycleCounter|boolean|string
    public List<TelemetryPointDto> Points { get; set; } = new();
}

public class TelemetryPointDto
{
    public DateTime T { get; set; }
    public decimal? Num { get; set; }
    public string? Txt { get; set; }
}

public class TelemetryCycleDto
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsSuccessful { get; set; }
}

public class TelemetrySignalMetaDto
{
    public string ParameterName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Зарегистрировать настройки + добавить секцию appsettings**

В `Program.cs` после строки 37 (`Configure<DowntimeSettings>...`) добавить:

```csharp
builder.Services.Configure<TelemetryDashboardSettings>(builder.Configuration.GetSection(TelemetryDashboardSettings.SectionName));
```
Убедиться, что вверху есть `using Wintime.Control.Shared.Settings;` (он уже используется для других настроек).

В `appsettings.json` и `appsettings.Development.json` добавить секцию верхнего уровня:

```json
"TelemetryDashboard": {
  "MaxWindowHours": 4,
  "MaxPoints": 50000
}
```

- [ ] **Step 4: Добавить DI в ImmController**

В `ImmController` добавить поля и параметры конструктора. Новые поля:

```csharp
    private readonly ITemplateCache _templateCache;
    private readonly TelemetryDashboardSettings _telemetryDashboard;
```
Конструктор — добавить параметры `ITemplateCache templateCache` и `IOptions<TelemetryDashboardSettings> telemetryDashboard`, и присвоения:
```csharp
        _templateCache = templateCache;
        _telemetryDashboard = telemetryDashboard.Value;
```
`using Wintime.Control.Core.Interfaces;`, `Microsoft.Extensions.Options`, `Wintime.Control.Shared.Settings` уже подключены в файле.

- [ ] **Step 5: Написать падающие интеграционные тесты**

Create `Wintime.Control.Tests.Integration/Imm/TelemetryDashboardTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Imm;

[Collection("Integration")]
public class TelemetryDashboardTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public TelemetryDashboardTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<Guid> SeedImmWithTelemetryAsync(DateTime from)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new Core.Entities.Imm
        {
            Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true
        };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();

        // Шаблон TestTemplate содержит датчик "temp" типа float.
        db.Telemetry.Add(new Telemetry { ImmId = imm.Id, Timestamp = from.AddMinutes(1), ParameterName = "temp", ValueNumeric = 218.4m });
        db.Telemetry.Add(new Telemetry { ImmId = imm.Id, Timestamp = from.AddMinutes(2), ParameterName = "temp", ValueNumeric = 219.1m });
        db.ImmCycles.Add(new ImmCycle
        {
            ImmId = imm.Id, StartTime = from.AddMinutes(1), EndTime = from.AddMinutes(2),
            DurationSeconds = 60, IsSuccessful = true, Cavities = 1
        });
        await db.SaveChangesAsync();
        return imm.Id;
    }

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Fact]
    public async Task Returns_Signals_Cycles_And_StatusSegments()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var to   = from.AddHours(1);
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = await ManagerClientAsync();
        var url = $"/api/imm/{immId}/telemetry-dashboard?from={from:O}&to={to:O}&parameters=temp";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<TelemetryDashboardDto>();
        dto.Should().NotBeNull();
        dto!.Signals.Should().ContainSingle();
        dto.Signals[0].ParameterName.Should().Be("temp");
        dto.Signals[0].Type.Should().Be("float");
        dto.Signals[0].Points.Should().HaveCount(2);
        dto.Cycles.Should().ContainSingle();
        dto.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task Empty_Parameters_Returns_400()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = await ManagerClientAsync();
        var url = $"/api/imm/{immId}/telemetry-dashboard?from={from:O}&to={from.AddHours(1):O}";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Window_Over_4h_Returns_400()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = await ManagerClientAsync();
        var url = $"/api/imm/{immId}/telemetry-dashboard?from={from:O}&to={from.AddHours(5):O}&parameters=temp";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Adjuster_Is_Forbidden()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var url = $"/api/imm/{immId}/telemetry-dashboard?from={from:O}&to={from.AddHours(1):O}&parameters=temp";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Signals_Endpoint_Returns_Template_Sensors()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = await ManagerClientAsync();
        var resp = await client.GetAsync($"/api/imm/{immId}/signals");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var signals = await resp.Content.ReadFromJsonAsync<List<TelemetrySignalMetaDto>>();
        signals.Should().NotBeNull();
        signals!.Should().Contain(s => s.ParameterName == "temp" && s.Type == "float");
    }

    [Fact]
    public async Task PointsFrom_Narrows_Telemetry_But_Keeps_FullWindow_Cycles()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var to   = from.AddHours(1);
        // Точки на +1 и +2 мин, цикл на +1..+2 мин.
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = await ManagerClientAsync();
        // pointsFrom на +90 c — первая точка (+1 мин) должна отсеяться, вторая (+2 мин) остаться.
        var pointsFrom = from.AddSeconds(90);
        var url = $"/api/imm/{immId}/telemetry-dashboard?from={from:O}&to={to:O}&parameters=temp&pointsFrom={pointsFrom:O}";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<TelemetryDashboardDto>();
        dto!.Signals[0].Points.Should().ContainSingle("точки сужены pointsFrom");
        dto.Cycles.Should().ContainSingle("циклы всегда за полное окно, pointsFrom их не трогает");
    }
}
```

- [ ] **Step 6: Запустить тесты — убедиться, что падают**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~TelemetryDashboardTests`
Expected: FAIL (эндпоинтов ещё нет — 404 вместо ожидаемых кодов).

- [ ] **Step 7: Реализовать эндпоинты `/signals` и `/telemetry-dashboard`**

Добавить в `ImmController` (например, рядом с `GetImmEffectiveStatusHistory`). Импорт `using Wintime.Control.Core.Cache;` не нужен (SensorTemplate в Entities); `System.Linq` доступен.

```csharp
/// <summary>
/// Метаданные сигналов ТПА (для чекбоксов выбора) — из шаблона оборудования.
/// </summary>
[HttpGet("{id:guid}/signals")]
[Authorize(Policy = "ManagerOrAdmin")]
public async Task<ActionResult<IEnumerable<TelemetrySignalMetaDto>>> GetImmSignals(Guid id)
{
    var imm = await _context.Imms.FirstOrDefaultAsync(i => i.Id == id);
    if (imm == null) return NotFound();

    var template = _templateCache.GetById(imm.TemplateId);
    var signals = (template?.Sensors ?? new List<SensorTemplate>())
        .Select(s => new TelemetrySignalMetaDto
        {
            ParameterName = s.ParameterName,
            Name = s.Name,
            Type = s.ParameterType,
        })
        .ToList();

    return Ok(signals);
}

/// <summary>
/// Агрегированное окно телеметрии одного ТПА: сигналы + циклы + сегменты эффективного статуса.
/// </summary>
[HttpGet("{id:guid}/telemetry-dashboard")]
[Authorize(Policy = "ManagerOrAdmin")]
public async Task<ActionResult<TelemetryDashboardDto>> GetTelemetryDashboard(
    Guid id,
    [FromQuery] DateTime from,
    [FromQuery] DateTime to,
    [FromQuery] List<string>? parameters = null,
    [FromQuery] DateTime? pointsFrom = null)
{
    // Guard: хотя бы один сигнал.
    if (parameters == null || parameters.Count == 0)
        return BadRequest("Выберите хотя бы один сигнал.");

    var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
    var toUtc   = DateTime.SpecifyKind(to,   DateTimeKind.Utc);

    // Guard: корректность и ширина окна.
    if (toUtc <= fromUtc)
        return BadRequest("Конец периода должен быть позже начала.");
    if (toUtc - fromUtc > TimeSpan.FromHours(_telemetryDashboard.MaxWindowHours))
        return BadRequest($"Период не может превышать {_telemetryDashboard.MaxWindowHours} ч.");

    var imm = await _context.Imms.FirstOrDefaultAsync(i => i.Id == id);
    if (imm == null) return NotFound();

    var nowUtc = DateTime.UtcNow;
    var effectiveTo = toUtc < nowUtc ? toUtc : nowUtc;

    // Дельта-подгрузка live: точки телеметрии берём от pointsFrom (если задан и внутри окна),
    // а циклы/статус-сегменты — всегда за полное окно [fromUtc, effectiveTo].
    var pointsFromUtc = pointsFrom.HasValue
        ? DateTime.SpecifyKind(pointsFrom.Value, DateTimeKind.Utc)
        : fromUtc;
    if (pointsFromUtc < fromUtc) pointsFromUtc = fromUtc;

    // Типы сигналов из шаблона (для оси/дорожки на фронте).
    var template = _templateCache.GetById(imm.TemplateId);
    var typeByName = (template?.Sensors ?? new List<SensorTemplate>())
        .GroupBy(s => s.ParameterName)
        .ToDictionary(g => g.Key, g => g.First().ParameterType);

    // Телеметрия окна с guard по объёму (Take N+1).
    var maxPoints = _telemetryDashboard.MaxPoints;
    var rows = await _context.Telemetry
        .Where(t => t.ImmId == id && parameters.Contains(t.ParameterName)
                    && t.Timestamp >= pointsFromUtc && t.Timestamp <= effectiveTo)
        .OrderBy(t => t.Timestamp)
        .Take(maxPoints + 1)
        .Select(t => new { t.ParameterName, t.Timestamp, t.ValueNumeric, t.ValueText })
        .ToListAsync();

    var truncated = rows.Count > maxPoints;
    if (truncated) rows = rows.Take(maxPoints).ToList();

    var pointsByName = rows
        .GroupBy(r => r.ParameterName)
        .ToDictionary(g => g.Key, g => g.Select(r => new TelemetryPointDto
        {
            T = r.Timestamp, Num = r.ValueNumeric, Txt = r.ValueText
        }).ToList());

    var signals = parameters.Select(p => new TelemetrySignalDto
    {
        ParameterName = p,
        Type = typeByName.TryGetValue(p, out var tp) ? tp : "string",
        Points = pointsByName.TryGetValue(p, out var pts) ? pts : new List<TelemetryPointDto>(),
    }).ToList();

    var cycles = await _context.ImmCycles
        .Where(c => c.ImmId == id && c.StartTime < effectiveTo && c.EndTime > fromUtc)
        .OrderBy(c => c.StartTime)
        .Select(c => new TelemetryCycleDto { Start = c.StartTime, End = c.EndTime, IsSuccessful = c.IsSuccessful })
        .ToListAsync();

    var (raw, tasks, downtimes) = await GatherEffectiveStatusInputsAsync(id, fromUtc, toUtc, effectiveTo);
    var statusSegments = EffectiveStatusTimeline.Build(raw, tasks, downtimes, fromUtc, effectiveTo)
        .Select(s => new EffectiveStatusSegmentDto
        {
            EffectiveStatus = s.EffectiveStatus, ChangedAt = s.Start, EndedAt = s.End
        })
        .ToList();

    return Ok(new TelemetryDashboardDto
    {
        Signals = signals,
        Cycles = cycles,
        StatusSegments = statusSegments,
        Truncated = truncated,
    });
}
```

- [ ] **Step 8: Удалить старый эндпоинт `/telemetry` и `TelemetryDto`**

В `ImmController.cs` удалить метод `GetImmTelemetry` целиком (сейчас строки ~331-363, вместе с XML-doc-комментарием `/// История телеметрии ТПА` и атрибутами `[HttpGet("{id:guid}/telemetry")]`).

Удалить файл `Wintime.Control.Core/DTOs/Imm/TelemetryDto.cs`:
```powershell
git rm Wintime.Control.Core/DTOs/Imm/TelemetryDto.cs
```

- [ ] **Step 9: Запустить новые тесты — зелёные**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~TelemetryDashboardTests`
Expected: PASS (6 тестов).

- [ ] **Step 10: Прогнать весь бэкенд — ничего не сломалось**

Run: `dotnet test Wintime.Control.Tests.Integration && dotnet test Wintime.Control.Tests.Unit`
Expected: PASS (все).

- [ ] **Step 11: Commit**

```powershell
git add Wintime.Control.Shared/Settings/TelemetryDashboardSettings.cs Wintime.Control.Core/DTOs/Imm/TelemetryDashboardDto.cs Wintime.Control.API/Program.cs Wintime.Control.API/appsettings.json Wintime.Control.API/appsettings.Development.json Wintime.Control.API/Controllers/ImmController.cs Wintime.Control.Tests.Integration/Imm/TelemetryDashboardTests.cs Wintime.Control.Core/DTOs/Imm/TelemetryDto.cs
git commit -m "feat(BL-23): эндпоинты signals + telemetry-dashboard, удалён неиспользуемый telemetry"
```

---

### Task 4: Фронт — чистые хелперы построения графика (Vitest)

**Files:**
- Create: `Wintime-Control-Frontend/src/utils/telemetryChart.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/telemetryChart.spec.js`

**Interfaces:**
- Consumes: —
- Produces:
  - `buildStepSeries(points, windowEndMs) → Array<[number, number|null]>` — пары `[timestampMs, value]`; если последняя точка раньше конца окна, добавляет замыкающую пару `[windowEndMs, lastValue]` (ступень держится до края).
  - `resolveAxisKind(type) → 'numeric' | 'state'` — `float|int|cycleCounter → 'numeric'`, `boolean|string|прочее → 'state'`.
  - `mergeLivePoints(existing, incoming, windowStartMs) → Array<point>` — дописывает только точки новее последней имеющейся и отбрасывает вышедшие за левый край окна.

- [ ] **Step 1: Написать падающие тесты**

Create `Wintime-Control-Frontend/src/utils/__tests__/telemetryChart.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import { buildStepSeries, resolveAxisKind, mergeLivePoints } from '@/utils/telemetryChart'

const t = (iso) => new Date(iso).getTime()

describe('buildStepSeries', () => {
  it('преобразует числовые точки в пары [ms, value]', () => {
    const points = [
      { t: '2026-07-27T08:00:00Z', num: 218.4 },
      { t: '2026-07-27T08:01:00Z', num: 219.1 },
    ]
    const end = t('2026-07-27T08:02:00Z')
    const series = buildStepSeries(points, end)
    expect(series[0]).toEqual([t('2026-07-27T08:00:00Z'), 218.4])
    expect(series[1]).toEqual([t('2026-07-27T08:01:00Z'), 219.1])
  })

  it('добавляет замыкающую точку до конца окна', () => {
    const points = [{ t: '2026-07-27T08:00:00Z', num: 5 }]
    const end = t('2026-07-27T08:05:00Z')
    const series = buildStepSeries(points, end)
    expect(series).toHaveLength(2)
    expect(series[1]).toEqual([end, 5])
  })

  it('булев текст маппится в 1/0', () => {
    const points = [{ t: '2026-07-27T08:00:00Z', txt: 'true' }]
    const series = buildStepSeries(points, t('2026-07-27T08:00:00Z'))
    expect(series[0][1]).toBe(1)
  })

  it('пустой вход даёт пустую серию', () => {
    expect(buildStepSeries([], t('2026-07-27T08:00:00Z'))).toEqual([])
  })
})

describe('resolveAxisKind', () => {
  it('числовые типы → numeric', () => {
    expect(resolveAxisKind('float')).toBe('numeric')
    expect(resolveAxisKind('int')).toBe('numeric')
    expect(resolveAxisKind('cycleCounter')).toBe('numeric')
  })
  it('boolean/string → state', () => {
    expect(resolveAxisKind('boolean')).toBe('state')
    expect(resolveAxisKind('string')).toBe('state')
    expect(resolveAxisKind('unknown')).toBe('state')
  })
})

describe('mergeLivePoints', () => {
  it('дописывает только точки новее последней и режет левый край', () => {
    const existing = [
      { t: '2026-07-27T08:00:00Z', num: 1 },
      { t: '2026-07-27T08:01:00Z', num: 2 },
    ]
    const incoming = [
      { t: '2026-07-27T08:01:00Z', num: 2 }, // дубль границы — не добавляем
      { t: '2026-07-27T08:02:00Z', num: 3 },
    ]
    const windowStart = t('2026-07-27T08:01:00Z')
    const merged = mergeLivePoints(existing, incoming, windowStart)
    expect(merged.map(p => p.num)).toEqual([2, 3]) // 08:00 ушёл за левый край
  })
})
```

- [ ] **Step 2: Запустить — падают**

Run: `cd Wintime-Control-Frontend && npx vitest run src/utils/__tests__/telemetryChart.spec.js`
Expected: FAIL (модуль не найден).

- [ ] **Step 3: Реализовать хелперы**

Create `Wintime-Control-Frontend/src/utils/telemetryChart.js`:

```js
// Чистые хелперы для дашборда телеметрии BL-23 (без зависимостей от echarts/Vue).

const NUMERIC_TYPES = new Set(['float', 'int', 'cycleCounter'])

function numericValue(p) {
  if (p.num !== null && p.num !== undefined) return Number(p.num)
  if (p.txt === 'true') return 1
  if (p.txt === 'false') return 0
  const n = Number(p.txt)
  return Number.isNaN(n) ? null : n
}

// Ступенчатая серия для echarts (series.step='end'): пары [timestampMs, value].
// Держим последнее значение до конца окна замыкающей парой.
export function buildStepSeries(points, windowEndMs) {
  const data = points.map(p => [new Date(p.t).getTime(), numericValue(p)])
  if (data.length > 0) {
    const last = data[data.length - 1]
    if (last[0] < windowEndMs) data.push([windowEndMs, last[1]])
  }
  return data
}

// Тип оси: числовая шкала vs дорожка состояний.
export function resolveAxisKind(type) {
  return NUMERIC_TYPES.has(type) ? 'numeric' : 'state'
}

// Слияние live-точек: добавляем только новее последней, режем левый край окна.
export function mergeLivePoints(existing, incoming, windowStartMs) {
  const lastT = existing.length ? new Date(existing[existing.length - 1].t).getTime() : -Infinity
  const fresh = incoming.filter(p => new Date(p.t).getTime() > lastT)
  return existing.concat(fresh).filter(p => new Date(p.t).getTime() >= windowStartMs)
}
```

- [ ] **Step 4: Запустить — зелёные**

Run: `cd Wintime-Control-Frontend && npx vitest run src/utils/__tests__/telemetryChart.spec.js`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Wintime-Control-Frontend/src/utils/telemetryChart.js Wintime-Control-Frontend/src/utils/__tests__/telemetryChart.spec.js
git commit -m "feat(BL-23): чистые хелперы графика телеметрии + Vitest"
```

---

### Task 5: Фронт — API-обёртки + компонент `SignalChart.vue`

**Files:**
- Create: `Wintime-Control-Frontend/src/api/telemetry.js`
- Create: `Wintime-Control-Frontend/src/components/telemetry/SignalChart.vue`
- Modify: `Wintime-Control-Frontend/src/api/imm.js` (удалить `getTelemetry`)
- Modify: `Wintime-Control-Frontend/src/api/dashboard.js` (удалить `getImmTelemetry`)

**Interfaces:**
- Consumes: `buildStepSeries`, `resolveAxisKind` (Task 4); `getEffectiveStatusMeta` (`@/constants/effectiveStatus`); эндпоинты `/signals`, `/telemetry-dashboard` (Task 3); `echarts`.
- Produces:
  - `telemetryApi.getSignals(id)`, `telemetryApi.getDashboard(id, { from, to, parameters })`.
  - Компонент `SignalChart` с props `{ signal, cycles, segments, windowStartMs, windowEndMs }`.

- [ ] **Step 1: Создать api-обёртки телеметрии**

Create `Wintime-Control-Frontend/src/api/telemetry.js`:

```js
import apiClient from './client'

export const telemetryApi = {
  // Метаданные сигналов ТПА (для чекбоксов).
  getSignals(id) {
    return apiClient.get(`/imm/${id}/signals`)
  },

  // Агрегированное окно телеметрии. parameters — массив имён сигналов.
  // pointsFrom (опц.) — дельта live: точки телеметрии от этого времени; циклы/статус — за полное окно.
  getDashboard(id, { from, to, parameters, pointsFrom }) {
    return apiClient.get(`/imm/${id}/telemetry-dashboard`, {
      params: { from, to, parameters, ...(pointsFrom ? { pointsFrom } : {}) },
      // ASP.NET List<string> ждёт повтор параметра без индексов: parameters=a&parameters=b
      paramsSerializer: { indexes: null },
    })
  },
}
```

- [ ] **Step 2: Удалить мёртвые обёртки старого эндпоинта**

В `src/api/imm.js` удалить метод `getTelemetry` (строки 24-26, вместе с ведущей пустой строкой). В `src/api/dashboard.js` удалить метод `getImmTelemetry` вместе с комментарием (строки 14-17). Прочие методы не трогать.

- [ ] **Step 3: Создать `SignalChart.vue`**

Create `Wintime-Control-Frontend/src/components/telemetry/SignalChart.vue`:

```vue
<template>
  <div class="signal-chart bg-white rounded-lg shadow-sm border">
    <div class="flex items-center justify-between px-3 py-1.5 border-b">
      <span class="text-sm font-medium text-gray-700">{{ signal.parameterName }}</span>
      <label v-if="axisKind === 'numeric'" class="text-xs text-gray-500 flex items-center gap-1 cursor-pointer">
        <input type="checkbox" v-model="fromZero" /> с нуля
      </label>
    </div>
    <div ref="chartRef" class="w-full h-44"></div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import * as echarts from 'echarts'
import { buildStepSeries, resolveAxisKind } from '@/utils/telemetryChart'
import { getEffectiveStatusMeta } from '@/constants/effectiveStatus'

const props = defineProps({
  signal:        { type: Object, required: true },   // { parameterName, type, points }
  cycles:        { type: Array,  default: () => [] }, // [{ start, end, isSuccessful }]
  segments:      { type: Array,  default: () => [] }, // [{ effectiveStatus, changedAt, endedAt }]
  windowStartMs: { type: Number, required: true },
  windowEndMs:   { type: Number, required: true },
})

const GROUP = 'telemetry-signals'
const chartRef = ref(null)
let chart = null
const fromZero = ref(false)
const axisKind = computed(() => resolveAxisKind(props.signal.type))

function buildOption() {
  const seriesData = buildStepSeries(props.signal.points, props.windowEndMs)

  // Фон эффективного статуса.
  const markArea = {
    silent: true,
    itemStyle: { opacity: 0.12 },
    data: props.segments.map(s => ([
      { xAxis: new Date(s.changedAt).getTime(), itemStyle: { color: getEffectiveStatusMeta(s.effectiveStatus).hex } },
      { xAxis: new Date(s.endedAt ?? props.windowEndMs).getTime() },
    ])),
  }

  // Вертикали границ циклов (начало — по годности, конец — серый).
  const markLine = {
    silent: true,
    symbol: 'none',
    label: { show: false },
    data: props.cycles.flatMap(c => ([
      { xAxis: new Date(c.start).getTime(), lineStyle: { color: c.isSuccessful ? '#16a34a' : '#dc2626', type: 'solid', width: 1 } },
      { xAxis: new Date(c.end).getTime(),   lineStyle: { color: '#9ca3af', type: 'dashed', width: 1 } },
    ])),
  }

  const yAxis = axisKind.value === 'numeric'
    ? { type: 'value', scale: !fromZero.value, min: fromZero.value ? 0 : null, axisLabel: { fontSize: 10 } }
    : { type: 'value', min: -0.1, max: 1.1, interval: 1,
        axisLabel: { fontSize: 10, formatter: v => (v === 1 ? '1' : v === 0 ? '0' : '') } }

  return {
    animation: false,
    grid: { left: 52, right: 12, top: 8, bottom: 22 },
    tooltip: { trigger: 'axis', axisPointer: { type: 'line' } },
    axisPointer: { link: [{ xAxisIndex: 'all' }] },
    xAxis: { type: 'time', min: props.windowStartMs, max: props.windowEndMs, axisLabel: { fontSize: 10 } },
    yAxis,
    series: [{
      name: props.signal.parameterName,
      type: 'line',
      step: 'end',
      showSymbol: false,
      connectNulls: false,
      data: seriesData,
      lineStyle: { width: axisKind.value === 'numeric' ? 1.5 : 2, color: '#2563eb' },
      itemStyle: { color: '#2563eb' },
      markArea,
      markLine,
    }],
  }
}

function render() {
  chart?.setOption(buildOption(), true)
}

function onResize() { chart?.resize() }

onMounted(async () => {
  await nextTick()
  chart = echarts.init(chartRef.value)
  chart.group = GROUP
  echarts.connect(GROUP) // общий crosshair/zoom по всем диаграммам столбца
  render()
  window.addEventListener('resize', onResize)
})

onUnmounted(() => {
  window.removeEventListener('resize', onResize)
  chart?.dispose()
})

watch(
  () => [props.signal, props.cycles, props.segments, props.windowStartMs, props.windowEndMs, fromZero.value],
  render,
  { deep: true },
)
</script>
```

- [ ] **Step 4: Проверить сборку фронта**

Run: `cd Wintime-Control-Frontend && npm run build`
Expected: сборка без ошибок (компонент компилируется, мёртвые обёртки удалены без поломок — они нигде не вызывались).

- [ ] **Step 5: Commit**

```powershell
git add Wintime-Control-Frontend/src/api/telemetry.js Wintime-Control-Frontend/src/components/telemetry/SignalChart.vue Wintime-Control-Frontend/src/api/imm.js Wintime-Control-Frontend/src/api/dashboard.js
git commit -m "feat(BL-23): api телеметрии + компонент SignalChart, удалены мёртвые обёртки"
```

---

### Task 6: Фронт — страница `TelemetryDashboardView.vue`, route и drill-in

**Files:**
- Create: `Wintime-Control-Frontend/src/views/telemetry/TelemetryDashboardView.vue`
- Modify: `Wintime-Control-Frontend/src/router/index.js`
- Modify: `Wintime-Control-Frontend/src/views/dashboard/ImmDetailModal.vue`

**Interfaces:**
- Consumes: `telemetryApi` (Task 5); `mergeLivePoints` (Task 4); `SignalChart` (Task 5); `useRoute`/`useRouter`.
- Produces: route `imm/:id/telemetry` (name `ImmTelemetry`); кнопка «Телеметрия» в `ImmDetailModal`.

- [ ] **Step 1: Добавить route**

В `src/router/index.js`, внутри `children` массива под `DefaultLayout` (например, после блока `orders`, до `reports`), добавить:

```js
      {
        path: 'imm/:id/telemetry',
        name: 'ImmTelemetry',
        component: () => import('@/views/telemetry/TelemetryDashboardView.vue'),
        meta: { roles: ['Admin', 'Manager'], title: 'Телеметрия ТПА' }
      },
```

- [ ] **Step 2: Создать страницу `TelemetryDashboardView.vue`**

Create `Wintime-Control-Frontend/src/views/telemetry/TelemetryDashboardView.vue`:

```vue
<template>
  <div class="p-4 space-y-4">
    <!-- Фильтры -->
    <div class="bg-white rounded-lg shadow-sm border p-4 space-y-3">
      <div class="flex flex-wrap items-center gap-4">
        <div class="flex items-center gap-2">
          <span class="text-sm text-gray-600">Режим:</span>
          <el-select v-model="mode" size="small" style="width: 160px" @change="onModeChange">
            <el-option label="История" value="history" />
            <el-option label="Живые данные" value="live" />
          </el-select>
        </div>

        <template v-if="mode === 'history'">
          <el-date-picker
            v-model="historyFrom" type="datetime" size="small"
            placeholder="Начало" format="YYYY-MM-DD HH:mm:ss" value-format="YYYY-MM-DDTHH:mm:ss"
          />
          <el-date-picker
            v-model="historyTo" type="datetime" size="small"
            placeholder="Конец" format="YYYY-MM-DD HH:mm:ss" value-format="YYYY-MM-DDTHH:mm:ss"
          />
          <el-button size="small" type="primary" @click="loadHistory">Показать</el-button>
        </template>

        <template v-else>
          <div class="flex items-center gap-2">
            <span class="text-sm text-gray-600">Период:</span>
            <el-select v-model="livePeriodMin" size="small" style="width: 140px" @change="restartLive">
              <el-option v-for="p in LIVE_PERIODS" :key="p.min" :label="p.label" :value="p.min" />
            </el-select>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-sm text-gray-600">Обновление:</span>
            <el-select v-model="livePollSec" size="small" style="width: 120px" @change="restartLive">
              <el-option v-for="s in LIVE_POLL_OPTIONS" :key="s" :label="`${s} c`" :value="s" />
            </el-select>
          </div>
        </template>
      </div>

      <!-- Сигналы -->
      <div class="flex flex-wrap items-center gap-3">
        <span class="text-sm text-gray-600">Сигналы:</span>
        <el-checkbox-group v-model="selected" @change="onSelectionChange">
          <el-checkbox v-for="s in availableSignals" :key="s.parameterName" :value="s.parameterName">
            {{ s.parameterName }}
          </el-checkbox>
        </el-checkbox-group>
      </div>

      <div v-if="truncated" class="text-sm text-amber-600">
        Показаны не все точки — сузьте период.
      </div>
    </div>

    <!-- Столбик диаграмм -->
    <div v-if="orderedSignals.length" class="space-y-3">
      <div v-for="(sig, idx) in orderedSignals" :key="sig.parameterName" class="relative">
        <div class="absolute right-2 top-1.5 z-10 flex gap-1">
          <el-button size="small" text :disabled="idx === 0" @click="move(idx, -1)">▲</el-button>
          <el-button size="small" text :disabled="idx === orderedSignals.length - 1" @click="move(idx, 1)">▼</el-button>
        </div>
        <SignalChart
          :signal="sig"
          :cycles="cycles"
          :segments="segments"
          :window-start-ms="windowStartMs"
          :window-end-ms="windowEndMs"
        />
      </div>
    </div>
    <el-empty v-else description="Выберите сигнал для отображения" />
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import SignalChart from '@/components/telemetry/SignalChart.vue'
import { telemetryApi } from '@/api/telemetry'
import { mergeLivePoints } from '@/utils/telemetryChart'

const LIVE_PERIODS = [
  { min: 1,   label: '1 минута' },
  { min: 5,   label: '5 минут' },
  { min: 15,  label: '15 минут' },
  { min: 30,  label: '30 минут' },
  { min: 60,  label: '1 час' },
  { min: 120, label: '2 часа' },
  { min: 240, label: '4 часа' },
]
const LIVE_POLL_OPTIONS = [2, 5, 10] // секунды между запросами; выбор пользователя

const route = useRoute()
const immId = route.params.id

const mode = ref('live')
const livePeriodMin = ref(15)
const livePollSec = ref(10) // частота live-опроса, сек (дефолт 10)
const historyFrom = ref(null)
const historyTo = ref(null)

const availableSignals = ref([]) // [{ parameterName, name, type }]
const selected = ref([])
const order = ref([])            // порядок parameterName в столбце

const signalsData = ref([])      // [{ parameterName, type, points }]
const cycles = ref([])
const segments = ref([])
const truncated = ref(false)

const windowStartMs = ref(Date.now() - 15 * 60000)
const windowEndMs = ref(Date.now())

let pollTimer = null

const orderedSignals = computed(() => {
  const byName = new Map(signalsData.value.map(s => [s.parameterName, s]))
  return order.value.map(n => byName.get(n)).filter(Boolean)
})

function toIsoUtc(ms) {
  return new Date(ms).toISOString()
}

async function fetchWindow(fromMs, toMs, { append = false, pointsFromMs = null } = {}) {
  if (selected.value.length === 0) {
    signalsData.value = []
    cycles.value = []
    segments.value = []
    return
  }
  try {
    const { data } = await telemetryApi.getDashboard(immId, {
      from: toIsoUtc(fromMs),
      to: toIsoUtc(toMs),
      parameters: selected.value,
      pointsFrom: pointsFromMs ? toIsoUtc(pointsFromMs) : undefined,
    })
    truncated.value = data.truncated
    cycles.value = data.cycles
    segments.value = data.statusSegments

    if (append) {
      const byName = new Map(signalsData.value.map(s => [s.parameterName, s]))
      signalsData.value = data.signals.map(s => {
        const prev = byName.get(s.parameterName)
        const points = prev
          ? mergeLivePoints(prev.points, s.points, windowStartMs.value)
          : s.points
        return { ...s, points }
      })
    } else {
      signalsData.value = data.signals
    }
  } catch (e) {
    const status = e?.response?.status
    if (status === 400) ElMessage.warning(e.response.data ?? 'Некорректный период')
    else ElMessage.error('Не удалось загрузить телеметрию')
  }
}

// ── История ──
async function loadHistory() {
  if (!historyFrom.value || !historyTo.value) {
    ElMessage.warning('Укажите начало и конец периода')
    return
  }
  const fromMs = new Date(historyFrom.value + 'Z').getTime()
  const toMs = new Date(historyTo.value + 'Z').getTime()
  if (toMs - fromMs > 4 * 3600_000) {
    ElMessage.warning('Период не может превышать 4 часа')
    return
  }
  windowStartMs.value = fromMs
  windowEndMs.value = toMs
  await fetchWindow(fromMs, toMs)
}

// ── Live ──
async function liveTick(initial = false) {
  const now = Date.now()
  windowEndMs.value = now
  windowStartMs.value = now - livePeriodMin.value * 60000
  // Окно всегда полное (from..now) — для циклов/статуса; точки телеметрии на приросте берём
  // дельтой через pointsFrom (от последней полученной точки).
  const pointsFromMs = initial ? null : Math.max(lastLoadedMs(), windowStartMs.value)
  await fetchWindow(windowStartMs.value, now, { append: !initial, pointsFromMs })
}

function lastLoadedMs() {
  let maxT = windowStartMs.value
  for (const s of signalsData.value) {
    const p = s.points[s.points.length - 1]
    if (p) maxT = Math.max(maxT, new Date(p.t).getTime())
  }
  return maxT
}

function startLive() {
  stopLive()
  liveTick(true)
  pollTimer = setInterval(() => liveTick(false), livePollSec.value * 1000)
}

function stopLive() {
  if (pollTimer) { clearInterval(pollTimer); pollTimer = null }
}

function restartLive() {
  if (mode.value === 'live') startLive()
}

function onModeChange() {
  if (mode.value === 'live') startLive()
  else { stopLive(); signalsData.value = [] }
}

function onSelectionChange() {
  syncOrder()
  if (mode.value === 'live') startLive()
  else if (historyFrom.value && historyTo.value) loadHistory()
}

function syncOrder() {
  // сохранить прежний порядок, добавить новые в конец, убрать снятые
  order.value = [
    ...order.value.filter(n => selected.value.includes(n)),
    ...selected.value.filter(n => !order.value.includes(n)),
  ]
}

function move(idx, delta) {
  const next = idx + delta
  if (next < 0 || next >= order.value.length) return
  const arr = [...order.value]
  ;[arr[idx], arr[next]] = [arr[next], arr[idx]]
  order.value = arr
}

onMounted(async () => {
  try {
    const { data } = await telemetryApi.getSignals(immId)
    availableSignals.value = data
    if (data.length) {
      selected.value = [data[0].parameterName] // по умолчанию первый сигнал
      syncOrder()
    }
  } catch {
    ElMessage.error('Не удалось загрузить список сигналов')
  }
  startLive() // режим по умолчанию — живые данные
})

onUnmounted(stopLive)
</script>
```

- [ ] **Step 3: Добавить кнопку «Телеметрия» в `ImmDetailModal.vue`**

В `src/views/dashboard/ImmDetailModal.vue` в блоке `<template #footer>` (строки ~69-75) добавить кнопку перед «Закрыть». Итоговый footer:

```vue
    <template #footer>
      <el-button type="primary" plain @click="goToTelemetry">Телеметрия</el-button>
      <el-button @click="visible = false">Закрыть</el-button>
      <el-button type="primary" :loading="loading" @click="loadData">
        Обновить
      </el-button>
    </template>
```

В `<script setup>` этого файла добавить импорт роутера и метод (рядом с существующей логикой):

```js
import { useRouter } from 'vue-router'
const router = useRouter()
function goToTelemetry() {
  visible.value = false
  router.push({ name: 'ImmTelemetry', params: { id: props.immId } })
}
```
(Кнопка «Обновить» — подпись из существующей разметки; если там иной текст, оставить как есть. Проверить, что `visible` — уже объявленный computed в файле, он есть на строке ~98.)

- [ ] **Step 4: Проверить сборку и Vitest**

Run: `cd Wintime-Control-Frontend && npm run build && npx vitest run`
Expected: сборка без ошибок; все Vitest-тесты зелёные.

- [ ] **Step 5: Ручной smoke (браузер)**

Запустить API (`dotnet run --project Wintime.Control.API`), эмулятор (`dotnet run --project Wintime.Control.Emulator`) и фронт (`npm run dev`). Войти как Manager, открыть дашборд → клик по карточке ТПА → в модалке «Телеметрия». Проверить:
- режим «Живые данные», выбор периода 1/5/15/30 мин, 1/2/4 ч и частоты обновления 2/5/10 с; графики обновляются;
- чекбоксы сигналов, ≥1 отмечен; каждый сигнал — своя диаграмма;
- crosshair синхронен по столбцу; ▲▼ меняют порядок; фон статуса и вертикали циклов видны;
- режим «История»: диапазон ≤4 ч отдаёт срез; >4 ч — предупреждение.

- [ ] **Step 6: Commit**

```powershell
git add Wintime-Control-Frontend/src/views/telemetry/TelemetryDashboardView.vue Wintime-Control-Frontend/src/router/index.js Wintime-Control-Frontend/src/views/dashboard/ImmDetailModal.vue
git commit -m "feat(BL-23): страница телеметрии, route imm/:id/telemetry, drill-in из карточки ТПА"
```

---

## Self-Review (выполнено при написании)

**Покрытие спеки:**
- §3 UC-1 drill-in → Task 6 (кнопка + route + дефолт live/15мин/первый сигнал). ✅
- §3 UC-2 live + периоды 1/5/15/30/60/120/240 → Task 6 (`LIVE_PERIODS`, дельта-поллинг). ✅
- §3 UC-3 история ≤4ч → Task 6 (`loadHistory` + гард). ✅
- §3 UC-4 выбор сигналов, ≥1 → Task 6 (checkbox-group, дефолт первый; backend-гард 400 → Task 3). ✅
- §3 UC-5 синхронный crosshair → Task 5 (`axisPointer.link` + `echarts.connect`). ✅
- §3 UC-6 reorder → Task 6 (▲▼ + `order`). ✅
- §3 UC-7 фон статуса + вертикали циклов → Task 5 (`markArea`/`markLine`). ✅
- §3 UC-8 truncated → Task 3 (Take N+1) + Task 6 (баннер). ✅
- §5 API telemetry-dashboard + guards + удаление старого → Task 3. ✅
- §5 `/signals` (для чекбоксов) → Task 3. ✅
- §7 step-серия, boolean/string дорожки, авто-шкала + «с нуля» → Task 4/5. ✅
- §8 индекс → Task 1; гарды → Task 3. ✅
- §9 тесты: интеграционные (Task 3), Vitest (Task 4). ✅

**Плейсхолдеры:** нет TODO/TBD; весь код приведён.

**Согласованность типов:** DTO `TelemetryDashboardDto`/`TelemetrySignalDto`/`TelemetryPointDto`/`TelemetryCycleDto`/`TelemetrySignalMetaDto` (Task 3) совпадают с потреблением на фронте (`data.signals/cycles/statusSegments/truncated`, точки `{ t, num, txt }`) — Task 5/6. Хелпер `GatherEffectiveStatusInputsAsync` определён в Task 2 и вызывается в Task 3. `buildStepSeries/resolveAxisKind/mergeLivePoints` определены в Task 4 и потребляются в Task 5/6.
