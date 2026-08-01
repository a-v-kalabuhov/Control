# Рабочий режим ТПА и двухуровневый цикл — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить в задание рабочий режим (автомат/полуавтомат) и эталонные длительности цикла, принимать от коннектора длительность цикла литья и паузы, поднять порог простоя до единого высокого значения.

**Architecture:** Три независимых среза. (1) Данные задания: новый enum `WorkMode` + три поля `ShiftTask` с доменной валидацией, дальше по цепочке DTO → контроллер → форма. (2) Приём телеметрии: два новых `ParameterType`, читаются в `CycleProcessingHandler` и ложатся в два новых nullable-поля `ImmCycle`. (3) Поведение: `ShouldCountOutput` учитывает режим, порог простоя в конфиге поднимается со 120 до 900 секунд — код детекта простоя не меняется.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql, xUnit + FluentAssertions, Vue 3 + Element Plus, Vitest.

**Спека:** `docs/superpowers/specs/2026-08-01-work-mode-and-two-level-cycle-design.md`

## Global Constraints

- Ветка: `feature/work-mode-and-two-level-cycle` (уже создана, спека в ней). Прямой push в `master` запрещён — только PR.
- Все `DateTime` в EF-запросах к Postgres обязаны иметь `Kind=Utc`. Новые поля — целочисленные, дат не добавляем.
- Единственный источник правды по роли — `User.Role`. Новые эндпоинты не заводим, права существующих не меняем.
- Никаких физических удалений сущностей.
- Порог простоя по умолчанию: `IdleThresholdSeconds = 900`.
- Значения `ParameterType`: строго `injectionDuration` и `cyclePause`, единицы — миллисекунды, целые.
- `WorkMode`: `Auto = 0`, `SemiAuto = 1`. Значения `Manual` нет — ручной режим это наладка, а не выпуск партии.
- Работы на стороне коннектора USR-Modbus **в этот план не входят** (отдельный приватный репозиторий). До них новые поля `ImmCycle` останутся `null` — это штатное поведение, а не дефект.

---

### Task 1: enum `WorkMode` и поля задания с доменной валидацией

**Files:**
- Create: `Wintime.Control.Core/Enums/WorkMode.cs`
- Modify: `Wintime.Control.Core/Entities/ShiftTask.cs`
- Test: `Wintime.Control.Tests.Unit/Entities/ShiftTaskCycleNormsTests.cs`

**Interfaces:**
- Consumes: `DomainException` из `Wintime.Control.Core.Exceptions` (маппится в HTTP 400 через ADR-0003).
- Produces:
  - `enum WorkMode { Auto = 0, SemiAuto = 1 }`
  - `ShiftTask.WorkMode` (тип `WorkMode`, default `Auto`), `ShiftTask.PlannedFullCycleSeconds` (`int?`), `ShiftTask.PlannedInjectionCycleSeconds` (`int?`)
  - `void ShiftTask.SetCycleNorms(WorkMode workMode, int? fullCycleSeconds, int? injectionCycleSeconds, bool requireFullCycle)`

- [ ] **Step 1: Написать падающие тесты**

Создать `Wintime.Control.Tests.Unit/Entities/ShiftTaskCycleNormsTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Xunit;

namespace Wintime.Control.Tests.Unit.Entities;

public class ShiftTaskCycleNormsTests
{
    private static ShiftTask NewTask() => new() { ImmId = Guid.NewGuid(), MoldId = Guid.NewGuid() };

    [Fact]
    public void Default_WorkMode_IsAuto()
    {
        NewTask().WorkMode.Should().Be(WorkMode.Auto);
    }

    [Fact]
    public void SetCycleNorms_ValidValues_AssignsAll()
    {
        var task = NewTask();

        task.SetCycleNorms(WorkMode.SemiAuto, 60, 25, requireFullCycle: true);

        task.WorkMode.Should().Be(WorkMode.SemiAuto);
        task.PlannedFullCycleSeconds.Should().Be(60);
        task.PlannedInjectionCycleSeconds.Should().Be(25);
    }

    [Fact]
    public void SetCycleNorms_NullFullCycle_WhenRequired_Throws()
    {
        var task = NewTask();

        var act = () => task.SetCycleNorms(WorkMode.Auto, null, null, requireFullCycle: true);

        act.Should().Throw<DomainException>()
           .WithMessage("*полного цикла*");
    }

    [Fact]
    public void SetCycleNorms_NullFullCycle_WhenNotRequired_KeepsNull()
    {
        var task = NewTask();

        task.SetCycleNorms(WorkMode.Auto, null, null, requireFullCycle: false);

        task.PlannedFullCycleSeconds.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetCycleNorms_NonPositiveFullCycle_Throws(int seconds)
    {
        var task = NewTask();

        var act = () => task.SetCycleNorms(WorkMode.Auto, seconds, null, requireFullCycle: true);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void SetCycleNorms_NonPositiveInjectionCycle_Throws(int seconds)
    {
        var task = NewTask();

        var act = () => task.SetCycleNorms(WorkMode.Auto, 60, seconds, requireFullCycle: true);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SetCycleNorms_InjectionCycleGreaterThanFullCycle_Throws()
    {
        var task = NewTask();

        var act = () => task.SetCycleNorms(WorkMode.Auto, 60, 61, requireFullCycle: true);

        act.Should().Throw<DomainException>()
           .WithMessage("*не может превышать*");
    }

    [Fact]
    public void SetCycleNorms_InjectionCycleEqualToFullCycle_Allowed()
    {
        var task = NewTask();

        task.SetCycleNorms(WorkMode.Auto, 60, 60, requireFullCycle: true);

        task.PlannedInjectionCycleSeconds.Should().Be(60);
    }

    [Fact]
    public void SetCycleNorms_InjectionCycleWithoutFullCycle_OnLegacyTask_Throws()
    {
        var task = NewTask();

        var act = () => task.SetCycleNorms(WorkMode.Auto, null, 25, requireFullCycle: false);

        act.Should().Throw<DomainException>()
           .WithMessage("*без эталона полного цикла*");
    }
}
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftTaskCycleNormsTests`
Expected: FAIL — компиляция не проходит, `WorkMode` и `SetCycleNorms` не существуют.

- [ ] **Step 3: Создать enum**

Создать `Wintime.Control.Core/Enums/WorkMode.cs`:

```csharp
namespace Wintime.Control.Core.Enums;

/// <summary>
/// Рабочий режим ТПА при выпуске партии — как технолог назначил работать.
/// <para>
/// НЕ путать с <see cref="Wintime.Control.Core.Constants.ImmMode"/>: тот описывает
/// текущее состояние ТПА, приходящее из телеметрии (auto/manual/idle/alarm).
/// Здесь — намерение, заданное в задании и не зависящее от телеметрии.
/// </para>
/// Ручного режима в перечислении нет: ручной используется только для наладки,
/// а не для выпуска партии, поэтому в задании он невозможен.
/// </summary>
public enum WorkMode
{
    /// <summary>Автомат: изделие извлекает сам ТПА (толкателем либо воздушным клапаном).</summary>
    Auto = 0,

    /// <summary>Полуавтомат: изделие извлекает оператор, длительность паузы зависит от человека.</summary>
    SemiAuto = 1
}
```

- [ ] **Step 4: Добавить поля и доменный метод в `ShiftTask`**

