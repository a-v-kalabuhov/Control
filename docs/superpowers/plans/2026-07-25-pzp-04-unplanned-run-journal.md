# PZP-04 — Журнал «работы без задания» + конвейер обработки цикла — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Дать менеджеру журнал эпизодов «работы без задания» (сироты-циклы) с ретро-привязкой к заданию, разбив монолитный `CycleProcessingHandler` на конвейер хендлеров с двухстадийной долговечностью.

**Architecture:** Оркестратор детектирует завершение цикла и **сразу** сохраняет `ImmCycle` (Стадия 1); затем поверх сохранённого цикла последовательно исполняются `ICycleHandler` (Стадия 2, каждый в try/catch): `TaskOutputHandler` (выпуск/материал) и `UnplannedRunHandler` (открытие эпизода для сироты). Эпизод — лёгкий конверт `UnplannedRun`; счётчик циклов, конец и средняя длительность **деривятся** из уже сохранённых циклов (derive-on-read), эпизод не обновляется на каждый цикл.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql (PostgreSQL 16), xUnit + FluentAssertions + NSubstitute (unit, InMemory DB), Testcontainers (integration, реальный Postgres), Vue 3 + Element Plus + Vitest.

## Global Constraints

- **Роль — только `User.Role`** (enum `UserRole`); `[Authorize(Roles=…)]` через `Wintime.Control.Shared.Constants.Roles`. Никаких Identity-ролей.
- **DateTime → Postgres:** все `DateTime` в EF-запросах к `timestamptz` обязаны иметь `Kind=Utc`. Даты из query string приводить сразу: `DateTime.SpecifyKind(value, DateTimeKind.Utc)`.
- **`Cavities` — снапшот** на момент цикла (`ImmCycle.Cavities`); fallback для старых записей (`=0`) — `Mold.Cavities`.
- **Тип изделия обязателен** (ADR-0007): назначать эпизоду можно только задание, у ПФ которого есть `ProductTypeId`.
- **Никогда не удалять сущности физически.**
- **master защищён** — вся работа в ветке `feature/pzp-04-unplanned-run-journal` (уже создана), только PR.
- **Порог смежности:** `1.5 × avgCycleDuration`, симметрично слева и справа от эпизода.
- Спека: `docs/superpowers/specs/2026-07-25-pzp-04-unplanned-run-journal-design.md`.

---

## File Structure

**Backend (Core):**
- Create `Wintime.Control.Core/Entities/UnplannedRun.cs` — сущность-конверт эпизода.
- Create `Wintime.Control.Core/DTOs/Mqtt/CompletedCycle.cs` — контекст завершённого цикла для конвейера.
- Create `Wintime.Control.Core/Policies/UnplannedRunAdjacency.cs` — чистые функции правила смежности (UC-5).
- Create `Wintime.Control.Core/DTOs/UnplannedRun/UnplannedRunDto.cs`, `TaskCandidateDto.cs` — DTO журнала/кандидатов.

**Backend (Infrastructure):**
- Create `Wintime.Control.Infrastructure/Handlers/ICycleHandler.cs` — интерфейс шага конвейера цикла.
- Create `Wintime.Control.Infrastructure/Handlers/TaskOutputHandler.cs` — вынос логики выпуска/материала.
- Create `Wintime.Control.Infrastructure/Handlers/UnplannedRunHandler.cs` — открытие эпизода для сироты.
- Modify `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs` — оркестратор: детекция + Стадия 1 + вызов конвейера.
- Modify `Wintime.Control.Infrastructure/Data/ControlDbContext.cs` — `DbSet<UnplannedRun>` + конфиг + partial-индексы.
- Modify `Wintime.Control.Infrastructure/Services/InfrastructureServiceExtensions.cs` — регистрация `ICycleHandler`.
- Create migration `AddUnplannedRun` (через `dotnet ef`).

**Backend (API):**
- Create `Wintime.Control.API/Controllers/UnplannedRunController.cs` — GET список, GET candidates, POST assign.
- Modify `Wintime.Control.API/Controllers/TasksController.cs` — закрытие эпизода в `StartTask`.

**Frontend:**
- Create `Wintime-Control-Frontend/src/api/unplannedRuns.js`.
- Create `Wintime-Control-Frontend/src/views/unplanned/UnplannedRunLogView.vue`.
- Modify `Wintime-Control-Frontend/src/router/index.js` — роут `unplanned-runs`.
- Modify `Wintime-Control-Frontend/src/layouts/DefaultLayout.vue` — пункт меню.

**Docs:**
- Create `docs/adr/0008-cycle-pipeline-and-unplanned-run.md`.

---

## Task 1: Сущность `UnplannedRun` + миграция + конфиг DbContext

**Files:**
- Create: `Wintime.Control.Core/Entities/UnplannedRun.cs`
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs:26` (DbSet) и `:141` (конфиг в `OnModelCreating`)
- Test: `Wintime.Control.Tests.Integration/UnplannedRuns/UnplannedRunPersistenceTests.cs`

**Interfaces:**
- Produces: сущность `UnplannedRun { Guid Id; Guid ImmId; DateTime StartTime; DateTime? ClosedAt; Guid? AssignedTaskId; string? AssignedByUserId; DateTime? AssignedAt; Imm Imm; ShiftTask? AssignedTask }`; `DbSet<UnplannedRun> UnplannedRuns`.

- [ ] **Step 1: Создать сущность**

`Wintime.Control.Core/Entities/UnplannedRun.cs`:

```csharp
namespace Wintime.Control.Core.Entities;

/// <summary>
/// Эпизод «работы без задания» (PZP-04). Лёгкий конверт: число сирот-циклов,
/// эффективный конец и средняя длительность НЕ хранятся — деривятся из ImmCycles.
/// За жизнь эпизода к нему идёт ~3 записи: создание, закрытие (StartTask), назначение.
/// </summary>
public class UnplannedRun : BaseEntity
{
    public Guid ImmId { get; set; }
    public DateTime StartTime { get; set; }        // = EndTime первого сироты-цикла
    public DateTime? ClosedAt { get; set; }        // ставится в StartTask; null = открыт
    public Guid? AssignedTaskId { get; set; }      // назначенное задание (ретро-привязка)
    public string? AssignedByUserId { get; set; }  // аудит: кто назначил (User.Id)
    public DateTime? AssignedAt { get; set; }      // аудит: когда

    // Navigation
    public Imm Imm { get; set; } = null!;
    public ShiftTask? AssignedTask { get; set; }
}
```

- [ ] **Step 2: Добавить DbSet**

`ControlDbContext.cs`, после строки `public DbSet<ProductType> ProductTypes { get; set; }` (:26):

```csharp
    public DbSet<UnplannedRun> UnplannedRuns { get; set; }
```

- [ ] **Step 3: Добавить конфиг сущности**

`ControlDbContext.cs`, в конце `OnModelCreating` (перед закрывающей `}` метода, после блока `AppHeartbeat`):

```csharp
        // Конфигурация UnplannedRun (PZP-04)
        builder.Entity<UnplannedRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Imm).WithMany().HasForeignKey(e => e.ImmId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AssignedTask).WithMany().HasForeignKey(e => e.AssignedTaskId).OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.StartTime).HasColumnType("timestamp with time zone");
            entity.Property(e => e.ClosedAt).HasColumnType("timestamp with time zone");
            entity.Property(e => e.AssignedAt).HasColumnType("timestamp with time zone");
            // Partial-индекс: поиск открытого эпизода ТПА (крошечный, только открытые)
            entity.HasIndex(e => e.ImmId).HasFilter("\"ClosedAt\" IS NULL").HasDatabaseName("IX_UnplannedRuns_Imm_Open");
            entity.ToTable("UnplannedRuns");
        });

        // Partial-индекс на ImmCycles под агрегат деривации и счётчик дашборда
        builder.Entity<ImmCycle>()
            .HasIndex(e => new { e.ImmId, e.EndTime })
            .HasFilter("\"TaskId\" IS NULL")
            .HasDatabaseName("IX_ImmCycles_Imm_Orphan");
```

- [ ] **Step 4: Создать миграцию**

Run:
```powershell
dotnet ef migrations add AddUnplannedRun --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: создан файл миграции в `Wintime.Control.Infrastructure/Migrations/*_AddUnplannedRun.cs` без ошибок. Открыть его и убедиться, что есть `CreateTable("UnplannedRuns")` и два `CreateIndex` с `filter:`.

- [ ] **Step 5: Написать интеграционный тест персистентности**

`Wintime.Control.Tests.Integration/UnplannedRuns/UnplannedRunPersistenceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.UnplannedRuns;

public class UnplannedRunPersistenceTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public UnplannedRunPersistenceTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task UnplannedRun_persists_and_reads_back_with_utc()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var run = new UnplannedRun { ImmId = _factory.TestImmId, StartTime = DateTime.UtcNow };
        db.UnplannedRuns.Add(run);
        await db.SaveChangesAsync();

        var loaded = await db.UnplannedRuns.FindAsync(run.Id);
        loaded.Should().NotBeNull();
        loaded!.ClosedAt.Should().BeNull();
    }
}
```

- [ ] **Step 6: Запустить тест**

Run:
```powershell
dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~UnplannedRunPersistenceTests
```
Expected: PASS (миграция применяется Testcontainers-ом, запись читается).

- [ ] **Step 7: Commit**

