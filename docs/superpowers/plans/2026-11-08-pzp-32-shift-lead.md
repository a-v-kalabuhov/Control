# PZP-32 Shift Lead and Operator Staffing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить роль старшего смены, позиционный план смен с виртуальными и сохранёнными днями, интервальный журнал назначений литейщиков и отдельный мобильный контур «Моя смена → ТПА → задание/история».

**Architecture:** В Core размещаются сущности и чистые политики окон смен, ближайшей смены, доступа и фактического интервала. Infrastructure материализует полный снимок дня, синхронизирует только будущие даты в транзакции справочника смен и сериализует изменения литейщиков PostgreSQL row lock + partial unique indexes. API возвращает серверные признаки прав; Vue не воспроизводит доменные правила, а лишь использует `canEditLead`/`canEditOperators` и перезапрашивает данные по таймеру окончания и каждые 60 секунд.

**Tech Stack:** ASP.NET Core 9, EF Core 9, Npgsql/PostgreSQL 16, xUnit + Testcontainers, Vue 3, Vue Router, Pinia, Element Plus, Vitest.

## Global Constraints

- Источник роли — только `User.Role`; Identity-роли не использовать. `ShiftLead` не добавлять в `AdjusterOrHigher`.
- Новая политика называется `ShiftLeadOrManagerOrAdmin` и включает ровно `ShiftLead`, `Manager`, `Admin`.
- `ShiftPlanItem` ссылается на позицию графика, не на `Shift.Id`; один человек может быть старшим нескольких колонок даты.
- В БД существуют только дни хотя бы с одним старшим; при первом старшем сохраняются все позиции актуального графика.
- Синхронизируются только `ShiftPlanDay.ShiftDate > текущая заводская дата`; сохранённые текущие и прошлые дни неизменны.
- Назначение литейщика требует старшего на той же колонке. Активных литейщиков на ТПА — максимум два; один Operator может быть на нескольких ТПА.
- Снятие не удаляет запись. Повторное назначение создаёт новый интервал. Прошлые смены read-only для всех ролей.
- Клиент не передаёт окно смены, `Slot`, `AssignedAt`, `UnassignedAt` или клиентское время.
- Все серверные `DateTime` — UTC с `Kind=Utc`; заводская дата — `DateOnly` по часам завода.
- Неснятое назначение фактически заканчивается в `EndAtUtc`; фоновый worker не нужен. PZP-31 и распределение выработки не входят в задачу.
- Для новых read-only экранов не открывать ShiftLead общие API заданий, произвольной истории ТПА и полного справочника персонала.
- Время назначений в UI — часовой пояс браузера; окно смены на плане — сохранённый `TimeZoneId` интервала.
- Версия перед PR: `VERSION` `1.31.0 → 1.32.0`.

## Карта файлов и границы

### Core

- `Entities/ShiftPlanDay.cs` — уникальная заводская дата и полный снимок позиций.
- `Entities/ShiftPlanItem.cs` — позиция, UTC-окно, пояс и один необязательный старший.
- `Entities/ShiftImmOperatorAssignment.cs` — интервал Operator на ТПА и технический `Slot`.
- `Policies/ShiftWindowCalculator.cs` — актуальный график → позиционные UTC-окна, включая ночные смены.
- `Policies/ShiftTiming.cs` — `Future`/`Current`/`Historical` строго по серверному `now`.
- `Policies/NearestShiftSelector.cs` — персональная ближайшая смена и ближайшее вхождение позиции для Manager/Admin.
- `Policies/OperatorWorkIntervalPolicy.cs` — пересечение назначения с окном смены.
- `Policies/ShiftPlanSynchronizer.cs` — чистая позиционная синхронизация загруженного дня.
- `Policies/ShiftTaskWindow.cs` — единая проверка принадлежности задания окну смены.
- `DTOs/ShiftPlans/ShiftPlanDtos.cs`, `DTOs/ShiftStaffing/ShiftStaffingDtos.cs` — контракты API.
- `Interfaces/IShiftPlanService.cs`, `Interfaces/IShiftStaffingService.cs` — границы application services.
- `Exceptions/ShiftAccessException.cs`, `Exceptions/ShiftNotFoundException.cs`, `Exceptions/ShiftConcurrencyException.cs` — 403/404/409 без зависимости Core от HTTP.

### Infrastructure / API

- `Services/ShiftPlanService.cs` — виртуальные дни, материализация, старшие, кандидаты, синхронизация.
- `Services/ShiftStaffingService.cs` — персональный доступ, read models, назначения и снятия.
- `Services/ShiftService.cs` — справочник и синхронизация в одной транзакции.
- `Data/ControlDbContext.cs` + новая миграция `Pzp32ShiftLeadStaffing` — схема и ограничения.
- `Controllers/ShiftPlanController.cs`, `Controllers/ShiftStaffingController.cs` — тонкие HTTP-адаптеры.
- `ExceptionHandling/DomainExceptionHandler.cs`, `Program.cs` — политики, DI и единый JSON `{ message }` для 400/403/404/409.

### Frontend

- `api/shiftPlans.js`, `api/shiftStaffing.js` — только новые endpoint'ы.
- `views/planning/ShiftPlanView.vue`, `components/planning/ShiftPlanItemDialog.vue` — независимые строки и колонки плана.
- `layouts/ShiftLeadLayout.vue` — отдельный layout, не `MobileLayout` наладчика.
- `views/shift-lead/ShiftLeadHomeView.vue` — ближайшая смена и список ТПА.
- `views/shift-lead/ShiftStaffingItemView.vue` — общий read/write экран ShiftLead и read/write экран Manager/Admin из плана.
- `views/shift-lead/ShiftLeadHistoryView.vue` — только собственная история ShiftLead.
- `views/shift-lead/ShiftStaffingTaskView.vue` — read-only задание и результаты.
- `stores/shiftStaffing.js` — загрузка, последовательность запросов, mutations и перезагрузка после 409.
- `composables/useShiftStaffingRefresh.js` — 60-секундный polling и точный таймер `EndAtUtc`.
- `components/tasks/TaskResultsTab.vue` — дополнительный режим загрузки только через новый staffing endpoint; старые режимы сохранить.

---

### Task 1: Роль ShiftLead и границы существующих прав

**Files:**
- Modify: `Wintime.Control.Core/Enums/UserRole.cs`
- Modify: `Wintime.Control.Shared/Constants/Roles.cs`
- Modify: `Wintime.Control.API/Program.cs:91-96`
- Modify: `Wintime-Control-Frontend/src/constants/roles.js`
- Modify: `Wintime-Control-Frontend/src/views/dictionary/PersonnelDictionary.vue`
- Modify: `Wintime-Control-Frontend/src/layouts/DefaultLayout.vue:224-232`
- Test: `Wintime.Control.Tests.Unit/Enums/ShiftLeadRoleTests.cs`
- Test: `Wintime.Control.Tests.Integration/Auth/PersonnelManagementTests.cs`
- Test: `Wintime-Control-Frontend/src/views/dictionary/__tests__/PersonnelDictionary.spec.js`

**Interfaces:**
- Produces: `UserRole.ShiftLead = 6`, `Roles.ShiftLead`, политика `ShiftLeadOrManagerOrAdmin`, frontend getter `isShiftLead` добавляется в Task 10.
- Consumes: существующие `UserManager<User>` и JWT claim `role`; Identity-роли не нужны.

- [ ] **Step 1: Написать failing unit/API tests на роль и запрет её включения в старую иерархию**

```csharp
using FluentAssertions;
using Wintime.Control.Core.Enums;
using Wintime.Control.Shared.Constants;
using Xunit;

namespace Wintime.Control.Tests.Unit.Enums;

public class ShiftLeadRoleTests
{
    [Fact]
    public void ShiftLead_has_stable_enum_value_and_shared_name()
    {
        ((int)UserRole.ShiftLead).Should().Be(6);
        UserRole.ShiftLead.ToString().Should().Be(Roles.ShiftLead);
        Roles.All.Should().Contain(Roles.ShiftLead);
    }

    [Fact]
    public void ShiftLead_is_not_adjuster_manager_or_admin()
    {
        Roles.ShiftLead.Should().NotBe(Roles.Adjuster);
        Roles.ShiftLead.Should().NotBe(Roles.Manager);
        Roles.ShiftLead.Should().NotBe(Roles.Admin);
    }
}
```

В `PersonnelManagementTests` добавить интеграционный сценарий: Manager создаёт активного `ShiftLead` с логином и паролем, повторный `GET /api/personnel/{id}` возвращает `role == "ShiftLead"`, login через `/api/auth/login` успешен, а `GET /api/tasks`, `GET /api/personnel`, `GET /api/imm` и `GET /api/planning/shift-work-plan` для этого JWT возвращают 403. Последний URL пока будет 404 до появления route; на этом этапе проверять только три уже существующих URL.

В Vitest добавить проверку, что диалог создания содержит `el-option` со значением `ShiftLead` и подписью `Старший смены`, и что сохранение без логина/пароля передаёт эту роль.

- [ ] **Step 2: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftLeadRoleTests`
Expected: FAIL — `UserRole` не содержит `ShiftLead`.

Run: `cd Wintime-Control-Frontend; npm test -- src/views/dictionary/__tests__/PersonnelDictionary.spec.js`
Expected: FAIL — нет option `ShiftLead`.

- [ ] **Step 3: Добавить роль, политику и подписи, не меняя существующие role attributes**

```csharp
public enum UserRole
{
    Admin = 0,
    Manager = 1,
    Adjuster = 2,
    Observer = 3,
    Emulator = 4,
    Operator = 5,
    ShiftLead = 6
}
```

```csharp
public const string ShiftLead = "ShiftLead";
public static readonly string[] All =
    { Admin, Manager, Adjuster, Observer, Emulator, Operator, ShiftLead };
```

В `Program.cs` зарегистрировать отдельную политику, оставив `AdjusterOrHigher` без изменений:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole(Roles.Admin))
    .AddPolicy("ManagerOrAdmin", policy => policy.RequireRole(Roles.Manager, Roles.Admin))
    .AddPolicy("AdjusterOrHigher", policy => policy.RequireRole(Roles.Adjuster, Roles.Manager, Roles.Admin))
    .AddPolicy("ShiftLeadOrManagerOrAdmin",
        policy => policy.RequireRole(Roles.ShiftLead, Roles.Manager, Roles.Admin));
```

Добавить `ShiftLead` в оба списка ролей `PersonnelDictionary.vue`, в `getRoleLabel` как `Старший смены`, в `DefaultLayout` как `Старший смены`; в `constants/roles.js` одновременно восполнить существующий `Operator` и новый `ShiftLead`. Не добавлять `ShiftLead` в `ImmController`, `TasksController`, `PersonnelController`, дашборд или mobile routes наладчика.

- [ ] **Step 4: Запустить GREEN и регрессию ролей**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftLeadRoleTests`
Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~PersonnelManagementTests`
Run: `cd Wintime-Control-Frontend; npm test -- src/views/dictionary/__tests__/PersonnelDictionary.spec.js src/composables/__tests__/usePermissions.spec.js`
Expected: все команды PASS; Operator и Adjuster сохраняют прежние права.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Core/Enums/UserRole.cs Wintime.Control.Shared/Constants/Roles.cs Wintime.Control.API/Program.cs Wintime.Control.Tests.Unit/Enums/ShiftLeadRoleTests.cs Wintime.Control.Tests.Integration/Auth/PersonnelManagementTests.cs Wintime-Control-Frontend/src/constants/roles.js Wintime-Control-Frontend/src/views/dictionary/PersonnelDictionary.vue Wintime-Control-Frontend/src/views/dictionary/__tests__/PersonnelDictionary.spec.js Wintime-Control-Frontend/src/layouts/DefaultLayout.vue
git commit -m "feat(personnel): добавить роль старшего смены (PZP-32)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 2: Чистые доменные политики окон, ближайшей смены и фактического интервала