В `Wintime.Control.Core/Entities/ShiftTask.cs` после `public int DefectQuantity { get; set; }` добавить поля:

```csharp
    // Рабочий режим и эталонные длительности цикла (спека 2026-08-01).
    // В будущем переезжают в технологическую карту.
    public WorkMode WorkMode { get; set; } = WorkMode.Auto;

    /// <summary>
    /// Эталонная длительность полного цикла, секунды. Обязателен для новых заданий
    /// (менеджер вычисляет его при планировании). <c>null</c> — legacy-задание,
    /// созданное до появления поля.
    /// </summary>
    public int? PlannedFullCycleSeconds { get; set; }

    /// <summary>
    /// Эталонная длительность цикла литья, секунды. Необязателен: технологический
    /// параметр, нужен только для анализа стабильности.
    /// </summary>
    public int? PlannedInjectionCycleSeconds { get; set; }
```

Перед `private void EnsureStatus(...)` добавить доменный метод:

```csharp
    /// <summary>
    /// Задать рабочий режим и эталонные длительности цикла.
    /// </summary>
    /// <param name="requireFullCycle">
    /// <c>true</c> при создании задания — эталон полного цикла обязателен.
    /// <c>false</c> при редактировании — legacy-задание можно сохранить без него.
    /// </param>
    public void SetCycleNorms(WorkMode workMode, int? fullCycleSeconds,
                              int? injectionCycleSeconds, bool requireFullCycle)
    {
        if (requireFullCycle && fullCycleSeconds is null)
            throw new DomainException("Не задан эталон полного цикла");

        if (fullCycleSeconds is <= 0)
            throw new DomainException("Эталон полного цикла должен быть больше нуля");

        if (injectionCycleSeconds is <= 0)
            throw new DomainException("Эталон цикла литья должен быть больше нуля");

        if (injectionCycleSeconds is not null)
        {
            if (fullCycleSeconds is null)
                throw new DomainException("Нельзя задать эталон цикла литья без эталона полного цикла");
            if (injectionCycleSeconds > fullCycleSeconds)
                throw new DomainException("Эталон цикла литья не может превышать эталон полного цикла");
        }

        WorkMode = workMode;
        PlannedFullCycleSeconds = fullCycleSeconds;
        PlannedInjectionCycleSeconds = injectionCycleSeconds;
    }
```

- [ ] **Step 5: Запустить тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftTaskCycleNormsTests`
Expected: PASS, 10 тестов.

- [ ] **Step 6: Прогнать весь unit-проект**

Run: `dotnet test Wintime.Control.Tests.Unit`
Expected: PASS, регрессий нет.

- [ ] **Step 7: Коммит**

```bash
git add Wintime.Control.Core/Enums/WorkMode.cs Wintime.Control.Core/Entities/ShiftTask.cs Wintime.Control.Tests.Unit/Entities/ShiftTaskCycleNormsTests.cs
git commit -m "feat(tasks): рабочий режим и эталонные длительности цикла в задании"
```

---

### Task 2: Поля `ImmCycle` и миграция БД

**Files:**
- Modify: `Wintime.Control.Core/Entities/ImmCycle.cs`
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs:75-118`
- Create: миграция в `Wintime.Control.Infrastructure/Migrations/` (имя генерируется)

**Interfaces:**
- Consumes: `ShiftTask.WorkMode` / `PlannedFullCycleSeconds` / `PlannedInjectionCycleSeconds` (Task 1).
- Produces: `ImmCycle.InjectionDurationMs` (`int?`), `ImmCycle.PauseDurationMs` (`int?`); колонки в БД для всех пяти новых полей.

- [ ] **Step 1: Добавить поля в `ImmCycle`**

В `Wintime.Control.Core/Entities/ImmCycle.cs` после свойства `Cavities` добавить:

```csharp
    /// <summary>
    /// Длительность цикла литья, миллисекунды: смыкание ПФ↑ → полное раскрытие↑.
    /// Приходит от коннектора сенсором типа <c>injectionDuration</c>.
    /// <c>null</c> — машина не отдаёт сигналы формы (штатный случай).
    /// </summary>
    public int? InjectionDurationMs { get; set; }

    /// <summary>
    /// Длительность паузы ПЕРЕД этим циклом литья, миллисекунды:
    /// предыдущее раскрытие↑ → смыкание↑. Сенсор типа <c>cyclePause</c>.
    /// Полный цикл = <see cref="InjectionDurationMs"/> + <see cref="PauseDurationMs"/>,
    /// отдельно не хранится.
    /// </summary>
    public int? PauseDurationMs { get; set; }
```

- [ ] **Step 2: Убедиться, что EF видит новые свойства без явной конфигурации**

Открыть `Wintime.Control.Infrastructure/Data/ControlDbContext.cs`, блок `builder.Entity<ImmCycle>(...)` (строки 109-118) и блок `ShiftTask` (строки 75-105). Дополнительная конфигурация **не требуется**: все пять полей — примитивные типы, EF выведет колонки по соглашению (`int?` → nullable integer, `WorkMode` → integer NOT NULL).

Никаких правок в этом файле делать не нужно — шаг проверочный.

- [ ] **Step 3: Создать миграцию**

Run:
```powershell
dotnet ef migrations add AddWorkModeAndCycleDurations --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: создан файл `Wintime.Control.Infrastructure/Migrations/<timestamp>_AddWorkModeAndCycleDurations.cs`.

- [ ] **Step 4: Проверить содержимое миграции**

Открыть сгенерированный файл. В `Up()` должно быть ровно пять `AddColumn`:

```csharp
migrationBuilder.AddColumn<int>(
    name: "WorkMode", table: "ShiftTasks", type: "integer",
    nullable: false, defaultValue: 0);
migrationBuilder.AddColumn<int>(
    name: "PlannedFullCycleSeconds", table: "ShiftTasks", type: "integer", nullable: true);
migrationBuilder.AddColumn<int>(
    name: "PlannedInjectionCycleSeconds", table: "ShiftTasks", type: "integer", nullable: true);
migrationBuilder.AddColumn<int>(
    name: "InjectionDurationMs", table: "ImmCycles", type: "integer", nullable: true);
migrationBuilder.AddColumn<int>(
    name: "PauseDurationMs", table: "ImmCycles", type: "integer", nullable: true);
```

Если `defaultValue: 0` у `WorkMode` отсутствует — дописать вручную. Без него миграция упадёт на непустой таблице `ShiftTasks`. Значение 0 = `WorkMode.Auto`, то есть существующие задания сохраняют текущее поведение.

Индексы не добавляем: по новым полям не фильтруем и не сортируем.

- [ ] **Step 5: Применить миграцию к локальной БД**

Run:
```powershell
dotnet ef database update --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: `Done.` без ошибок.

- [ ] **Step 6: Прогнать интеграционные тесты**

Run: `dotnet test Wintime.Control.Tests.Integration`
Expected: PASS. Схема поменялась, но новые поля пока никем не заполняются — существующие тесты должны остаться зелёными.

- [ ] **Step 7: Коммит**

```bash
git add Wintime.Control.Core/Entities/ImmCycle.cs Wintime.Control.Infrastructure/Migrations/
git commit -m "feat(cycles): поля длительности цикла литья и паузы в ImmCycle + миграция"
```