```powershell
git add Wintime.Control.Core/Entities/UnplannedRun.cs Wintime.Control.Infrastructure/Data/ControlDbContext.cs Wintime.Control.Infrastructure/Migrations Wintime.Control.Tests.Integration/UnplannedRuns/UnplannedRunPersistenceTests.cs
git commit -m "feat(PZP-04): сущность UnplannedRun + миграция + partial-индексы"
```

---

## Task 2: Конвейер цикла — `CompletedCycle`, `ICycleHandler`, рефактор оркестратора

Извлекаем из `CycleProcessingHandler` производную обработку в конвейер. Оркестратор оставляет детекцию + Стадию 1 (немедленное сохранение цикла) + последовательный вызов `ICycleHandler` в try/catch. **Логика выпуска пока переносится в оркестратор временно НЕ будет — она уедет в Task 3;** в этом таске оркестратор после Стадии 1 только вызывает (пока пустой) список хендлеров.

**Files:**
- Create: `Wintime.Control.Core/DTOs/Mqtt/CompletedCycle.cs`
- Create: `Wintime.Control.Infrastructure/Handlers/ICycleHandler.cs`
- Modify: `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs`
- Modify: `Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs`

**Interfaces:**
- Produces: `record CompletedCycle(ImmCycle Cycle, ShiftTask? ActiveTask, string Mode)`; `interface ICycleHandler { Task HandleAsync(CompletedCycle cycle, CancellationToken ct = default); }`; конструктор `CycleProcessingHandler(ControlDbContext db, ICycleTracker tracker, IEnumerable<ICycleHandler> handlers, ILogger<CycleProcessingHandler> logger)`.
- Consumes: `ImmCycle`, `ShiftTask` (Task 1 и существующие).

- [ ] **Step 1: Создать `CompletedCycle`**

`Wintime.Control.Core/DTOs/Mqtt/CompletedCycle.cs`:

```csharp
using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.DTOs.Mqtt;

/// <summary>
/// Уже сохранённый (Стадия 1) завершённый цикл + контекст для конвейера ICycleHandler.
/// </summary>
public record CompletedCycle(ImmCycle Cycle, ShiftTask? ActiveTask, string Mode);
```

- [ ] **Step 2: Создать интерфейс `ICycleHandler`**

`Wintime.Control.Infrastructure/Handlers/ICycleHandler.cs`:

```csharp
using Wintime.Control.Core.DTOs.Mqtt;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Шаг конвейера обработки завершённого цикла (Стадия 2). Исполняется поверх
/// уже сохранённого ImmCycle; сбой одного шага не должен ронять остальные.
/// </summary>
public interface ICycleHandler
{
    SystemTask HandleAsync(CompletedCycle cycle, CancellationToken ct = default);
}
```

- [ ] **Step 3: Написать падающий тест долговечности оркестратора**

Заменить весь файл `Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs` на версию, тестирующую ТОЛЬКО оркестрацию (выпуск переехал в Task 3). Ключевые тесты: (1) при завершении цикла `ImmCycle` сохранён; (2) сироты-цикл (нет задания, auto) сохранён с `TaskId=null`; (3) если хендлер стадии 2 бросает исключение — цикл всё равно в БД и вызваны все хендлеры.

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Handlers;
using Wintime.Control.Tests.Unit.Helpers;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Imm = Wintime.Control.Core.Entities.Imm;
using Mold = Wintime.Control.Core.Entities.Mold;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Handlers;

public class CycleProcessingHandlerTests
{
    private readonly ICycleTracker _tracker = Substitute.For<ICycleTracker>();

    private static ControlDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class ThrowingHandler : ICycleHandler
    {
        public bool Called { get; private set; }
        public SystemTask HandleAsync(CompletedCycle cycle, CancellationToken ct = default)
        {
            Called = true;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class SpyHandler : ICycleHandler
    {
        public bool Called { get; private set; }
        public SystemTask HandleAsync(CompletedCycle cycle, CancellationToken ct = default)
        {
            Called = true;
            return SystemTask.CompletedTask;
        }
    }

    private static MqttProcessingContext MakeCycleContext(Guid immId, int counter, string mode)
    {
        var sensor = PipelineTestFixtures.MakeSensor("counter", type: "cycleCounter");
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var message = PipelineTestFixtures.MakeMessage(immId,
            sensors: new Dictionary<string, string> { ["counter"] = counter.ToString() }, mode: mode);
        var device = PipelineTestFixtures.MakeImmDto(immId);
        return PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device, template: template);
    }

    [Fact]
    public async SystemTask Persists_orphan_cycle_and_runs_all_handlers_even_when_one_throws()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        // активный цикл в трекере: auto, счётчик 5 → приходит 6 → цикл завершён
        _tracker.Get(immId).Returns(new CycleState(DateTime.UtcNow.AddSeconds(-20), 5, "auto"));

        var throwing = new ThrowingHandler();
        var spy = new SpyHandler();
        var sut = new CycleProcessingHandler(db, _tracker, [throwing, spy], NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeCycleContext(immId, 6, "auto"));

        var cycle = await db.ImmCycles.SingleOrDefaultAsync();
        cycle.Should().NotBeNull("цикл сохранён на Стадии 1 до конвейера");
        cycle!.TaskId.Should().BeNull("нет активного задания → сирота");
        throwing.Called.Should().BeTrue();
        spy.Called.Should().BeTrue("сбой одного хендлера не прерывает конвейер");
    }
}
```

- [ ] **Step 4: Запустить тест — убедиться, что не компилируется/падает**

Run:
```powershell
dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~CycleProcessingHandlerTests
```
Expected: ошибка компиляции (конструктор `CycleProcessingHandler` ещё старый, нет `ICycleHandler`). Это ожидаемо.

- [ ] **Step 5: Отрефакторить оркестратор**

Заменить `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs` целиком:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Entities;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Оркестратор обработки циклов: детектирует завершение цикла по cycleCounter,
/// СРАЗУ сохраняет ImmCycle (Стадия 1 — долговечность), затем поверх сохранённого
/// цикла последовательно исполняет ICycleHandler (Стадия 2), каждый в try/catch.
/// </summary>
public class CycleProcessingHandler : ICycleProcessingHandler
{
    private readonly ControlDbContext _db;
    private readonly ICycleTracker _tracker;
    private readonly IEnumerable<ICycleHandler> _handlers;
    private readonly ILogger<CycleProcessingHandler> _logger;

    public CycleProcessingHandler(
        ControlDbContext db,
        ICycleTracker tracker,
        IEnumerable<ICycleHandler> handlers,
        ILogger<CycleProcessingHandler> logger)
    {
        _db = db;
        _tracker = tracker;
        _handlers = handlers;
        _logger = logger;
    }

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

        var activeTask = await _db.ShiftTasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(
                t => t.ImmId == immId
                  && (t.Status == EntityTaskStatus.Setup || t.Status == EntityTaskStatus.InProgress),
                ct);

        var taskStatus = ActiveTaskStatusMap.From(activeTask?.Status);

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
            await _db.SaveChangesAsync(ct); // СТАДИЯ 1 — цикл долговечен

            var completed = new CompletedCycle(cycle, activeTask, currentMode);
            foreach (var handler in _handlers) // СТАДИЯ 2
            {
                try
                {
                    await handler.HandleAsync(completed, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Cycle handler {Handler} failed for IMM {ImmId} (cycle {CycleId})",
                        handler.GetType().Name, immId, cycle.Id);
                }
            }

            _logger.LogDebug("IMM {ImmId}: cycle saved — duration {Duration}s, successful={Success}",
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
}
```

- [ ] **Step 6: Запустить тест оркестратора**

Run:
```powershell
dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~CycleProcessingHandlerTests
```
Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add Wintime.Control.Core/DTOs/Mqtt/CompletedCycle.cs Wintime.Control.Infrastructure/Handlers/ICycleHandler.cs Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs
git commit -m "refactor(PZP-04): CycleProcessingHandler → оркестратор + конвейер ICycleHandler (двухстадийная долговечность)"
```

---

## Task 3: `TaskOutputHandler` — вынос учёта выпуска/материала

Логика подсчёта `ActualQuantity`/материала (ранее в монолите) становится первым `ICycleHandler`.

**Files:**
- Create: `Wintime.Control.Infrastructure/Handlers/TaskOutputHandler.cs`
- Test: `Wintime.Control.Tests.Unit/Handlers/TaskOutputHandlerTests.cs`

**Interfaces:**
- Consumes: `CompletedCycle` (Task 2), `IEmulatorControlService`, `CycleProcessingPolicy.ShouldCountOutput`.
- Produces: `class TaskOutputHandler : ICycleHandler`.

- [ ] **Step 1: Написать падающие тесты**

`Wintime.Control.Tests.Unit/Handlers/TaskOutputHandlerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Handlers;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Imm = Wintime.Control.Core.Entities.Imm;
using ImmCycle = Wintime.Control.Core.Entities.ImmCycle;
using Mold = Wintime.Control.Core.Entities.Mold;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Handlers;

public class TaskOutputHandlerTests
{
    private readonly IEmulatorControlService _emulator = Substitute.For<IEmulatorControlService>();