**Files:**
- Create: `Wintime.Control.Core/Entities/ShiftPlanDay.cs`
- Create: `Wintime.Control.Core/Entities/ShiftPlanItem.cs`
- Create: `Wintime.Control.Core/Entities/ShiftImmOperatorAssignment.cs`
- Create: `Wintime.Control.Core/Policies/ShiftWindowCalculator.cs`
- Create: `Wintime.Control.Core/Policies/ShiftTiming.cs`
- Create: `Wintime.Control.Core/Policies/NearestShiftSelector.cs`
- Create: `Wintime.Control.Core/Policies/OperatorWorkIntervalPolicy.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ShiftWindowCalculatorTests.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ShiftTimingTests.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/NearestShiftSelectorTests.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/OperatorWorkIntervalPolicyTests.cs`

**Interfaces:**
- Produces: `ShiftWindow(int Position, DateTime StartAtUtc, DateTime EndAtUtc, string TimeZoneId)`.
- Produces: `ShiftWindowCalculator.Build(DateOnly shiftDate, IReadOnlyCollection<Shift> shifts)`.
- Produces: `ShiftState Resolve(DateTime startAtUtc, DateTime endAtUtc, DateTime nowUtc)`.
- Produces: `NearestShiftSelector.ForLead(IEnumerable<ShiftPlanItem> items, string userId, DateTime nowUtc)`.
- Produces: `NearestShiftSelector.IsNearestForManager(IEnumerable<ShiftPlanItem> savedItems, ShiftPlanItem candidate, DateTime nowUtc)`.
- Produces: `OperatorWorkInterval(DateTime WorkStart, DateTime WorkEnd, bool IsEmpty)`.

- [ ] **Step 1: Написать failing tests для ночной смены, UTC и границ**

```csharp
[Fact]
public void Build_sorts_positions_and_keeps_night_shift_on_start_date()
{
    var shifts = new[]
    {
        new Shift { StartMinutes = 20 * 60, DurationMinutes = 8 * 60, TimeZoneId = "Europe/Moscow" },
        new Shift { StartMinutes = 8 * 60, DurationMinutes = 8 * 60, TimeZoneId = "Europe/Moscow" }
    };

    var windows = ShiftWindowCalculator.Build(new DateOnly(2026, 11, 8), shifts);

    windows.Select(w => w.Position).Should().Equal(1, 2);
    windows[0].StartAtUtc.Should().Be(DateTime.Parse("2026-11-08T05:00:00Z").ToUniversalTime());
    windows[1].StartAtUtc.Should().Be(DateTime.Parse("2026-11-08T17:00:00Z").ToUniversalTime());
    windows[1].EndAtUtc.Should().Be(DateTime.Parse("2026-11-09T01:00:00Z").ToUniversalTime());
    windows.Should().OnlyContain(w => w.StartAtUtc.Kind == DateTimeKind.Utc && w.EndAtUtc.Kind == DateTimeKind.Utc);
}

[Theory]
[InlineData("2026-11-08T04:59:59Z", "Future")]
[InlineData("2026-11-08T05:00:00Z", "Current")]
[InlineData("2026-11-08T12:59:59Z", "Current")]
[InlineData("2026-11-08T13:00:00Z", "Historical")]
public void Resolve_uses_half_open_interval(string now, string expected)
{
    var state = ShiftTiming.Resolve(
        DateTime.Parse("2026-11-08T05:00:00Z").ToUniversalTime(),
        DateTime.Parse("2026-11-08T13:00:00Z").ToUniversalTime(),
        DateTime.Parse(now).ToUniversalTime());
    state.ToString().Should().Be(expected);
}
```

Добавить test `Build_uses_each_shift_timezone_and_moves_invalid_local_time_forward` на `America/New_York` и дате DST: локальное невалидное время сдвигается шагами 15 минут так же, как в `CurrentShiftWindow`.

- [ ] **Step 2: Написать failing tests выбора ближайшей и manager-доступа**

```csharp
[Fact]
public void ForLead_prefers_current_then_earliest_future_then_null()
{
    var now = Utc("2026-11-08T12:00:00Z");
    var current = Item("lead", "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 1);
    var future = Item("lead", "2026-11-09T08:00:00Z", "2026-11-09T16:00:00Z", 1);
    var later = Item("lead", "2026-11-10T08:00:00Z", "2026-11-10T16:00:00Z", 1);
    var foreign = Item("other", "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 2);

    NearestShiftSelector.ForLead(new[] { later, foreign, future, current }, "lead", now).Should().BeSameAs(current);
    NearestShiftSelector.ForLead(new[] { later, future }, "lead", now).Should().BeSameAs(future);
    NearestShiftSelector.ForLead(new[] { foreign }, "lead", now).Should().BeNull();
}

[Fact]
public void ForLead_moves_to_next_column_of_same_person_after_end()
{
    var now = Utc("2026-11-08T16:00:00Z");
    var ended = Item("lead", "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 1);
    var next = Item("lead", "2026-11-08T16:00:00Z", "2026-11-09T00:00:00Z", 2);
    NearestShiftSelector.ForLead(new[] { ended, next }, "lead", now).Should().BeSameAs(next);
}
```

Отдельными tests проверить: Manager/Admin может менять только текущую позицию со старшим, а если текущей нет — earliest future той же `Position`; более поздняя future, историческая и колонка без старшего возвращают `false`.

- [ ] **Step 3: Написать failing tests фактического интервала**

```csharp
[Theory]
[InlineData("2026-11-08T06:00:00Z", null, "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", false)]
[InlineData("2026-11-08T10:00:00Z", "2026-11-08T11:00:00Z", "2026-11-08T10:00:00Z", "2026-11-08T11:00:00Z", false)]
[InlineData("2026-11-08T06:00:00Z", "2026-11-08T07:00:00Z", "2026-11-08T08:00:00Z", "2026-11-08T07:00:00Z", true)]
[InlineData("2026-11-08T17:00:00Z", null, "2026-11-08T17:00:00Z", "2026-11-08T16:00:00Z", true)]
public void Resolve_clamps_assignment_to_shift(
    string assigned, string? unassigned, string expectedStart, string expectedEnd, bool empty)
{
    var result = OperatorWorkIntervalPolicy.Resolve(
        Utc(assigned), unassigned == null ? null : Utc(unassigned),
        Utc("2026-11-08T08:00:00Z"), Utc("2026-11-08T16:00:00Z"));
    result.WorkStart.Should().Be(Utc(expectedStart));
    result.WorkEnd.Should().Be(Utc(expectedEnd));
    result.IsEmpty.Should().Be(empty);
}
```

Здесь общий helper тестового файла: `static DateTime Utc(string value) => DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);` и `Item` создаёт `ShiftPlanItem` с заданными `ShiftLeadId`, `Position`, UTC-окном. Три entity создаются в этом Task 2 по определениям из Task 3, чтобы политики и tests работали независимо от EF-миграции.

- [ ] **Step 4: Реализовать чистые функции без EF и фонового состояния**

```csharp
namespace Wintime.Control.Core.Policies;

public sealed record ShiftWindow(int Position, DateTime StartAtUtc, DateTime EndAtUtc, string TimeZoneId);

public static class ShiftWindowCalculator
{
    public static IReadOnlyList<ShiftWindow> Build(DateOnly shiftDate, IReadOnlyCollection<Entities.Shift> shifts) =>
        shifts.OrderBy(s => s.StartMinutes).Select((shift, index) =>
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(shift.TimeZoneId);
            var localStart = shiftDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified)
                .AddMinutes(shift.StartMinutes);
            var localEnd = localStart.AddMinutes(shift.DurationMinutes);
            return new ShiftWindow(index + 1, ToUtc(localStart, tz), ToUtc(localEnd, tz), shift.TimeZoneId);
        }).ToList();

    private static DateTime ToUtc(DateTime local, TimeZoneInfo tz)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (tz.IsInvalidTime(local)) local = local.AddMinutes(15);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, tz), DateTimeKind.Utc);
    }
}
```

```csharp
public enum ShiftState { Future, Current, Historical }

public static class ShiftTiming
{
    public static ShiftState Resolve(DateTime startAtUtc, DateTime endAtUtc, DateTime nowUtc)
    {
        startAtUtc = DateTime.SpecifyKind(startAtUtc, DateTimeKind.Utc);
        endAtUtc = DateTime.SpecifyKind(endAtUtc, DateTimeKind.Utc);
        nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        if (nowUtc < startAtUtc) return ShiftState.Future;
        return nowUtc < endAtUtc ? ShiftState.Current : ShiftState.Historical;
    }
}
```

`ForLead` фильтрует `ShiftLeadId == userId`, сначала `StartAtUtc <= now && now < EndAtUtc` по `(StartAtUtc, Position)`, затем `now < StartAtUtc` по `(StartAtUtc, Position)`. `IsNearestForManager` ищет среди сохранённых items той же позиции с непустым старшим текущий, иначе earliest future, и дополнительно требует `now < candidate.EndAtUtc` и `candidate.ShiftLeadId != null`.

```csharp
public sealed record OperatorWorkInterval(DateTime WorkStart, DateTime WorkEnd, bool IsEmpty);

public static class OperatorWorkIntervalPolicy
{
    public static OperatorWorkInterval Resolve(
        DateTime assignedAt, DateTime? unassignedAt, DateTime shiftStartAtUtc, DateTime shiftEndAtUtc)
    {
        var start = assignedAt > shiftStartAtUtc ? assignedAt : shiftStartAtUtc;
        var assignmentEnd = unassignedAt ?? shiftEndAtUtc;
        var end = assignmentEnd < shiftEndAtUtc ? assignmentEnd : shiftEndAtUtc;
        return new OperatorWorkInterval(start, end, end < start);
    }
}
```

- [ ] **Step 5: Запустить GREEN**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ShiftWindowCalculatorTests|FullyQualifiedName~ShiftTimingTests|FullyQualifiedName~NearestShiftSelectorTests|FullyQualifiedName~OperatorWorkIntervalPolicyTests"`
Expected: PASS; текущая/историческая граница ровно в `EndAtUtc` подтверждена.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/Entities/ShiftPlanDay.cs Wintime.Control.Core/Entities/ShiftPlanItem.cs Wintime.Control.Core/Entities/ShiftImmOperatorAssignment.cs Wintime.Control.Core/Policies/ShiftWindowCalculator.cs Wintime.Control.Core/Policies/ShiftTiming.cs Wintime.Control.Core/Policies/NearestShiftSelector.cs Wintime.Control.Core/Policies/OperatorWorkIntervalPolicy.cs Wintime.Control.Tests.Unit/Policies
git commit -m "feat(shifts): вычислять окна и ближайшую смену (PZP-32)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 3: Сущности, PostgreSQL-миграция и ограничения целостности