---

### Task 3: API — DTO, создание и редактирование задания

**Files:**
- Modify: `Wintime.Control.Core/DTOs/Tasks/CreateTaskRequestDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Tasks/UpdateTaskRequestDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Tasks/TaskDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Tasks/TaskMappingExtensions.cs:11-39`
- Modify: `Wintime.Control.API/Controllers/TasksController.cs:200-234` (CreateTask), `:241-266` (UpdateTask)
- Modify: `Wintime.Control.Tests.Integration/Tasks/TasksControllerTests.cs:287-293`
- Modify: `Wintime.Control.Tests.Integration/Tasks/TaskProductTypeGuardTests.cs:41-57`
- Modify: `Wintime.Control.Tests.Integration/Orders/TaskOrderBindingApiTests.cs:125-162`
- Test: `Wintime.Control.Tests.Integration/Tasks/TaskCycleNormsApiTests.cs`

**Interfaces:**
- Consumes: `ShiftTask.SetCycleNorms(WorkMode, int?, int?, bool)` (Task 1), `WorkMode` (Task 1).
- Produces: поля `workMode` / `plannedFullCycleSeconds` / `plannedInjectionCycleSeconds` в JSON запросов `POST /api/tasks`, `PUT /api/tasks/{id}` и в ответе `TaskDto`.

- [ ] **Step 1: Написать падающие интеграционные тесты**

Создать `Wintime.Control.Tests.Integration/Tasks/TaskCycleNormsApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskCycleNormsApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IntegrationTestFactory _factory;
    public TaskCycleNormsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private object CreateBody(object? workMode, int? full, int? injection) => new
    {
        immId = _factory.TestImmId,
        moldId = _factory.TestMoldId,
        planQuantity = 100,
        workMode,
        plannedFullCycleSeconds = full,
        plannedInjectionCycleSeconds = injection
    };

    [Fact]
    public async Task CreateTask_WithoutFullCycle_Returns400()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", CreateBody("Auto", null, null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTask_WithFullCycleOnly_Returns201AndEchoesFields()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", CreateBody("SemiAuto", 60, null));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("workMode").GetString().Should().Be("SemiAuto");
        body.GetProperty("plannedFullCycleSeconds").GetInt32().Should().Be(60);
        body.GetProperty("plannedInjectionCycleSeconds").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CreateTask_DefaultWorkMode_IsAuto()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId,
            moldId = _factory.TestMoldId,
            planQuantity = 100,
            plannedFullCycleSeconds = 45
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("workMode").GetString().Should().Be("Auto");
    }

    [Fact]
    public async Task CreateTask_InjectionCycleGreaterThanFullCycle_Returns400()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", CreateBody("Auto", 60, 61));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTask_ChangesWorkModeAndNorms()
    {
        var client = await ManagerClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateBody("Auto", 60, 20));
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetString()!;

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new
        {
            workMode = "SemiAuto",
            plannedFullCycleSeconds = 90,
            plannedInjectionCycleSeconds = 30
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);
        after.GetProperty("workMode").GetString().Should().Be("SemiAuto");
        after.GetProperty("plannedFullCycleSeconds").GetInt32().Should().Be(90);
        after.GetProperty("plannedInjectionCycleSeconds").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task UpdateTask_WithoutCycleFields_KeepsExistingValues()
    {
        var client = await ManagerClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateBody("SemiAuto", 75, 25));
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetString()!;

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { note = "только заметка" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);
        after.GetProperty("workMode").GetString().Should().Be("SemiAuto");
        after.GetProperty("plannedFullCycleSeconds").GetInt32().Should().Be(75);
    }
}
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~TaskCycleNormsApiTests`
Expected: FAIL — в ответе нет свойств `workMode` / `plannedFullCycleSeconds`, создание без эталона возвращает 201 вместо 400.

- [ ] **Step 3: Расширить DTO запросов**

`Wintime.Control.Core/DTOs/Tasks/CreateTaskRequestDto.cs` — заменить целиком:

```csharp
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Tasks;

public class CreateTaskRequestDto
{
    public Guid ImmId { get; set; }
    public Guid MoldId { get; set; }
    public Guid? OrderId { get; set; }
    public string? PersonnelId { get; set; }
    public int PlanQuantity { get; set; }
    public string? Note { get; set; }
    public DateTime? PlannedDate { get; set; }

    /// <summary>Рабочий режим; не передан — «автомат».</summary>
    public WorkMode WorkMode { get; set; } = WorkMode.Auto;

    /// <summary>Эталон полного цикла, секунды. Обязателен.</summary>
    public int? PlannedFullCycleSeconds { get; set; }

    /// <summary>Эталон цикла литья, секунды. Необязателен.</summary>
    public int? PlannedInjectionCycleSeconds { get; set; }
}
```

`Wintime.Control.Core/DTOs/Tasks/UpdateTaskRequestDto.cs` — заменить целиком:

```csharp
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Tasks;

public class UpdateTaskRequestDto
{
    public int? PlanQuantity { get; set; }
    public string? Note { get; set; }
    public DateTime? PlannedDate { get; set; }
    public Enums.TaskStatus? Status { get; set; }

    /// <summary>Рабочий режим; <c>null</c> — не менять.</summary>
    public WorkMode? WorkMode { get; set; }

    /// <summary>Эталон полного цикла, секунды; <c>null</c> — не менять.</summary>
    public int? PlannedFullCycleSeconds { get; set; }

    /// <summary>Эталон цикла литья, секунды; <c>null</c> — не менять.</summary>
    public int? PlannedInjectionCycleSeconds { get; set; }
}
```

- [ ] **Step 4: Расширить `TaskDto` и маппинг**

В `Wintime.Control.Core/DTOs/Tasks/TaskDto.cs` перед `public DateTime CreatedAt { get; set; }` добавить:

```csharp
    // Рабочий режим и эталоны цикла (спека 2026-08-01)
    public Enums.WorkMode WorkMode { get; set; }
    public int? PlannedFullCycleSeconds { get; set; }
    public int? PlannedInjectionCycleSeconds { get; set; }
```

В `Wintime.Control.Core/DTOs/Tasks/TaskMappingExtensions.cs` в инициализатор перед `CreatedAt = t.CreatedAt,` добавить:

```csharp
        WorkMode = t.WorkMode,
        PlannedFullCycleSeconds = t.PlannedFullCycleSeconds,
        PlannedInjectionCycleSeconds = t.PlannedInjectionCycleSeconds,
```

- [ ] **Step 5: Заполнять поля при создании задания**

В `Wintime.Control.API/Controllers/TasksController.cs`, метод `CreateTask`, сразу после блока инициализации `var task = new Core.Entities.ShiftTask { ... };` (перед `if (request.OrderId.HasValue)`) добавить:

```csharp
        task.SetCycleNorms(
            request.WorkMode,
            request.PlannedFullCycleSeconds,
            request.PlannedInjectionCycleSeconds,
            requireFullCycle: true);
```

`DomainException` из метода превращается в HTTP 400 существующим middleware (ADR-0003), дополнительный `try/catch` не нужен.

- [ ] **Step 6: Обрабатывать поля при редактировании**

В `Wintime.Control.API/Controllers/TasksController.cs`, метод `UpdateTask`, перед `if (request.Status.HasValue)` добавить:

```csharp
        bool cycleFieldsTouched = request.WorkMode.HasValue
                               || request.PlannedFullCycleSeconds.HasValue
                               || request.PlannedInjectionCycleSeconds.HasValue;
        if (cycleFieldsTouched)
        {
            // Не переданные поля сохраняют текущее значение задания.
            // requireFullCycle: false — legacy-задание без эталона можно сохранить,
            // не вынуждая менеджера выдумывать цифру задним числом.
            task.SetCycleNorms(
                request.WorkMode ?? task.WorkMode,
                request.PlannedFullCycleSeconds ?? task.PlannedFullCycleSeconds,
                request.PlannedInjectionCycleSeconds ?? task.PlannedInjectionCycleSeconds,
                requireFullCycle: false);
        }
```

- [ ] **Step 7: Починить существующие интеграционные тесты, создающие задания**

Эталон полного цикла стал обязательным, поэтому все существующие вызовы `POST /api/tasks` без него начнут получать 400. Правки:

`Wintime.Control.Tests.Integration/Tasks/TasksControllerTests.cs:287-293` — заменить хелпер:

```csharp
    private object MakeCreateRequest(Guid? immId = null) => new
    {
        immId = immId ?? _factory.TestImmId,
        moldId = _factory.TestMoldId,
        planQuantity = 100,
        note = "Integration test task",
        plannedFullCycleSeconds = 30
    };
```

`Wintime.Control.Tests.Integration/Tasks/TaskProductTypeGuardTests.cs` — в обоих телах запроса добавить поле:

```csharp
            immId = _factory.TestImmId, moldId, planQuantity = 10, note = "x",
            plannedFullCycleSeconds = 30
```

и

```csharp
            immId = _factory.TestImmId, moldId = _factory.TestMoldId, planQuantity = 10, note = "x",
            plannedFullCycleSeconds = 30
```

`Wintime.Control.Tests.Integration/Orders/TaskOrderBindingApiTests.cs:125-162` — во всех трёх литералах `new CreateTaskRequestDto { ... }` добавить `PlannedFullCycleSeconds = 30`, например:

```csharp
        var response = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequestDto
        {
            ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId, PlanQuantity = 10,
            OrderId = orderId, PlannedFullCycleSeconds = 30
        });
```

- [ ] **Step 8: Найти оставшиеся места создания заданий через API**

Run: `git grep -n "api/tasks\"" -- Wintime.Control.Tests.Integration`
Ожидается: все найденные `PostAsJsonAsync` содержат `plannedFullCycleSeconds` либо `PlannedFullCycleSeconds`. Если попался пропущенный — добавить туда `= 30` по образцу выше.

- [ ] **Step 9: Запустить новые тесты**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~TaskCycleNormsApiTests`
Expected: PASS, 6 тестов.

- [ ] **Step 10: Прогнать весь интеграционный проект**

Run: `dotnet test Wintime.Control.Tests.Integration`
Expected: PASS полностью.

- [ ] **Step 11: Коммит**

```bash
git add Wintime.Control.Core/DTOs/Tasks/ Wintime.Control.API/Controllers/TasksController.cs Wintime.Control.Tests.Integration/
git commit -m "feat(api): рабочий режим и эталоны цикла в создании и редактировании задания"
```

---

### Task 4: Приём длительностей от коннектора

**Files:**
- Modify: `Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs:144-152`
- Modify: `Wintime.Control.Infrastructure/Cache/TemplateCache.cs:46-60`
- Modify: `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs:88-105`
- Test: `Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs` (дополнить)
- Test: `Wintime.Control.Tests.Unit/Cache/TemplateCacheTests.cs` (дополнить)
- Test: `Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs` (дополнить)

**Interfaces:**
- Consumes: `ImmCycle.InjectionDurationMs`, `ImmCycle.PauseDurationMs` (Task 2); `SensorTemplate(Name, ParameterName, ParameterType, Threshold, AllowedValues, Required)`.
- Produces: значения `ParameterType` — `"injectionDuration"`, `"cyclePause"`; заполненные поля `ImmCycle`.

- [ ] **Step 1: Написать падающий тест валидации типов**

В `Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs` добавить строки к существующим `[Theory]`. Найти атрибут `[InlineData("cycleCounter", "100")]` и добавить рядом:

```csharp
    [InlineData("injectionDuration", "12500")]
    [InlineData("cyclePause", "3400")]
```

Найти атрибут `[InlineData("cycleCounter", "one")]` и добавить рядом:

```csharp
    [InlineData("injectionDuration", "12.5")]
    [InlineData("cyclePause", "abc")]
```

- [ ] **Step 2: Написать падающий тест принудительного `Threshold = 0`**

В `Wintime.Control.Tests.Unit/Cache/TemplateCacheTests.cs` добавить тест:

Рядом с `Upsert_SensorWithType_ParsesParameterType` (строки 245-265) добавить два теста, используя тот же хелпер `MakeTemplate` и тот же безаргументный конструктор `new TemplateCache()`:

```csharp
    /// <summary>
    /// Новые семантические типы длительностей должны разбираться так же,
    /// как cycleCounter.
    /// </summary>
    [Theory]
    [InlineData("injectionDuration")]
    [InlineData("cyclePause")]
    public void Upsert_DurationSensor_ParsesParameterType(string type)
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: $$"""
            {
                "sensors": [
                    { "name": "s", "field": "s", "type": "{{type}}" }
                ]
            }
            """);

        cache.Upsert(template);

        cache.GetById(template.Id)!.Sensors.Single().ParameterType.Should().Be(type);
    }

    /// <summary>
    /// COV-фильтрация для длительностей цикла обязана быть выключена: ненулевой
    /// порог в конфиге принудительно обнуляется, иначе фильтр (ADR-0005, вариант B)
    /// подставит значение прошлого цикла и вариация схлопнется в ровную линию.
    /// </summary>
    [Theory]
    [InlineData("injectionDuration")]
    [InlineData("cyclePause")]
    public void Upsert_DurationSensorWithThreshold_ForcesThresholdToZero(string type)
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: $$"""
            {
                "sensors": [
                    { "name": "s", "field": "s", "type": "{{type}}", "threshold": 50 }
                ]
            }
            """);

        cache.Upsert(template);

        cache.GetById(template.Id)!.Sensors.Single().Threshold.Should().Be(0m);
    }

    /// <summary>
    /// Обнуление порога не должно задевать остальные типы.
    /// </summary>
    [Fact]
    public void Upsert_FloatSensorWithThreshold_KeepsThreshold()
    {
        var cache = new TemplateCache();
        var template = MakeTemplate(json: """
            {
                "sensors": [
                    { "name": "s", "field": "s", "type": "float", "threshold": 50 }
                ]
            }
            """);

        cache.Upsert(template);

        cache.GetById(template.Id)!.Sensors.Single().Threshold.Should().Be(50m);
    }