    private static ControlDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async SystemTask InProgress_auto_success_increments_actual_quantity_and_material()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var mold = new Mold { Name = "M", FormId = Guid.NewGuid().ToString(), Cavities = 2, PartWeightGrams = 10m, RunnerWeightGrams = 5m };
        var imm = new Imm { Id = immId, Name = "IMM", IsActive = true };
        var task = new EntityTask { ImmId = immId, MoldId = mold.Id, Mold = mold, Imm = imm, PlanQuantity = 1000, Status = EntityTaskStatus.InProgress };
        db.AddRange(mold, imm, task);
        await db.SaveChangesAsync();

        var cycle = new ImmCycle { ImmId = immId, TaskId = task.Id, MoldId = mold.Id, IsSuccessful = true, Cavities = 2, StartTime = DateTime.UtcNow.AddSeconds(-10), EndTime = DateTime.UtcNow };
        db.ImmCycles.Add(cycle);
        await db.SaveChangesAsync();

        var sut = new TaskOutputHandler(db, _emulator);
        await sut.HandleAsync(new CompletedCycle(cycle, task, "auto"));

        var reloaded = await db.ShiftTasks.FindAsync(task.Id);
        reloaded!.ActualQuantity.Should().Be(2);
        reloaded.ActualMaterialWeightGrams.Should().Be(2 * 10m + 5m);
    }

    [Fact]
    public async SystemTask Orphan_cycle_does_not_count_output()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();
        var cycle = new ImmCycle { ImmId = immId, TaskId = null, IsSuccessful = true, Cavities = 0, StartTime = DateTime.UtcNow.AddSeconds(-10), EndTime = DateTime.UtcNow };
        db.ImmCycles.Add(cycle);
        await db.SaveChangesAsync();

        var sut = new TaskOutputHandler(db, _emulator);
        await sut.HandleAsync(new CompletedCycle(cycle, null, "auto")); // не должно бросить
    }
}
```

- [ ] **Step 2: Запустить — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~TaskOutputHandlerTests`
Expected: ошибка компиляции (`TaskOutputHandler` не существует).

- [ ] **Step 3: Реализовать `TaskOutputHandler`**

`Wintime.Control.Infrastructure/Handlers/TaskOutputHandler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Стадия 2: учёт выпуска и материала активного InProgress-задания по завершённому циклу.
/// Правила — CycleProcessingPolicy.ShouldCountOutput (InProgress + auto + нет открытого простоя).
/// </summary>
public class TaskOutputHandler : ICycleHandler
{
    private readonly ControlDbContext _db;
    private readonly IEmulatorControlService _emulator;

    public TaskOutputHandler(ControlDbContext db, IEmulatorControlService emulator)
    {
        _db = db;
        _emulator = emulator;
    }

    public async SystemTask HandleAsync(CompletedCycle completed, CancellationToken ct = default)
    {
        var task = completed.ActiveTask;
        var cycle = completed.Cycle;
        if (task is null || !cycle.IsSuccessful)
            return;

        var taskStatus = ActiveTaskStatusMap.From(task.Status);
        bool hasOpenDowntime = await _db.Events.AnyAsync(
            e => e.ImmId == cycle.ImmId
              && e.EventType == Core.Enums.EventType.Downtime
              && e.EndTime == null, ct);

        if (!CycleProcessingPolicy.ShouldCountOutput(completed.Mode, taskStatus, hasOpenDowntime))
            return;

        task.ActualQuantity += cycle.Cavities;
        task.ActualMaterialWeightGrams += cycle.Cavities * task.Mold.PartWeightGrams + task.Mold.RunnerWeightGrams;
        if (task.ActualQuantity >= task.PlanQuantity)
            await _emulator.SetModeAsync(cycle.ImmId.ToString(), "idle", ct);

        await _db.SaveChangesAsync(ct);
    }
}
```

> Примечание: `ActiveTaskStatusMap` и `CycleProcessingPolicy` — существующие типы в `Wintime.Control.Core`. `task.Mold` уже загружен оркестратором через `Include(t => t.Mold)`.

- [ ] **Step 4: Запустить тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~TaskOutputHandlerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Infrastructure/Handlers/TaskOutputHandler.cs Wintime.Control.Tests.Unit/Handlers/TaskOutputHandlerTests.cs
git commit -m "feat(PZP-04): TaskOutputHandler — учёт выпуска/материала как шаг конвейера"
```

---

## Task 4: `UnplannedRunHandler` — открытие эпизода для сироты + регистрация конвейера

**Files:**
- Create: `Wintime.Control.Infrastructure/Handlers/UnplannedRunHandler.cs`
- Modify: `Wintime.Control.Infrastructure/Services/InfrastructureServiceExtensions.cs:63`
- Test: `Wintime.Control.Tests.Unit/Handlers/UnplannedRunHandlerTests.cs`

**Interfaces:**
- Consumes: `CompletedCycle`, `DbSet<UnplannedRun>` (Task 1).
- Produces: `class UnplannedRunHandler : ICycleHandler`.

- [ ] **Step 1: Написать падающие тесты**

`Wintime.Control.Tests.Unit/Handlers/UnplannedRunHandlerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Handlers;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Handlers;

public class UnplannedRunHandlerTests
{
    private static ControlDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ImmCycle OrphanCycle(Guid immId, DateTime end)
        => new() { ImmId = immId, TaskId = null, Cavities = 0, IsSuccessful = true, StartTime = end.AddSeconds(-10), EndTime = end };

    [Fact]
    public async SystemTask Opens_episode_on_first_orphan_cycle()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var end = DateTime.UtcNow;
        var sut = new UnplannedRunHandler(db);

        await sut.HandleAsync(new CompletedCycle(OrphanCycle(immId, end), null, "auto"));

        var run = await db.UnplannedRuns.SingleOrDefaultAsync();
        run.Should().NotBeNull();
        run!.ImmId.Should().Be(immId);
        run.StartTime.Should().Be(end);
        run.ClosedAt.Should().BeNull();
    }

    [Fact]
    public async SystemTask Does_not_duplicate_when_open_episode_exists()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = DateTime.UtcNow.AddMinutes(-5) });
        await db.SaveChangesAsync();
        var sut = new UnplannedRunHandler(db);

        await sut.HandleAsync(new CompletedCycle(OrphanCycle(immId, DateTime.UtcNow), null, "auto"));

        (await db.UnplannedRuns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async SystemTask Ignores_cycle_with_active_task()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var task = new EntityTask { ImmId = immId, Status = EntityTaskStatus.InProgress };
        var cycle = new ImmCycle { ImmId = immId, TaskId = task.Id, EndTime = DateTime.UtcNow };
        var sut = new UnplannedRunHandler(db);

        await sut.HandleAsync(new CompletedCycle(cycle, task, "auto"));

        (await db.UnplannedRuns.CountAsync()).Should().Be(0);
    }
}
```

- [ ] **Step 2: Запустить — падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~UnplannedRunHandlerTests`
Expected: ошибка компиляции (`UnplannedRunHandler` не существует).

- [ ] **Step 3: Реализовать `UnplannedRunHandler`**

`Wintime.Control.Infrastructure/Handlers/UnplannedRunHandler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Стадия 2: если завершённый цикл — сирота (нет активного задания), открывает
/// эпизод «работы без задания» для ТПА, если открытого ещё нет. Продление и счётчик
/// НЕ хранятся — деривятся из ImmCycles (derive-on-read).
/// </summary>
public class UnplannedRunHandler : ICycleHandler
{
    private readonly ControlDbContext _db;

    public UnplannedRunHandler(ControlDbContext db) => _db = db;

    public async SystemTask HandleAsync(CompletedCycle completed, CancellationToken ct = default)
    {
        if (completed.ActiveTask is not null)
            return; // не сирота

        var immId = completed.Cycle.ImmId;
        bool hasOpen = await _db.UnplannedRuns.AnyAsync(r => r.ImmId == immId && r.ClosedAt == null, ct);
        if (hasOpen)
            return;

        _db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = completed.Cycle.EndTime });
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Зарегистрировать хендлеры в DI (в порядке исполнения)**

`InfrastructureServiceExtensions.cs`, заменить строку `services.AddScoped<ICycleProcessingHandler, CycleProcessingHandler>();` (:63) на:

```csharp
        // Конвейер обработки цикла (Стадия 2) — порядок важен
        services.AddScoped<ICycleHandler, TaskOutputHandler>();
        services.AddScoped<ICycleHandler, UnplannedRunHandler>();
        services.AddScoped<ICycleProcessingHandler, CycleProcessingHandler>();
```

- [ ] **Step 5: Запустить тесты хендлера + полный unit-прогон**

Run:
```powershell
dotnet test Wintime.Control.Tests.Unit
```
Expected: PASS (все юнит-тесты зелёные, включая рефактор Task 2/3).

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Infrastructure/Handlers/UnplannedRunHandler.cs Wintime.Control.Infrastructure/Services/InfrastructureServiceExtensions.cs Wintime.Control.Tests.Unit/Handlers/UnplannedRunHandlerTests.cs
git commit -m "feat(PZP-04): UnplannedRunHandler + регистрация конвейера цикла в DI"
```

---

## Task 5: Закрытие эпизода в `StartTask`

**Files:**
- Modify: `Wintime.Control.API/Controllers/TasksController.cs:264-282`
- Test: `Wintime.Control.Tests.Integration/UnplannedRuns/StartTaskClosesEpisodeTests.cs`

**Interfaces:**
- Consumes: `DbSet<UnplannedRun>`; существующий `StartTask` (`Issued→Setup`).

- [ ] **Step 1: Написать падающий интеграционный тест**