**Files:**
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs`
- Create: `Wintime.Control.Infrastructure/Migrations/<timestamp>_Pzp32ShiftLeadStaffing.cs`
- Create: `Wintime.Control.Infrastructure/Migrations/<timestamp>_Pzp32ShiftLeadStaffing.Designer.cs`
- Modify: `Wintime.Control.Infrastructure/Migrations/ControlDbContextModelSnapshot.cs`
- Test: `Wintime.Control.Tests.Integration/Migrations/Pzp32ShiftLeadStaffingMigrationTests.cs`

**Interfaces:**
- Produces: `DbSet<ShiftPlanDay> ShiftPlanDays`, `DbSet<ShiftPlanItem> ShiftPlanItems`, `DbSet<ShiftImmOperatorAssignment> ShiftImmOperatorAssignments`.
- Produces: навигации `Day.Items`, `Item.Day`, `Item.ShiftLead`, `Item.OperatorAssignments`, `Assignment.ShiftPlanItem`, `Assignment.Imm`, `Assignment.Operator`.

- [ ] **Step 1: Написать failing migration tests против реального PostgreSQL**

Тестовый класс по образцу `Pzp36CavitiesMigrationTests` поднимает собственный `postgres:16-alpine`, применяет миграции через `IMigrator` и выполняет следующие проверки:

1. `Up` создаёт три таблицы; `ShiftPlanDays.ShiftDate` уникальна; `(ShiftPlanDayId, Position)` уникальна; FK на справочную `Shifts` отсутствует.
2. Повтор `(ShiftPlanItemId, ImmId, Slot)` при `UnassignedAt IS NULL` отклоняется; после установки `UnassignedAt` такой же Slot можно вставить новой строкой.
3. Повтор активного `(ShiftPlanItemId, ImmId, OperatorId)` отклоняется; тот же Operator на другом `ImmId` допускается; два других Operator на слотах 1 и 2 допускаются; третий активный слот невозможен из-за `Slot IN (1,2)` и unique slot.
4. `Slot = 0`, `Slot = 3`, `UnassignedAt < AssignedAt` и `StartAtUtc >= EndAtUtc` отклоняются check constraints.
5. Два разных старших на одной позиции невозможны по модели, один `ShiftLeadId` на разных позициях одного дня допускается.
6. `Down` удаляет только новые таблицы и не меняет `Shifts`, `Users`, `Imms`.

Для проверки SQL-исключений использовать `NpgsqlException.PostgresException.SqlState == "23505"` для unique и `"23514"` для check, а не сообщения сервера.

- [ ] **Step 2: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~Pzp32ShiftLeadStaffingMigrationTests`
Expected: FAIL — миграция и таблицы отсутствуют.

- [ ] **Step 3: Добавить entities и EF configuration**

```csharp
public class ShiftPlanDay : BaseEntity
{
    public DateOnly ShiftDate { get; set; }
    public List<ShiftPlanItem> Items { get; set; } = new();
}

public class ShiftPlanItem : BaseEntity
{
    public Guid ShiftPlanDayId { get; set; }
    public int Position { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public string TimeZoneId { get; set; } = "Europe/Moscow";
    public string? ShiftLeadId { get; set; }
    public ShiftPlanDay Day { get; set; } = null!;
    public User? ShiftLead { get; set; }
    public List<ShiftImmOperatorAssignment> OperatorAssignments { get; set; } = new();
}

public class ShiftImmOperatorAssignment : BaseEntity
{
    public Guid ShiftPlanItemId { get; set; }
    public Guid ImmId { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public int Slot { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? UnassignedAt { get; set; }
    public ShiftPlanItem ShiftPlanItem { get; set; } = null!;
    public Imm Imm { get; set; } = null!;
    public User Operator { get; set; } = null!;
}
```

В `OnModelCreating` настроить `ValueGeneratedNever` для всех трёх `Id`, UTC `timestamp with time zone`, `TimeZoneId` max 100, `ShiftLeadId`/`OperatorId` FK `Restrict`, `ImmId` FK `Restrict`, `Day → Items` и `Item → Assignments` `Cascade`. Cascade нужен только для удаления ещё будущего плана; application services обязаны запрещать удаление истории.

```csharp
entity.HasIndex(a => new { a.ShiftPlanItemId, a.ImmId, a.Slot })
    .IsUnique().HasFilter("\"UnassignedAt\" IS NULL")
    .HasDatabaseName("IX_ShiftImmOperatorAssignments_ActiveSlot");
entity.HasIndex(a => new { a.ShiftPlanItemId, a.ImmId, a.OperatorId })
    .IsUnique().HasFilter("\"UnassignedAt\" IS NULL")
    .HasDatabaseName("IX_ShiftImmOperatorAssignments_ActiveOperator");
entity.ToTable("ShiftImmOperatorAssignments", t =>
{
    t.HasCheckConstraint("CK_ShiftImmOperatorAssignments_Slot", "\"Slot\" IN (1, 2)");
    t.HasCheckConstraint("CK_ShiftImmOperatorAssignments_UnassignedAt",
        "\"UnassignedAt\" IS NULL OR \"UnassignedAt\" >= \"AssignedAt\"");
});
```

Для `ShiftPlanItem` добавить check `Position >= 1` и `StartAtUtc < EndAtUtc`; для `ShiftPlanDay` unique `ShiftDate`.

- [ ] **Step 4: Сгенерировать миграцию и проверить generated SQL/model snapshot**

Run: `dotnet ef migrations add Pzp32ShiftLeadStaffing --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`
Expected: созданы migration, Designer и обновлён snapshot; оба проекта переданы обязательными флагами.

В созданной миграции вручную проверить оба `filter: "\"UnassignedAt\" IS NULL"`, имена ограничений и отсутствие FK `ShiftPlanItems → Shifts`. Не добавлять unique `(ShiftPlanDayId, ShiftLeadId)` и не добавлять ограничение Operator между разными ТПА.

- [ ] **Step 5: Запустить GREEN и проверить pending model changes**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~Pzp32ShiftLeadStaffingMigrationTests`
Run: `dotnet ef migrations has-pending-model-changes --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`
Expected: migration tests PASS; EF сообщает об отсутствии pending changes.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/Entities Wintime.Control.Infrastructure/Data/ControlDbContext.cs Wintime.Control.Infrastructure/Migrations Wintime.Control.Tests.Integration/Migrations/Pzp32ShiftLeadStaffingMigrationTests.cs
git commit -m "feat(db): добавить план смен и интервалы литейщиков (PZP-32)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 4: Виртуальные/сохранённые дни, кандидаты и назначение старшего

**Files:**
- Create: `Wintime.Control.Core/DTOs/ShiftPlans/ShiftPlanDtos.cs`
- Create: `Wintime.Control.Core/Interfaces/IShiftPlanService.cs`
- Create: `Wintime.Control.Core/Exceptions/ShiftAccessException.cs`
- Create: `Wintime.Control.Core/Exceptions/ShiftNotFoundException.cs`
- Create: `Wintime.Control.Core/Exceptions/ShiftConcurrencyException.cs`
- Create: `Wintime.Control.Infrastructure/Services/ShiftPlanService.cs`
- Create: `Wintime.Control.API/Controllers/ShiftPlanController.cs`
- Modify: `Wintime.Control.API/ExceptionHandling/DomainExceptionHandler.cs`
- Modify: `Wintime.Control.API/Program.cs`
- Test: `Wintime.Control.Tests.Integration/Planning/ShiftPlanApiTests.cs`

**Interfaces:**
- Produces: `GetAsync(DateOnly from, DateOnly to, CancellationToken ct)`.
- Produces: `SetLeadAsync(DateOnly date, int position, string? shiftLeadId, CancellationToken ct)` → полный `ShiftPlanDayDto`.
- Produces: `GetLeadCandidatesAsync(CancellationToken ct)` → `IReadOnlyList<StaffPersonDto>`.
- Consumes: `ShiftWindowCalculator`, `ShiftTiming`, `IFactoryClock.UtcNow/GetScheduleAsync`.

DTO фиксируются так:

```csharp
public sealed class ShiftPlanDayDto
{
    public DateOnly ShiftDate { get; set; }
    public bool IsSaved { get; set; }
    public bool IsFactoryToday { get; set; }
    public List<ShiftPlanItemDto> Items { get; set; } = new();
}