```

- [ ] **Step 3: Написать падающий тест записи длительностей в цикл**

В `Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs` рядом с существующим приватным хелпером `MakeCycleContext` (строки 46-54) добавить второй, с сенсорами длительностей:

```csharp
    /// <summary>
    /// Контекст с тремя сенсорами: счётчик + защёлкнутые длительности цикла литья и паузы.
    /// </summary>
    private static MqttProcessingContext MakeCycleContextWithDurations(
        Guid immId, int counter, string mode, string injectionMs, string pauseMs)
    {
        var sensors = new[]
        {
            PipelineTestFixtures.MakeSensor("counter", type: "cycleCounter"),
            PipelineTestFixtures.MakeSensor("inj", type: "injectionDuration"),
            PipelineTestFixtures.MakeSensor("pause", type: "cyclePause")
        };
        var template = PipelineTestFixtures.MakeTemplate(sensors);
        var message = PipelineTestFixtures.MakeMessage(immId,
            sensors: new Dictionary<string, string>
            {
                ["counter"] = counter.ToString(),
                ["inj"] = injectionMs,
                ["pause"] = pauseMs
            }, mode: mode);
        var device = PipelineTestFixtures.MakeImmDto(immId);
        return PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}",
            data: message, device: device, template: template);
    }
```

и два теста (по образцу `Persists_orphan_cycle_and_runs_all_handlers_even_when_one_throws`, строки 56-78):

```csharp
    [Fact]
    public async SystemTask Completed_cycle_stores_injection_duration_and_pause()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        _tracker.Get(immId).Returns(new CycleState(DateTime.UtcNow.AddSeconds(-20), 5, "auto"));

        var sut = new CycleProcessingHandler(db, _tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        // Значения приходят защёлкнутыми в сообщении, закрывающем цикл.
        await sut.ProcessAsync(MakeCycleContextWithDurations(immId, 6, "auto", "12500", "3400"));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.InjectionDurationMs.Should().Be(12500);
        cycle.PauseDurationMs.Should().Be(3400);
    }

    [Fact]
    public async SystemTask Completed_cycle_without_duration_sensors_leaves_fields_null()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        _tracker.Get(immId).Returns(new CycleState(DateTime.UtcNow.AddSeconds(-20), 5, "auto"));

        var sut = new CycleProcessingHandler(db, _tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        // MakeCycleContext даёт шаблон только со счётчиком — машина без сигналов формы.
        await sut.ProcessAsync(MakeCycleContext(immId, 6, "auto"));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.InjectionDurationMs.Should().BeNull();
        cycle.PauseDurationMs.Should().BeNull();
    }
```

- [ ] **Step 4: Запустить тесты и убедиться, что они падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ValidateTelemetryDataHandlerTests|FullyQualifiedName~TemplateCacheTests|FullyQualifiedName~CycleProcessingHandlerTests"`
Expected: FAIL — новые типы не проходят валидацию, `Threshold` остаётся 50, полей длительности у цикла нет.

- [ ] **Step 5: Разрешить новые типы в валидации**

В `Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs`, метод `TryValidateSensorValue`, в `switch` после строки `"cycleCounter" => int.TryParse(...)` добавить:

```csharp
            "injectionDuration" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            "cyclePause"        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
```

В том же файле, метод `HasChangedBeyondThreshold`, расширить ветку целочисленных типов, чтобы новые типы сравнивались как целые:

```csharp
        else if (sensor.ParameterType is "int" or "cycleCounter" or "injectionDuration" or "cyclePause")
```

- [ ] **Step 6: Принудительно обнулить `Threshold` для новых типов**

В `Wintime.Control.Infrastructure/Cache/TemplateCache.cs`, метод `Parse`, заменить строку разбора порога:

```csharp
                    var threshold = s.TryGetProperty("threshold", out var th) && th.TryGetDecimal(out var thVal) ? thVal : 0m;
```

на:

```csharp
                    var threshold = s.TryGetProperty("threshold", out var th) && th.TryGetDecimal(out var thVal) ? thVal : 0m;

                    // COV-фильтрация для длительностей цикла обязана быть выключена:
                    // при ненулевом пороге фильтр подставит значение прошлого цикла
                    // (ADR-0005, вариант B) и вместо реальной вариации получится ровная
                    // линия — то есть потеряется ровно то, ради чего эти сенсоры заведены.
                    if (type is "injectionDuration" or "cyclePause" && threshold != 0m)
                        threshold = 0m;
```

- [ ] **Step 7: Читать длительности при сборке цикла**

В `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs`, метод `ProcessAsync`, внутри блока `if (cycleEnded)` перед созданием `var cycle = new ImmCycle {...}` добавить:

```csharp
            // Длительности приходят защёлкнутыми: коннектор обновляет их на событиях
            // формы и повторяет в каждом сообщении. Сенсоров нет — поля остаются null.
            int? injectionDurationMs = ReadIntSensor(template, data, "injectionDuration");
            int? pauseDurationMs = ReadIntSensor(template, data, "cyclePause");
```

и в инициализатор `ImmCycle` после `Cavities = cavities` добавить:

```csharp
                Cavities = cavities,
                InjectionDurationMs = injectionDurationMs,
                PauseDurationMs = pauseDurationMs
```

В конце класса (перед закрывающей скобкой) добавить приватный хелпер:

```csharp
    /// <summary>
    /// Прочитать целочисленный сенсор по семантическому типу шаблона.
    /// Возвращает <c>null</c>, если сенсор не описан в шаблоне, отсутствует
    /// в сообщении или значение не парсится.
    /// </summary>
    private static int? ReadIntSensor(CachedTemplate template, MqttTelemetryMessage data, string parameterType)
    {
        var sensor = template.Sensors.FirstOrDefault(s => s.ParameterType == parameterType);
        if (sensor is null)
            return null;
        if (!data.Sensors.TryGetValue(sensor.ParameterName, out var raw))
            return null;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
```

Типы взяты из `MqttProcessingContext`: `Template` имеет тип `CachedTemplate?`, `Data` — `MqttTelemetryMessage?`. Оба уже используются в методе, дополнительных `using` не требуется — `System.Globalization` и `Wintime.Control.Core.DTOs.Mqtt` в файле есть.

- [ ] **Step 8: Запустить тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ValidateTelemetryDataHandlerTests|FullyQualifiedName~TemplateCacheTests|FullyQualifiedName~CycleProcessingHandlerTests"`
Expected: PASS.

- [ ] **Step 9: Прогнать весь unit-проект**

Run: `dotnet test Wintime.Control.Tests.Unit`
Expected: PASS.

- [ ] **Step 10: Коммит**

```bash
git add Wintime.Control.Infrastructure/ Wintime.Control.Tests.Unit/
git commit -m "feat(telemetry): приём длительности цикла литья и паузы от коннектора"
```

---

### Task 5: Учёт выпуска в полуавтомате

**Files:**
- Modify: `Wintime.Control.Core/Policies/CycleProcessingPolicy.cs:23-30`
- Modify: `Wintime.Control.Infrastructure/Handlers/TaskOutputHandler.cs:40`
- Test: `Wintime.Control.Tests.Unit/Policies/CycleProcessingPolicyTests.cs`

**Interfaces:**
- Consumes: `WorkMode` (Task 1), `ShiftTask.WorkMode` (Task 1).
- Produces: `bool CycleProcessingPolicy.ShouldCountOutput(string mode, ActiveTaskStatus task, bool hasOpenDowntime, WorkMode workMode)` — **сигнатура меняется**, добавляется четвёртый параметр.

- [ ] **Step 1: Дописать падающие тесты**