`Wintime.Control.Tests.Integration/UnplannedRuns/StartTaskClosesEpisodeTests.cs`:

```csharp
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;

namespace Wintime.Control.Tests.Integration.UnplannedRuns;

public class StartTaskClosesEpisodeTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public StartTaskClosesEpisodeTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task StartTask_closes_open_unplanned_run_on_same_imm()
    {
        var immId = await _factory.CreateFreshImmAsync();
        Guid taskId, runId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var task = new EntityTask { ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 100, Status = TaskStatus.Issued, IssuedAt = DateTime.UtcNow };
            db.ShiftTasks.Add(task);
            var run = new UnplannedRun { ImmId = immId, StartTime = DateTime.UtcNow.AddMinutes(-10) };
            db.UnplannedRuns.Add(run);
            await db.SaveChangesAsync();
            taskId = task.Id; runId = run.Id;
        }

        var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, "test_adjuster", "Adjuster123!");
        var resp = await client.PostAsync($"/api/tasks/{taskId}/start", null);
        resp.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var run = await db.UnplannedRuns.FindAsync(runId);
            run!.ClosedAt.Should().NotBeNull();
        }
    }
}
```

> Если хелпера `TestAuth` в проекте нет — использовать существующий способ аутентификации из соседних интеграционных тестов (посмотреть, как логинятся `DowntimeReasonCrudTests`), и повторить его здесь.

- [ ] **Step 2: Запустить — падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~StartTaskClosesEpisodeTests`
Expected: FAIL (`ClosedAt` остаётся null).

- [ ] **Step 3: Закрыть эпизод в `StartTask`**

`TasksController.cs`, в методе `StartTask`, между `task.StartSetup();` и `await _context.SaveChangesAsync();` (:279-281) вставить:

```csharp
        // PZP-04: закрыть открытый эпизод «работы без задания» этого ТПА —
        // старт задания = детерминированная граница эпизода.
        var openRun = await _context.UnplannedRuns
            .Where(r => r.ImmId == task.ImmId && r.ClosedAt == null)
            .OrderBy(r => r.StartTime)
            .FirstOrDefaultAsync();
        if (openRun != null)
            openRun.ClosedAt = DateTime.UtcNow;
```

Убедиться, что в начале файла есть `using Microsoft.EntityFrameworkCore;` (для `FirstOrDefaultAsync`); если нет — добавить.

- [ ] **Step 4: Запустить тест**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~StartTaskClosesEpisodeTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.API/Controllers/TasksController.cs Wintime.Control.Tests.Integration/UnplannedRuns/StartTaskClosesEpisodeTests.cs
git commit -m "feat(PZP-04): закрытие эпизода работы без задания при StartTask"
```

---

## Task 6: Правило смежности `UnplannedRunAdjacency` (чистые функции)

**Files:**
- Create: `Wintime.Control.Core/Policies/UnplannedRunAdjacency.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/UnplannedRunAdjacencyTests.cs`

**Interfaces:**
- Produces: статические функции `Overlaps(tStart,tEnd,eStart,eEnd)`, `AdjacentAfter(taskFirstActivity,eEnd,avgCycleSeconds)`, `AdjacentBefore(taskLastCycleEnd,eStart,avgCycleSeconds)`, `SameDate(taskDate,eStart)`; константа `ToleranceFactor = 1.5`.

- [ ] **Step 1: Написать падающие тесты**

`Wintime.Control.Tests.Unit/Policies/UnplannedRunAdjacencyTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class UnplannedRunAdjacencyTests
{
    private static readonly DateTime E0 = new(2026, 7, 25, 10, 0, 0, DateTimeKind.Utc);   // episode start
    private static readonly DateTime E1 = new(2026, 7, 25, 10, 30, 0, DateTimeKind.Utc);  // episode end
    private const double Avg = 60; // сек

    [Fact]
    public void Overlaps_true_when_intervals_intersect()
        => UnplannedRunAdjacency.Overlaps(E0.AddMinutes(15), E1.AddMinutes(15), E0, E1).Should().BeTrue();

    [Fact]
    public void Overlaps_false_when_disjoint()
        => UnplannedRunAdjacency.Overlaps(E1.AddMinutes(5), E1.AddMinutes(20), E0, E1).Should().BeFalse();

    [Fact]
    public void AdjacentAfter_true_when_gap_below_threshold()
        => UnplannedRunAdjacency.AdjacentAfter(E1.AddSeconds(80), E1, Avg).Should().BeTrue(); // 80 < 1.5*60=90

    [Fact]
    public void AdjacentAfter_false_when_gap_at_or_above_threshold()
        => UnplannedRunAdjacency.AdjacentAfter(E1.AddSeconds(90), E1, Avg).Should().BeFalse();

    [Fact]
    public void AdjacentBefore_true_when_gap_below_threshold()
        => UnplannedRunAdjacency.AdjacentBefore(E0.AddSeconds(-80), E0, Avg).Should().BeTrue();

    [Fact]
    public void AdjacentBefore_false_when_gap_at_or_above_threshold()
        => UnplannedRunAdjacency.AdjacentBefore(E0.AddSeconds(-90), E0, Avg).Should().BeFalse();

    [Fact]
    public void SameDate_true_when_same_calendar_day_utc()
        => UnplannedRunAdjacency.SameDate(new DateTime(2026, 7, 25, 23, 59, 0, DateTimeKind.Utc), E0).Should().BeTrue();
}
```

- [ ] **Step 2: Запустить — падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~UnplannedRunAdjacencyTests`
Expected: ошибка компиляции.

- [ ] **Step 3: Реализовать политику**

`Wintime.Control.Core/Policies/UnplannedRunAdjacency.cs`:

```csharp
namespace Wintime.Control.Core.Policies;

/// <summary>
/// Правило смежности эпизода «работы без задания» и задания (PZP-04, UC-5).
/// Задание допустимо в кандидаты, если у него нет разрыва во времени с эпизодом
/// (пересечение / сразу после / сразу перед) либо это задание без циклов в ту же дату.
/// Порог разрыва — 1.5 × средней длительности цикла эпизода, симметрично с обеих сторон.
/// </summary>
public static class UnplannedRunAdjacency
{
    public const double ToleranceFactor = 1.5;

    /// <summary>Рабочий интервал задания пересекается с окном эпизода.</summary>
    public static bool Overlaps(DateTime taskStart, DateTime taskEnd, DateTime episodeStart, DateTime episodeEnd)
        => taskStart < episodeEnd && taskEnd > episodeStart;

    /// <summary>Задание началось сразу после эпизода (разрыв справа меньше порога).</summary>
    public static bool AdjacentAfter(DateTime taskFirstActivity, DateTime episodeEnd, double avgCycleSeconds)
        => taskFirstActivity >= episodeEnd
        && (taskFirstActivity - episodeEnd).TotalSeconds < ToleranceFactor * avgCycleSeconds;

    /// <summary>Задание закончилось сразу перед эпизодом (разрыв слева меньше порога).</summary>
    public static bool AdjacentBefore(DateTime taskLastCycleEnd, DateTime episodeStart, double avgCycleSeconds)
        => taskLastCycleEnd <= episodeStart
        && (episodeStart - taskLastCycleEnd).TotalSeconds < ToleranceFactor * avgCycleSeconds;

    /// <summary>Дата задания (без времени) совпадает с датой начала эпизода (UTC).</summary>
    public static bool SameDate(DateTime taskDate, DateTime episodeStart)
        => taskDate.Date == episodeStart.Date;
}
```

- [ ] **Step 4: Запустить тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~UnplannedRunAdjacencyTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Core/Policies/UnplannedRunAdjacency.cs Wintime.Control.Tests.Unit/Policies/UnplannedRunAdjacencyTests.cs
git commit -m "feat(PZP-04): чистое правило смежности UnplannedRunAdjacency (UC-5)"
```

---

## Task 7: DTO + `UnplannedRunController` — список журнала и кандидаты

**Files:**
- Create: `Wintime.Control.Core/DTOs/UnplannedRun/UnplannedRunDto.cs`
- Create: `Wintime.Control.Core/DTOs/UnplannedRun/TaskCandidateDto.cs`
- Create: `Wintime.Control.API/Controllers/UnplannedRunController.cs`
- Test: `Wintime.Control.Tests.Integration/UnplannedRuns/UnplannedRunJournalTests.cs`

**Interfaces:**
- Produces: `GET /api/unplanned-runs`, `GET /api/unplanned-runs/{id}/candidates`. `UnplannedRunDto { Id, ImmId, ImmName, StartTime, EndTime, CycleCount, AvgCycleDuration, AssignedTaskId, AssignedTaskLabel, PersonnelName }`; `TaskCandidateDto { TaskId, Label, PersonnelName, Recommended }`.
- Consumes: `UnplannedRunAdjacency` (Task 6), `DbSet<UnplannedRun>`, `ImmCycles`, `ShiftTasks`.

- [ ] **Step 1: Создать DTO**

`Wintime.Control.Core/DTOs/UnplannedRun/UnplannedRunDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.UnplannedRun;

public class UnplannedRunDto
{
    public Guid Id { get; set; }
    public Guid ImmId { get; set; }
    public string ImmName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }         // деривированный конец (последний цикл)
    public int CycleCount { get; set; }            // деривированное число циклов эпизода
    public double AvgCycleDuration { get; set; }   // сек
    public bool IsClosed { get; set; }
    public Guid? AssignedTaskId { get; set; }
    public string? AssignedTaskLabel { get; set; } // № / ПФ назначенного задания
    public string? PersonnelName { get; set; }     // ФИО наладчика назначенного задания
}
```