public sealed class ShiftPlanItemDto
{
    public Guid? Id { get; set; }
    public int Position { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public string? ShiftLeadId { get; set; }
    public string? ShiftLeadName { get; set; }
    public ShiftState State { get; set; }
    public bool CanEditLead { get; set; }
    public bool CanEditOperators { get; set; }
    public int OperatorAssignmentCount { get; set; }
}

public sealed record SetShiftLeadRequest(string? ShiftLeadId);
public sealed record StaffPersonDto(string Id, string FullName, string? EmployeeId);
```

- [ ] **Step 1: Написать failing integration tests виртуального чтения и материализации**

В `ShiftPlanApiTests` создать Manager/Admin clients, активных ShiftLead, Manager, Admin, Operator и архивного ShiftLead. Проверить:

- `GET /api/shift-plan?from=2026-11-08&to=2026-11-10` возвращает каждую дату ровно один раз, `isSaved=false`, `id=null`, позиции отсортированы, окна вычислены сервером; диапазон `from > to` даёт 400.
- `lead-candidates` содержит активных ShiftLead и Manager, включая текущего Manager, но не Admin, Operator, Adjuster, Observer, архивного ShiftLead; DTO содержит только `id`, `fullName`, `employeeId`.
- Первый `PUT /api/shift-plan/days/{futureDate}/items/2/lead` с телом `{ "shiftLeadId": "lead-id" }` создаёт один `ShiftPlanDay` и **все** позиции графика, а не только вторую.
- Повторное чтение использует сохранённые окна (`isSaved=true`, Guid у всех items). Клиент не передаёт границы окон.
- Переназначение до `StartAtUtc` сохраняет `ShiftPlanItem.Id` и assignments; Manager может назначить себя; Admin как кандидат получает 400; архивный/прочая роль получает 400.
- `PUT` в момент `StartAtUtc` и после него даёт 400. Прошлая виртуальная дата тоже не редактируется.
- Очистка старшего с любыми интервалами литейщиков даёт 400. Очистка без интервалов удаляет только старшего; после очистки последнего старшего день и его пустые items удаляются, `GET` снова возвращает виртуальный день.
- Параллельная материализация одного виртуального дня даёт один успешный результат и один 409, в БД остаётся один день и один полный набор позиций.
- ShiftLead/Adjuster/Observer/Operator не имеют доступа ко всем трём endpoint'ам (403).

Для временных тестов сидировать сохранённые items относительно `DateTime.UtcNow`, а запрос на виртуальную future-дату делать через `PUT /api/shifts` с известным графиком; после каждого такого теста восстанавливать исходный график, чтобы не влиять на соседние tests.

- [ ] **Step 2: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftPlanApiTests`
Expected: FAIL — `/api/shift-plan` отсутствует.

- [ ] **Step 3: Реализовать маппинг 400/403/404/409 и сервис**

`DomainExceptionHandler` должен сопоставлять `DomainException → 400`, `ShiftAccessException → 403`, `ShiftNotFoundException → 404`, `ShiftConcurrencyException → 409`; во всех случаях JSON остаётся `{ "message": exception.Message }`.

`GetAsync` загружает сохранённые дни диапазона с `Items → ShiftLead` и `Items → OperatorAssignments`. Для отсутствующих дат вызывает `ShiftWindowCalculator.Build`; сохранённые текущие/прошлые не пересчитывает. `IsFactoryToday` вычисляется через `IFactoryClock.GetScheduleAsync().ToLocal(now)`. `CanEditLead = now < StartAtUtc`; `CanEditOperators` вычисляется `NearestShiftSelector.IsNearestForManager` среди всех сохранённых items запрошенного диапазона плюс ближайших кандидатов вне диапазона, поэтому более поздний item не становится ошибочно editable только потому, что ранняя смена не входит в фильтр.

`SetLeadAsync`:

1. Начать `ExecutionStrategy` + transaction. Существующий день блокировать запросом `SELECT * FROM "ShiftPlanDays" WHERE "Id" = @id FOR UPDATE`; для виртуального дня рассчитывать окна из актуального `Shifts` на сервере.
2. Проверить `position` в диапазоне актуальных/сохранённых позиций, иначе `ShiftNotFoundException`.
3. Проверить `now < StartAtUtc`; при назначении проверить активного User с ролью ровно ShiftLead или Manager.
4. При первом назначении добавить Day и все Items, затем записать старшего только выбранной позиции.
5. При `shiftLeadId == null` запретить очистку, если `item.OperatorAssignments.Count > 0`; после очистки последнего старшего удалить день.
6. Сохранить один раз и вернуть полный DTO дня. `PostgresException 23505` на гонке материализации преобразовать в `ShiftConcurrencyException("План смены уже изменён другим пользователем")`.

В `Program.cs`: `AddScoped<IShiftPlanService, ShiftPlanService>()`.

- [ ] **Step 4: Добавить тонкий controller**

```csharp
[ApiController]
[Route("api/shift-plan")]
[Authorize(Policy = "ManagerOrAdmin")]
public class ShiftPlanController : ControllerBase
{
    private readonly IShiftPlanService _service;
    public ShiftPlanController(IShiftPlanService service) => _service = service;

    [HttpGet]
    public Task<IReadOnlyList<ShiftPlanDayDto>> Get(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        _service.GetAsync(from, to, ct);

    [HttpGet("lead-candidates")]
    public Task<IReadOnlyList<StaffPersonDto>> Candidates(CancellationToken ct) =>
        _service.GetLeadCandidatesAsync(ct);

    [HttpPut("days/{date}/items/{position:int}/lead")]
    public Task<ShiftPlanDayDto> SetLead(
        DateOnly date, int position, [FromBody] SetShiftLeadRequest request, CancellationToken ct) =>
        _service.SetLeadAsync(date, position, request.ShiftLeadId, ct);
}
```

- [ ] **Step 5: Запустить GREEN**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftPlanApiTests|FullyQualifiedName~Pzp32ShiftLeadStaffingMigrationTests"`
Expected: PASS, включая полный снимок, удаление последнего дня, роли и параллельную материализацию.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/DTOs/ShiftPlans Wintime.Control.Core/Interfaces/IShiftPlanService.cs Wintime.Control.Core/Exceptions Wintime.Control.Infrastructure/Services/ShiftPlanService.cs Wintime.Control.API/Controllers/ShiftPlanController.cs Wintime.Control.API/ExceptionHandling/DomainExceptionHandler.cs Wintime.Control.API/Program.cs Wintime.Control.Tests.Integration/Planning/ShiftPlanApiTests.cs
git commit -m "feat(planning): сохранять старших на позиции смен (PZP-32)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 5: Позиционная синхронизация графика только для будущих дат

**Files:**
- Create: `Wintime.Control.Core/Policies/ShiftPlanSynchronizer.cs`
- Modify: `Wintime.Control.Core/Interfaces/IShiftPlanService.cs`
- Modify: `Wintime.Control.Core/Interfaces/IShiftService.cs`
- Modify: `Wintime.Control.Infrastructure/Services/ShiftPlanService.cs`
- Modify: `Wintime.Control.Infrastructure/Services/ShiftService.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ShiftPlanSynchronizerTests.cs`
- Test: `Wintime.Control.Tests.Integration/Planning/ShiftPlanScheduleSynchronizationTests.cs`

**Interfaces:**
- Produces: `ShiftPlanSynchronizer.Apply(ShiftPlanDay day, IReadOnlyList<ShiftWindow> windows)` — меняет loaded graph, не обращается к БД.
- Produces: `IShiftPlanService.SynchronizeFutureDaysAsync(IReadOnlyList<Shift> newSchedule, DateTime nowUtc, CancellationToken ct)` — загружает/мутирует тот же `ControlDbContext`, не вызывает `SaveChangesAsync`.
- Consumes: существующий `ShiftService.SaveShiftsAsync`; один `ControlDbContext` на транзакцию.

- [ ] **Step 1: Написать failing unit tests позиционного diff**

```csharp
[Fact]
public void Apply_preserves_ids_leads_and_assignments_on_remaining_positions()
{
    var day = DayWithTwoItems();
    var firstId = day.Items[0].Id;
    var firstLead = day.Items[0].ShiftLeadId;
    var firstAssignment = day.Items[0].OperatorAssignments.Single();
    var windows = new[]
    {
        new ShiftWindow(1, Utc("2026-11-10T06:00:00Z"), Utc("2026-11-10T14:00:00Z"), "Europe/Moscow"),
        new ShiftWindow(2, Utc("2026-11-10T14:00:00Z"), Utc("2026-11-10T22:00:00Z"), "Europe/Moscow"),
        new ShiftWindow(3, Utc("2026-11-10T22:00:00Z"), Utc("2026-11-11T06:00:00Z"), "Europe/Moscow")
    };

    var removed = ShiftPlanSynchronizer.Apply(day, windows);

    day.Items.Should().HaveCount(3);
    day.Items[0].Id.Should().Be(firstId);
    day.Items[0].ShiftLeadId.Should().Be(firstLead);
    day.Items[0].OperatorAssignments.Should().ContainSame(firstAssignment);
    day.Items[0].StartAtUtc.Should().Be(windows[0].StartAtUtc);
    day.Items[2].ShiftLeadId.Should().BeNull();
    removed.Should().BeEmpty();
}
```

Дополнить tests: сокращение с 3 до 1 возвращает удалённые items позиций 2 и 3 вместе с их будущими assignments; одинаковое число позиций обновляет только окна; после удаления всех старших `day.Items.Any(i => i.ShiftLeadId != null) == false` — service обязан удалить день; текущая/прошлая дата в pure policy не участвует, потому что фильтр выполняет Infrastructure query.

- [ ] **Step 2: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftPlanSynchronizerTests`
Expected: FAIL — `ShiftPlanSynchronizer` отсутствует.

- [ ] **Step 3: Реализовать pure synchronizer и транзакционный вызов**

`Apply` строит словарь оставшихся items по `Position`, возвращает `List<ShiftPlanItem> removed`, обновляет `StartAtUtc/EndAtUtc/TimeZoneId` оставшихся, добавляет новые с `ShiftLeadId=null`, сортирует `day.Items` по позиции. Не удаляет assignments оставшихся items.

`IShiftService.SaveShiftsAsync` получает optional `CancellationToken ct = default`, поэтому существующий controller продолжает вызывать метод без дополнительного аргумента. Сама реализация меняется на единую транзакцию:

```csharp
public async Task<IReadOnlyList<ShiftDto>> SaveShiftsAsync(
    IReadOnlyList<ShiftScheduleItemDto> shifts, CancellationToken ct = default)
{
    var nowUtc = _factoryClock.UtcNow;
    await using var tx = await _context.Database.BeginTransactionAsync(ct);
    var existing = await _context.Shifts.ToListAsync(ct);
    _context.Shifts.RemoveRange(existing);
    var newShifts = shifts.OrderBy(s => s.StartMinutes).Select(s => new Shift
    {
        StartMinutes = s.StartMinutes,
        DurationMinutes = s.DurationMinutes,
        BreakStartMinutes = s.BreakStartMinutes,
        BreakDurationMinutes = s.BreakDurationMinutes,
        TimeZoneId = s.TimeZoneId
    }).ToList();
    _context.Shifts.AddRange(newShifts);
    await _shiftPlan.SynchronizeFutureDaysAsync(newShifts, nowUtc, ct);
    await _context.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return newShifts.Select((s, i) => ToDto(s, i + 1)).ToList();
}
```

`SynchronizeFutureDaysAsync` вычисляет factory today один раз, загружает только `ShiftDate > today` с items и assignments, применяет `Apply` к каждому дню, передаёт removed в `_context.ShiftPlanItems.RemoveRange`, удаляет дни без старших. Он не вызывает `SaveChangesAsync`: откат любого исключения отменяет и справочник, и все дни.

- [ ] **Step 4: Написать и прогнать integration tests транзакции и защиты дат**

В `ShiftPlanScheduleSynchronizationTests` проверить на реальном API:

- future day: сокращение удаляет лишние items и их предварительные assignments; удлинение добавляет пустые; сохранённые `Id`, старшие и assignments оставшихся позиций не меняются; все окна получают новый график.
- сохранённые factory today и вчерашний день остаются побайтово по доменным полям неизменными, даже если сегодняшняя смена ещё не началась.
- виртуальные current/past при следующем GET отражают новый актуальный график.
- если после сокращения не осталось старших, сохранённый день исчезает.
- исключение при синхронизации (test-only отказ через `DbUpdateException` или явно закрытое соединение в инфраструктурном test) откатывает и новый справочник, и изменённые дни. Не ослаблять production validation ради теста.
- `PUT /api/shifts` по-прежнему доступен только Manager/Admin и возвращает прежние ошибки валидации.

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftPlanSynchronizerTests`
Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftPlanScheduleSynchronizationTests|FullyQualifiedName~ShiftServiceValidateTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Core/Policies/ShiftPlanSynchronizer.cs Wintime.Control.Core/Interfaces/IShiftPlanService.cs Wintime.Control.Core/Interfaces/IShiftService.cs Wintime.Control.Infrastructure/Services/ShiftPlanService.cs Wintime.Control.Infrastructure/Services/ShiftService.cs Wintime.Control.Tests.Unit/Policies/ShiftPlanSynchronizerTests.cs Wintime.Control.Tests.Integration/Planning/ShiftPlanScheduleSynchronizationTests.cs
git commit -m "feat(shifts): синхронизировать будущие планы по позициям (PZP-32)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 6: Read API ближайшей/исторической смены, ТПА и заданий

**Files:**
- Create: `Wintime.Control.Core/Policies/ShiftTaskWindow.cs`
- Create: `Wintime.Control.Core/DTOs/ShiftStaffing/ShiftStaffingDtos.cs`
- Create: `Wintime.Control.Core/Interfaces/IShiftStaffingService.cs`
- Create: `Wintime.Control.Infrastructure/Services/ShiftStaffingService.cs`
- Create: `Wintime.Control.API/Controllers/ShiftStaffingController.cs`
- Modify: `Wintime.Control.API/Program.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ShiftTaskWindowTests.cs`
- Test: `Wintime.Control.Tests.Integration/ShiftStaffing/ShiftStaffingReadApiTests.cs`

**Interfaces:**
- Produces: `GetNearestAsync(string userId, CancellationToken ct)` → `NearestShiftDto`.
- Produces: `GetHistoryAsync(string userId, CancellationToken ct)` → summaries только собственных исторических items.
- Produces: `GetItemAsync(Guid itemId, StaffingCaller caller, CancellationToken ct)` → `ShiftStaffingItemDto`.
- Produces: `GetOperatorsAsync(CancellationToken ct)` → active Operator сокращённый `StaffPersonDto`.
- Produces: `StaffingCaller(string UserId, bool IsShiftLead, bool IsManagerOrAdmin)`.

DTO:

```csharp
public sealed record NearestShiftDto(ShiftStaffingSummaryDto? Item);
public sealed record StaffingCaller(string UserId, bool IsShiftLead, bool IsManagerOrAdmin);

public class ShiftStaffingSummaryDto
{
    public Guid ItemId { get; set; }
    public DateOnly ShiftDate { get; set; }
    public int Position { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public ShiftState State { get; set; }
    public bool CanEditOperators { get; set; }
}

public class ShiftStaffingItemDto
{
    public ShiftStaffingSummaryDto Shift { get; set; } = new();
    public string ShiftLeadId { get; set; } = string.Empty;
    public string ShiftLeadName { get; set; } = string.Empty;
    public List<ShiftStaffingImmDto> Imms { get; set; } = new();
}

public class ShiftStaffingImmDto
{
    public Guid ImmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? InventoryNumber { get; set; }
    public bool IsActive { get; set; }
    public string EffectiveStatus { get; set; } = EffectiveStatus.Offline;
    public List<TaskDto> Tasks { get; set; } = new();
    public List<ShiftOperatorAssignmentDto> Assignments { get; set; } = new();
    public int ActiveOperatorCount { get; set; }
}

public class ShiftOperatorAssignmentDto
{
    public Guid Id { get; set; }
    public Guid ImmId { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string? OperatorEmployeeId { get; set; }
    public int Slot { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? UnassignedAt { get; set; }
    public DateTime WorkStart { get; set; }
    public DateTime WorkEnd { get; set; }
    public bool IsWorkIntervalEmpty { get; set; }
}
```

- [ ] **Step 1: Написать failing tests принадлежности заданий**

`ShiftTaskWindow` покрыть xUnit: Planned/Issued включаются только при `PlannedStartAt >= StartAtUtc && PlannedStartAt < EndAtUtc`; Setup/InProgress/Completed/Closed — только через `TaskActivityWindow.OverlapsPeriod`; Completed внутри смены включается, Completed до неё исключается, задание через несколько смен включается в каждую пересекаемую. Ровно на `EndAtUtc` planned не входит; activity, завершённая ровно в `StartAtUtc`, не входит.

- [ ] **Step 2: Написать failing read integration tests**

Проверить отдельными сценариями:

- nearest ShiftLead: current имеет приоритет, затем earliest future; чужие и historical в nearest не попадают; при отсутствии current/future возвращается 200 `{ "item": null }`; несколько колонок одного человека переключаются ровно в `EndAtUtc`.
- history: только собственные `EndAtUtc <= now`, сортировка по `StartAtUtc` descending; никаких более поздних future.
- `GET items/{itemId}` ShiftLead разрешает nearest и собственную историю, но чужую и более позднюю future отклоняет 403; несуществующий Guid — 404. Manager/Admin читают любой сохранённый item; `canEditOperators` вычисляет сервер по ближайшему вхождению позиции.
- nearest/current возвращает **все активные** ТПА, включая без задания; archived без связей исключён. history дополнительно включает archived ТПА только если у него есть задание или assignment в этой смене.
- planned/issued и activity tasks отбираются правилами Step 1 и не дублируются при совпадении обоих запросов. TaskDto заполнен через существующий `ToDto(schedule)`.
- operators возвращает только активных Operator, включая Operator без `UserName`, и не содержит Login/Role/Qualification.
- `GET /api/shift-staffing/nearest` и `/history` разрешены только ShiftLead; остальные read endpoints используют `ShiftLeadOrManagerOrAdmin`. Adjuster/Observer/Operator получают 403. ShiftLead по-прежнему получает 403 на `/api/tasks`, `/api/tasks/{id}`, `/api/personnel`, `/api/imm/{id}/effective-status-history`.

- [ ] **Step 3: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftTaskWindowTests`
Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftStaffingReadApiTests`
Expected: FAIL — политика и endpoints отсутствуют.

- [ ] **Step 4: Реализовать доступ и read model**

`GetNearestAsync` использует `NearestShiftSelector.ForLead` по items текущего user с `EndAtUtc > now`; `GetHistoryAsync` — `ShiftLeadId == userId && EndAtUtc <= now`. `GetItemAsync` сначала находит item, затем проверяет: Manager/Admin — любой; ShiftLead — `ShiftLeadId == userId` и (`item.Id == nearest.Id` или `EndAtUtc <= now`). Чужая/неближайшая — 403, отсутствующая — 404.

`CanEditOperators`:
- ShiftLead: item является nearest, принадлежит ему, имеет старшего и `now < EndAtUtc`.
- Manager/Admin: `NearestShiftSelector.IsNearestForManager` и `now < EndAtUtc`.
- Для historical всегда `false`.

`ShiftTaskWindow` содержит EF expression для activity и чистую `Belongs(ShiftTask task, DateTime plannedStartAtUtc, DateTime shiftStartAtUtc, DateTime shiftEndAtUtc, DateTime nowUtc)` для повторной серверной проверки. Planned candidates ограничить широким `PlannedDate ±1 день` от UTC-окна, затем точно отфильтровать после `ToDto`; activity query использовать `TaskActivityWindow.OverlapsPeriod`. Объединить по `Id`.

Для current/future effective status вычислить live по `IImmStatusCache`, активным Setup/InProgress, открытым Downtime и `DowntimeSettings.IdleThresholdSeconds`, используя `ImmEffectiveStatus.Resolve`. Для historical взять последний сегмент `EffectiveStatusTimeline.Build` в `[StartAtUtc, EndAtUtc]` через `IEffectiveStatusHistoryService`; при отсутствии сегмента вернуть `Offline`.

Assignments маппить всеми интервалами по `AssignedAt`, включая архивных Operator, и рассчитывать `WorkStart/WorkEnd/IsWorkIntervalEmpty` через `OperatorWorkIntervalPolicy`. `ActiveOperatorCount = Count(UnassignedAt == null)`.

Controller извлекает caller только из `ClaimTypes.NameIdentifier` и `User.IsInRole`; клиент не передаёт userId. Зарегистрировать `IShiftStaffingService` scoped.

- [ ] **Step 5: Запустить GREEN**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ShiftTaskWindowTests|FullyQualifiedName~NearestShiftSelectorTests"`
Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftStaffingReadApiTests|FullyQualifiedName~OperatorDashboardTests"`
Expected: PASS; ShiftLead не получил ни одного старого права, Operator не потерял свои.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/Policies/ShiftTaskWindow.cs Wintime.Control.Core/DTOs/ShiftStaffing Wintime.Control.Core/Interfaces/IShiftStaffingService.cs Wintime.Control.Infrastructure/Services/ShiftStaffingService.cs Wintime.Control.API/Controllers/ShiftStaffingController.cs Wintime.Control.API/Program.cs Wintime.Control.Tests.Unit/Policies/ShiftTaskWindowTests.cs Wintime.Control.Tests.Integration/ShiftStaffing/ShiftStaffingReadApiTests.cs
git commit -m "feat(staffing): отдавать ближайшую смену и её ТПА (PZP-32)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 7: Read-only задание и результаты внутри разрешённой смены

**Files:**
- Modify: `Wintime.Control.Core/DTOs/ShiftStaffing/ShiftStaffingDtos.cs`
- Modify: `Wintime.Control.Core/Interfaces/IShiftStaffingService.cs`
- Modify: `Wintime.Control.Infrastructure/Services/ShiftStaffingService.cs`
- Modify: `Wintime.Control.API/Controllers/ShiftStaffingController.cs`
- Test: `Wintime.Control.Tests.Integration/ShiftStaffing/ShiftStaffingTaskApiTests.cs`

**Interfaces:**
- Produces: `GetTaskAsync(Guid itemId, Guid taskId, StaffingCaller caller, CancellationToken ct)` → `ShiftStaffingTaskDto`.
- Consumes: `ShiftTaskWindow.Belongs`, тот же access check, что `GetItemAsync`, существующие `TaskMappingExtensions.ToDto` и `ITaskResultsService.GetAsync`.

```csharp
public sealed class ShiftStaffingTaskDto
{
    public TaskDto Task { get; set; } = new();
    public TaskResultsDto Results { get; set; } = new();
}
```

- [ ] **Step 1: Написать failing integration tests membership**

Проверить:

- ShiftLead получает TaskDto + results для задания из nearest и собственной исторической смены; кнопки/операций изменения endpoint не предоставляет.
- Planned/Issued определяются по `PlannedStartAt`; остальные — по activity; Completed внутри окна доступен.
- Задание другого ТПА той же смены допустимо, если оно входит в `items/{itemId}`; задание вне окна или из другого item — 404, даже если само задание существует.
- Чужий/неближайший item ShiftLead — 403 до раскрытия задания; отсутствующий item/task — 404.
- Manager/Admin могут читать задание любого сохранённого item, включая historical, но не получают право вызывать mutations после окончания.
- Внутри используется только новый `/api/shift-staffing/items/{itemId}/tasks/{taskId}`; старые `/api/tasks/{id}` и `/api/tasks/{id}/results` для ShiftLead остаются 403.

- [ ] **Step 2: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftStaffingTaskApiTests`
Expected: FAIL — endpoint отсутствует.

- [ ] **Step 3: Реализовать повторное использование одной membership-функции**

В `ShiftStaffingService` выделить приватный `LoadAccessibleItemAsync`, используемый и `GetItemAsync`, и `GetTaskAsync`. Загрузить task со всеми навигациями, необходимыми `ToDto`; вычислить `plannedStartAt` через `FactorySchedule`; если `ShiftTaskWindow.Belongs(task, plannedStartAt, item.StartAtUtc, item.EndAtUtc, now)` false или `task.ImmId` отсутствует среди разрешённых ТПА item — вернуть `ShiftNotFoundException("Задание не найдено в этой смене")`.

После успешной проверки получить `ITaskResultsService.GetAsync(taskId)` и вернуть оба DTO. Не дублировать расчёт результатов и не вызывать старый controller внутренне.

Controller action:

```csharp
[HttpGet("items/{itemId:guid}/tasks/{taskId:guid}")]
[Authorize(Policy = "ShiftLeadOrManagerOrAdmin")]
public Task<ShiftStaffingTaskDto> GetTask(Guid itemId, Guid taskId, CancellationToken ct) =>
    _service.GetTaskAsync(itemId, taskId, Caller(), ct);
```

- [ ] **Step 4: Запустить GREEN**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftStaffingTaskApiTests|FullyQualifiedName~TaskResultsApiTests"`
Expected: PASS; существующие Task results не изменились.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Core/DTOs/ShiftStaffing/ShiftStaffingDtos.cs Wintime.Control.Core/Interfaces/IShiftStaffingService.cs Wintime.Control.Infrastructure/Services/ShiftStaffingService.cs Wintime.Control.API/Controllers/ShiftStaffingController.cs Wintime.Control.Tests.Integration/ShiftStaffing/ShiftStaffingTaskApiTests.cs
git commit -m "feat(staffing): открывать задание только внутри смены (PZP-32)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 8: Назначение/снятие литейщиков, Slot и параллельные запросы

**Files:**
- Modify: `Wintime.Control.Core/DTOs/ShiftStaffing/ShiftStaffingDtos.cs`
- Modify: `Wintime.Control.Core/Interfaces/IShiftStaffingService.cs`
- Modify: `Wintime.Control.Infrastructure/Services/ShiftStaffingService.cs`
- Modify: `Wintime.Control.API/Controllers/ShiftStaffingController.cs`
- Test: `Wintime.Control.Tests.Integration/ShiftStaffing/ShiftStaffingMutationApiTests.cs`

**Interfaces:**
- Produces: `AssignAsync(Guid itemId, AssignOperatorRequest request, StaffingCaller caller, CancellationToken ct)` → `ShiftOperatorAssignmentDto`.
- Produces: `UnassignAsync(Guid assignmentId, StaffingCaller caller, CancellationToken ct)` → `ShiftOperatorAssignmentDto`.
- Request содержит ровно `Guid ImmId`, `string OperatorId`; Slot и время вычисляет сервер.

```csharp
public sealed record AssignOperatorRequest(Guid ImmId, string OperatorId);
```

- [ ] **Step 1: Написать failing integration tests доменных правил**

Покрыть:

- назначение на активный ТПА без задания успешно; первый получает Slot 1, второй Slot 2; `AssignedAt` близко к серверному `DateTime.UtcNow`, `UnassignedAt=null`.
- третий активный на том же ТПА — 400 с русским сообщением; повтор активного Operator на том же ТПА — 400; тот же Operator на другом ТПА той же смены — успех.
- архивный/несуществующий Operator, не-Operator, архивный/несуществующий ТПА — 400 для неподходящей роли/архива и 404 для отсутствующей сущности; Operator без логина допускается.
- item без старшего — 400 для ShiftLead/Manager/Admin; historical — 400; чужой/неближайший ShiftLead и более поздний Manager item — 403.
- снятие до начала и во время смены ставит серверный `UnassignedAt`; повторное снятие — 400; после `EndAtUtc` — 400; запись остаётся в БД и read API.
- назначение и снятие до начала создают сохранённый пустой work interval; неснятый после окончания имеет `UnassignedAt=null`, но `WorkEnd=EndAtUtc`.
- после снятия со Slot 1 новый Operator получает новую строку со Slot 1; старая строка не изменяется, кроме её первоначального `UnassignedAt`.
- все ошибки содержат русское `message`; конфликт параллельного изменения — 409.

- [ ] **Step 2: Написать отдельный failing concurrency test**

Создать item с одним старшим и одним активным Operator. Поднять два независимых `HttpClient` с одной ролью и синхронизировать старт двух POST разных Operator на один ТПА через `TaskCompletionSource`. Оба запроса могут пройти начальную валидацию, но row lock обязан сериализовать выбор Slot: итог — два активных Slot 1/2, ни один не потерян. Затем два параллельных POST третьего/четвёртого: оба завершаются 400 или один 400 и один 409, в БД остаётся ровно два активных.

Отдельно два параллельных назначения одного Operator на один ТПА: ровно одна активная запись; один запрос 2xx, другой 400 либо 409. Два параллельных unassign одного assignment: ровно одно успешное снятие, второе 400 либо 409, одна метка `UnassignedAt`.

- [ ] **Step 3: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftStaffingMutationApiTests`
Expected: FAIL — POST endpoints отсутствуют.

- [ ] **Step 4: Реализовать row lock и mutations в одной транзакции**

Обе операции выполнять через Npgsql execution strategy и transaction. Для assign сначала прочитать item для 404, затем заблокировать родителя:

```csharp
var item = await _context.ShiftPlanItems
    .FromSqlInterpolated($"SELECT * FROM \"ShiftPlanItems\" WHERE \"Id\" = {itemId} FOR UPDATE")
    .Include(i => i.Day)
    .Include(i => i.ShiftLead)
    .Include(i => i.OperatorAssignments)
    .SingleAsync(ct);
```

После lock повторно проверить caller/nearest, старшего и `now < EndAtUtc`, активность ТПА и `User.Role == Operator && IsActive`. Активные assignments читать в том же DbContext после ожидания lock; не полагаться на snapshot, полученный до блокировки. Проверить duplicate, затем:

```csharp
var usedSlots = item.OperatorAssignments
    .Where(a => a.ImmId == request.ImmId && a.UnassignedAt == null)
    .Select(a => a.Slot).ToHashSet();
var slot = new[] { 1, 2 }.FirstOrDefault(s => !usedSlots.Contains(s));
if (slot == 0)
    throw new DomainException("На один ТПА можно назначить не более двух литейщиков");
var assignment = new ShiftImmOperatorAssignment
{
    ShiftPlanItemId = item.Id,
    ImmId = request.ImmId,
    OperatorId = request.OperatorId,
    Slot = slot,
    AssignedAt = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc)
};
```

Unassign загружает assignment, затем блокирует его родительский item тем же `FOR UPDATE`, повторно загружает активную строку и проверяет `UnassignedAt == null`, права и время. Установить только `UnassignedAt = _clock.UtcNow`. Не закрывать автоматически другие записи и не менять задание/ТПА.

Перехватывать `DbUpdateException` с `PostgresException.SqlState == "23505"` и serialization/deadlock `40001`/`40P01` в `ShiftConcurrencyException("Состав литейщиков изменился. Обновите данные смены")`. Обычный domain duplicate остаётся 400; гонка не должна превращаться в 500.

Controller:

```csharp
[HttpPost("items/{itemId:guid}/operators")]
[Authorize(Policy = "ShiftLeadOrManagerOrAdmin")]
public Task<ShiftOperatorAssignmentDto> Assign(
    Guid itemId, [FromBody] AssignOperatorRequest request, CancellationToken ct) =>
    _service.AssignAsync(itemId, request, Caller(), ct);