В `Wintime.Control.Tests.Unit/Policies/CycleProcessingPolicyTests.cs` существующий `Matrix_MatchesSpecDocument` вызывает `ShouldCountOutput` с тремя аргументами. Обновить его вызов, добавив режим «автомат» (матрица описывает именно автоматический режим):

```csharp
        CycleProcessingPolicy.ShouldCountOutput(signal, task, hasOpenDowntime, WorkMode.Auto)
            .Should().Be(expectedOutput);
```

и добавить `using Wintime.Control.Core.Enums;` если его нет.

Затем добавить новый тест полуавтомата:

```csharp
    // В полуавтомате ТПА между циклами ждёт оператора, и коннектор по своему
    // таймауту успевает уйти в idle до того, как цикл завершится. Требовать
    // mode == auto здесь означало бы терять выпуск на каждом цикле.
    [Theory]
    // signal, task, hasOpenDowntime, expectedOutput
    [InlineData("auto",   ActiveTaskStatus.InProgress, false, true)]
    [InlineData("idle",   ActiveTaskStatus.InProgress, false, true)]
    [InlineData("alarm",  ActiveTaskStatus.InProgress, false, true)]
    [InlineData("manual", ActiveTaskStatus.InProgress, false, true)]
    [InlineData("idle",   ActiveTaskStatus.InProgress, true,  false)] // открытый простой
    [InlineData("idle",   ActiveTaskStatus.Setup,      false, false)] // наладка
    [InlineData("idle",   ActiveTaskStatus.None,       false, false)] // нет задания
    public void ShouldCountOutput_SemiAuto_IgnoresMode(
        string signal, ActiveTaskStatus task, bool hasOpenDowntime, bool expectedOutput)
    {
        CycleProcessingPolicy.ShouldCountOutput(signal, task, hasOpenDowntime, WorkMode.SemiAuto)
            .Should().Be(expectedOutput);
    }
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~CycleProcessingPolicyTests`
Expected: FAIL — компиляция не проходит, у `ShouldCountOutput` три параметра.

- [ ] **Step 3: Изменить политику**

В `Wintime.Control.Core/Policies/CycleProcessingPolicy.cs` заменить метод `ShouldCountOutput`:

```csharp
    /// <summary>
    /// Учитывать ли выпуск (ActualQuantity / материал задания).
    /// Общее условие: задание InProgress И нет открытого простоя.
    /// В автомате дополнительно требуется режим auto; в полуавтомате это условие
    /// снято — там ТПА между циклами ждёт оператора, и коннектор по своему таймауту
    /// успевает уйти в idle до завершения цикла.
    /// </summary>
    public static bool ShouldCountOutput(string mode, ActiveTaskStatus task,
                                         bool hasOpenDowntime, WorkMode workMode)
    {
        if (task != ActiveTaskStatus.InProgress || hasOpenDowntime)
            return false;

        return workMode == WorkMode.SemiAuto
            || ImmMode.Normalize(mode) == ImmMode.Auto;
    }
```

Убедиться, что `using Wintime.Control.Core.Enums;` в файле есть (он уже используется для `ActiveTaskStatus`).

- [ ] **Step 4: Передать режим из хендлера**

В `Wintime.Control.Infrastructure/Handlers/TaskOutputHandler.cs` заменить строку 40:

```csharp
        if (!CycleProcessingPolicy.ShouldCountOutput(completed.Mode, taskStatus, hasOpenDowntime, task.WorkMode))
            return;
```

и обновить XML-doc класса (строка 14):

```csharp
/// Правила — CycleProcessingPolicy.ShouldCountOutput (InProgress + нет простоя;
/// в автомате дополнительно mode == auto, в полуавтомате это условие снято).
```

- [ ] **Step 5: Запустить тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~CycleProcessingPolicyTests`
Expected: PASS.

- [ ] **Step 6: Прогнать оба тестовых проекта**

Run: `dotnet test Wintime.Control.Tests.Unit`
Run: `dotnet test Wintime.Control.Tests.Integration`
Expected: PASS оба.

- [ ] **Step 7: Коммит**

```bash
git add Wintime.Control.Core/Policies/CycleProcessingPolicy.cs Wintime.Control.Infrastructure/Handlers/TaskOutputHandler.cs Wintime.Control.Tests.Unit/Policies/CycleProcessingPolicyTests.cs
git commit -m "feat(cycles): засчитывать выпуск в полуавтомате независимо от режима телеметрии"
```

---

### Task 6: Единый высокий порог простоя

**Files:**
- Modify: `Wintime.Control.Shared/Settings/DowntimeSettings.cs:8`
- Modify: `Wintime.Control.API/appsettings.json:42-45`

**Interfaces:**
- Consumes: ничего из предыдущих задач.
- Produces: `DowntimeSettings.IdleThresholdSeconds` со значением по умолчанию 900.

- [ ] **Step 1: Поднять значение по умолчанию и объяснить его**

В `Wintime.Control.Shared/Settings/DowntimeSettings.cs` заменить свойство:

```csharp
    /// <summary>
    /// Порог: сколько секунд не-Auto при активном задании считать простоем.
    /// <para>
    /// 900 секунд (15 минут) — намеренно высокое значение, единое для автомата и
    /// полуавтомата. Журнал простоев есть очередь работы для наладчика и материал
    /// для разбора у начальника; записи ценой в минуту простоя обесценивают обе роли.
    /// Короткие остановы не теряются — длительность каждой паузы пишется в ImmCycle
    /// и разбирается аналитикой, без действий человека. Подробности — ADR-0010.
    /// </para>
    /// </summary>
    public int IdleThresholdSeconds { get; set; } = 900;
```

- [ ] **Step 2: Обновить конфигурацию API**

В `Wintime.Control.API/appsettings.json` в секции `"Downtime"` заменить значение:

```json
  "Downtime": {
    "IdleThresholdSeconds": 900,
    "PollingIntervalSeconds": 10
  },
```

- [ ] **Step 3: Проверить, что тесты простоев не зависят от значения по умолчанию**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~Downtime"`
Expected: PASS. Тесты задают порог явно (`IdleThresholdSeconds = threshold` в `DowntimeDetectionWorkerTests`, параметр `thresholdSeconds` в `DowntimeDecisionTests`), поэтому смена значения по умолчанию их не затрагивает. Если какой-то тест всё же покраснел — он полагался на дефолт неявно; исправить его, задав порог явно, а не подгонять новое значение.

- [ ] **Step 4: Прогнать оба тестовых проекта**

Run: `dotnet test Wintime.Control.Tests.Unit`
Run: `dotnet test Wintime.Control.Tests.Integration`
Expected: PASS оба.

- [ ] **Step 5: Коммит**

```bash
git add Wintime.Control.Shared/Settings/DowntimeSettings.cs Wintime.Control.API/appsettings.json
git commit -m "feat(downtime): единый порог простоя 15 минут вместо 2"
```

---