`Wintime.Control.Core/DTOs/UnplannedRun/TaskCandidateDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.UnplannedRun;

public class TaskCandidateDto
{
    public Guid TaskId { get; set; }
    public string Label { get; set; } = "";        // человекочитаемое: ПФ + план + дата
    public string? PersonnelName { get; set; }
    public bool Recommended { get; set; }
}
```

- [ ] **Step 2: Написать падающий интеграционный тест (список + кандидаты)**

`Wintime.Control.Tests.Integration/UnplannedRuns/UnplannedRunJournalTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.UnplannedRun;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;

namespace Wintime.Control.Tests.Integration.UnplannedRuns;

public class UnplannedRunJournalTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public UnplannedRunJournalTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task List_returns_derived_cycle_count_and_end()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var start = DateTime.UtcNow.AddMinutes(-20);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = start });
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start, EndTime = start.AddSeconds(60), DurationSeconds = 60, IsSuccessful = true });
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start.AddSeconds(60), EndTime = start.AddSeconds(120), DurationSeconds = 60, IsSuccessful = true });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, "test_manager", "Manager123!");
        var list = await client.GetFromJsonAsync<List<UnplannedRunDto>>($"/api/unplanned-runs?immId={immId}");

        var run = list!.Single();
        run.CycleCount.Should().Be(2);
        run.AvgCycleDuration.Should().Be(60);
        run.EndTime.Should().Be(start.AddSeconds(120));
    }

    [Fact]
    public async Task Candidates_returns_only_same_imm_adjacent_tasks()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var otherImm = await _factory.CreateFreshImmAsync();
        var start = DateTime.UtcNow.AddMinutes(-20);
        Guid runId, adjacentTaskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var run = new UnplannedRun { ImmId = immId, StartTime = start, ClosedAt = start.AddMinutes(2) };
            db.UnplannedRuns.Add(run);
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start, EndTime = start.AddSeconds(60), DurationSeconds = 60, IsSuccessful = true });

            // задание того же ТПА, начавшееся сразу после эпизода (Setup)
            var adj = new EntityTask { ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 50, Status = TaskStatus.Setup, IssuedAt = start, SetupStartedAt = start.AddMinutes(2) };
            db.ShiftTasks.Add(adj);
            // задание ДРУГОГО ТПА — не должно попасть
            db.ShiftTasks.Add(new EntityTask { ImmId = otherImm, MoldId = _factory.TestMoldId, PlanQuantity = 50, Status = TaskStatus.Setup, SetupStartedAt = start.AddMinutes(2) });
            await db.SaveChangesAsync();
            runId = run.Id; adjacentTaskId = adj.Id;
        }

        var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, "test_manager", "Manager123!");
        var candidates = await client.GetFromJsonAsync<List<TaskCandidateDto>>($"/api/unplanned-runs/{runId}/candidates");

        candidates!.Select(c => c.TaskId).Should().ContainSingle().Which.Should().Be(adjacentTaskId);
        candidates.Single().Recommended.Should().BeTrue();
    }
}
```

> `TestAuth.AuthenticateAsync` — использовать тот же способ, что и другие интеграционные тесты (см. примечание в Task 5, Step 1).

- [ ] **Step 3: Запустить — падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~UnplannedRunJournalTests`
Expected: FAIL/404 (контроллера нет).

- [ ] **Step 4: Реализовать контроллер (список + кандидаты)**

`Wintime.Control.API/Controllers/UnplannedRunController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.UnplannedRun;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/unplanned-runs")]
[Authorize]
public class UnplannedRunController : ControllerBase
{
    private readonly ControlDbContext _context;
    public UnplannedRunController(ControlDbContext context) => _context = context;

    /// <summary>Журнал эпизодов «работы без задания» с деривированными агрегатами.</summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<IEnumerable<UnplannedRunDto>>> GetList(
        [FromQuery] Guid? immId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool? assigned = null)
    {
        if (from.HasValue) from = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
        if (to.HasValue)   to   = DateTime.SpecifyKind(to.Value,   DateTimeKind.Utc);

        var query = _context.UnplannedRuns
            .Include(r => r.Imm)
            .Include(r => r.AssignedTask).ThenInclude(t => t!.Mold)
            .Include(r => r.AssignedTask).ThenInclude(t => t!.Personnel)
            .AsQueryable();

        if (immId.HasValue) query = query.Where(r => r.ImmId == immId.Value);
        if (from.HasValue)  query = query.Where(r => r.StartTime >= from.Value);
        if (to.HasValue)    query = query.Where(r => r.StartTime <= to.Value);
        if (assigned.HasValue)
            query = assigned.Value ? query.Where(r => r.AssignedTaskId != null)
                                   : query.Where(r => r.AssignedTaskId == null);

        var runs = await query.OrderByDescending(r => r.StartTime).ToListAsync();

        var dtos = new List<UnplannedRunDto>(runs.Count);
        foreach (var r in runs)
        {
            var agg = await ComputeAggregatesAsync(r.Id, r.ImmId, r.StartTime, r.ClosedAt, r.AssignedTaskId);
            dtos.Add(new UnplannedRunDto
            {
                Id = r.Id,
                ImmId = r.ImmId,
                ImmName = r.Imm.Name,
                StartTime = r.StartTime,
                EndTime = agg.EndTime,
                CycleCount = agg.CycleCount,
                AvgCycleDuration = agg.AvgCycleDuration,
                IsClosed = r.ClosedAt != null,
                AssignedTaskId = r.AssignedTaskId,
                AssignedTaskLabel = r.AssignedTask == null ? null
                    : $"{r.AssignedTask.Mold.Name} · план {r.AssignedTask.PlanQuantity}",
                PersonnelName = r.AssignedTask?.Personnel?.FullName
            });
        }
        return Ok(dtos);
    }

    /// <summary>Задания-кандидаты для привязки: только этот ТПА, без разрыва во времени (UC-5).</summary>
    [HttpGet("{id:guid}/candidates")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<IEnumerable<TaskCandidateDto>>> GetCandidates(Guid id)
    {
        var run = await _context.UnplannedRuns.FindAsync(id);
        if (run == null) return NotFound("Эпизод не найден");

        var agg = await ComputeAggregatesAsync(run.Id, run.ImmId, run.StartTime, run.ClosedAt, run.AssignedTaskId);
        var episodeEnd = agg.EndTime ?? run.StartTime;
        var avg = agg.AvgCycleDuration > 0 ? agg.AvgCycleDuration : 1; // защита от деления/нулевого порога

        // Все задания этого ТПА + агрегаты их циклов (первый/последний цикл, число)
        var tasks = await _context.ShiftTasks
            .Where(t => t.ImmId == run.ImmId)
            .Include(t => t.Mold)
            .Include(t => t.Personnel)
            .ToListAsync();

        var cycleAgg = await _context.ImmCycles
            .Where(c => c.ImmId == run.ImmId && c.TaskId != null)
            .GroupBy(c => c.TaskId!.Value)
            .Select(g => new { TaskId = g.Key, First = g.Min(c => c.StartTime), Last = g.Max(c => c.EndTime), Count = g.Count() })
            .ToListAsync();

        var candidates = new List<TaskCandidateDto>();
        foreach (var t in tasks)
        {
            var cyc = cycleAgg.FirstOrDefault(a => a.TaskId == t.Id);
            bool hasCycles = cyc != null && cyc.Count > 0;

            DateTime? tStart = t.SetupStartedAt ?? t.StartedAt;
            DateTime? tEnd = t.ClosedAt ?? t.CompletedAt;

            bool overlaps = tStart.HasValue && tEnd.HasValue
                && UnplannedRunAdjacency.Overlaps(tStart.Value, tEnd.Value, run.StartTime, episodeEnd);

            DateTime? firstActivity = hasCycles ? cyc!.First : tStart;
            bool after = firstActivity.HasValue
                && UnplannedRunAdjacency.AdjacentAfter(firstActivity.Value, episodeEnd, avg);

            bool before = hasCycles
                && UnplannedRunAdjacency.AdjacentBefore(cyc!.Last, run.StartTime, avg);

            var taskDate = t.PlannedDate ?? t.IssuedAt ?? t.CreatedAt;
            bool backdated = !hasCycles && UnplannedRunAdjacency.SameDate(taskDate, run.StartTime);

            if (!(overlaps || after || before || backdated))
                continue;

            candidates.Add(new TaskCandidateDto
            {
                TaskId = t.Id,
                Label = $"{t.Mold.Name} · план {t.PlanQuantity} · {taskDate:dd.MM.yyyy}",
                PersonnelName = t.Personnel?.FullName,
                // приоритет рекомендации: overlap > after > before — проставим на втором проходе
                Recommended = false
            });

            // временно сохраним признаки в локальном словаре через кортеж —
            // проще пересчитать приоритет отдельным проходом ниже
        }

        // Рекомендованный по приоритету: пересечение → сразу после → сразу перед
        MarkRecommended(candidates, tasks, cycleAgg, run.StartTime, episodeEnd, avg);
        return Ok(candidates);
    }

    private static void MarkRecommended(
        List<TaskCandidateDto> candidates,
        List<Core.Entities.ShiftTask> tasks,
        IEnumerable<dynamic> cycleAgg,
        DateTime episodeStart, DateTime episodeEnd, double avg)
    {
        Guid? pick = null;
        // 1. пересечение
        foreach (var c in candidates)
        {
            var t = tasks.First(x => x.Id == c.TaskId);
            var tStart = t.SetupStartedAt ?? t.StartedAt;
            var tEnd = t.ClosedAt ?? t.CompletedAt;
            if (tStart.HasValue && tEnd.HasValue &&
                UnplannedRunAdjacency.Overlaps(tStart.Value, tEnd.Value, episodeStart, episodeEnd))
            { pick = c.TaskId; break; }
        }
        // 2. сразу после
        if (pick == null)
            foreach (var c in candidates)
            {
                var t = tasks.First(x => x.Id == c.TaskId);
                var start = t.SetupStartedAt ?? t.StartedAt;
                if (start.HasValue && UnplannedRunAdjacency.AdjacentAfter(start.Value, episodeEnd, avg))
                { pick = c.TaskId; break; }
            }
        if (pick != null)
        {
            var rec = candidates.FirstOrDefault(c => c.TaskId == pick);
            if (rec != null) rec.Recommended = true;
        }
    }

    private record Aggregates(int CycleCount, DateTime? EndTime, double AvgCycleDuration);

    /// <summary>Деривация агрегатов эпизода из ImmCycles (derive-on-read).</summary>
    private async Task<Aggregates> ComputeAggregatesAsync(
        Guid runId, Guid immId, DateTime startTime, DateTime? closedAt, Guid? assignedTaskId)
    {
        var q = _context.ImmCycles.Where(c => c.ImmId == immId && c.EndTime >= startTime);
        if (closedAt.HasValue)
            q = q.Where(c => c.EndTime <= closedAt.Value);
        // сироты + (после назначения) циклы назначенного задания
        q = assignedTaskId.HasValue
            ? q.Where(c => c.TaskId == null || c.TaskId == assignedTaskId.Value)
            : q.Where(c => c.TaskId == null);

        var list = await q.Select(c => new { c.EndTime, c.DurationSeconds }).ToListAsync();
        if (list.Count == 0)
            return new Aggregates(0, null, 0);
        return new Aggregates(list.Count, list.Max(c => c.EndTime), list.Average(c => c.DurationSeconds));
    }
}
```

> Порог рекомендации «сразу перед» (приоритет 3) в первом срезе не подсвечиваем отдельно (условие (c) уже участвует в eligibility); рекомендация ограничена overlap→after, что покрывает основные кейсы. Полный приоритет с «сразу перед» — тривиальное расширение `MarkRecommended`, если понадобится.

- [ ] **Step 5: Зарегистрировать роут**

Контроллеры маппятся автоматически (`[ApiController]` + `MapControllers()`), доп. регистрация не нужна. Убедиться сборкой.

Run:
```powershell
dotnet build Wintime.Control.API
```
Expected: успешная сборка.

- [ ] **Step 6: Запустить интеграционные тесты журнала**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~UnplannedRunJournalTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add Wintime.Control.Core/DTOs/UnplannedRun Wintime.Control.API/Controllers/UnplannedRunController.cs Wintime.Control.Tests.Integration/UnplannedRuns/UnplannedRunJournalTests.cs
git commit -m "feat(PZP-04): журнал работы без задания — список + кандидаты (UC-4/UC-5)"
```