[HttpPost("assignments/{assignmentId:guid}/unassign")]
[Authorize(Policy = "ShiftLeadOrManagerOrAdmin")]
public Task<ShiftOperatorAssignmentDto> Unassign(Guid assignmentId, CancellationToken ct) =>
    _service.UnassignAsync(assignmentId, Caller(), ct);
```

- [ ] **Step 5: Запустить GREEN и повторный concurrency прогон**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftStaffingMutationApiTests`
Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftStaffingMutationApiTests -- xUnit.ParallelizeTestCollections=false`
Expected: оба прогона PASS; нарушений partial unique indexes нет, история не удаляется.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/DTOs/ShiftStaffing/ShiftStaffingDtos.cs Wintime.Control.Core/Interfaces/IShiftStaffingService.cs Wintime.Control.Infrastructure/Services/ShiftStaffingService.cs Wintime.Control.API/Controllers/ShiftStaffingController.cs Wintime.Control.Tests.Integration/ShiftStaffing/ShiftStaffingMutationApiTests.cs
git commit -m "feat(staffing): назначать литейщиков интервалами (PZP-32)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 9: Desktop «Планирование → План смен»

**Files:**
- Create: `Wintime-Control-Frontend/src/api/shiftPlans.js`
- Create: `Wintime-Control-Frontend/src/views/planning/ShiftPlanView.vue`
- Create: `Wintime-Control-Frontend/src/components/planning/ShiftPlanItemDialog.vue`
- Modify: `Wintime-Control-Frontend/src/router/index.js`
- Modify: `Wintime-Control-Frontend/src/layouts/DefaultLayout.vue`
- Test: `Wintime-Control-Frontend/src/views/planning/__tests__/ShiftPlanView.spec.js`
- Test: `Wintime-Control-Frontend/src/components/planning/__tests__/ShiftPlanItemDialog.spec.js`
- Test: `Wintime-Control-Frontend/src/router/__tests__/shiftPlanRoute.spec.js`

**Interfaces:**
- Consumes: все endpoint'ы `/api/shift-plan` и DTO из Task 4; `/shift-lead/items/:itemId` появляется в Task 10.
- Produces: named route `ShiftPlan` с `roles: ['Admin', 'Manager']`, `title: 'План смен'`.

API wrapper:

```js
import apiClient from './client'