### Task 7: Форма задания и карточка на фронтенде

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/tasks/TaskFormModal.vue`
- Modify: `Wintime-Control-Frontend/src/views/tasks/TaskDetailModal.vue`
- Test: `Wintime-Control-Frontend/src/views/tasks/__tests__/TaskFormModal.spec.js`

**Interfaces:**
- Consumes: поля `workMode` / `plannedFullCycleSeconds` / `plannedInjectionCycleSeconds` в теле `tasksApi.create` и `tasksApi.update`, и те же поля в объекте задания из API (Task 3).
- Produces: ничего для последующих задач.

- [ ] **Step 1: Написать падающие тесты**

В `Wintime-Control-Frontend/src/views/tasks/__tests__/TaskFormModal.spec.js` добавить новый блок в конец файла:

```javascript
describe('TaskFormModal — рабочий режим и эталоны цикла', () => {
  beforeEach(() => vi.clearAllMocks())

  it('по умолчанию режим «автомат»', async () => {
    const wrapper = mountModal()
    await flushPromises()

    expect(wrapper.vm.form.workMode).toBe('Auto')
  })

  it('передаёт режим и эталоны в tasksApi.create', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.immId = 'imm-1'
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.planQuantity = 10
    wrapper.vm.form.workMode = 'SemiAuto'
    wrapper.vm.form.plannedFullCycleSeconds = 60
    wrapper.vm.form.plannedInjectionCycleSeconds = 25
    await flushPromises()

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.create).toHaveBeenCalledWith(
      expect.objectContaining({
        workMode: 'SemiAuto',
        plannedFullCycleSeconds: 60,
        plannedInjectionCycleSeconds: 25
      })
    )
  })

  it('не отправляет форму без эталона полного цикла', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.immId = 'imm-1'
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.planQuantity = 10
    wrapper.vm.form.plannedFullCycleSeconds = null
    await flushPromises()

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.create).not.toHaveBeenCalled()
  })

  it('не отправляет форму, если цикл литья больше полного цикла', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.immId = 'imm-1'
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.planQuantity = 10
    wrapper.vm.form.plannedFullCycleSeconds = 60
    wrapper.vm.form.plannedInjectionCycleSeconds = 61
    await flushPromises()

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.create).not.toHaveBeenCalled()
  })

  it('при редактировании заполняет поля из задания', async () => {
    const task = {
      id: 'task-3', immId: 'imm-1', moldId: 'mold-1', personnelId: '',
      planQuantity: 10, plannedDate: null, note: '', orderId: null,
      workMode: 'SemiAuto', plannedFullCycleSeconds: 90,
      plannedInjectionCycleSeconds: 30
    }
    const wrapper = mountModal({ task })
    await flushPromises()

    expect(wrapper.vm.form.workMode).toBe('SemiAuto')
    expect(wrapper.vm.form.plannedFullCycleSeconds).toBe(90)
    expect(wrapper.vm.form.plannedInjectionCycleSeconds).toBe(30)
  })
})
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `cd Wintime-Control-Frontend && npx vitest run src/views/tasks/__tests__/TaskFormModal.spec.js`
Expected: FAIL — `form.workMode` равен `undefined`, форма отправляется без эталона.

- [ ] **Step 3: Добавить поля в состояние формы**

В `Wintime-Control-Frontend/src/views/tasks/TaskFormModal.vue` в объекте `form` (реактивный `reactive({...})`) добавить три поля:

```javascript
const form = reactive({
  immId: '',
  moldId: '',
  personnelId: '',
  planQuantity: 1000,
  plannedDate: null,
  note: '',
  orderId: null,
  workMode: 'Auto',
  plannedFullCycleSeconds: null,
  plannedInjectionCycleSeconds: null
})
```

В `resetForm` добавить те же три поля с теми же значениями:

```javascript
const resetForm = () => {
  Object.assign(form, {
    immId: '',
    moldId: '',
    personnelId: '',
    planQuantity: 1000,
    plannedDate: null,
    note: '',
    orderId: props.lockedOrder?.id ?? null,
    workMode: 'Auto',
    plannedFullCycleSeconds: null,
    plannedInjectionCycleSeconds: null
  })
  orderOptions.value = props.lockedOrder ? [props.lockedOrder] : []
  initialOrderId.value = null
}
```

В `populateForm` добавить чтение из задания:

```javascript
    orderId: task.orderId || null,
    workMode: task.workMode || 'Auto',
    plannedFullCycleSeconds: task.plannedFullCycleSeconds ?? null,
    plannedInjectionCycleSeconds: task.plannedInjectionCycleSeconds ?? null
```

- [ ] **Step 4: Добавить правила валидации**

В объект `rules` добавить:

```javascript
  plannedFullCycleSeconds: [
    { required: true, message: 'Введите эталон полного цикла', trigger: 'blur' },
    { type: 'number', min: 1, message: 'Эталон должен быть больше 0', trigger: 'blur' }
  ],
  plannedInjectionCycleSeconds: [
    {
      validator: (rule, value, callback) => {
        if (value === null || value === undefined) return callback()
        if (value < 1) return callback(new Error('Эталон должен быть больше 0'))
        if (form.plannedFullCycleSeconds && value > form.plannedFullCycleSeconds)
          return callback(new Error('Цикл литья не может превышать полный цикл'))
        callback()
      },
      trigger: 'blur'
    }
  ]
```

- [ ] **Step 5: Добавить поля в разметку**

В `TaskFormModal.vue` после блока `<el-form-item label="План (шт.)" prop="planQuantity">…</el-form-item>` вставить три элемента:

```vue
      <el-form-item label="Рабочий режим" prop="workMode">
        <el-select v-model="form.workMode" class="w-full">
          <el-option label="Автомат" value="Auto" />
          <el-option label="Полуавтомат" value="SemiAuto" />
        </el-select>
      </el-form-item>

      <el-form-item label="Полный цикл (сек.)" prop="plannedFullCycleSeconds">
        <el-input-number
          v-model="form.plannedFullCycleSeconds"
          :min="1"
          :max="86400"
          class="w-full"
          controls-position="right"
        />
      </el-form-item>

      <el-form-item label="Цикл литья (сек.)" prop="plannedInjectionCycleSeconds">
        <el-input-number
          v-model="form.plannedInjectionCycleSeconds"
          :min="1"
          :max="86400"
          class="w-full"
          controls-position="right"
        />
        <div class="text-xs text-gray-500 mt-1">Необязательно — нужен для анализа стабильности</div>
      </el-form-item>
```

- [ ] **Step 6: Показать поля в карточке задания**

В `Wintime-Control-Frontend/src/views/tasks/TaskDetailModal.vue` в блоке `<el-descriptions>` после элемента `<el-descriptions-item label="Наладчик">…</el-descriptions-item>` (строки 23-25) вставить три элемента:

```vue
        <el-descriptions-item label="Рабочий режим">
          {{ task?.workMode === 'SemiAuto' ? 'Полуавтомат' : 'Автомат' }}
        </el-descriptions-item>
        <el-descriptions-item label="Полный цикл">
          {{ task?.plannedFullCycleSeconds ? `${task.plannedFullCycleSeconds} с` : '—' }}
        </el-descriptions-item>
        <el-descriptions-item label="Цикл литья">
          {{ task?.plannedInjectionCycleSeconds ? `${task.plannedInjectionCycleSeconds} с` : '—' }}
        </el-descriptions-item>
```

- [ ] **Step 7: Запустить тесты**

Run: `cd Wintime-Control-Frontend && npx vitest run src/views/tasks/__tests__/TaskFormModal.spec.js`
Expected: PASS.

- [ ] **Step 8: Прогнать весь Vitest и сборку**

Run: `cd Wintime-Control-Frontend && npx vitest run`
Expected: PASS полностью.