---

## Task 8: Назначение задания эпизоду (`assign`) + переназначение

**Files:**
- Create: `Wintime.Control.Core/DTOs/UnplannedRun/AssignTaskRequestDto.cs`
- Modify: `Wintime.Control.API/Controllers/UnplannedRunController.cs` (добавить `POST {id}/assign`)
- Test: `Wintime.Control.Tests.Integration/UnplannedRuns/AssignTaskTests.cs`

**Interfaces:**
- Produces: `POST /api/unplanned-runs/{id}/assign` с телом `{ Guid TaskId }`.
- Consumes: `UnplannedRunAdjacency`, бэкфилл `ImmCycles`, пересчёт `ShiftTask.ActualQuantity/ActualMaterialWeightGrams`.

- [ ] **Step 1: Создать DTO запроса**

`Wintime.Control.Core/DTOs/UnplannedRun/AssignTaskRequestDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.UnplannedRun;

public class AssignTaskRequestDto
{
    public Guid TaskId { get; set; }
}
```

- [ ] **Step 2: Написать падающие тесты (назначение, чужой ТПА, переназначение)**

`Wintime.Control.Tests.Integration/UnplannedRuns/AssignTaskTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.UnplannedRun;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;

namespace Wintime.Control.Tests.Integration.UnplannedRuns;

public class AssignTaskTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public AssignTaskTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<(Guid runId, Guid taskId, Guid immId)> SeedEpisodeWithAdjacentTaskAsync()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var start = DateTime.UtcNow.AddMinutes(-20);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var run = new UnplannedRun { ImmId = immId, StartTime = start, ClosedAt = start.AddMinutes(2) };
        db.UnplannedRuns.Add(run);
        // 2 сироты-цикла по 2 гнезда, годные
        db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, Cavities = 0, IsSuccessful = true, StartTime = start, EndTime = start.AddSeconds(60), DurationSeconds = 60 });
        db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, Cavities = 0, IsSuccessful = true, StartTime = start.AddSeconds(60), EndTime = start.AddSeconds(120), DurationSeconds = 60 });
        var task = new EntityTask { ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 50, Status = TaskStatus.Setup, IssuedAt = start, SetupStartedAt = start.AddMinutes(2) };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return (run.Id, task.Id, immId);
    }

    [Fact]
    public async Task Assign_backfills_orphan_cycles_and_recomputes_output()
    {
        var (runId, taskId, immId) = await SeedEpisodeWithAdjacentTaskAsync();
        var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, "test_manager", "Manager123!");

        var resp = await client.PostAsJsonAsync($"/api/unplanned-runs/{runId}/assign", new AssignTaskRequestDto { TaskId = taskId });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var cycles = await db.ImmCycles.Where(c => c.ImmId == immId).ToListAsync();
        cycles.Should().OnlyContain(c => c.TaskId == taskId && c.MoldId == _factory.TestMoldId);
        cycles.Should().OnlyContain(c => c.Cavities == 1); // из Mold.Cavities тестовой ПФ (=1)
        var task = await db.ShiftTasks.FindAsync(taskId);
        task!.ActualQuantity.Should().Be(2); // 2 цикла × 1 гнездо
        var run = await db.UnplannedRuns.FindAsync(runId);
        run!.AssignedTaskId.Should().Be(taskId);
        run.AssignedByUserId.Should().NotBeNull();
    }

    [Fact]
    public async Task Assign_rejects_task_of_other_imm()
    {
        var (runId, _, _) = await SeedEpisodeWithAdjacentTaskAsync();
        var otherImm = await _factory.CreateFreshImmAsync();
        Guid otherTaskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var t = new EntityTask { ImmId = otherImm, MoldId = _factory.TestMoldId, PlanQuantity = 10, Status = TaskStatus.Issued, IssuedAt = DateTime.UtcNow };
            db.ShiftTasks.Add(t);
            await db.SaveChangesAsync();
            otherTaskId = t.Id;
        }
        var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, "test_manager", "Manager123!");

        var resp = await client.PostAsJsonAsync($"/api/unplanned-runs/{runId}/assign", new AssignTaskRequestDto { TaskId = otherTaskId });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reassign_rolls_back_previous_binding()
    {
        var (runId, taskA, immId) = await SeedEpisodeWithAdjacentTaskAsync();
        var client = _factory.CreateClient();
        await TestAuth.AuthenticateAsync(client, "test_manager", "Manager123!");
        await client.PostAsJsonAsync($"/api/unplanned-runs/{runId}/assign", new AssignTaskRequestDto { TaskId = taskA });

        // второе смежное задание того же ТПА
        Guid taskB;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var run = await db.UnplannedRuns.FindAsync(runId);
            var t = new EntityTask { ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 50, Status = TaskStatus.Setup, IssuedAt = run!.StartTime, SetupStartedAt = run.ClosedAt };
            db.ShiftTasks.Add(t);
            await db.SaveChangesAsync();
            taskB = t.Id;
        }

        var resp = await client.PostAsJsonAsync($"/api/unplanned-runs/{runId}/assign", new AssignTaskRequestDto { TaskId = taskB });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var taskAReloaded = await db.ShiftTasks.FindAsync(taskA);
            taskAReloaded!.ActualQuantity.Should().Be(0, "откат прежней привязки уменьшил выпуск A");
            var taskBReloaded = await db.ShiftTasks.FindAsync(taskB);
            taskBReloaded!.ActualQuantity.Should().Be(2);
        }
    }
}
```