export const shiftPlansApi = {
  getRange: (from, to) => apiClient.get('/shift-plan', { params: { from, to } }),
  getLeadCandidates: () => apiClient.get('/shift-plan/lead-candidates'),
  setLead: (date, position, shiftLeadId) =>
    apiClient.put(`/shift-plan/days/${date}/items/${position}/lead`, { shiftLeadId })
}
```

- [ ] **Step 1: Написать failing Vitest таблицы и карточки**

`ShiftPlanView.spec.js` mock `shiftPlansApi` и устанавливает системное время. Fixtures содержат: сохранённый past с одной колонкой, current date с двумя, future с тремя. Проверить:

- три строки имеют независимое число `.shift-plan-item` — 1/2/3; нет фиксированных пустых ячеек «Нет в графике».
- заголовок ровно «Дата | Смены и старшие»; каждая ячейка показывает `Смена N`, интервал в её `TimeZoneId` и фамилию/`Не назначен`.
- past имеет класс `shift-past`, текущая дата `shift-current-date`, future `shift-future`; диапазон дат повторно вызывает `getRange(from,to)`.

`ShiftPlanItemDialog.spec.js`:

- список candidates содержит Manager (включая текущего пользователя); Admin не добавляется на клиенте.
- `canEditLead=false` отключает select, назначение и очистку.
- `operatorAssignmentCount>0` отключает именно очистку, но переназначение остаётся доступным при `canEditLead=true`.
- сохранение вызывает `setLead(date, position, id/null)`, передаёт loading, при 409 показывает сообщение и emit `reload`.
- кнопка «Открыть ТПА и литейщиков» ведёт на `/shift-lead/items/{item.id}` только для сохранённого item со старшим.

- [ ] **Step 2: Запустить RED**

Run: `cd Wintime-Control-Frontend; npm test -- src/views/planning/__tests__/ShiftPlanView.spec.js src/components/planning/__tests__/ShiftPlanItemDialog.spec.js src/router/__tests__/shiftPlanRoute.spec.js`
Expected: FAIL — компоненты и route отсутствуют.

- [ ] **Step 3: Реализовать view и dialog**

`ShiftPlanView` хранит `from`, `to`, `days`, `candidates`, `loading`, `selectedItem`; `load` делает `Promise.all([getRange, getLeadCandidates])`, защищается sequence number от устаревших ответов. Использовать `el-date-picker type="daterange" value-format="YYYY-MM-DD"`, `el-table` с двумя columns; во второй — обычный flex-wrap список кнопок, а не динамически выровненная общая сетка.

Форматирование окна:

```js
import dayjs from 'dayjs'
import utc from 'dayjs/plugin/utc'
import timezone from 'dayjs/plugin/timezone'
dayjs.extend(utc)
dayjs.extend(timezone)