Run: `cd Wintime-Control-Frontend && npm run build`
Expected: сборка проходит без ошибок.

- [ ] **Step 9: Коммит**

```bash
git add Wintime-Control-Frontend/src/views/tasks/
git commit -m "feat(ui): рабочий режим и эталоны цикла в форме и карточке задания"
```

---

### Task 8: ADR и обновление CLAUDE.md

**Files:**
- Create: `docs/adr/0010-single-high-downtime-threshold.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: решения из Task 5 и Task 6.
- Produces: ничего для последующих задач.

- [ ] **Step 1: Написать ADR**

Создать `docs/adr/0010-single-high-downtime-threshold.md`:

```markdown
# ADR-0010: Единый высокий порог простоя, короткие остановы — в аналитику

- **Статус:** Accepted
- **Дата:** 2026-08-01

## Контекст

ТПА работают в автоматическом и полуавтоматическом режимах. В полуавтомате оператор
извлекает изделие вручную, поэтому пауза между циклами зависит от человека и
принципиально нестабильна. Порог авто-простоя составлял 120 секунд не-`auto` при
активном задании (ADR-0006) — в полуавтомате это давало запись простоя почти на каждой
паузе оператора.

Первоначально рассматривался режимо-зависимый порог: рабочий режим становится параметром
задания и определяет, сколько терпеть паузу. Обсуждение показало, что проблема не в
чувствительности детектора, а в назначении журнала простоев.

## Решение

Порог простоя — **единый и высокий: 900 секунд (15 минут)**, одинаковый для автомата и
полуавтомата, из конфигурации приложения. Отсчёт идёт от любого не-`auto` статуса и
сбрасывается только возвратом в `auto`; `alarm` и `manual` отсчёт продолжают.

Короткие остановы не теряются: коннектор отдаёт длительность цикла литья и паузы по
каждому циклу, они пишутся в `ImmCycle`, а в задании хранится эталон полного цикла.
«ТПА встал на 4 минуты» становится метрикой потери темпа, вычисляемой из данных о циклах,
и не требует ни одного действия человека.

Рабочий режим (`ShiftTask.WorkMode`) и эталоны длительностей остаются параметрами задания,
но **на детект простоя не влияют**. Режим влияет только на учёт выпуска: в полуавтомате
условие `mode == auto` в `CycleProcessingPolicy.ShouldCountOutput` снято.

## Альтернативы

- **Режимо-зависимый порог** (автомат 120 с, полуавтомат 900 с) — отвергнут: сохраняет
  в журнале шум коротких остановов в автомате, ради которого и заводился высокий порог
  в полуавтомате. Две константы вместо одной без выигрыша по существу.
- **Порог от эталона полного цикла × коэффициент** — отвергнут: самонастройка под скорость
  ПФ выглядит привлекательно, но добавляет неочевидную зависимость журнала от поля,
  которое менеджер заполняет для планирования, а не для детекции.
- **Немедленное открытие простоя по `alarm`** — отвергнут: авария, сбрасывающаяся за
  минуту, это шум; серьёзная переживёт 15 минут и попадёт в журнал с корректным началом,
  поскольку начало ставится в момент смены статуса, а не обнаружения.

## Последствия

- Журнал простоев содержит только значимые события: наладчик заполняет причины реже, но
  осмысленно, начальник разбирает журнал, а не пролистывает.
- Код детекта не изменился вовсе — `DowntimeDecision` уже принимал порог параметром,
  поменялось одно значение в настройках.
- Авария в журнале появляется с задержкой до 15 минут.
- Длительная наладка посреди задания InProgress попадает в журнал как простой: `manual`
  отсчёт не сбрасывает.
- Аналитика коротких остановов становится обязательной работой, а не опцией — без неё
  потери темпа не видны нигде. Вынесена в отдельную спеку.
```

- [ ] **Step 2: Дополнить CLAUDE.md**

В `CLAUDE.md` в раздел «Инварианты домена» после блока про `Cavities` добавить:

```markdown
### Рабочий режим — параметр задания, не телеметрия

`ShiftTask.WorkMode` (`Auto`/`SemiAuto`) — как технолог назначил работать; ручного режима
нет, он же наладка. Не путать с `ImmMode` (`auto`/`manual`/`idle`/`alarm`) — это состояние
ТПА из телеметрии. Режим влияет только на учёт выпуска: в полуавтомате условие
`mode == auto` в `ShouldCountOutput` снято, потому что ТПА между циклами ждёт оператора.
На детект простоя режим **не влияет** — порог единый и высокий (900 с), см. ADR-0010.
Эталоны `PlannedFullCycleSeconds` (обязателен) и `PlannedInjectionCycleSeconds` хранятся
для планирования и аналитики.

### Цикл литья и пауза — от коннектора, полный цикл — производный

`ImmCycle.InjectionDurationMs` (смыкание↑ → раскрытие↑) и `ImmCycle.PauseDurationMs`
(пауза перед циклом) приходят сенсорами типов `injectionDuration` / `cyclePause` в
миллисекундах. Полный цикл — их сумма, **не хранится**. У этих типов COV-фильтрация
принудительно выключена (`Threshold = 0`), иначе вариация схлопнется в ровную линию.
Оба поля nullable: машина без сигналов формы работает по-старому.
```

- [ ] **Step 3: Проверить, что документация не разошлась с кодом**

Run: `git grep -n "IdleThresholdSeconds" -- Wintime.Control.Shared Wintime.Control.API/appsettings.json`
Expected: значение 900 в обоих местах, как и написано в ADR.

Run: `git grep -n "ShouldCountOutput" -- Wintime.Control.Core Wintime.Control.Infrastructure`
Expected: сигнатура с четырьмя параметрами во всех местах.

- [ ] **Step 4: Коммит**

```bash
git add docs/adr/0010-single-high-downtime-threshold.md CLAUDE.md
git commit -m "docs: ADR-0010 о едином пороге простоя + инварианты в CLAUDE.md"
```

---

## Финальная проверка

- [ ] **Step 1: Полный прогон тестов**

Run: `dotnet test Wintime.Control.Tests.Unit`
Run: `dotnet test Wintime.Control.Tests.Integration`
Run: `cd Wintime-Control-Frontend && npx vitest run`
Run: `cd Wintime-Control-Frontend && npm run build`

Expected: всё зелёное. Записать фактические числа тестов — они пойдут в описание PR.

- [ ] **Step 2: Ручной smoke в браузере**

1. Поднять API (`dotnet run --project Wintime.Control.API`) и фронт (`npm run dev`).
2. Создать задание: убедиться, что без «Полный цикл (сек.)» форма не отправляется, а с ним задание создаётся.
3. Выбрать «Полуавтомат», сохранить, открыть карточку — режим и эталоны отображаются.
4. Отредактировать задание, поменять режим на «Автомат» — значение сохраняется.

- [ ] **Step 3: PR**

```bash
git push -u origin feature/work-mode-and-two-level-cycle
gh pr create --title "Рабочий режим ТПА и двухуровневый цикл литья" --body "..."
```

В тело PR включить: ссылку на спеку и ADR-0010, числа тестов из шага 1, и явное предупреждение, что поля `InjectionDurationMs`/`PauseDurationMs` останутся `null` до доработки коннектора USR-Modbus в отдельном репозитории.