- [ ] **Step 3: Запустить — падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~AssignTaskTests`
Expected: FAIL/404 (endpoint отсутствует).

- [ ] **Step 4: Реализовать endpoint `assign`**

Добавить в `UnplannedRunController` метод (и `using System.Security.Claims;` в начало файла):

```csharp
    /// <summary>Ретро-привязка эпизода к заданию: бэкфилл сирот-циклов + пересчёт выпуска (UC-6/UC-7).</summary>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTaskRequestDto request)
    {
        var run = await _context.UnplannedRuns.FirstOrDefaultAsync(r => r.Id == id);
        if (run == null) return NotFound("Эпизод не найден");

        var task = await _context.ShiftTasks.Include(t => t.Mold).FirstOrDefaultAsync(t => t.Id == request.TaskId);
        if (task == null) return NotFound("Задание не найдено");

        if (task.ImmId != run.ImmId)
            return BadRequest("Задание принадлежит другому ТПА");
        if (task.Mold.ProductTypeId == null)
            return BadRequest("У пресс-формы задания не задан тип изделия");

        var agg = await ComputeAggregatesAsync(run.Id, run.ImmId, run.StartTime, run.ClosedAt, run.AssignedTaskId);
        var episodeEnd = agg.EndTime ?? run.StartTime;
        if (!await IsAdjacentAsync(task, run.StartTime, episodeEnd, agg.AvgCycleDuration))
            return BadRequest("Задание не смежно эпизоду по времени");

        // UC-7: откат прежней привязки
        if (run.AssignedTaskId.HasValue && run.AssignedTaskId.Value != task.Id)
            await RollbackBindingAsync(run, run.AssignedTaskId.Value);

        // Бэкфилл сирот-циклов окна эпизода
        var windowQuery = _context.ImmCycles.Where(c => c.ImmId == run.ImmId && c.TaskId == null && c.EndTime >= run.StartTime);
        if (run.ClosedAt.HasValue)
            windowQuery = windowQuery.Where(c => c.EndTime <= run.ClosedAt.Value);
        var cycles = await windowQuery.ToListAsync();

        decimal addedQty = 0, addedWeight = 0;
        foreach (var c in cycles)
        {
            c.TaskId = task.Id;
            c.MoldId = task.MoldId;
            c.Cavities = task.Mold.Cavities; // снапшот текущей гнёздности (ADR-0001)
            if (c.IsSuccessful)
            {
                addedQty += c.Cavities;
                addedWeight += c.Cavities * task.Mold.PartWeightGrams + task.Mold.RunnerWeightGrams;
            }
        }
        task.ActualQuantity += (int)addedQty;
        task.ActualMaterialWeightGrams += addedWeight;

        run.AssignedTaskId = task.Id;
        run.AssignedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        run.AssignedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Задание назначено" });
    }

    private async Task<bool> IsAdjacentAsync(Core.Entities.ShiftTask task, DateTime episodeStart, DateTime episodeEnd, double avgCycleDuration)
    {
        var avg = avgCycleDuration > 0 ? avgCycleDuration : 1;
        var cyc = await _context.ImmCycles
            .Where(c => c.TaskId == task.Id)
            .GroupBy(c => c.TaskId)
            .Select(g => new { First = g.Min(c => c.StartTime), Last = g.Max(c => c.EndTime), Count = g.Count() })
            .FirstOrDefaultAsync();
        bool hasCycles = cyc != null && cyc.Count > 0;

        DateTime? tStart = task.SetupStartedAt ?? task.StartedAt;
        DateTime? tEnd = task.ClosedAt ?? task.CompletedAt;
        if (tStart.HasValue && tEnd.HasValue && UnplannedRunAdjacency.Overlaps(tStart.Value, tEnd.Value, episodeStart, episodeEnd))
            return true;

        DateTime? firstActivity = hasCycles ? cyc!.First : tStart;
        if (firstActivity.HasValue && UnplannedRunAdjacency.AdjacentAfter(firstActivity.Value, episodeEnd, avg))
            return true;
        if (hasCycles && UnplannedRunAdjacency.AdjacentBefore(cyc!.Last, episodeStart, avg))
            return true;

        var taskDate = task.PlannedDate ?? task.IssuedAt ?? task.CreatedAt;
        return !hasCycles && UnplannedRunAdjacency.SameDate(taskDate, episodeStart);
    }

    private async Task RollbackBindingAsync(Core.Entities.UnplannedRun run, Guid previousTaskId)
    {
        var prevTask = await _context.ShiftTasks.Include(t => t.Mold).FirstOrDefaultAsync(t => t.Id == previousTaskId);
        var windowQuery = _context.ImmCycles.Where(c => c.ImmId == run.ImmId && c.TaskId == previousTaskId && c.EndTime >= run.StartTime);
        if (run.ClosedAt.HasValue)
            windowQuery = windowQuery.Where(c => c.EndTime <= run.ClosedAt.Value);
        var cycles = await windowQuery.ToListAsync();

        decimal qty = 0, weight = 0;
        foreach (var c in cycles)
        {
            if (c.IsSuccessful && prevTask != null)
            {
                qty += c.Cavities;
                weight += c.Cavities * prevTask.Mold.PartWeightGrams + prevTask.Mold.RunnerWeightGrams;
            }
            c.TaskId = null;
            c.MoldId = null;
            c.Cavities = 0;
        }
        if (prevTask != null)
        {
            prevTask.ActualQuantity -= (int)qty;
            prevTask.ActualMaterialWeightGrams -= weight;
        }
    }
```

- [ ] **Step 5: Запустить тесты assign**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~AssignTaskTests`
Expected: PASS.

- [ ] **Step 6: Полный прогон backend-тестов**

Run:
```powershell
dotnet test Wintime.Control.Tests.Unit
dotnet test Wintime.Control.Tests.Integration
```
Expected: всё зелёное.

- [ ] **Step 7: Commit**

```powershell
git add Wintime.Control.Core/DTOs/UnplannedRun/AssignTaskRequestDto.cs Wintime.Control.API/Controllers/UnplannedRunController.cs Wintime.Control.Tests.Integration/UnplannedRuns/AssignTaskTests.cs
git commit -m "feat(PZP-04): назначение задания эпизоду + переназначение с откатом (UC-6/UC-7)"
```

---

## Task 9: Фронтенд — журнал работы без задания

**Files:**
- Create: `Wintime-Control-Frontend/src/api/unplannedRuns.js`
- Create: `Wintime-Control-Frontend/src/views/unplanned/UnplannedRunLogView.vue`
- Modify: `Wintime-Control-Frontend/src/router/index.js` (после блока `downtimes`, :42)
- Modify: `Wintime-Control-Frontend/src/layouts/DefaultLayout.vue` (после пункта «Журнал простоев», :45)
- Test: `Wintime-Control-Frontend/src/views/unplanned/__tests__/unplannedRunLog.spec.js`

**Interfaces:**
- Consumes: `GET /api/unplanned-runs`, `GET /api/unplanned-runs/{id}/candidates`, `POST /api/unplanned-runs/{id}/assign`.

- [ ] **Step 1: Создать API-модуль**

`Wintime-Control-Frontend/src/api/unplannedRuns.js`:

```javascript
import apiClient from './client'

export const unplannedRunsApi = {
  getList(params) {
    return apiClient.get('/unplanned-runs', { params })
  },
  getCandidates(id) {
    return apiClient.get(`/unplanned-runs/${id}/candidates`)
  },
  assign(id, taskId) {
    return apiClient.post(`/unplanned-runs/${id}/assign`, { taskId })
  }
}
```

- [ ] **Step 2: Написать падающий Vitest на форматирование строки журнала**

`Wintime-Control-Frontend/src/views/unplanned/__tests__/unplannedRunLog.spec.js`:

```javascript
import { describe, it, expect } from 'vitest'
import { formatRunStatus } from '../unplannedRunFormat'

describe('formatRunStatus', () => {
  it('показывает «Назначено» при наличии задания', () => {
    expect(formatRunStatus({ assignedTaskId: 'x', assignedTaskLabel: 'ПФ · план 50' }))
      .toBe('Назначено: ПФ · план 50')
  })
  it('показывает «Не назначено» без задания', () => {
    expect(formatRunStatus({ assignedTaskId: null })).toBe('Не назначено')
  })
})
```

- [ ] **Step 3: Запустить — падает**

Run: `cd Wintime-Control-Frontend && npx vitest run src/views/unplanned`
Expected: FAIL (модуль `unplannedRunFormat` не существует).

- [ ] **Step 4: Создать хелпер форматирования**

`Wintime-Control-Frontend/src/views/unplanned/unplannedRunFormat.js`:

```javascript
export function formatRunStatus(run) {
  if (run.assignedTaskId) {
    return `Назначено: ${run.assignedTaskLabel ?? ''}`.trim()
  }
  return 'Не назначено'
}
```

- [ ] **Step 5: Запустить — проходит**

Run: `cd Wintime-Control-Frontend && npx vitest run src/views/unplanned`
Expected: PASS.

- [ ] **Step 6: Создать вью журнала**

`Wintime-Control-Frontend/src/views/unplanned/UnplannedRunLogView.vue` (по образцу `DowntimeLogView.vue` — Element Plus `el-table` + диалог назначения):