export function formatShiftWindow(item) {
  const start = dayjs(item.startAtUtc).tz(item.timeZoneId).format('HH:mm')
  const end = dayjs(item.endAtUtc).tz(item.timeZoneId).format('HH:mm')
  return `${start}–${end}`
}
```

Dialog получает `day`, `item`, `candidates`, `saving`; select и clear опираются только на серверные flags. После успешного PUT emit `saved` с возвращённым днём; view заменяет день в `days` и обновляет выбранный item по позиции. Ошибка 400/409 показывается через `apiErrorMessage`; после 409 вызывается полный `load`.

Добавить menu item `/planning/shift-plan` с подписью `План смен` внутрь существующего «Планирование» для Manager/Admin, не переименовывая «План работ на смену». Route guard пока должен разрешать только Manager/Admin.

- [ ] **Step 4: Запустить GREEN и существующие planning tests**

Run: `cd Wintime-Control-Frontend; npm test -- src/views/planning/__tests__/ShiftPlanView.spec.js src/components/planning/__tests__/ShiftPlanItemDialog.spec.js src/router/__tests__/shiftPlanRoute.spec.js src/router/__tests__/shiftWorkPlanRoute.spec.js src/views/planning/__tests__/ShiftWorkPlanView.spec.js`
Expected: PASS; PZP-18 не изменён.

- [ ] **Step 5: Commit**

```powershell
git add Wintime-Control-Frontend/src/api/shiftPlans.js Wintime-Control-Frontend/src/views/planning/ShiftPlanView.vue Wintime-Control-Frontend/src/components/planning/ShiftPlanItemDialog.vue Wintime-Control-Frontend/src/views/planning/__tests__/ShiftPlanView.spec.js Wintime-Control-Frontend/src/components/planning/__tests__/ShiftPlanItemDialog.spec.js Wintime-Control-Frontend/src/router/index.js Wintime-Control-Frontend/src/router/__tests__/shiftPlanRoute.spec.js Wintime-Control-Frontend/src/layouts/DefaultLayout.vue
git commit -m "feat(planning-ui): добавить страницу плана смен (PZP-32)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 10: Отдельный route family и layout ShiftLead

**Files:**
- Create: `Wintime-Control-Frontend/src/layouts/ShiftLeadLayout.vue`
- Create: `Wintime-Control-Frontend/src/api/shiftStaffing.js`
- Create: `Wintime-Control-Frontend/src/stores/shiftStaffing.js`
- Modify: `Wintime-Control-Frontend/src/stores/auth.js`
- Modify: `Wintime-Control-Frontend/src/router/index.js`
- Modify: `Wintime-Control-Frontend/src/views/auth/LoginView.vue`
- Test: `Wintime-Control-Frontend/src/router/__tests__/shiftLeadRoute.spec.js`
- Test: `Wintime-Control-Frontend/src/layouts/__tests__/ShiftLeadLayout.spec.js`
- Test: `Wintime-Control-Frontend/src/stores/__tests__/shiftStaffing.spec.js`

**Interfaces:**
- Produces: `authStore.isShiftLead`, `authStore.homePath`.
- Routes: `/shift-lead`, `/shift-lead/history`, `/shift-lead/items/:itemId`, `/shift-lead/items/:itemId/tasks/:taskId`.
- Manager/Admin разрешены только на два item/task route; home/history — только ShiftLead.
- Produces: Pinia actions `loadNearest`, `loadItem`, `loadHistory`, `loadOperators`, `assign`, `unassign`.

API wrapper:

```js
import apiClient from './client'

export const shiftStaffingApi = {
  nearest: () => apiClient.get('/shift-staffing/nearest'),
  history: () => apiClient.get('/shift-staffing/history'),
  item: itemId => apiClient.get(`/shift-staffing/items/${itemId}`),
  task: (itemId, taskId) => apiClient.get(`/shift-staffing/items/${itemId}/tasks/${taskId}`),
  operators: () => apiClient.get('/shift-staffing/operators'),
  assign: (itemId, immId, operatorId) =>
    apiClient.post(`/shift-staffing/items/${itemId}/operators`, { immId, operatorId }),
  unassign: assignmentId =>
    apiClient.post(`/shift-staffing/assignments/${assignmentId}/unassign`)
}
```

- [ ] **Step 1: Написать failing route tests независимо от isAdjuster**

```js
it.each(['/', '/tasks', '/mobile/tasks', '/planning/shift-plan'])(
  'перенаправляет ShiftLead с %s на собственный домашний маршрут', async path => {
    authenticate('ShiftLead')
    await router.push(path)
    expect(router.currentRoute.value.path).toBe('/shift-lead')
  }
)

it('не использует правила Adjuster для ShiftLead', async () => {
  authenticate('ShiftLead')
  const auth = useAuthStore()
  expect(auth.isShiftLead).toBe(true)
  expect(auth.isAdjuster).toBe(false)
  await router.push('/shift-lead/history')
  expect(router.currentRoute.value.path).toBe('/shift-lead/history')
})

it('пускает Manager на выбранный item, но не в персональную историю', async () => {
  authenticate('Manager')
  await router.push('/shift-lead/items/item-1')
  expect(router.currentRoute.value.path).toBe('/shift-lead/items/item-1')
  await router.push('/shift-lead/history')
  expect(router.currentRoute.value.path).toBe('/')
})
```

Добавить cases: authenticated ShiftLead на `/login` → `/shift-lead`; Adjuster по-прежнему → `/mobile/tasks`; Operator → `/`; Manager/Admin не попадают в layout наладчика; direct task route доступен всем трём новым ролям; Adjuster/Observer/Operator получают redirect по role guard.

- [ ] **Step 2: Написать failing store tests последовательности и ошибок**

Mock только `shiftStaffingApi`. Проверить:

- `loadNearest` сохраняет summary и загружает detail через `item(itemId)`; `{ item:null }` оставляет прежний detail для read-only просмотра завершившейся, вызывает повторный `item` и выставляет `canEditOperators=false` по ответу сервера; если прежней смены не было — empty state.
- устаревший ответ более раннего `loadItem` не перезаписывает новый item (sequence number).
- `assign` не отправляет Slot/время; успех перезагружает item; `unassign` отправляет POST без тела и перезагружает item.
- 400 показывает доменное сообщение; 403 помечает `canEditOperators=false` и перезагружает; 409 вызывает `ElMessage` и обязательную перезагрузку; pending request блокирует повторный submit, но фоновый refresh не показывает первичный spinner.

- [ ] **Step 3: Запустить RED**

Run: `cd Wintime-Control-Frontend; npm test -- src/router/__tests__/shiftLeadRoute.spec.js src/layouts/__tests__/ShiftLeadLayout.spec.js src/stores/__tests__/shiftStaffing.spec.js`
Expected: FAIL — layout/store/getter/routes отсутствуют.

- [ ] **Step 4: Реализовать homePath, guard и отдельный layout**

В auth store:

```js
isShiftLead: (state) => (state.user?.role ?? state.userRole) === 'ShiftLead',
homePath: (state) => {
  const role = state.user?.role ?? state.userRole
  if (role === 'Adjuster') return '/mobile/tasks'
  if (role === 'ShiftLead') return '/shift-lead'
  return '/'
}
```

Login использует `router.push(authStore.homePath)`. Router guard использует `homePath` для Login и role fallback; сначала обрабатывает Adjuster, затем ShiftLead, затем meta.roles. Новый family имеет `meta.isShiftLeadUi=true`, поэтому Manager/Admin могут открыть item без редиректа, а ShiftLead не может выйти в DefaultLayout/MobileLayout.

Layout содержит шапку «Старший смены», logout и только для ShiftLead две ссылки «Моя смена» и «История». Для Manager/Admin вместо них — «Назад к плану смен». Не импортировать `MobileLayout`, `useMobileStore`, scanner или API наладчика. Content — `<router-view />` с max-width для мобильного чтения.

Store использует `nearest`, `currentItem`, `history`, `operators`, `loading`, `refreshing`, `saving`, `loadSeq`. Все права берутся из `currentItem.shift.canEditOperators`; локально сравнение времени не является источником доступа.

- [ ] **Step 5: Запустить GREEN и регрессию router**

Run: `cd Wintime-Control-Frontend; npm test -- src/router src/stores/__tests__/shiftStaffing.spec.js src/layouts/__tests__/ShiftLeadLayout.spec.js src/layouts/__tests__/DefaultLayout.operator.spec.js`
Expected: PASS; маршруты Operator/Adjuster/Manager не изменились.

- [ ] **Step 6: Commit**

```powershell
git add Wintime-Control-Frontend/src/layouts/ShiftLeadLayout.vue Wintime-Control-Frontend/src/api/shiftStaffing.js Wintime-Control-Frontend/src/stores/shiftStaffing.js Wintime-Control-Frontend/src/stores/auth.js Wintime-Control-Frontend/src/router/index.js Wintime-Control-Frontend/src/views/auth/LoginView.vue Wintime-Control-Frontend/src/router/__tests__/shiftLeadRoute.spec.js Wintime-Control-Frontend/src/layouts/__tests__/ShiftLeadLayout.spec.js Wintime-Control-Frontend/src/stores/__tests__/shiftStaffing.spec.js
git commit -m "feat(shift-lead-ui): выделить мобильный контур старшего (PZP-32)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 11: Мобильный список ТПА → карточка → задание/история и таймеры

**Files:**
- Create: `Wintime-Control-Frontend/src/composables/useShiftStaffingRefresh.js`
- Create: `Wintime-Control-Frontend/src/views/shift-lead/ShiftLeadHomeView.vue`
- Create: `Wintime-Control-Frontend/src/views/shift-lead/ShiftStaffingItemView.vue`
- Create: `Wintime-Control-Frontend/src/views/shift-lead/ShiftLeadHistoryView.vue`
- Create: `Wintime-Control-Frontend/src/views/shift-lead/ShiftStaffingTaskView.vue`
- Modify: `Wintime-Control-Frontend/src/components/tasks/TaskResultsTab.vue`
- Test: `Wintime-Control-Frontend/src/composables/__tests__/useShiftStaffingRefresh.spec.js`
- Test: `Wintime-Control-Frontend/src/views/shift-lead/__tests__/ShiftLeadHomeView.spec.js`
- Test: `Wintime-Control-Frontend/src/views/shift-lead/__tests__/ShiftStaffingItemView.spec.js`
- Test: `Wintime-Control-Frontend/src/views/shift-lead/__tests__/ShiftLeadHistoryView.spec.js`
- Test: `Wintime-Control-Frontend/src/views/shift-lead/__tests__/ShiftStaffingTaskView.spec.js`
- Test: `Wintime-Control-Frontend/src/components/tasks/__tests__/TaskResultsTab.spec.js`

**Interfaces:**
- Consumes: store и routes из Task 10; только API `shiftStaffingApi`.
- Produces: `useShiftStaffingRefresh({ getEndAtUtc, refresh })` с cleanup и reschedule.
- TaskResultsTab получает optional prop `staffingItemId`; при нём загружает `{ task, results }` через `shiftStaffingApi.task`, иначе сохраняет прежние operator/manager ветки.

- [ ] **Step 1: Написать failing timer tests с fake timers**

```js
it('обновляет каждые 60 секунд и в EndAtUtc, очищая оба таймера', async () => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-11-08T15:59:00Z'))
  const refresh = vi.fn().mockResolvedValue(undefined)
  const getEndAtUtc = vi.fn(() => '2026-11-08T16:00:00Z')
  const { stop } = useShiftStaffingRefresh({ getEndAtUtc, refresh })

  await vi.advanceTimersByTimeAsync(60_000)
  expect(refresh).toHaveBeenCalledTimes(1)
  await vi.advanceTimersByTimeAsync(60_000)
  expect(refresh).toHaveBeenCalledTimes(2)
  stop()
  await vi.advanceTimersByTimeAsync(120_000)
  expect(refresh).toHaveBeenCalledTimes(2)
})
```

Добавить tests: `EndAtUtc <= now` вызывает refresh немедленно, после refresh новый EndAtUtc пересоздаёт timeout; для даты дальше 24.8 суток используется capped timeout и последующий reschedule, а не переполнение; unmounted не оставляет timers; overlapping refresh не запускается параллельно. Периодическая константа экспортируется как `SHIFT_STAFFING_REFRESH_MS = 60_000`.

- [ ] **Step 2: Написать failing UI tests пользовательского потока**

Home fixtures: nearest current + два активных ТПА, один с заданием, другой без. Проверить:

- список содержит эффективные статусы через общий `getEffectiveStatusMeta`, краткие задания и `1/2`/`2/2`; ТПА без задания содержит `Задание отсутствует` и остаётся кликабельным.
- click ТПА → `/shift-lead/items/{itemId}`; Manager из плана открывает тот же компонент, не mobile наладчика.
- отсутствие назначений показывает заглушку, не запрашивает общие tasks/personnel APIs.

Item tests:

- активные и уже снятые интервалы отображаются с именами, `AssignedAt/UnassignedAt` в browser timezone, active count; future assignment содержит `Работа учитывается с начала смены`.
- два активных скрывают/блокируют «Назначить»; снятие освобождает место; повторное назначение вызывает новый `assign`; ТПА без задания разрешает назначение.
- выбор Operator включает безлогинового и уже назначенного на другой ТПА; активный на этом ТПА исключается из candidates как UX, сервер остаётся защитой.
- `canEditOperators=false` убирает все «Снять»/«Назначить» в истории и после окончания; после 409 появляется сообщение и item перезагружен.
- loading блокирует повторные mutations; task click → `/shift-lead/items/{itemId}/tasks/{taskId}`.

History tests: загружаются только `shiftStaffingApi.history`, summaries ведут на item, редактирования нет, более поздние future не подмешиваются клиентом.

Task tests: `ShiftStaffingTaskView` и `TaskResultsTab` вызывают только `shiftStaffingApi.item/task`; не вызывают `tasksApi`, `dashboardApi`, `personnelApi` или произвольную историю ТПА; mutation controls отсутствуют.

- [ ] **Step 3: Запустить RED**

Run: `cd Wintime-Control-Frontend; npm test -- src/composables/__tests__/useShiftStaffingRefresh.spec.js src/views/shift-lead src/components/tasks/__tests__/TaskResultsTab.spec.js`
Expected: FAIL — views/composable и staffing-режим results отсутствуют.

- [ ] **Step 4: Реализовать composable таймеров**

```js
import { onScopeDispose } from 'vue'

export const SHIFT_STAFFING_REFRESH_MS = 60_000
const MAX_TIMEOUT_MS = 2_147_483_647

export function useShiftStaffingRefresh({ getEndAtUtc, refresh }) {
  let stopped = false
  let running = false
  let pollTimer = null
  let endTimer = null

  async function runRefresh() {
    if (stopped || running) return
    running = true
    try { await refresh() } finally {
      running = false
      if (!stopped) scheduleEnd()
    }
  }

  function scheduleEnd() {
    clearTimeout(endTimer)
    const end = getEndAtUtc()
    if (!end || stopped) return
    const delay = Math.max(0, Math.min(Date.parse(end) - Date.now() + 50, MAX_TIMEOUT_MS))
    endTimer = setTimeout(runRefresh, delay)
  }

  pollTimer = setInterval(runRefresh, SHIFT_STAFFING_REFRESH_MS)
  scheduleEnd()
  function stop() {
    stopped = true
    clearInterval(pollTimer)
    clearTimeout(endTimer)
  }
  onScopeDispose(stop)
  return { stop, refresh: runRefresh }
}
```

После `loadNearest` Home берёт EndAtUtc из нового summary; если nearest исчез, store перезагружает завершившийся item, и сервер возвращает read-only flags. При появлении следующей смены Home заменяет summary/detail, таймер перенастраивается на новый EndAtUtc.

- [ ] **Step 5: Реализовать четыре экрана и staffing-режим результатов**

Home на mounted `loadNearest`, рендерит `currentItem.imms`, использует `useShiftStaffingRefresh({ getEndAtUtc: () => store.nearest?.endAtUtc, refresh: store.loadNearest })`. Не запускать отдельный независимый polling в Item, чтобы два экрана не дублировали запросы; Item имеет ручное «Обновить» и reload после mutations, а при открытии из Home получает уже обновляемый store. При прямом открытии Item запускает тот же composable с `refresh = () => store.loadItem(route.params.itemId)` и `getEndAtUtc = () => store.currentItem?.shift.endAtUtc`.

Item рендерит все задания read-only, активные и исторические assignments. Формат меток только `dayjs(value).format('DD.MM.YYYY HH:mm')` без `.tz(factory)`. Предварительное пояснение показывается, если `Date.now() < Date.parse(shift.startAtUtc)` и есть активные назначения; это только текст, не разрешение.

History загружает summaries и открывает Item; никаких дополнительных фильтров по userId на клиенте.

Task view получает detail item из store, находит task по route param, показывает базовые поля и `TaskResultsTab :task-id="taskId" :staffing-item-id="itemId"`. В `TaskResultsTab.load` добавить первую ветку:

```js
if (props.staffingItemId) {
  const response = await shiftStaffingApi.task(props.staffingItemId, props.taskId)
  results.value = response.data.results
} else {
  const res = props.operator
    ? await dashboardApi.getTaskResults(props.taskId)
    : await tasksApi.getResults(props.taskId)
  results.value = res.data
}
```

Сохранить silent refresh раз в 60 секунд только для inProgress и очистку существующего timer.

- [ ] **Step 6: Запустить GREEN и весь frontend suite**

Run: `cd Wintime-Control-Frontend; npm test -- src/composables/__tests__/useShiftStaffingRefresh.spec.js src/views/shift-lead src/components/tasks/__tests__/TaskResultsTab.spec.js`
Run: `cd Wintime-Control-Frontend; npm test`
Expected: PASS; тесты подтверждают EndAtUtc + 60 секунд, исчезновение controls, 409 reload и отсутствие запрещённых API.

- [ ] **Step 7: Commit**

```powershell
git add Wintime-Control-Frontend/src/composables/useShiftStaffingRefresh.js Wintime-Control-Frontend/src/composables/__tests__/useShiftStaffingRefresh.spec.js Wintime-Control-Frontend/src/views/shift-lead Wintime-Control-Frontend/src/components/tasks/TaskResultsTab.vue Wintime-Control-Frontend/src/components/tasks/__tests__/TaskResultsTab.spec.js
git commit -m "feat(shift-lead-ui): добавить ТПА, литейщиков и историю (PZP-32)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 12: ADR-0027, CLAUDE.md, VERSION и полная проверка

**Files:**
- Create: `docs/adr/0027-shift-plan-and-operator-assignments.md`
- Modify: `docs/adr/README.md`
- Modify: `CLAUDE.md`
- Modify: `VERSION`
- Verify: все изменённые исходники и tests предыдущих задач

**Interfaces:**
- Produces: зафиксированные доменные инварианты и ссылка на ADR-0027.
- Produces: `VERSION` содержит ровно `1.32.0`.

- [ ] **Step 1: Написать ADR-0027 по MADR**

Разделы: `Status: Accepted`, `Date: 2026-11-08`, контекст, решение, отвергнутые альтернативы, последствия. В решении зафиксировать:

- позиционный `ShiftPlanItem` без `ShiftId`; virtual day из актуального графика, сохранённый день как полный snapshot;
- материализация первым старшим, удаление после последнего очищенного старшего;
- синхронизация только future factory dates, сохранение Id/старших/assignments оставшихся позиций;
- nearest и historical как чистые функции `now`, без status field и background worker;
- интервальный журнал, Slot 1/2 и два partial unique indexes, row lock;
- `WorkStart/WorkEnd` для PZP-31, неснятое назначение ограничено `EndAtUtc`.

Отвергнутые альтернативы обязательно: стабильный `ShiftId` в назначениях; отдельное вхождение смены без старшего; хранение только текущего состава; дополнительно фоновое закрытие назначений и редактирование истории Manager/Admin.

Добавить запись в `docs/adr/README.md` после ADR-0026.

- [ ] **Step 2: Обновить CLAUDE.md правилом-итогом**

В раздел роли добавить: ShiftLead вне иерархии, собственная политика, только новый контур; Manager/Admin план и ближайшие позиции; общие API не открыты. Добавить новый подраздел «План смен и литейщики (PZP-32)» с моделью `ShiftPlanDay/ShiftPlanItem`, виртуальными днями, полной материализацией, future-only позиционной синхронизацией, nearest/history, двумя активными через Slot, интервалами и фактическим WorkStart/WorkEnd. Указать ADR-0027 и отсутствие worker. Не дублировать весь ADR.

Записать в `VERSION`:

```text
1.32.0
```

- [ ] **Step 3: Проверить документацию против реализации и спецификации**

Run: `git diff --check`
Expected: нет whitespace errors.

Проверить, что docs не обещают PZP-31, ручной выпуск/брак, постоянную привязку старшего или изменение прошлого. Проверить VERSION `1.32.0`, отсутствие FK `ShiftId`, обе partial unique filters, `ShiftLeadOrManagerOrAdmin`, отсутствие ShiftLead в `AdjusterOrHigher`.

- [ ] **Step 4: Запустить полную backend verification**

Run: `dotnet test Wintime.Control.sln --configuration Release`
Expected: все unit и integration tests PASS, включая Testcontainers/PostgreSQL 16; failed = 0. Если Docker недоступен, не объявлять integration проверку выполненной: зафиксировать конкретную ошибку и запустить после восстановления Docker.

Run: `dotnet ef migrations has-pending-model-changes --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`
Expected: pending model changes отсутствуют.

- [ ] **Step 5: Запустить полную frontend verification и production build**

Run: `cd Wintime-Control-Frontend; npm test`
Expected: все Vitest files/tests PASS.

Run: `cd Wintime-Control-Frontend; npm run build`
Expected: Vite build успешен; новые lazy routes и layout включены без unresolved imports.

- [ ] **Step 6: Финальный self-review инвариантов перед commit**

Проверить по коду и tests каждый пункт:

- первый старший создаёт полный день; последний очищенный удаляет его;
- текущие/прошлые сохранённые дни не синхронизируются;
- один Operator на нескольких ТПА разрешён, третий активный на одном запрещён;
- назначение без задания работает, но без старшего нет;
- снятие сохраняет интервал, повторное назначение создаёт новый;
- ровно `EndAtUtc` read-only и nearest переходит к следующей;
- Manager/Admin не редактируют поздние future и historical;
- ShiftLead видит только nearest и собственную историю;
- mobile flow не вызывает старые запрещённые API;
- UI обновляется в EndAtUtc и каждые 60 секунд, stale 409 перезагружает;
- production code не завершает задания/ТПА при окончании смены и не содержит нового background worker.

- [ ] **Step 7: Commit**

```powershell
git add docs/adr/0027-shift-plan-and-operator-assignments.md docs/adr/README.md CLAUDE.md VERSION
git commit -m "docs(pzp-32): зафиксировать архитектуру плана смен" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

После этого не создавать PR без отдельного указания пользователя. Ветка `feature/pzp-32-shift-lead` готова к review с VERSION `1.32.0`.