```vue
<template>
  <div class="p-4">
    <h2 class="text-xl font-semibold mb-4">Журнал работы без задания</h2>

    <el-table :data="runs" v-loading="loading" border>
      <el-table-column label="ТПА" prop="immName" min-width="140" />
      <el-table-column label="Начало" min-width="160">
        <template #default="{ row }">{{ formatDt(row.startTime) }}</template>
      </el-table-column>
      <el-table-column label="Конец" min-width="160">
        <template #default="{ row }">{{ row.endTime ? formatDt(row.endTime) : '—' }}</template>
      </el-table-column>
      <el-table-column label="Циклов" prop="cycleCount" width="90" align="center" />
      <el-table-column label="Статус" min-width="200">
        <template #default="{ row }">{{ formatRunStatus(row) }}</template>
      </el-table-column>
      <el-table-column label="Наладчик" prop="personnelName" min-width="160">
        <template #default="{ row }">{{ row.personnelName ?? '—' }}</template>
      </el-table-column>
      <el-table-column label="Действие" width="160">
        <template #default="{ row }">
          <el-button size="small" type="primary" @click="openAssign(row)">Назначить</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="assignVisible" title="Назначить задание эпизоду" width="520px">
      <el-select v-model="selectedTaskId" placeholder="Выберите задание" class="w-full">
        <el-option
          v-for="c in candidates"
          :key="c.taskId"
          :value="c.taskId"
          :label="(c.recommended ? '★ ' : '') + c.label + (c.personnelName ? ` · ${c.personnelName}` : '')"
        />
      </el-select>
      <p v-if="recommendedLabel" class="text-sm text-gray-500 mt-2">
        Рекомендуется: примыкает к заданию «{{ recommendedLabel }}»
      </p>
      <template #footer>
        <el-button @click="assignVisible = false">Отмена</el-button>
        <el-button type="primary" :disabled="!selectedTaskId" @click="confirmAssign">Назначить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { unplannedRunsApi } from '@/api/unplannedRuns'
import { formatRunStatus } from './unplannedRunFormat'

const runs = ref([])
const loading = ref(false)
const assignVisible = ref(false)
const candidates = ref([])
const selectedTaskId = ref(null)
const currentRun = ref(null)

const recommendedLabel = computed(() => candidates.value.find(c => c.recommended)?.label ?? '')

function formatDt(v) {
  return new Date(v).toLocaleString('ru-RU')
}

async function load() {
  loading.value = true
  try {
    const { data } = await unplannedRunsApi.getList({})
    runs.value = data
  } catch (e) {
    ElMessage.error('Не удалось загрузить журнал')
  } finally {
    loading.value = false
  }
}

async function openAssign(row) {
  currentRun.value = row
  selectedTaskId.value = null
  try {
    const { data } = await unplannedRunsApi.getCandidates(row.id)
    candidates.value = data
    selectedTaskId.value = data.find(c => c.recommended)?.taskId ?? null
    assignVisible.value = true
  } catch (e) {
    ElMessage.error('Не удалось загрузить кандидатов')
  }
}

async function confirmAssign() {
  try {
    await unplannedRunsApi.assign(currentRun.value.id, selectedTaskId.value)
    ElMessage.success('Задание назначено')
    assignVisible.value = false
    await load()
  } catch (e) {
    ElMessage.error(e.response?.data ?? 'Ошибка назначения')
  }
}

onMounted(load)
</script>
```

- [ ] **Step 7: Добавить роут**

`router/index.js`, после блока `downtimes` (закрывающая `}` на :42, перед блоком `reports`):

```javascript
      {
        path: 'unplanned-runs',
        name: 'UnplannedRunLog',
        component: () => import('@/views/unplanned/UnplannedRunLogView.vue'),
        meta: {
          roles: ['Admin', 'Manager'],
          title: 'Журнал работы без задания'
        }
      },
```

- [ ] **Step 8: Добавить пункт меню**

`DefaultLayout.vue`, после `el-menu-item` «Журнал простоев» (:45):

```html
          <!-- Журнал работы без задания (для Manager, Admin) -->
          <el-menu-item
            v-if="canAccess(['Admin', 'Manager'])"
            index="/unplanned-runs"
          >
            <el-icon><Warning /></el-icon>
            <span>Работа без задания</span>
          </el-menu-item>
```

Убедиться, что иконка `Warning` импортирована из `@element-plus/icons-vue` в `<script setup>` этого файла; если нет — добавить её в существующий импорт иконок.

- [ ] **Step 9: Собрать фронт и прогнать Vitest**

Run:
```powershell
cd Wintime-Control-Frontend
npx vitest run
npm run build
```
Expected: тесты зелёные, сборка успешна.

- [ ] **Step 10: Commit**

```powershell
git add Wintime-Control-Frontend/src/api/unplannedRuns.js Wintime-Control-Frontend/src/views/unplanned Wintime-Control-Frontend/src/router/index.js Wintime-Control-Frontend/src/layouts/DefaultLayout.vue
git commit -m "feat(PZP-04): фронт — журнал работы без задания + назначение (UC-4/UC-6)"
```

---

## Task 10: ADR-0008

**Files:**
- Create: `docs/adr/0008-cycle-pipeline-and-unplanned-run.md`

- [ ] **Step 1: Написать ADR**

`docs/adr/0008-cycle-pipeline-and-unplanned-run.md` (формат MADR, по образцу `docs/adr/0006-effective-imm-state.md`):

```markdown
# 0008. Конвейер обработки цикла + журнал «работы без задания» (UnplannedRun)

- Статус: Принято
- Дата: 2026-07-25

## Контекст и проблема

Сироты-циклы (`ImmCycle.TaskId = null`) при работе ТПА без принятого задания приводили к
неучтённому выпуску и ресурсу ПФ. `CycleProcessingHandler` был монолитом (детекция + запись +
учёт выпуска), что мешало добавить обработку сирот и уведомления.

## Решение

1. **Двухстадийная обработка цикла.** Оркестратор детектирует завершение цикла и СРАЗУ сохраняет
   `ImmCycle` (Стадия 1). Затем поверх сохранённого цикла последовательно исполняются `ICycleHandler`
   (Стадия 2), каждый в try/catch — сбой шага не теряет цикл и не роняет остальные шаги.
2. **Эпизод `UnplannedRun` — конверт с derive-on-read.** Число циклов, конец и средняя длительность
   деривятся из `ImmCycles`; эпизод не обновляется на каждый цикл (≈3 записи за жизнь).
3. **Ретро-привязка.** Менеджер назначает эпизоду задание того же ТПА, смежное по времени
   (порог `1.5 × avgCycleDuration`, симметрично); бэкфилл сирот-циклов (`TaskId/MoldId/Cavities`) и
   пересчёт выпуска. Переназначение откатывает прежнюю привязку.

## Альтернативы

- Эфемерная выборка сирот без журнала — отклонена: нельзя исправить ошибочную привязку.
- Хранить счётчик на эпизоде и обновлять на каждый цикл — отклонено: лишние UPDATE и индексная
  амплификация; derive-on-read из уже сохранённых циклов чище (цикл — источник правды).
- Sweep-воркер закрытия эпизода по таймауту — отложен (B2): закрытие делаем детерминированно в StartTask.

## Последствия

- Новый паттерн: обработка телеметрии расширяется хендлерами без правки оркестратора (задел под
  уведомления PZP-11/MUN-09). Счётчик на карточке ТПА (PZP-06) — дешёвый агрегат по открытому эпизоду.
- Денормализованный `ActualQuantity` может кратковременно дрейфовать при сбое шага; отчёт считает
  выпуск из `IsSuccessful`-циклов и самовосстанавливается.
```

- [ ] **Step 2: Commit**

```powershell
git add docs/adr/0008-cycle-pipeline-and-unplanned-run.md
git commit -m "docs(PZP-04): ADR-0008 — конвейер цикла + UnplannedRun"
```

---

## Итоговая проверка (после всех задач)

- [ ] **Полный прогон:**

```powershell
dotnet test Wintime.Control.Tests.Unit
dotnet test Wintime.Control.Tests.Integration
cd Wintime-Control-Frontend && npx vitest run && npm run build
```
Expected: всё зелёное.

- [ ] **Ручной smoke (по возможности):** запустить API + эмулятор, довести ТПА до «работы без задания» (Auto без активного задания), убедиться, что эпизод появился в журнале, назначить смежное задание, проверить пересчёт выпуска и ресурса ПФ.

- [ ] **PR:**

```powershell
git push -u origin feature/pzp-04-unplanned-run-journal
gh pr create --title "PZP-04: журнал работы без задания + конвейер обработки цикла" --body "См. спеку docs/superpowers/specs/2026-07-25-pzp-04-unplanned-run-journal-design.md и ADR-0008."
```

---

## Self-Review (для автора плана)

**Покрытие спеки:**
- UC-1 (открытие эпизода) → Task 4. UC-2 (долговечность) → Task 2. UC-3 (закрытие в StartTask) → Task 5.
- UC-4 (список) → Task 7. UC-5 (кандидаты/смежность/рекомендация) → Task 6 + Task 7. UC-6 (назначение) → Task 8. UC-7 (переназначение) → Task 8.
- Конвейер/двухстадийность → Task 2/3/4. Сущность+индексы → Task 1. Фронт → Task 9. ADR → Task 10.

**Отложено осознанно (вне первого среза, в спеке §11):** PZP-06 (счётчик на карточке ТПА), BL-24 (Гантт), B2 sweep-воркер, вариант B (создание задания), уведомления. Рекомендация «сразу перед» (приоритет 3) — eligibility есть (Task 6/7/8), подсветка ограничена overlap→after; полный приоритет — тривиальное расширение `MarkRecommended`.

**Риск сроков:** самая тяжёлая пара — Task 7 и Task 8 (кандидаты + assign с откатом). Если окно поджимает — Task 9 (фронт) можно сузить до списка без диалога назначения, оставив назначение на API-этап, но это ухудшит демо.
