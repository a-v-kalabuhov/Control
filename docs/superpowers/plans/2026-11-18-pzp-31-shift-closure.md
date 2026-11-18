# PZP-31 Shift Closure and Shift Report Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить электронный сменный рапорт с фактическими интервалами литейщиков, динамическим черновиком, автоматическими показателями, распределением выпуска, транзакционным закрытием в неизменяемый снимок, аудируемыми исправлениями и двумя PDF-режимами.

**Architecture:** Core содержит сущности рапорта и чистые политики окон, плана, циклов, объединения интервалов, распределения и валидации. Infrastructure строит динамическую read model из актуальных заданий/циклов/статусов, а при закрытии повторяет чтение под `ShiftScheduleLock` и сохраняет полный типизированный снимок; закрытые чтение и PDF используют только снимок. Vue получает серверные права и ошибки полей, использует единый редактор ручных значений для ПК и мобильного UI и не вызывает из контура ShiftLead общие API заданий, истории и персонала.

**Tech Stack:** ASP.NET Core 9, EF Core 9, Npgsql/PostgreSQL 16 (`btree_gist`, exclusion constraints), xUnit + Testcontainers, QuestPDF 2026.2.4, Vue 3, Vue Router, Element Plus, Vitest.

## Global Constraints

- Спецификация: `docs/superpowers/specs/2026-11-18-pzp-31-shift-closure-design.md`; она согласована. Найденное новое противоречие остановить и вынести пользователю, не менять требования молча.
- Роль — только `User.Role`; использовать `ShiftLeadOrManagerOrAdmin` и `ManagerOrAdmin`. ShiftLead работает только со своими сменами; общие API заданий, произвольной истории ТПА и полного персонала ему не открывать.
- Все интервалы полуоткрытые `[start, end)`. Цикл относится к смене и интервалу по `EndTime`; `EndTime == ShiftEnd` принадлежит следующей смене.
- Все `DateTime` в БД и EF-запросах — UTC с `Kind=Utc`; клиентские границы назначений нормализовать сразу, `Unspecified` отклонять. Время в UI — пояс браузера, в PDF — `ShiftPlanItem.TimeZoneId`.
- `AssignedAt`/`UnassignedAt` — фактические границы работы; автор и время создания/правки хранятся отдельно. `UnassignedAt = null` означает работу до `EndAtUtc`; background worker не добавлять.
- До закрытия плановые и автоматические значения не хранить как черновик: они пересчитываются при каждом чтении. Ручные поля хранятся отдельно. Закрытый снимок не пересчитывать.
- `FactEnd = min(ClosedAt, EndAtUtc)` при закрытии и `min(now, EndAtUtc)` для динамического черновика; до начала смены учётное окно пустое. Досрочное закрытие ограничивает только факт, план считается на полное окно смены.
- Автоматический выпуск — только успешные закрытые циклы текущего `TaskId`; `PauseDurationMs` в средний цикл не входит. Для старого `ImmCycle.Cavities == 0` использовать `Mold.Cavities` по существующему инварианту.
- Объединять соседние интервалы с одинаковым множеством `OperatorId` до распределения и округления; `Slot` на объединение не влияет. При двух литейщиках Slot 1 **на начало объединённого интервала** получает `ceil(Q/2)`, второй — `floor(Q/2)`.
- Ненулевой автоматический выпуск без литейщика блокирует закрытие; нулевой сирота-интервал — нет. Ручные годные и брак между литейщиками не распределять.
- Расхождение автоматического выпуска и ручных годных + брака не блокирует и не подсвечивает закрытие. Проверки `Брак <= Годные` нет.
- Эффективная тарная единица — `ActualPackagingUnit ?? TaskPackagingUnit`; при наличии выпуска она и фактический вес обязательны.
- Закрытие, интервалы, план и черновик сериализуются через `ShiftScheduleLock`; exclusion violation `23P01`, unique `23505`, serialization `40001` и deadlock `40P01` преобразуются в 409.
- Закрытие не вызывает методы жизненного цикла `ShiftTask`, не меняет `ActualQuantity`, `DefectQuantity`, расход или другие поля задания и не останавливает ТПА.
- После закрытия Manager/Admin меняет только шесть разрешённых ручных полей с непустой причиной; правка и `ShiftClosureCorrection` атомарны. План, автоматические значения, интервалы и доли заморожены.
- Не реализовывать перенос брака в `ShiftTask`, PZP-36, PZP-37, распределение ручного выпуска, складское принятие, электронные подписи и фоновое закрытие.
- Каждая задача выполняется через RED → GREEN → регрессию → отдельный commit. Для PostgreSQL-ограничений и конкурентности не заменять Testcontainers на InMemory.
- `VERSION` не менять до Task 18; перед PR поднять минорно `1.33.1 → 1.34.0`. PR без отдельного указания пользователя не создавать.

## Карта файлов и границы

### Core

- `Entities/ShiftClosure.cs`, `ShiftClosureEntry.cs`, `ShiftClosureOutputShare.cs`, `ShiftClosureCorrection.cs` — заголовок, типизированный снимок строк, доли и аудит.
- `Entities/ShiftImmOperatorAssignment.cs`, `ShiftPlanItem.cs` — фактические интервалы, авторы правок и one-to-one closure.
- `Policies/OperatorWorkIntervalPolicy.cs` — новые фактические границы и пустой интервал `WorkStart == WorkEnd`.
- `Policies/ShiftClosurePlanCalculator.cs` — остаток, полный план смены и плановый расход.
- `Policies/ShiftClosureCycleMetrics.cs` — выпуск и средний цикл по `EndTime`.
- `Policies/ShiftOutputDistributor.cs` — элементарные/объединённые интервалы, доли и сироты.
- `Policies/ShiftOfflineFallback.cs` — пересечение сырого `Offline` с окном активности.
- `Policies/ShiftClosureValidator.cs` — диапазоны, полнота закрытия/исправления, early close.
- `DTOs/ShiftClosures/ShiftClosureDtos.cs` — read model, ручной ввод, доли, timeline, pending, corrections и field errors.
- `Interfaces/IShiftClosureService.cs`, `IShiftClosurePdfService.cs` — application и PDF границы.

### Infrastructure / API

- `Services/ShiftClosureReadModelLoader.cs` — единая загрузка динамической read model для GET, закрытия и индикаторов.
- `Services/ShiftClosureService.cs` — доступ, pending, draft, close и corrections.
- `Services/ShiftClosureIndicatorService.cs` — пакетные `closureStatus`/`hasOrphanOutput` для плана без N+1 HTTP/read-model вызовов.
- `Services/ShiftStaffingService.cs` — фактические интервалы, Slot на всём интервале, closure-aware доступ.
- `Services/ShiftPlanService.cs` — индикаторы, предупреждения и самоназначение Manager на текущую смену без старшего.
- `Reports/ShiftClosurePdfService.cs` — два QuestPDF-документа.
- `Data/ControlDbContext.cs` и миграции `Pzp31ActualOperatorIntervals`, `Pzp31ShiftClosures` — схема, exclusion constraints, снимки и индексы.
- `Controllers/ShiftClosuresController.cs`, `ShiftStaffingController.cs`, `ShiftPlanController.cs` — тонкие HTTP-адаптеры.
- `ExceptionHandling/DomainExceptionHandler.cs` — обратносовместимый `{ message }` плюс `{ code, errors }` для closure validation.

### Frontend

- `api/shiftClosures.js` — только `/api/shift-closures`, включая blob PDF.
- `api/shiftStaffing.js` — фактические границы и `PUT /interval`.
- `composables/useShiftClosureEditor.js` — ручной ввод, dirty-state, сохранение, закрытие и защита от фоновой перезаписи.
- `components/shift-closures/ShiftClosureTable.vue` — ПК-таблица.
- `components/shift-closures/ShiftClosureCards.vue` — мобильные карточки.
- `components/shift-closures/ShiftClosureTimeline.vue` — объединённые интервалы, сироты и действие «Назначить».
- `components/shift-closures/ShiftClosureCorrectionsDialog.vue` — причина, пакетная правка и история.
- `views/planning/ShiftWorkPlanView.vue` — вкладка «Сменный рапорт».
- `views/shift-lead/ShiftClosureView.vue`, `ShiftLeadHomeView.vue`, `ShiftStaffingItemView.vue` — рапорт, pending и временная шкала.
- `views/planning/ShiftPlanView.vue`, `components/planning/ShiftPlanItemDialog.vue` — статусы и предупреждения.

---

### Task 1: Аудит назначений и временные exclusion constraints

**Files:**

- Modify: `Wintime.Control.Core/Entities/ShiftImmOperatorAssignment.cs`
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs`
- Create: `Wintime.Control.Infrastructure/Migrations/<timestamp>_Pzp31ActualOperatorIntervals.cs`
- Create: `Wintime.Control.Infrastructure/Migrations/<timestamp>_Pzp31ActualOperatorIntervals.Designer.cs`
- Modify: `Wintime.Control.Infrastructure/Migrations/ControlDbContextModelSnapshot.cs`
- Test: `Wintime.Control.Tests.Integration/Migrations/Pzp31ActualOperatorIntervalsMigrationTests.cs`
- Modify: `Wintime.Control.Tests.Integration/Migrations/Pzp32ShiftLeadStaffingMigrationTests.cs`

**Interfaces:**

- Produces: `ShiftImmOperatorAssignment.CreatedById`, `CreatedBy`, `UpdatedById?`, `UpdatedBy`, `UpdatedAt?`; унаследованный `BaseEntity.CreatedAt` остаётся серверным временем создания записи.
- Produces: `EX_ShiftImmOperatorAssignments_SlotRange` и `EX_ShiftImmOperatorAssignments_OperatorRange` на полуоткрытом `tstzrange("AssignedAt", "UnassignedAt", '[)')` с неограниченным верхним краем при `NULL`.
- Consumes: существующие `ShiftPlanItems`, `Users`, `Imms`; исторические строки не удаляются.

- [ ] **Step 1: Написать failing migration tests на реальном PostgreSQL 16**

По образцу `Pzp32ShiftLeadStaffingMigrationTests` поднять отдельный контейнер, применить миграции через `IMigrator` и проверить:

1. Два разных Operator на слотах 1/2 с пересекающимися интервалами разрешены; третий не помещается в Slot 1/2.
2. Пересечение одного Slot даёт `PostgresException.SqlState == "23P01"`; соседние `[10:00,11:00)` и `[11:00,12:00)` разрешены.
3. Пересечение одного Operator на том же `(ShiftPlanItemId, ImmId)` даёт `23P01`; тот же Operator на другом Imm разрешён.
4. Пустой диапазон не конфликтует; `UnassignedAt < AssignedAt` по-прежнему даёт `23514`.
5. `CreatedById` обязателен, `CreatedAt` сохранён; `UpdatedById`/`UpdatedAt` nullable до первой правки; FK авторов — Restrict.
6. Старые partial unique indexes отсутствуют после Up и восстановлены после Down. Расширение `btree_gist` в Down не удалять.

```csharp
[Fact]
public async Task Same_operator_cannot_overlap_on_same_imm_but_can_on_another_imm()
{
    await MigrateToLatestAsync();
    var first = NewAssignment(slot: 1, start: "2026-11-18T08:00:00Z", end: "2026-11-18T12:00:00Z");
    var overlapping = NewAssignment(slot: 2, start: "2026-11-18T11:00:00Z", end: "2026-11-18T13:00:00Z");
    await InsertAsync(first);
    var error = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(overlapping));
    error.SqlState.Should().Be("23P01");

    overlapping.ImmId = NewImmId;
    await InsertAsync(overlapping); // не бросает: другой ТПА
}
```

Старый PZP-32 migration test либо явно остановить на `20261108010425_Pzp32ShiftLeadStaffing` для проверки исторического Up, либо обновить его latest-ожидания на exclusion constraints; не оставлять двойное противоречивое ожидание partial indexes на latest.

- [ ] **Step 2: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~Pzp31ActualOperatorIntervalsMigrationTests`
Expected: FAIL — новые столбцы и exclusion constraints отсутствуют.

- [ ] **Step 3: Добавить audit-поля и убрать active-only уникальность из EF-модели**

```csharp
public class ShiftImmOperatorAssignment : BaseEntity
{
    public Guid ShiftPlanItemId { get; set; }
    public Guid ImmId { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public int Slot { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? UnassignedAt { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public string? UpdatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ShiftPlanItem ShiftPlanItem { get; set; } = null!;
    public Imm Imm { get; set; } = null!;
    public User Operator { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
    public User? UpdatedBy { get; set; }
}
```

В `ControlDbContext` удалить `IX_ShiftImmOperatorAssignments_ActiveSlot` и `IX_ShiftImmOperatorAssignments_ActiveOperator`, сохранить checks Slot и `UnassignedAt >= AssignedAt`, настроить FK авторов Restrict. Exclusion constraints добавляются raw SQL в миграции, а не выдаются за EF unique indexes.

- [ ] **Step 4: Сгенерировать миграцию и добавить PostgreSQL-ограничения**

Run: `dotnet ef migrations add Pzp31ActualOperatorIntervals --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`

Backfill существующих строк: `CreatedById = ShiftPlanItem.ShiftLeadId`, унаследованный `CreatedAt` не менять, `UpdatedById = NULL`, `UpdatedAt = NULL`; затем сделать `CreatedById` required. В Up выполнить:

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE "ShiftImmOperatorAssignments"
ADD CONSTRAINT "EX_ShiftImmOperatorAssignments_SlotRange"
EXCLUDE USING gist (
  "ShiftPlanItemId" WITH =,
  "ImmId" WITH =,
  "Slot" WITH =,
  tstzrange("AssignedAt", "UnassignedAt", '[)') WITH &&
);

ALTER TABLE "ShiftImmOperatorAssignments"
ADD CONSTRAINT "EX_ShiftImmOperatorAssignments_OperatorRange"
EXCLUDE USING gist (
  "ShiftPlanItemId" WITH =,
  "ImmId" WITH =,
  "OperatorId" WITH =,
  tstzrange("AssignedAt", "UnassignedAt", '[)') WITH &&
);
```

В Down удалить оба constraint и восстановить два прежних filtered unique index; `DROP EXTENSION` не выполнять. Не добавлять SQL с `now()` как постоянный default авторства.

- [ ] **Step 5: Запустить GREEN и проверить модель**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~Pzp31ActualOperatorIntervalsMigrationTests|FullyQualifiedName~Pzp32ShiftLeadStaffingMigrationTests"`
Run: `dotnet ef migrations has-pending-model-changes --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`
Expected: оба test target PASS; pending model changes отсутствуют.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/Entities/ShiftImmOperatorAssignment.cs Wintime.Control.Infrastructure/Data/ControlDbContext.cs Wintime.Control.Infrastructure/Migrations Wintime.Control.Tests.Integration/Migrations/Pzp31ActualOperatorIntervalsMigrationTests.cs Wintime.Control.Tests.Integration/Migrations/Pzp32ShiftLeadStaffingMigrationTests.cs
git commit -m "feat(staffing): добавить временные ограничения интервалов (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 2: Фактические интервалы, Slot на всём интервале и доступ до закрытия

**Files:**

- Modify: `Wintime.Control.Core/Policies/OperatorWorkIntervalPolicy.cs`
- Modify: `Wintime.Control.Core/DTOs/ShiftStaffing/ShiftStaffingDtos.cs`
- Modify: `Wintime.Control.Core/Interfaces/IShiftStaffingService.cs`
- Modify: `Wintime.Control.Infrastructure/Services/ShiftStaffingService.cs`
- Modify: `Wintime.Control.API/Controllers/ShiftStaffingController.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/OperatorWorkIntervalPolicyTests.cs`
- Test: `Wintime.Control.Tests.Integration/ShiftStaffing/ShiftStaffingIntervalApiTests.cs`
- Modify: `Wintime.Control.Tests.Integration/ShiftStaffing/ShiftStaffingMutationApiTests.cs`
- Modify: `Wintime.Control.Tests.Integration/ShiftStaffing/ShiftStaffingReadApiTests.cs`

**Interfaces:**

- Produces: `AssignOperatorRequest(Guid ImmId, string OperatorId, DateTime? AssignedAt, DateTime? UnassignedAt)`.
- Produces: `UnassignOperatorRequest(DateTime? UnassignedAt)` и `UpdateOperatorIntervalRequest(DateTime AssignedAt, DateTime? UnassignedAt)`.
- Produces: `UnassignAsync(Guid assignmentId, UnassignOperatorRequest request, StaffingCaller caller, CancellationToken ct)`.
- Produces: `UpdateIntervalAsync(Guid assignmentId, UpdateOperatorIntervalRequest request, StaffingCaller caller, CancellationToken ct)`.
- Produces: изменённые `POST /api/shift-staffing/items/{itemId}/operators` и `POST /api/shift-staffing/assignments/{assignmentId}/unassign`, новый `PUT /api/shift-staffing/assignments/{assignmentId}/interval`.
- Produces: `OperatorWorkIntervalPolicy.Resolve(DateTime assignedAt, DateTime? unassignedAt, DateTime shiftStartAtUtc, DateTime shiftEndAtUtc)` с `IsEmpty = WorkEnd <= WorkStart`.

- [ ] **Step 1: Переписать unit tests на фактические умолчания и границы**

```csharp
[Fact]
public void Resolve_uses_actual_bounds_and_marks_pre_start_cancel_empty()
{
    var result = OperatorWorkIntervalPolicy.Resolve(
        Utc("2026-11-18T08:00:00"), Utc("2026-11-18T08:00:00"),
        Utc("2026-11-18T08:00:00"), Utc("2026-11-18T20:00:00"));

    result.WorkStart.Should().Be(result.WorkEnd);
    result.IsEmpty.Should().BeTrue();
}

[Fact]
public void Unassign_default_is_clamped_to_actual_work_and_shift_end()
{
    OperatorWorkIntervalPolicy.DefaultUnassignedAt(
        Utc("2026-11-18T21:00:00"), Utc("2026-11-18T08:00:00"),
        Utc("2026-11-18T20:00:00")).Should().Be(Utc("2026-11-18T20:00:00"));
}
```

Добавить cases: назначение без границ начинается в `ShiftStart`; null-конец заканчивается в `ShiftEnd`; конец до фактического начала запрещён; равенство допускается только для отмены предварительного назначения; PUT с пустым интервалом запрещён; обе границы обязаны быть внутри окна смены.

- [ ] **Step 2: Написать failing API tests на mutations и доступ**

Покрыть:

- POST без границ создаёт `[ShiftStart, ShiftEnd)` с `UnassignedAt = null`, даже если запрос выполнен позже; explicit границы сохраняются, авторы/время создания серверные.
- Unassign без границы использует `min(max(now, WorkStart), ShiftEnd)`; explicit конец сохраняется; повторное снятие запрещено.
- PUT меняет обе границы и Updated audit, не меняя Created audit и Id.
- Два одновременных литейщика разрешены; третий на пересечении — 409. Один Operator на разных ТПА разрешён, пересечение на том же — 409.
- Последовательные занятые участки Slot 1/2, когда ни один Slot не свободен на всём запрошенном интервале, дают 409; автоматического разбиения нет.
- Завершённая собственная незакрытая смена ShiftLead и завершённая незакрытая смена Manager/Admin допускают POST/unassign/PUT. Nearest UI при этом не меняется.
- Поздняя будущая смена Manager/Admin и чужая/ненеближайшая будущая ShiftLead по-прежнему 403; уже закрытая до запроса смена — 400 `shiftClosed`; смена без старшего — 400.
- Если mutation началась до закрытия, но после ожидания `ShiftScheduleLock` обнаружила новый Closed, она возвращает 409; это отличается от обычного редактирования уже закрытой смены.
- `23P01`, `23505`, `40001`, `40P01` не превращаются в 500.

- [ ] **Step 3: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~OperatorWorkIntervalPolicyTests`
Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftStaffingIntervalApiTests|FullyQualifiedName~ShiftStaffingMutationApiTests|FullyQualifiedName~ShiftStaffingReadApiTests"`
Expected: FAIL на новых request members, historical edit и overlap capacity.

- [ ] **Step 4: Реализовать политику и interval-aware mutations**

```csharp
public static DateTime DefaultUnassignedAt(
    DateTime nowUtc, DateTime workStartUtc, DateTime shiftEndUtc)
{
    var afterStart = nowUtc >= workStartUtc ? nowUtc : workStartUtc;
    return afterStart <= shiftEndUtc ? afterStart : shiftEndUtc;
}

private static bool Overlaps(
    ShiftImmOperatorAssignment assignment, DateTime start, DateTime end, Guid exceptId) =>
    assignment.Id != exceptId &&
    assignment.AssignedAt < end &&
    (assignment.UnassignedAt == null || assignment.UnassignedAt > start);
```

В каждой mutation использовать execution strategy → transaction → `ShiftScheduleLock` → `FOR UPDATE` родительского item → повторную проверку доступа/старшего/closure → блокировки Imm/Operator при назначении → выбор Slot. Для POST оценить Slot 1, затем 2 по `Overlaps` на всём `[requestedStart, requestedEnd ?? ShiftEnd)`; при отсутствии единого свободного Slot выбросить `ShiftConcurrencyException` с просьбой изменить границы. Для PUT исключить текущий assignment и повторно выбрать Slot.

`ValidateMutationAccessAsync` больше не приравнивает historical к read-only: ShiftLead требует собственный item и nearest **либо** ended unclosed; Manager/Admin требует nearest **либо** ended unclosed. До ожидания lock зафиксировать исходное closure state; если оно было не-Closed, а после lock стало Closed, выбросить `ShiftConcurrencyException` 409. `ShiftClosure.State == Closed`, уже существовавшее до запроса, запрещает mutation с 400 `shiftClosed`. Чтение nearest/history и read-only задания не расширять. В `IsConcurrencyFailure` добавить `PostgresErrorCodes.ExclusionViolation` (`23P01`).

В DTO добавить `CreatedById`, `CreatedAt`, `UpdatedById`, `UpdatedAt`; `CanEditOperators` вычислять тем же closure-aware правилом. Все серверные метки получать из `IFactoryClock.UtcNow` с `Kind=Utc`.

- [ ] **Step 5: Добавить controller routes и запустить GREEN**

```csharp
[HttpPut("assignments/{assignmentId:guid}/interval")]
public Task<ShiftOperatorAssignmentDto> UpdateInterval(
    Guid assignmentId, [FromBody] UpdateOperatorIntervalRequest request, CancellationToken ct) =>
    _service.UpdateIntervalAsync(assignmentId, request, Caller(), ct);
```

Изменить существующий unassign на `[FromBody] UnassignOperatorRequest request`; пустое JSON `{}` означает умолчание. Не принимать Slot, автора или CreatedAt от клиента.

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~OperatorWorkIntervalPolicyTests`
Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftStaffingIntervalApiTests|FullyQualifiedName~ShiftStaffingMutationApiTests|FullyQualifiedName~ShiftStaffingReadApiTests"`
Expected: PASS, включая concurrency и обновлённые historical expectations.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/Policies/OperatorWorkIntervalPolicy.cs Wintime.Control.Core/DTOs/ShiftStaffing/ShiftStaffingDtos.cs Wintime.Control.Core/Interfaces/IShiftStaffingService.cs Wintime.Control.Infrastructure/Services/ShiftStaffingService.cs Wintime.Control.API/Controllers/ShiftStaffingController.cs Wintime.Control.Tests.Unit/Policies/OperatorWorkIntervalPolicyTests.cs Wintime.Control.Tests.Integration/ShiftStaffing
git commit -m "feat(staffing): редактировать фактические интервалы до закрытия (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 3: Сущности сменного рапорта и схема снимка

**Files:**

- Create: `Wintime.Control.Core/Entities/ShiftClosure.cs`
- Create: `Wintime.Control.Core/Entities/ShiftClosureEntry.cs`
- Create: `Wintime.Control.Core/Entities/ShiftClosureOutputShare.cs`
- Create: `Wintime.Control.Core/Entities/ShiftClosureCorrection.cs`
- Modify: `Wintime.Control.Core/Entities/ShiftPlanItem.cs`
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs`
- Create: `Wintime.Control.Infrastructure/Migrations/<timestamp>_Pzp31ShiftClosures.cs`
- Create: `Wintime.Control.Infrastructure/Migrations/<timestamp>_Pzp31ShiftClosures.Designer.cs`
- Modify: `Wintime.Control.Infrastructure/Migrations/ControlDbContextModelSnapshot.cs`
- Test: `Wintime.Control.Tests.Integration/Migrations/Pzp31ShiftClosuresMigrationTests.cs`

**Interfaces:**

- Produces: `ShiftClosureState { Draft = 0, Closed = 1 }` и DbSets `ShiftClosures`, `ShiftClosureEntries`, `ShiftClosureOutputShares`, `ShiftClosureCorrections`.
- Produces: one-to-one `ShiftPlanItem.Closure`; `ClosedEarly` не хранится.
- Produces: typed snapshot columns; JSON существует только в `ShiftClosureCorrection.ChangesJson`.

- [ ] **Step 1: Написать failing schema tests**

Проверить на PostgreSQL:

- unique `ShiftClosures.ShiftPlanItemId`, unique `(ShiftClosureId, ShiftTaskId)`, unique `(ShiftClosureEntryId, IntervalStartUtc, IntervalEndUtc, OperatorId)`;
- nullable/manual checks: вес > 0, цикл > 0, тара > 0, места >= 0 и numeric(18,2), годные/брак/доли >= 0, Slot 1/2;
- `ClosedAt`/`ClosedById` либо оба заполнены у Closed, либо оба null у Draft;
- `ChangesJson` имеет тип `jsonb`, `Reason` обязателен после Trim на application-уровне;
- FK Task/Imm/Operator/User Restrict; Closure→Entries→Shares и Closure→Corrections Cascade;
- индекс `IX_ImmCycles_TaskId_EndTime` существует;
- Down удаляет только новые объекты и новый индекс циклов.

- [ ] **Step 2: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~Pzp31ShiftClosuresMigrationTests`
Expected: FAIL — таблицы отсутствуют.

- [ ] **Step 3: Добавить entities со всеми snapshot-полями**

```csharp
public enum ShiftClosureState { Draft = 0, Closed = 1 }

public class ShiftClosure : BaseEntity
{
    public Guid ShiftPlanItemId { get; set; }
    public ShiftClosureState State { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public string? ClosedById { get; set; }
    public DateTime? ClosedAt { get; set; }
    public ShiftPlanItem ShiftPlanItem { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
    public User? ClosedBy { get; set; }
    public List<ShiftClosureEntry> Entries { get; set; } = new();
    public List<ShiftClosureCorrection> Corrections { get; set; } = new();
}
```

`ShiftClosureEntry : BaseEntity` должен содержать ровно следующие рабочие поля и снимок:

```csharp
public Guid ShiftClosureId { get; set; }
public Guid ShiftTaskId { get; set; }
public Guid ImmId { get; set; }
public string ImmNameSnapshot { get; set; } = string.Empty;
public string ProductNameSnapshot { get; set; } = string.Empty;
public string PlasticIngredientsSnapshot { get; set; } = string.Empty;
public string ColorantIngredientsSnapshot { get; set; } = string.Empty;
public int RemainingAtStart { get; set; }
public decimal NormPartWeightGrams { get; set; }
public int? PlannedFullCycleSeconds { get; set; }
public int? TaskPackagingUnit { get; set; }
public int Cavities { get; set; }
public int ShiftPlanQuantity { get; set; }
public decimal PlannedMaterialWeightGrams { get; set; }
public int AutomaticOutputQuantity { get; set; }
public double? AutomaticAverageFullCycleSeconds { get; set; }
public bool IsManualFallbackRequired { get; set; }
public decimal? ActualPartWeightGrams { get; set; }
public int? ManualFullCycleSeconds { get; set; }
public int? ActualPackagingUnit { get; set; }
public decimal? PackagingPlaces { get; set; }
public int? ManualGoodQuantity { get; set; }
public int? ManualDefectQuantity { get; set; }
public ShiftClosure ShiftClosure { get; set; } = null!;
public ShiftTask ShiftTask { get; set; } = null!;
public Imm Imm { get; set; } = null!;
public List<ShiftClosureOutputShare> OutputShares { get; set; } = new();
```

`ShiftClosureOutputShare : BaseEntity`: `ShiftClosureEntryId`, `IntervalStartUtc`, `IntervalEndUtc`, `OperatorId`, `OperatorFullNameSnapshot`, `OperatorEmployeeIdSnapshot` (string?), `SlotAtIntervalStart`, `Quantity`. `ShiftClosureCorrection : BaseEntity`: `ShiftClosureId`, `ChangedById`, `ChangedAt`, `Reason`, `ChangesJson` и навигации. `ShiftPlanItem.Closure` nullable.

- [ ] **Step 4: Настроить EF и сгенерировать миграцию**

Использовать существующую точность `ShiftTask.PartWeightGrams` для обоих весов, `decimal(18,2)` для `PackagingPlaces` и `PlannedMaterialWeightGrams`. Настроить уникальность, checks и Restrict/Cascade из Step 1. Добавить простой индекс `(TaskId, EndTime)` для предыдущего и сменного выпуска.

Run: `dotnet ef migrations add Pzp31ShiftClosures --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`
Expected: созданы migration/Designer и обновлён snapshot.

- [ ] **Step 5: Запустить GREEN**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~Pzp31ShiftClosuresMigrationTests|FullyQualifiedName~Pzp31ActualOperatorIntervalsMigrationTests"`
Run: `dotnet ef migrations has-pending-model-changes --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`
Expected: PASS; pending model changes отсутствуют.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/Entities Wintime.Control.Infrastructure/Data/ControlDbContext.cs Wintime.Control.Infrastructure/Migrations Wintime.Control.Tests.Integration/Migrations/Pzp31ShiftClosuresMigrationTests.cs
git commit -m "feat(db): добавить модель сменного рапорта (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 4: Отбор строк, остаток, план смены, расход и автоматические циклы

**Files:**

- Create: `Wintime.Control.Core/Policies/ShiftClosurePlanCalculator.cs`
- Create: `Wintime.Control.Core/Policies/ShiftClosureCycleMetrics.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ShiftClosurePlanCalculatorTests.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ShiftClosureCycleMetricsTests.cs`
- Modify: `Wintime.Control.Tests.Unit/Policies/ShiftTaskWindowTests.cs`

**Interfaces:**

- Consumes: `ShiftTaskWindow.Belongs`, `TaskActivityWindow.Release`, `TaskPeriodPlan.Calculate`, `TaskPlanCalculator.Calculate`.
- Produces: `ShiftClosurePlanCalculator.RemainingAtStart(int planQuantity, int previousOutput)`.
- Produces: `ShiftClosurePlanCalculator.Calculate(ShiftTask task, DateTime plannedStartAtUtc, DateTime shiftStartUtc, DateTime shiftEndUtc, int previousOutput)`.
- Produces: `ShiftClosurePlanCalculator.MaterialWeightGrams(int shiftPlanQuantity, int cavities, decimal partWeightGrams, decimal runnerWeightGrams)`.
- Produces: `ShiftClosureCycleMetrics.Calculate(IEnumerable<ImmCycle> cycles, Guid taskId, DateTime shiftStartUtc, DateTime factEndUtc)` → `ShiftCycleMetrics(int OutputQuantity, double? AverageFullCycleSeconds)`.
- Produces: `ShiftClosureCycleMetrics.PreviousOutput(IEnumerable<ImmCycle> cycles, Guid taskId, DateTime shiftStartUtc)`.

- [ ] **Step 1: Написать failing tests границ и полного плана при досрочном закрытии**

```csharp
[Fact]
public void Plan_for_open_release_uses_full_shift_not_fact_end()
{
    var task = StartedTask(planQuantity: 100, cavities: 2, cycleSeconds: 60,
        startedAt: Utc("2026-11-18T08:00:00"));
    var plan = ShiftClosurePlanCalculator.Calculate(task,
        Utc("2026-11-18T07:30:00"), Utc("2026-11-18T08:00:00"),
        Utc("2026-11-18T10:00:00"), previousOutput: 20);

    plan.Should().Be(80); // норматив за 2 часа = 100, ограничен остатком 80; ClosedAt не параметр
}

[Fact]
public void Material_includes_runner_once_for_last_partial_shot()
{
    ShiftClosurePlanCalculator.MaterialWeightGrams(5, 2, 10m, 4m)
        .Should().Be(62m); // 5*10 + ceil(5/2)*4
}
```

Добавить отдельные tests: Planned/Issued по `PlannedStartAt`; Setup/InProgress/Completed/Closed по activity overlap; переходящее задание; расчётное начало после наладки; неполная смена; нулевой/невычислимый план; `max(0, Plan-Previous)`; нулевой расход.

- [ ] **Step 2: Написать failing tests выпуска и среднего цикла**

```csharp
[Fact]
public void Metrics_use_end_time_and_exclude_open_unsuccessful_and_shift_end()
{
    var taskId = Guid.NewGuid();
    var cycles = new[]
    {
        ClosedCycle(taskId, "2026-11-18T07:59:59", cavities: 2, duration: 20),
        ClosedCycle(taskId, "2026-11-18T08:00:00", cavities: 3, duration: 30),
        ClosedCycle(taskId, "2026-11-18T09:59:59", cavities: 4, duration: 40),
        ClosedCycle(taskId, "2026-11-18T10:00:00", cavities: 8, duration: 50),
        OpenCycle(taskId, cavities: 16)
    };

    var result = ShiftClosureCycleMetrics.Calculate(cycles, taskId,
        Utc("2026-11-18T08:00:00"), Utc("2026-11-18T10:00:00"));

    result.OutputQuantity.Should().Be(7);
    result.AverageFullCycleSeconds.Should().Be(35);
}
```

Добавить неуспешный цикл, другой TaskId, цикл начатый до смены и завершённый внутри, `PauseDurationMs` без влияния, `Cavities=0 → Mold.Cavities`, пустой результат `(0,null)`, PreviousOutput строго `< ShiftStart`.

- [ ] **Step 3: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ShiftClosurePlanCalculatorTests|FullyQualifiedName~ShiftClosureCycleMetricsTests|FullyQualifiedName~ShiftTaskWindowTests"`
Expected: FAIL — новые калькуляторы отсутствуют.

- [ ] **Step 4: Реализовать чистые калькуляторы через существующие политики**

```csharp
public static int Calculate(ShiftTask task, DateTime plannedStartAtUtc,
    DateTime shiftStartUtc, DateTime shiftEndUtc, int previousOutput)
{
    var remaining = RemainingAtStart(task.PlanQuantity, previousOutput);
    Interval? release;
    if (task.StartedAt.HasValue)
    {
        release = TaskActivityWindow.Release(task, shiftEndUtc);
        if (release != null && !task.CompletedAt.HasValue && !task.ClosedAt.HasValue)
            release = new Interval(release.Start, shiftEndUtc);
    }
    else
    {
        var start = plannedStartAtUtc.AddMinutes(task.PlannedSetupMinutes);
        var plan = TaskPlanCalculator.Calculate(plannedStartAtUtc,
            task.PlannedSetupMinutes, task.PlanQuantity, task.Cavities,
            task.PlannedFullCycleSeconds, 0, 0);
        release = plan.ReleaseSeconds is long seconds
            ? new Interval(start, start.AddSeconds(seconds))
            : null;
    }

    return Math.Min(remaining, TaskPeriodPlan.Calculate(release,
        task.PlanQuantity, task.Cavities, task.PlannedFullCycleSeconds,
        shiftStartUtc, shiftEndUtc));
}
```

В metrics сначала фильтровать `TaskId`, `IsSuccessful`, `EndTime != null`, затем полуоткрытое окно. Сумма использует `c.Cavities > 0 ? c.Cavities : c.Mold?.Cavities ?? 0`; среднее — только `DurationSeconds`. Не использовать `TaskResultsService`, `ActualQuantity`, паузу или sample threshold. Расход возвращает граммы: `P*W + ceil(P/(decimal)C)*R`, при неpositive P/C возвращает 0.

- [ ] **Step 5: Запустить GREEN**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ShiftClosurePlanCalculatorTests|FullyQualifiedName~ShiftClosureCycleMetricsTests|FullyQualifiedName~ShiftTaskWindowTests|FullyQualifiedName~TaskPeriodPlanTests"`
Expected: все PASS; сумма планов по смежным сменам не превышает норматив задания.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/Policies/ShiftClosurePlanCalculator.cs Wintime.Control.Core/Policies/ShiftClosureCycleMetrics.cs Wintime.Control.Tests.Unit/Policies/ShiftClosurePlanCalculatorTests.cs Wintime.Control.Tests.Unit/Policies/ShiftClosureCycleMetricsTests.cs Wintime.Control.Tests.Unit/Policies/ShiftTaskWindowTests.cs
git commit -m "feat(closures): рассчитать план и автоматические показатели (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 5: Объединённые интервалы, сироты и индивидуальные доли

**Files:**

- Create: `Wintime.Control.Core/Policies/ShiftOutputDistributor.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ShiftOutputDistributorTests.cs`

**Interfaces:**

- Produces: `ShiftOperatorSlice(Guid AssignmentId, string OperatorId, string FullName, string? EmployeeId, int Slot, DateTime StartUtc, DateTime EndUtc)`.
- Produces: `ShiftOutputCycle(Guid TaskId, DateTime EndUtc, int Quantity)`.
- Produces: `MergedShiftInterval(DateTime StartUtc, DateTime EndUtc, IReadOnlyList<ShiftOperatorSlice> Operators)`; Operators сохраняют Slot на начало merged interval.
- Produces: `ShiftOutputShareModel(Guid TaskId, DateTime StartUtc, DateTime EndUtc, string OperatorId, string FullName, string? EmployeeId, int SlotAtIntervalStart, int Quantity)`.
- Produces: `ShiftOrphanOutputModel(Guid TaskId, DateTime StartUtc, DateTime EndUtc, int Quantity)`.
- Produces: `Distribute(IReadOnlyList<ShiftOperatorSlice> assignments, IReadOnlyList<ShiftOutputCycle> cycles, IReadOnlyDictionary<Guid, Interval?> taskWindows, DateTime shiftStartUtc, DateTime factEndUtc)` → `ShiftOutputDistribution(MergedIntervals, Shares, Orphans)`.

- [ ] **Step 1: Написать failing tests объединения до округления**

```csharp
[Fact]
public void Adjacent_same_pair_is_merged_before_odd_split_and_slot_swap_does_not_split()
{
    var a = Operator("A", slot: 1, "08:00", "09:00");
    var b = Operator("B", slot: 2, "08:00", "09:00");
    var aLater = Operator("A", slot: 2, "09:00", "10:00");
    var bLater = Operator("B", slot: 1, "09:00", "10:00");
    var cycles = new[] { Cycle("09:00", 1), Cycle("09:30", 1) };

    var result = ShiftOutputDistributor.Distribute(
        new[] { a, b, aLater, bLater }, cycles, OneTaskWindow(), ShiftStart, FactEnd);

    result.MergedIntervals.Should().ContainSingle();
    result.Shares.Where(s => s.OperatorId == "A").Sum(s => s.Quantity).Should().Be(1);
    result.Shares.Where(s => s.OperatorId == "B").Sum(s => s.Quantity).Should().Be(1);
    result.Shares.Should().OnlyContain(s => s.Quantity == 1);
}
```

Добавить: один Operator получает всё; чётный/нечётный Q; нулевые доли в intersecting task window; суммирование одного человека из разных интервалов; разрыв не объединяется; разные составы не объединяются; границы заданий не разбивают merged interval; два TaskId группируются отдельно; цикл ровно на границе идёт в следующий; сирота только при Q>0; сумма долей + сирот равна автоматическому выпуску без потери/удвоения.

- [ ] **Step 2: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftOutputDistributorTests`
Expected: FAIL — тип распределителя отсутствует.

- [ ] **Step 3: Реализовать границы, merge и распределение**

Алгоритм обязателен:

1. Обрезать assignments учётным окном, исключить `end <= start`; включить `ShiftStart` и `FactEnd` в список границ, чтобы были видны сироты до первого/после последнего назначения.
2. Для каждого elementary interval выбрать активные assignments по `a.StartUtc <= start && a.EndUtc >= end`, сгруппировать по одному Operator.
3. Ключ состава — отсортированные distinct `OperatorId`, без Slot. Соседние `previous.End == next.Start` с равным ключом объединять; Operators и Slot взять из первого elementary interval.
4. Для каждого merged interval и каждого task window из `taskWindows` с непустым пересечением создать доли всем Operators даже при Q=0. Циклы группировать сначала по TaskId, затем по merged interval.
5. При одном Operator отдать Q; при двух упорядочить по `SlotAtIntervalStart`, первому `(Q+1)/2`, второму `Q/2`; при нуле Operators и Q>0 создать сироту.

```csharp
var firstShare = (quantity + 1) / 2;
var secondShare = quantity / 2;
```

Не округлять elementary intervals отдельно и не использовать текущий Slot последующего назначения для уже начавшегося merged interval.

- [ ] **Step 4: Запустить GREEN**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftOutputDistributorTests`
Expected: все PASS; property-style проверка суммы по нескольким наборам интервалов/циклов PASS.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Core/Policies/ShiftOutputDistributor.cs Wintime.Control.Tests.Unit/Policies/ShiftOutputDistributorTests.cs
git commit -m "feat(closures): распределить выпуск по объединённым интервалам (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 6: Offline fallback, диапазоны и условия закрытия

**Files:**

- Create: `Wintime.Control.Core/Policies/ShiftOfflineFallback.cs`
- Create: `Wintime.Control.Core/Policies/ShiftClosureValidator.cs`
- Create: `Wintime.Control.Core/DTOs/ShiftClosures/ShiftClosureDtos.cs`
- Create: `Wintime.Control.Core/Exceptions/ShiftClosureValidationException.cs`
- Modify: `Wintime.Control.API/ExceptionHandling/DomainExceptionHandler.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ShiftOfflineFallbackTests.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ShiftClosureValidatorTests.cs`
- Test: `Wintime.Control.Tests.Integration/Errors/DomainExceptionHandlerTests.cs`

**Interfaces:**

- Produces: `ShiftOfflineFallback.IsRequired(IEnumerable<RawSegment> raw, Interval? taskActivity, DateTime shiftStartUtc, DateTime factEndUtc, bool hasOperators)`.
- Produces: `ShiftClosureFieldError(Guid TaskId, string Field, string Code)` и `ShiftClosureValidationException(string code, string message, IReadOnlyList<ShiftClosureFieldError> errors)`.
- Produces: `ShiftClosureValidator.Validate(ShiftClosureValidationEntry entry, bool requireCompleteness)` → список field errors.
- Produces: `ShiftClosureValidator.ValidateCloseTiming(DateTime nowUtc, DateTime startUtc, DateTime endUtc, bool confirmEarlyClose)`.
- Produces: HTTP 400 `{ message, code, errors: [{ taskId, field, code }] }`; старые DomainException сохраняют `{ message }`.

- [ ] **Step 1: Написать failing tests Offline**

```csharp
[Fact]
public void Any_raw_offline_inside_activity_requires_fallback_only_with_operators()
{
    var raw = new[] { new RawSegment(ImmStatus.Offline,
        Utc("2026-11-18T09:00:00"), Utc("2026-11-18T09:05:00")) };
    var activity = new Interval(Utc("2026-11-18T08:00:00"), Utc("2026-11-18T10:00:00"));

    ShiftOfflineFallback.IsRequired(raw, activity, ShiftStart, FactEnd, hasOperators: true)
        .Should().BeTrue();
    ShiftOfflineFallback.IsRequired(raw, activity, ShiftStart, FactEnd, hasOperators: false)
        .Should().BeFalse();
}
```

Добавить Offline вне activity, Offline после FactEnd, отсутствие Offline, `Idle` без циклов, отсутствие raw segments и null activity. Не выводить Offline из отсутствия циклов или effective timeline gap.

- [ ] **Step 2: Написать failing validation matrix**

Покрыть обе стадии `requireCompleteness=false/true`:

- draft проверяет только заполненные диапазоны и не требует полноты;
- fallback требует `ManualFullCycleSeconds`, `ManualGoodQuantity`, `ManualDefectQuantity`, даже при части автоматических циклов;
- обычный режим эти три поля не требует;
- AutomaticOutput>0 **или** ManualGood+ManualDefect>0 требует вес и effective packaging; null ручных количеств не считается явным нулём;
- `ActualPackagingUnit ?? TaskPackagingUnit` удовлетворяет проверку;
- вес/цикл/тара >0 если заполнены, места >=0 и не более двух знаков, годные/брак независимые int>=0; `Брак > Годные` допустим;
- строка без Operators не требует fallback;
- до Start — `closeBeforeShiftStart`; ровно Start разрешено; early без подтверждения — `earlyCloseConfirmationRequired`, с подтверждением — успех.

- [ ] **Step 3: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ShiftOfflineFallbackTests|FullyQualifiedName~ShiftClosureValidatorTests"`
Expected: FAIL — политики отсутствуют.

- [ ] **Step 4: Реализовать чистую валидацию и structured errors**

`ShiftClosureValidationEntry` содержит `TaskId`, `HasOperators`, `IsManualFallbackRequired`, `AutomaticOutputQuantity`, `TaskPackagingUnit` и шесть manual fields. Сначала всегда проверять диапазоны заполненных полей; при `requireCompleteness` добавлять fallback и обязательные вес/тару. Места проверять через `value == decimal.Round(value, 2)`; не добавлять сверку ручного и автоматического выпуска.

`ShiftClosureValidationException : DomainException` несёт `Code` и `Errors`. В handler использовать pattern matching перед базовым `DomainException` и сериализовать `errors`; для ранних/общих ошибок допускается пустой массив. Русское `message` сохранить для существующего frontend.

- [ ] **Step 5: Запустить GREEN**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ShiftOfflineFallbackTests|FullyQualifiedName~ShiftClosureValidatorTests"`
Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~DomainExceptionHandlerTests`
Expected: PASS; прежние ответы ошибок не сломаны. Если отдельного handler test suite нет, создать `Wintime.Control.Tests.Integration/Errors/DomainExceptionHandlerTests.cs` с old/new response contracts.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/Policies/ShiftOfflineFallback.cs Wintime.Control.Core/Policies/ShiftClosureValidator.cs Wintime.Control.Core/DTOs/ShiftClosures/ShiftClosureDtos.cs Wintime.Control.Core/Exceptions/ShiftClosureValidationException.cs Wintime.Control.API/ExceptionHandling/DomainExceptionHandler.cs Wintime.Control.Tests.Unit/Policies/ShiftOfflineFallbackTests.cs Wintime.Control.Tests.Unit/Policies/ShiftClosureValidatorTests.cs Wintime.Control.Tests.Integration/Errors
git commit -m "feat(closures): проверить fallback и ручные показатели (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 7: Динамическая read model, доли/timeline и pending API

**Files:**

- Modify: `Wintime.Control.Core/DTOs/ShiftClosures/ShiftClosureDtos.cs`
- Create: `Wintime.Control.Core/Interfaces/IShiftClosureService.cs`
- Create: `Wintime.Control.Infrastructure/Services/ShiftClosureReadModelLoader.cs`
- Create: `Wintime.Control.Infrastructure/Services/ShiftClosureService.cs`
- Create: `Wintime.Control.API/Controllers/ShiftClosuresController.cs`
- Modify: `Wintime.Control.API/Program.cs`
- Test: `Wintime.Control.Tests.Integration/ShiftClosures/ShiftClosureReadApiTests.cs`

**Interfaces:**

- Produces: `IShiftClosureService.GetAsync(Guid itemId, StaffingCaller caller, CancellationToken ct)` → `ShiftClosureDto`.
- Produces: `GetPendingAsync(StaffingCaller caller, CancellationToken ct)` → `IReadOnlyList<ShiftClosurePendingDto>`.
- Produces: `GetClosedAsync(Guid closureId, StaffingCaller caller, CancellationToken ct)` для последующих PDF/corrections.
- Produces: `GET /api/shift-closures/items/{itemId}` и `GET /api/shift-closures/pending`.

`ShiftClosureDto` содержит `Id?`, `ShiftPlanItemId`, `State`, `ShiftDate`, `StartAtUtc`, `EndAtUtc`, `FactEndUtc`, `TimeZoneId`, `ShiftLeadId/Name`, `CreatedById`, `CreatedAt`, `ClosedById/Name`, `ClosedAt`, `ClosedEarly`, `Entries`, `Timelines`. Entry содержит все колонки спецификации, шесть manual fields, `HasOperators`, `IsManualFallbackRequired`, `OutputShares` и `OperatorTotals`. Timeline содержит `ImmId`, `ImmName` и merged intervals; каждый interval — границы, Operators, `AutomaticOutputQuantity`, `HasOrphanOutput` и `TaskOutputs[{taskId,quantity}]`.

- [ ] **Step 1: Написать failing integration tests динамического чтения**

Сценарии:

1. GET без записи `ShiftClosure` возвращает `id=null`, `state=Draft`, вычисленные Planned/Issued и Completed/Closed строки, план/остаток/автоматические показатели и полный план при текущем early окне.
2. Новое задание появляется без сохранения; отменённое не начавшееся исчезает; начавшееся остаётся.
3. Ответ содержит объединённые интервалы, индивидуальные/нулевые доли, task grouping и сироту; нулевой сирота не отмечен ошибкой.
4. Raw Offline помечает fallback; online Idle не помечает.
5. Pending возвращает только `EndAtUtc <= now` и незакрытые: ShiftLead — собственные, Manager/Admin — доступные. Наличие следующей nearest смены не скрывает pending.
6. Чужая смена ShiftLead — 403; Operator/Adjuster/Observer — 403; отсутствующий item — 404; item без старшего — 400 `shiftLeadRequired`.
7. До Start план и строки доступны, автоматическое окно пустое, `FactEndUtc <= ShiftStart` не создаёт отрицательных интервалов.

- [ ] **Step 2: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftClosureReadApiTests`
Expected: FAIL — controller/DI отсутствуют.

- [ ] **Step 3: Реализовать единый loader актуальных данных**

`ShiftClosureReadModelLoader` должен:

1. Загрузить item/day/lead/closure и проверить доступ отдельно от nearest. Для closed сразу проецировать только snapshots, не загружать текущие tasks/cycles/statuses.
2. Для draft повторить двухчастный candidate query из `ShiftStaffingService`: planned candidates + `ShiftTaskWindow.ActivityOverlaps`, существующие includes `Imm`, `Mold`, `Product`, `RecipeIngredients.Material`, `OriginalMaterial`; точную фильтрацию выполнить `ShiftTaskWindow.Belongs` с `PlannedStartAt` из `FactorySchedule`.
3. Загрузить successful closed cycles выбранных TaskId с `EndTime < FactEnd` и Mold для fallback cavities; отдельно получить PreviousOutput `< ShiftStart` и текущие циклы `>= ShiftStart && < FactEnd`. Не загружать циклы других заданий в распределение.
4. Загрузить assignments с Operator и сырой `EffectiveStatusInputs.Raw` через `GatherAsync(immIds, ShiftStart, FactEnd, FactEnd)`. До Start/при пустом окне пропустить range query и вернуть пустой raw/циклы.
5. Сформировать activity window через `TaskActivityWindow.Activity(task, FactEnd)`, slices и cycles для `ShiftOutputDistributor`; taskWindows ограничивают допустимый состав и нулевые доли. Timeline строить по каждому Imm заданий, сироты суммировать только Q>0.
6. Подставить сохранённые manual entries по TaskId; строки исчезнувших не начавшихся заданий игнорировать, но физически не удалять на GET.

Не использовать `ShiftTask.ActualQuantity/DefectQuantity`, frontend selector PZP-18 или `TaskResultsService`.

- [ ] **Step 4: Реализовать service/controller и pending**

Controller класс — `[Route("api/shift-closures")]`, `[Authorize(Policy="ShiftLeadOrManagerOrAdmin")]`; `Caller()` переиспользовать из staffing pattern. `GetPendingAsync` фильтрует `ShiftLeadId == caller.UserId` для ShiftLead и `EndAtUtc <= now`, закрытые исключает, сортирует по StartAtUtc descending. Pending DTO включает summary смены, старшего и `hasOrphanOutput` из loader.

`GetClosedAsync` на этом шаге проверяет существование/доступ и возвращает snapshot projection; полноценное создание Closed появится в Task 9. Зарегистрировать loader/service scoped в `Program.cs`.

- [ ] **Step 5: Запустить GREEN и регрессию staffing**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftClosureReadApiTests|FullyQualifiedName~ShiftStaffingReadApiTests|FullyQualifiedName~ShiftStaffingTaskApiTests"`
Expected: PASS; ShiftLead не получил новых прав на общие API.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/DTOs/ShiftClosures/ShiftClosureDtos.cs Wintime.Control.Core/Interfaces/IShiftClosureService.cs Wintime.Control.Infrastructure/Services/ShiftClosureReadModelLoader.cs Wintime.Control.Infrastructure/Services/ShiftClosureService.cs Wintime.Control.API/Controllers/ShiftClosuresController.cs Wintime.Control.API/Program.cs Wintime.Control.Tests.Integration/ShiftClosures/ShiftClosureReadApiTests.cs
git commit -m "feat(closures): читать динамический черновик и pending (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 8: Пакетное сохранение ручного черновика

**Files:**

- Modify: `Wintime.Control.Core/DTOs/ShiftClosures/ShiftClosureDtos.cs`
- Modify: `Wintime.Control.Core/Interfaces/IShiftClosureService.cs`
- Modify: `Wintime.Control.Infrastructure/Services/ShiftClosureService.cs`
- Modify: `Wintime.Control.API/Controllers/ShiftClosuresController.cs`
- Test: `Wintime.Control.Tests.Integration/ShiftClosures/ShiftClosureDraftApiTests.cs`

**Interfaces:**

- Produces: `SaveShiftClosureDraftRequest(IReadOnlyList<ShiftClosureManualEntryInput> Entries)`.
- Produces: `ShiftClosureManualEntryInput(Guid ShiftTaskId, decimal? ActualPartWeightGrams, int? ManualFullCycleSeconds, int? ActualPackagingUnit, decimal? PackagingPlaces, int? ManualGoodQuantity, int? ManualDefectQuantity)`.
- Produces: `SaveDraftAsync(Guid itemId, SaveShiftClosureDraftRequest request, StaffingCaller caller, CancellationToken ct)` → обновлённый `ShiftClosureDto`.
- Produces: `PUT /api/shift-closures/items/{itemId}/draft`.

- [ ] **Step 1: Написать failing tests создания и обновления черновика**

Проверить:

- первый PUT создаёт один Draft с CreatedBy/CreatedAt и Entries только для переданных TaskId; каждая новая Entry получает реальные `ShiftTaskId` и `ImmId` из серверной read model, поэтому обязательные FK валидны; повторный PUT обновляет существующие, не создавая дубли;
- повторный GET возвращает актуальные вычисляемые значения и сохранённые ручные; новые автоматические циклы меняют automatic, но не ручные поля;
- переданный набор — полная замена шести manual fields для указанного TaskId; отсутствующий TaskId не очищается;
- нечлен смены/несуществующий TaskId — 404; closed — 400; чужой ShiftLead — 403; две параллельные попытки первого сохранения дают один Draft, проигравший получает 409;
- draft принимает неполный fallback, но отклоняет negative weight/zero cycle/negative places/более двух decimal places/negative quantities с `taskId` и `field`;
- request не содержит вычисляемые поля, Slot, доли, closure State или клиентское CreatedAt.

- [ ] **Step 2: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftClosureDraftApiTests`
Expected: FAIL — PUT отсутствует.

- [ ] **Step 3: Реализовать транзакционный upsert только ручных полей**

Использовать transaction + `ShiftScheduleLock` + `FOR UPDATE` item, повторно проверить доступ и not closed. До записи получить актуальный набор TaskId через loader; не доверять клиентскому составу. Валидация вызывается с `requireCompleteness=false` для каждой supplied entry.

Если closure отсутствует, создать с `State=Draft`, `CreatedById=caller.UserId`, `CreatedAt=now`. Для каждой supplied entry найти/создать `ShiftClosureEntry`, при создании обязательно заполнить `ShiftTaskId` и `ImmId` из загруженного сервером задания, затем записать только шесть ручных полей. Остальные snapshot-колонки до закрытия остаются default (`string.Empty`, нули/null по типу) и не используются read model; на заполненность снимка draft не рассчитывает. Не вызывать `SaveChangesAsync` частями. Unique/exclusion/serialization/deadlock переводить в 409. После commit вернуть свежий `GetAsync`.

- [ ] **Step 4: Запустить GREEN и unit validation regression**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftClosureDraftApiTests|FullyQualifiedName~ShiftClosureReadApiTests"`
Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftClosureValidatorTests`
Expected: PASS; ручные значения не копируются в автоматические snapshot-колонки.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Core/DTOs/ShiftClosures/ShiftClosureDtos.cs Wintime.Control.Core/Interfaces/IShiftClosureService.cs Wintime.Control.Infrastructure/Services/ShiftClosureService.cs Wintime.Control.API/Controllers/ShiftClosuresController.cs Wintime.Control.Tests.Integration/ShiftClosures/ShiftClosureDraftApiTests.cs
git commit -m "feat(closures): сохранять ручные поля черновика (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 9: Транзакционное закрытие и неизменяемый снимок

**Files:**

- Modify: `Wintime.Control.Core/DTOs/ShiftClosures/ShiftClosureDtos.cs`
- Modify: `Wintime.Control.Core/Interfaces/IShiftClosureService.cs`
- Modify: `Wintime.Control.Infrastructure/Services/ShiftClosureService.cs`
- Modify: `Wintime.Control.API/Controllers/ShiftClosuresController.cs`
- Test: `Wintime.Control.Tests.Integration/ShiftClosures/ShiftClosureCloseApiTests.cs`

**Interfaces:**

- Produces: `CloseShiftClosureRequest(bool ConfirmEarlyClose)`.
- Produces: `CloseAsync(Guid itemId, CloseShiftClosureRequest request, StaffingCaller caller, CancellationToken ct)` → закрытый `ShiftClosureDto`.
- Produces: `POST /api/shift-closures/items/{itemId}/close`.
- Consumes: все чистые политики Tasks 4–6 и dynamic loader Task 7.

- [ ] **Step 1: Написать failing integration tests полного закрытия**

Обязательная матрица:

- close создаёт Entries для **всех** актуальных строк, включая нетронутые, и Shares для всех разрешённых Operators с нулевыми долями; исчезнувшие не начавшиеся draft entries в Closed не попадают;
- snapshot содержит Imm/Product names, plastics/colorants, остаток, нормы, тару задания, Cavities, ShiftPlanQuantity, PlannedMaterialWeightGrams, automatic output/average, fallback и финальные manual fields;
- `ClosedAt` серверное UTC, `ClosedById` текущий пользователь, `ClosedEarly = ClosedAt < EndAtUtc`; повторное закрытие — 409;
- досрочное закрытие без confirm — 400 `earlyCloseConfirmationRequired`; с confirm факт обрезан ClosedAt, план остаётся на полную смену;
- сирота Q>0 блокирует с `orphanOutput`; ретроактивное назначение через Task 2 устраняет блокировку; нулевой сирота не блокирует;
- fallback и обязательные вес/тара возвращают field errors; обычный режим неполных ручных полей допускает; расхождение ручного/автоматического выпуска допускает;
- строка без Operators получает ManualGood=ManualDefect=0 и не требует fallback; смена без заданий закрывается пустой;
- два параллельных close дают один Closed и один 409; параллельная правка интервала либо видна целиком в снимке, либо возвращает 409 после обнаружения закрытия под lock, промежуточного состояния нет.

- [ ] **Step 2: Написать отдельные failing tests неизменности**

После close изменить название Imm/Product, рецепт, PartWeight/PackagingUnit/Cavities задания, добавить поздний цикл внутри старого окна и выполнить retro-binding другого цикла. Повторный GET обязан побайтно по значениям DTO вернуть исходный снимок и исходные Shares. Проверить, что поздно закрывшийся цикл тоже не меняет automatic.

Сохранить до close `ShiftTask.UpdatedAt`, `ActualQuantity`, `DefectQuantity`, `ActualMaterialWeightGrams` и статус; после close они обязаны быть теми же. Изменения самих заданий после close в этом test выполняются отдельно и к ним это требование не относится.

- [ ] **Step 3: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftClosureCloseApiTests`
Expected: FAIL — close route отсутствует.

- [ ] **Step 4: Реализовать close как один повторный расчёт и save**

Внутри execution strategy + transaction выполнить строго:

1. `ShiftScheduleLock.AcquireAsync`; `FOR UPDATE` item; повторно проверить существование, старшего, caller, not closed и `now >= StartAtUtc`.
2. `ValidateCloseTiming(now, Start, End, request.ConfirmEarlyClose)`; установить `FactEnd=min(now,End)`.
3. Перечитать tasks/cycles/assignments/raw statuses через loader **в этом же DbContext после lock**; не использовать DTO, загруженный до lock.
4. Построить все Entries/Shares. Для строк без Operators принудительно установить ManualGood=ManualDefect=0. Выполнить `ShiftClosureValidator.Validate(entry, requireCompleteness: true)` для каждой строки и отклонить любой Q>0 сироту.
5. Создать closure, если его ещё не было; заполнить Created audit при первом создании. Удалить из текущего closure только Entries для TaskId, не входящих в актуальный draft. Заполнить все snapshot columns и заменить Shares; затем `State=Closed`, `ClosedAt=now`, `ClosedById=caller`.
6. Один `SaveChangesAsync`, commit. Не вызывать `TaskOutputHandler`, методы `ShiftTask` или обновление циклов.

При `DbUpdateException` unique/exclusion/serialization/deadlock откатить и выбросить `ShiftConcurrencyException`. Повторный closed, обнаруженный после lock, тоже 409.

- [ ] **Step 5: Запустить GREEN и повторный concurrency прогон**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftClosureCloseApiTests|FullyQualifiedName~ShiftClosureDraftApiTests|FullyQualifiedName~ShiftClosureReadApiTests"`
Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftClosureCloseApiTests -- xUnit.ParallelizeTestCollections=false`
Expected: оба PASS; один снимок, нет потерянных/удвоенных Shares, задание закрытием не изменено.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/DTOs/ShiftClosures/ShiftClosureDtos.cs Wintime.Control.Core/Interfaces/IShiftClosureService.cs Wintime.Control.Infrastructure/Services/ShiftClosureService.cs Wintime.Control.API/Controllers/ShiftClosuresController.cs Wintime.Control.Tests.Integration/ShiftClosures/ShiftClosureCloseApiTests.cs
git commit -m "feat(closures): закрывать смену неизменяемым снимком (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 10: Исправления закрытого рапорта и аудит

**Files:**

- Modify: `Wintime.Control.Core/DTOs/ShiftClosures/ShiftClosureDtos.cs`
- Modify: `Wintime.Control.Core/Interfaces/IShiftClosureService.cs`
- Modify: `Wintime.Control.Infrastructure/Services/ShiftClosureService.cs`
- Modify: `Wintime.Control.API/Controllers/ShiftClosuresController.cs`
- Test: `Wintime.Control.Tests.Integration/ShiftClosures/ShiftClosureCorrectionApiTests.cs`

**Interfaces:**

- Produces: `CorrectShiftClosureRequest(string Reason, IReadOnlyList<ShiftClosureManualEntryInput> Entries)`.
- Produces: `CorrectAsync(Guid closureId, CorrectShiftClosureRequest request, StaffingCaller caller, CancellationToken ct)`.
- Produces: `GetCorrectionsAsync(Guid closureId, StaffingCaller caller, CancellationToken ct)` → `IReadOnlyList<ShiftClosureCorrectionDto>`.
- Produces: `PATCH /api/shift-closures/{closureId}/corrections` (`ManagerOrAdmin`) и `GET /api/shift-closures/{closureId}/corrections` (`ShiftLeadOrManagerOrAdmin` + own check).

- [ ] **Step 1: Написать failing tests аудита и атомарности**

Проверить:

- Manager/Admin может менять ровно шесть manual fields без reopen; State остаётся Closed, ClosedAt/план/автоматические/Shares/интервалы не меняются;
- Reason после Trim обязателен; ChangedBy/ChangedAt серверные; `ChangesJson` содержит только фактически изменённые `{taskId,field,oldValue,newValue}` для одной пакетной правки;
- GET history возвращает изменения в хронологическом порядке и ChangedByName; собственный ShiftLead читает историю, чужой — 403;
- ShiftLead PATCH — 403, Operator/Adjuster/Observer — 403;
- изменённые строки повторно проходят fallback и вес/эффективную тару по frozen automatic; невалидная вторая строка откатывает и первую, и audit insert;
- пустой список фактических изменений — 400 `noChanges`; несуществующий closure/task — 404;
- ручные годные/брак могут исправляться независимо; правило принудительного нуля без Operators применяется при close, а не запрещает последующую аудируемую Manager/Admin коррекцию.

- [ ] **Step 2: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftClosureCorrectionApiTests`
Expected: FAIL — PATCH/history отсутствуют.

- [ ] **Step 3: Реализовать атомарную correction + append-only audit**

В transaction заблокировать closure `FOR UPDATE`, проверить Closed и права. Для каждого TaskId найти существующий snapshot Entry; построить prospective six-field values и вызвать полнотную валидацию с frozen `AutomaticOutputQuantity`, `IsManualFallbackRequired`, `TaskPackagingUnit` и `HasOperators`, вычисленным из frozen Shares (не из актуальных назначений).

Сформировать typed change list только для `old != new`. Сохранить все изменённые Entries и один `ShiftClosureCorrection` с `ChangesJson = JsonSerializer.Serialize(changes)` в одном `SaveChangesAsync`. Не менять `ClosedAt`, `ClosedById`, Created audit или State. GET десериализует JSON в `ShiftClosureCorrectionChangeDto(Guid TaskId,string Field,object? OldValue,object? NewValue)`.

- [ ] **Step 4: Запустить GREEN и закрытую регрессию**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftClosureCorrectionApiTests|FullyQualifiedName~ShiftClosureCloseApiTests"`
Expected: PASS; rollback не оставляет частичных Entries/Corrections; Shares неизменны.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Core/DTOs/ShiftClosures/ShiftClosureDtos.cs Wintime.Control.Core/Interfaces/IShiftClosureService.cs Wintime.Control.Infrastructure/Services/ShiftClosureService.cs Wintime.Control.API/Controllers/ShiftClosuresController.cs Wintime.Control.Tests.Integration/ShiftClosures/ShiftClosureCorrectionApiTests.cs
git commit -m "feat(closures): аудировать исправления закрытого рапорта (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 11: Индикаторы плана смен и самоназначение Manager

**Files:**

- Create: `Wintime.Control.Core/Interfaces/IShiftClosureIndicatorService.cs`
- Create: `Wintime.Control.Infrastructure/Services/ShiftClosureIndicatorService.cs`
- Modify: `Wintime.Control.Core/DTOs/ShiftPlans/ShiftPlanDtos.cs`
- Modify: `Wintime.Control.Core/Interfaces/IShiftPlanService.cs`
- Modify: `Wintime.Control.Infrastructure/Services/ShiftPlanService.cs`
- Modify: `Wintime.Control.API/Controllers/ShiftPlanController.cs`
- Modify: `Wintime.Control.API/Program.cs`
- Test: `Wintime.Control.Tests.Integration/Planning/ShiftPlanClosureIndicatorTests.cs`
- Modify: `Wintime.Control.Tests.Integration/Planning/ShiftPlanApiTests.cs`
- Modify: `Wintime.Control.Tests.Integration/Planning/ShiftPlanScheduleSynchronizationTests.cs`

**Interfaces:**

- Produces: `ShiftPlanItemDto.ClosureStatus` (`Draft`/`Closed`/`NotClosed`), `HasOrphanOutput`, `HasMissingLeadWarning`.
- Produces: `GetAsync(DateOnly from, DateOnly to, string callerUserId, CancellationToken ct)` и `SetLeadAsync(DateOnly date, int position, string? shiftLeadId, string callerUserId, CancellationToken ct)`.
- Produces: пакетный `IShiftClosureIndicatorService.GetAsync(IReadOnlyCollection<Guid> itemIds, DateTime nowUtc, CancellationToken ct)`.
- Consumes: loader Task 7; не делать один HTTP/полный closure GET на каждую позицию.

- [ ] **Step 1: Написать failing tests статусов, сирот и предупреждений**

Проверить:

- Closed при наличии Closed; NotClosed для ended unclosed; Draft для materialized future/current с старшим, включая динамический черновик без записи; virtual item без старшего не имеет closure status (`null`);
- `hasOrphanOutput=true` только при ненулевом автоматическом выпуске без Operators; нулевой сирота false; Closed использует frozen Shares/снимок и не пересчитывается;
- предупреждение отсутствия старшего — для текущей и nearest позиции, включая virtual; поздняя будущая/прошлая без старшего не получает текущее предупреждение;
- `CanEditOperators` для Manager/Admin включает ended unclosed и исключает Closed/позднюю future;
- показательный диапазон из нескольких дней не вызывает отдельный closure HTTP/read service на item.

- [ ] **Step 2: Написать failing tests UC-11 и защиты closure при смене старшего**

Текущая virtual/materialized позиция без старшего: Manager передаёт собственный Id после `StartAtUtc`, получает материализованный полный день и доступный рапорт. Admin сам себя назначить не может; другой ShiftLead/Manager после Start не назначается; ended self-assign не разрешать. До Start прежние правила кандидатов сохраняются.

Очистка старшего при существующем Draft/Closed запрещена; удаление последнего пустого дня не удаляет closure. Синхронизация будущего графика, удаляющая позицию с Draft, откатывается вместе с изменением справочника; текущие/прошлые Closed не затрагиваются.

- [ ] **Step 3: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftPlanClosureIndicatorTests|FullyQualifiedName~ShiftPlanApiTests|FullyQualifiedName~ShiftPlanScheduleSynchronizationTests"`
Expected: FAIL на новых полях и self-assign.

- [ ] **Step 4: Реализовать пакетные индикаторы и доступ**

`closureStatus`: Closed, иначе NotClosed при `now>=End`, иначе Draft. Orphan indicators вычислять пакетно через общий loader/его query-компоненты для открытых items; для Closed достаточно snapshot (валидное закрытие гарантирует false). `HasMissingLeadWarning` вычислять по `ShiftTiming` и существующему nearest-per-position алгоритму, но не привязывать к наличию старшего в nearest candidates.

Для self-assign после Start проверить `callerUserId == shiftLeadId`, `now < EndAtUtc`, `item.ShiftLeadId == null` и загруженный `User.Role == Manager && IsActive`; все остальные `now>=Start` правки по-прежнему запрещены. Controller передаёт NameIdentifier в Get/SetLead. `CanEditLead` отражает это узкое правило.

Загрузить Closure в SetLead/SynchronizeFutureDays; запретить clear/удаление позиции с любым closure. Не ослаблять guards assignments и не удалять историю.

- [ ] **Step 5: Запустить GREEN**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftPlanClosureIndicatorTests|FullyQualifiedName~ShiftPlanApiTests|FullyQualifiedName~ShiftPlanScheduleSynchronizationTests|FullyQualifiedName~ShiftClosureCloseApiTests"`
Expected: PASS; полный день материализуется один раз, будущий справочник откатывается при защищённом Draft.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/Interfaces/IShiftClosureIndicatorService.cs Wintime.Control.Infrastructure/Services/ShiftClosureIndicatorService.cs Wintime.Control.Core/DTOs/ShiftPlans/ShiftPlanDtos.cs Wintime.Control.Core/Interfaces/IShiftPlanService.cs Wintime.Control.Infrastructure/Services/ShiftPlanService.cs Wintime.Control.API/Controllers/ShiftPlanController.cs Wintime.Control.API/Program.cs Wintime.Control.Tests.Integration/Planning
git commit -m "feat(shift-plan): показать закрытие и выпуск без литейщика (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 12: QuestPDF — актуальный бланк и закрытый рапорт

**Files:**

- Create: `Wintime.Control.Core/Interfaces/IShiftClosurePdfService.cs`
- Create: `Wintime.Control.Infrastructure/Reports/ShiftClosurePdfService.cs`
- Modify: `Wintime.Control.API/Controllers/ShiftClosuresController.cs`
- Modify: `Wintime.Control.API/Program.cs`
- Test: `Wintime.Control.Tests.Unit/Reports/ShiftClosurePdfModelTests.cs`
- Test: `Wintime.Control.Tests.Integration/ShiftClosures/ShiftClosurePdfApiTests.cs`

**Interfaces:**

- Produces: `byte[] RenderBlank(ShiftClosureDto draft, DateTime generatedAtUtc)` и `byte[] RenderClosed(ShiftClosureDto closed)`.
- Produces: `GET /api/shift-closures/items/{itemId}/blank-pdf` и `GET /api/shift-closures/{closureId}/pdf`.
- Consumes: `GetAsync`/`GetClosedAsync`; никаких собственных запросов к Task/Imm/User у PDF renderer.

- [ ] **Step 1: Написать failing unit tests PDF model**

Создать внутренний `ShiftClosurePdfModelFactory` и проверить:

- blank копирует актуальные план/автоматические/доли, но очищает все шесть manual fields, содержит «Бланк — смена не закрыта» и server generatedAt;
- closed использует только snapshots, содержит старшего, автора/время закрытия и при early — «Смена закрыта досрочно: фактическое время закрытия …, плановое окончание …»;
- Operators формируют подстроки с EmployeeId, FullName, суммарной долей и пустой Signature; ManualGood/Defect общие по заданию;
- WarehouseAccepted и Signature пустые; локальное время вычисляется `TimeZoneInfo.ConvertTimeFromUtc` с item.TimeZoneId;
- изменение текущего `ShiftTask`/User после построения closed DTO не влияет на model.

- [ ] **Step 2: Написать failing API tests обоих режимов**

Проверить `Content-Type application/pdf`, nonempty bytes начинаются `%PDF-`, filename `.pdf`; blank доступен до/во время/после End пока Draft и содержит server-generated новый документ, не создавая Closure; blank для Closed — 400; closed PDF только для Closed, 404 для Draft/отсутствующего. Собственный ShiftLead допущен, чужой/Operator/Adjuster/Observer — 403.

После получения closed PDF изменить актуальные справочники/циклы/назначения, повторный GET/PDF использует тот же frozen DTO; audited manual correction — единственный путь изменить последующий PDF.

- [ ] **Step 3: Запустить RED**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftClosurePdfModelTests`
Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ShiftClosurePdfApiTests`
Expected: FAIL — renderer/routes отсутствуют.

- [ ] **Step 4: Реализовать альбомный A4 с повторяющимися заголовками**

Использовать `QuestPDF.Settings.License = LicenseType.Community` и существующий `Document.Create` pattern. Page: `PageSizes.A4.Landscape()`, header со сменой/режимом, content table с `Header()` повторяемым на страницах, footer с номером страницы. Группировать колонки веса, цикла и выпуска; recipe/cavities/places/расход показывать по спецификации. Для Operators внутри Entry формировать подстроки, а не размножать ManualGood/Defect на каждого человека.

`RenderBlank` через model factory принудительно очищает manual fields даже при заполненном draft. `RenderClosed` отклоняет non-Closed `DomainException`. Controller возвращает `File(bytes, "application/pdf", fileName)`; blank generatedAt берёт из серверных часов и не сохраняется как документ.

- [ ] **Step 5: Запустить GREEN**

Run: `dotnet test Wintime.Control.Tests.Unit --filter FullyQualifiedName~ShiftClosurePdfModelTests`
Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftClosurePdfApiTests|FullyQualifiedName~ShiftClosureCloseApiTests"`
Expected: PASS; оба документа генерируются без текущих справочников в closed path.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/Interfaces/IShiftClosurePdfService.cs Wintime.Control.Infrastructure/Reports/ShiftClosurePdfService.cs Wintime.Control.API/Controllers/ShiftClosuresController.cs Wintime.Control.API/Program.cs Wintime.Control.Tests.Unit/Reports/ShiftClosurePdfModelTests.cs Wintime.Control.Tests.Integration/ShiftClosures/ShiftClosurePdfApiTests.cs
git commit -m "feat(closures): печатать бланк и закрытый рапорт (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 13: ПК — вкладка «Сменный рапорт» и ручной редактор

**Files:**

- Create: `Wintime-Control-Frontend/src/api/shiftClosures.js`
- Create: `Wintime-Control-Frontend/src/composables/useShiftClosureEditor.js`
- Create: `Wintime-Control-Frontend/src/components/shift-closures/ShiftClosureTable.vue`
- Modify: `Wintime-Control-Frontend/src/views/planning/ShiftWorkPlanView.vue`
- Test: `Wintime-Control-Frontend/src/api/__tests__/shiftClosures.spec.js`
- Test: `Wintime-Control-Frontend/src/composables/__tests__/useShiftClosureEditor.spec.js`
- Test: `Wintime-Control-Frontend/src/components/shift-closures/__tests__/ShiftClosureTable.spec.js`
- Test: `Wintime-Control-Frontend/src/views/planning/__tests__/ShiftWorkPlanView.spec.js`

**Interfaces:**

- Consumes: GET/PUT/close API Tasks 7–9; server `CanEditOperators` не дублируется в новом closure DTO — возможность ручного редактирования определяется `state !== 'Closed'` и ролью маршрута.
- Produces: `useShiftClosureEditor(itemId)` с `report`, `loading`, `saving`, `fieldErrors`, `isDirty`, `load({preserveDirty})`, `updateManual`, `save`, `close({confirmEarlyClose})`.
- Produces: таблица со всеми колонками спецификации и grouped weight/cycle/output headers.

- [ ] **Step 1: Написать failing Vitest на разрешение ShiftPlanItem, таблицу и серверные ошибки**

Добавить test: выбранные дата и `shift.number` сопоставляются с `ShiftPlanItem.Position` через `shiftPlansApi.getRange(date,date)`; closure загружается только по материализованному `item.id`. Для virtual item (`id=null`) или item без `ShiftLeadId` вкладка показывает объяснение и не вызывает closure API; после self-assign/материализации повторное разрешение включает рапорт.

Проверить: read-only automatic/план/остаток/recipe/cavities; editable шесть manual fields; fallback badge; grouped columns; Operators/доли; пустые Warehouse/Signature; field error сопоставляется `taskId+field` конкретному `el-input-number`; save отправляет только manual fields; Closed отключает редактирование и close.

```js
it('maps_server_field_error_to_the_matching_task_input', async () => {
  api.saveDraft.mockRejectedValue({ response: { data: {
    code: 'required', errors: [{ taskId: 'task-1', field: 'actualPartWeightGrams', code: 'required' }]
  } } })
  const wrapper = mountTable({ entries: [entry({ shiftTaskId: 'task-1', automaticOutputQuantity: 2 })] })
  await wrapper.find('[data-action="save"]').trigger('click')
  await flushPromises()
  expect(wrapper.get('[data-error="task-1:actualPartWeightGrams"]').text()).toContain('обязатель')
})
```

- [ ] **Step 2: Написать failing tests dirty-state и early close**

`load({preserveDirty:true})` обязан обновлять вычисляемые поля, сохраняя локальные manual values для dirty TaskId; фоновый `load` без явного разрешения пропускается при `isDirty`. `save` и явное изменение интервалов разрешают refresh; после save ручные значения уже persisted.

При `now < EndAtUtc` close открывает предупреждение; первый вызов API без confirm при ответе `earlyCloseConfirmationRequired` показывает dialog, повтор передаёт `{confirmEarlyClose:true}`. Без подтверждения запрос закрытия не повторяется. Если перед close есть несохранённые изменения, composable сначала выполняет PUT, затем повторно загружает вычисляемые значения и только после успеха вызывает close; ошибка сохранения отменяет close и сохраняет ввод.

- [ ] **Step 3: Запустить RED**

Run: `cd Wintime-Control-Frontend; npm test -- src/api/__tests__/shiftClosures.spec.js src/composables/__tests__/useShiftClosureEditor.spec.js src/components/shift-closures/__tests__/ShiftClosureTable.spec.js src/views/planning/__tests__/ShiftWorkPlanView.spec.js`
Expected: FAIL — модули/вкладка отсутствуют.

- [ ] **Step 4: Реализовать API module, composable и таблицу**

`api/shiftClosures.js` экспортирует `getByItem`, `getPending`, `saveDraft`, `close`, `correct`, `getCorrections`, `getBlankPdf`, `getClosedPdf`; последние два используют `responseType:'blob'`. В composable хранить `localManualByTask` отдельно от server report; merge по TaskId не перезаписывает dirty input. После 409 показать сообщение и перезагрузить актуальное состояние, после field errors не очищать ввод.

Добавить вкладку рядом с назначением наладчиков. Сохранить существующие дату/номер смены для первой вкладки, а для closure дополнительно вызвать `shiftPlansApi.getRange` и сопоставить сохранённый item по `Position == shift.number`; никакие задания для closure не загружать через PZP-18. Вкладка без materialized `item.id`/старшего показывает необходимость назначить старшего и не вызывает closure API. Загрузка строк идёт только через `shiftClosures.getByItem`, не через существующий selector/API PZP-18. Использовать русские подписи спецификации, `el-input-number`, два знака для мест. Автоматические поля read-only.

- [ ] **Step 5: Запустить GREEN и регрессию существующего плана работ**

Run: `cd Wintime-Control-Frontend; npm test -- src/api/__tests__/shiftClosures.spec.js src/composables/__tests__/useShiftClosureEditor.spec.js src/components/shift-closures/__tests__/ShiftClosureTable.spec.js src/views/planning/__tests__/ShiftWorkPlanView.spec.js`
Run: `cd Wintime-Control-Frontend; npm test -- src/views/planning src/components/planning`
Expected: PASS; существующая вкладка назначения наладчиков не изменила API.

- [ ] **Step 6: Commit**

```powershell
git add Wintime-Control-Frontend/src/api/shiftClosures.js Wintime-Control-Frontend/src/composables/useShiftClosureEditor.js Wintime-Control-Frontend/src/api/__tests__/shiftClosures.spec.js src/composables/__tests__/useShiftClosureEditor.spec.js Wintime-Control-Frontend/src/components/shift-closures Wintime-Control-Frontend/src/views/planning/ShiftWorkPlanView.vue Wintime-Control-Frontend/src/views/planning/__tests__/ShiftWorkPlanView.spec.js
git commit -m "feat(closure-ui): добавить сменный рапорт на ПК (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 14: Мобильный `/shift-lead` — карточки рапорта и «Требует закрытия»

**Files:**

- Create: `Wintime-Control-Frontend/src/views/shift-lead/ShiftClosureView.vue`
- Create: `Wintime-Control-Frontend/src/components/shift-closures/ShiftClosureCards.vue`
- Modify: `Wintime-Control-Frontend/src/views/shift-lead/ShiftLeadHomeView.vue`
- Modify: `Wintime-Control-Frontend/src/router/index.js`
- Test: `Wintime-Control-Frontend/src/components/shift-closures/__tests__/ShiftClosureCards.spec.js`
- Test: `Wintime-Control-Frontend/src/views/shift-lead/__tests__/ShiftClosureView.spec.js`
- Test: `Wintime-Control-Frontend/src/views/shift-lead/__tests__/ShiftLeadHomeView.spec.js`
- Test: `Wintime-Control-Frontend/src/router/__tests__/shiftLeadRoute.spec.js`

**Interfaces:**

- Consumes: `useShiftClosureEditor`, `getPending`; никакого общего `tasks.js`, personnel или arbitrary history API.
- Produces: route `/shift-lead/items/:itemId/report` и кнопка «Сменный рапорт».

- [ ] **Step 1: Написать failing Vitest на pending и карточки**

Проверить: отдельный блок «Требует закрытия» отображается одновременно с nearest; каждая pending ссылка открывает собственный item; пустой pending скрыт. Карточка содержит те же данные/проверки, что ПК, fallback, Operators/доли и серверные field errors; ручной ввод сохраняется пакетно.

После `EndAtUtc` nearest обновляется по существующему таймеру, а pending остаётся доступным. История/pending незакрытая допускает ручной ввод, Closed read-only. Polling composable при dirty не заменяет manual values; явное сохранение обновляет computed.

Проверить маршруты: ShiftLead попадает в свой route family; Manager/Admin могут открыть выбранную смену; Operator/Adjuster/Observer запрещены; mock axios не содержит вызовов `/api/tasks`, `/api/personnel`, общей истории ТПА.

- [ ] **Step 2: Запустить RED**

Run: `cd Wintime-Control-Frontend; npm test -- src/components/shift-closures/__tests__/ShiftClosureCards.spec.js src/views/shift-lead src/router/__tests__/shiftLeadRoute.spec.js`
Expected: FAIL — view/route/pending отсутствуют.

- [ ] **Step 3: Реализовать карточки, view и route**

В `ShiftClosureView` использовать общий composable и `ShiftClosureCards`, не дублировать правила валидации. В Home добавить кнопку на nearest closure и отдельную секцию pending из `getPending`; не заменять nearest на pending. Добавить lazy route в существующий ShiftLead layout и навигацию назад к смене. Для Manager/Admin тот же component допускается из плана, но права определяет сервер.

Существующий 60-second polling не должен вызывать `load`, если `isDirty`; после save/interval mutation выполнить явный `load({preserveDirty:true})`. Closed скрывает save/close и открывает read-only просмотр.

- [ ] **Step 4: Запустить GREEN и route regression**

Run: `cd Wintime-Control-Frontend; npm test -- src/components/shift-closures/__tests__/ShiftClosureCards.spec.js src/views/shift-lead src/router`
Expected: PASS; старые nearest/history таймеры и редиректы не сломаны.

- [ ] **Step 5: Commit**

```powershell
git add Wintime-Control-Frontend/src/views/shift-lead Wintime-Control-Frontend/src/components/shift-closures Wintime-Control-Frontend/src/router
git commit -m "feat(shift-lead-ui): добавить рапорт и pending на мобильном (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 15: Временная шкала ТПА и редактирование фактических интервалов

**Files:**

- Create: `Wintime-Control-Frontend/src/components/shift-closures/ShiftClosureTimeline.vue`
- Create: `Wintime-Control-Frontend/src/components/shift-staffing/OperatorIntervalDialog.vue`
- Modify: `Wintime-Control-Frontend/src/api/shiftStaffing.js`
- Modify: `Wintime-Control-Frontend/src/views/shift-lead/ShiftStaffingItemView.vue`
- Modify: `Wintime-Control-Frontend/src/stores/shiftStaffing.js`
- Test: `Wintime-Control-Frontend/src/components/shift-closures/__tests__/ShiftClosureTimeline.spec.js`
- Test: `Wintime-Control-Frontend/src/components/shift-staffing/__tests__/OperatorIntervalDialog.spec.js`
- Test: `Wintime-Control-Frontend/src/views/shift-lead/__tests__/ShiftStaffingItemView.spec.js`
- Test: `Wintime-Control-Frontend/src/api/__tests__/shiftStaffing.spec.js`
- Modify: `Wintime-Control-Frontend/src/stores/__tests__/shiftStaffing.spec.js`

**Interfaces:**

- Consumes: `report.Timelines`, staffing assignments и mutations Task 2.
- Produces: `assignOperator({immId,operatorId,assignedAt,unassignedAt})`, `unassignOperator(id,{unassignedAt})`, `updateOperatorInterval(id,{assignedAt,unassignedAt})`.
- Produces: действие «Назначить» на сироте с предзаполненными границами.

- [ ] **Step 1: Написать failing Vitest на merged intervals и сироту**

Проверить: timeline выводит каждый merged interval с границами, списком Operators и automatic quantity; Q=0 без Operators не подсвечен ошибкой, Q>0 без Operators выделен и содержит «Назначить». Действие передаёт `immId`, `interval.StartUtc/EndUtc` в форму. Slot не показывать как рабочее место.

Форма назначения/снятия/PUT передаёт выбранные фактические границы; пустые поля POST дают серверные умолчания; unassign по умолчанию отправляет `{}`. Время input/output — пояс браузера через существующие dayjs helpers, API получает ISO UTC. При 409 форма сообщает и перезагружает item/report. Closed скрывает mutations; ended unclosed показывает их.

- [ ] **Step 2: Запустить RED**

Run: `cd Wintime-Control-Frontend; npm test -- src/components/shift-closures/__tests__/ShiftClosureTimeline.spec.js src/components/shift-staffing/__tests__/OperatorIntervalDialog.spec.js src/views/shift-lead/__tests__/ShiftStaffingItemView.spec.js src/api/__tests__/shiftStaffing.spec.js src/stores/__tests__/shiftStaffing.spec.js`
Expected: FAIL — timeline/dialog/новые mutations отсутствуют.

- [ ] **Step 3: Реализовать timeline и формы без дублирования Slot-логики**

`ShiftClosureTimeline` получает готовые merged intervals от сервера и не объединяет их повторно. В карточку ТПА добавить timeline и кнопки редактирования assignment. `OperatorIntervalDialog` использует один режим create/unassign/edit; submit вызывает соответствующий API. После успешной mutation перезагрузить staffing item и closure report с сохранением dirty manual values.

Не закрывать интервалы локальным timer и не вычислять Slot на клиенте. При 400 `shiftClosed` заблокировать форму и перезагрузить; при 409 показать изменённую занятость и актуальные интервалы.

- [ ] **Step 4: Запустить GREEN и staffing regression**

Run: `cd Wintime-Control-Frontend; npm test -- src/components/shift-closures/__tests__/ShiftClosureTimeline.spec.js src/components/shift-staffing src/views/shift-lead src/stores`
Expected: PASS; назначение без задания, два литейщика и повторное назначение сохраняются.

- [ ] **Step 5: Commit**

```powershell
git add Wintime-Control-Frontend/src/components/shift-closures/ShiftClosureTimeline.vue Wintime-Control-Frontend/src/components/shift-closures/__tests__/ShiftClosureTimeline.spec.js Wintime-Control-Frontend/src/components/shift-staffing Wintime-Control-Frontend/src/api/shiftStaffing.js Wintime-Control-Frontend/src/views/shift-lead/ShiftStaffingItemView.vue Wintime-Control-Frontend/src/views/shift-lead/__tests__/ShiftStaffingItemView.spec.js Wintime-Control-Frontend/src/stores
git commit -m "feat(staffing-ui): редактировать интервалы на временной шкале (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 16: UI исправлений, истории и двух PDF-режимов

**Files:**

- Create: `Wintime-Control-Frontend/src/components/shift-closures/ShiftClosureCorrectionsDialog.vue`
- Create: `Wintime-Control-Frontend/src/components/shift-closures/ShiftClosurePdfActions.vue`
- Create: `Wintime-Control-Frontend/src/utils/downloadBlob.js`
- Modify: `Wintime-Control-Frontend/src/views/planning/ShiftWorkPlanView.vue`
- Modify: `Wintime-Control-Frontend/src/views/shift-lead/ShiftClosureView.vue`
- Test: `Wintime-Control-Frontend/src/components/shift-closures/__tests__/ShiftClosureCorrectionsDialog.spec.js`
- Test: `Wintime-Control-Frontend/src/components/shift-closures/__tests__/ShiftClosurePdfActions.spec.js`

**Interfaces:**

- Consumes: corrections/PDF API Tasks 10 и 12 и `usePermissions().canAccess(['Manager', 'Admin'])`; не использовать exact-Manager getter как ManagerOrAdmin.
- Produces: «Исправить показатели» с Reason и историей; кнопки «Печать бланка»/«Печать рапорта».

- [ ] **Step 1: Написать failing Vitest на права, причину и печать**

Проверить: Closed read-only по умолчанию; Manager/Admin открывает correction dialog, ShiftLead не видит action, но видит историю собственной смены. Пустая/blank Reason блокирует submit; payload содержит Reason и только шесть manual fields; успешный PATCH обновляет report и history. Field errors отображаются у строк, при 409 данные перезагружаются.

До close доступен только blank, после — только closed PDF. Оба вызывают правильный endpoint с blob response, создают object URL и `.pdf` download; API error не создаёт пустой файл. Проверить оба действия в ПК и мобильном представлении через общий component.

- [ ] **Step 2: Запустить RED**

Run: `cd Wintime-Control-Frontend; npm test -- src/components/shift-closures/__tests__/ShiftClosureCorrectionsDialog.spec.js src/components/shift-closures/__tests__/ShiftClosurePdfActions.spec.js`
Expected: FAIL — components отсутствуют.

- [ ] **Step 3: Реализовать общие dialog/actions и download helper**

`downloadBlob` принимает `Blob` и filename, создаёт ссылку и освобождает object URL через `URL.revokeObjectURL`; не связывать closure с analytics reports store или POST `/reports/export/pdf`. Dialog загружает history при открытии, показывает ChangedByName/ChangedAt/Reason/список old→new. После correction не пересчитывать automatic локально.

PDF actions выбирают endpoint по `state`; не предлагать closed PDF для dynamic Draft и не сохранять blank как документ. Ранние/403 ошибки показывать существующим notification pattern.

- [ ] **Step 4: Запустить GREEN**

Run: `cd Wintime-Control-Frontend; npm test -- src/components/shift-closures src/views/planning/__tests__/ShiftWorkPlanView.spec.js src/views/shift-lead/__tests__/ShiftClosureView.spec.js`
Expected: PASS; ShiftLead не получил action исправления, но получил history/PDF собственной смены.

- [ ] **Step 5: Commit**

```powershell
git add Wintime-Control-Frontend/src/components/shift-closures Wintime-Control-Frontend/src/utils Wintime-Control-Frontend/src/views/planning/ShiftWorkPlanView.vue Wintime-Control-Frontend/src/views/shift-lead/ShiftClosureView.vue
git commit -m "feat(closure-ui): добавить исправления и печать (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 17: ПК «План смен» — закрытие, сироты и отсутствие старшего

**Files:**

- Modify: `Wintime-Control-Frontend/src/views/planning/ShiftPlanView.vue`
- Modify: `Wintime-Control-Frontend/src/components/planning/ShiftPlanItemDialog.vue`
- Test: `Wintime-Control-Frontend/src/views/planning/__tests__/ShiftPlanView.spec.js`
- Test: `Wintime-Control-Frontend/src/components/planning/__tests__/ShiftPlanItemDialog.spec.js`

**Interfaces:**

- Consumes: `closureStatus`, `hasOrphanOutput`, `hasMissingLeadWarning`, `canEditLead`, `canEditOperators` Task 11.
- Produces: видимые индикаторы «Закрыта», «Не закрыта», «Есть выпуск без литейщика» и предупреждение «Не назначен старший».

- [ ] **Step 1: Написать failing Vitest на индикаторы и self-assign**

Проверить: Closed — «Закрыта»; ended unclosed — «Не закрыта»; Draft future/current без лишнего pending badge; `hasOrphanOutput` показывает предупреждение независимо от closure status; virtual без старшего не получает несуществующий closure badge.

Current/nearest `hasMissingLeadWarning` отображает предупреждение. Для текущей lead-less virtual позиции Manager видит действие «Назначить себя старшим» и PUT передаёт собственный Id; Admin/другой пользователь не видит self-action; после успеха item становится saved и доступна ссылка на рапорт. Closed/поздняя future сохраняют запрет mutations.

- [ ] **Step 2: Запустить RED**

Run: `cd Wintime-Control-Frontend; npm test -- src/views/planning/__tests__/ShiftPlanView.spec.js src/components/planning/__tests__/ShiftPlanItemDialog.spec.js`
Expected: FAIL — badges/self-action отсутствуют.

- [ ] **Step 3: Реализовать отображение только по server flags**

Добавить компактные badges в ячейку/карточку позиции и ссылку на closure для materialized item с старшим. Не вычислять nearest, closure state или orphan на клиенте. Self-action показывать только при server `canEditLead`, отсутствии ShiftLead и роли Manager; после PUT обновить план из ответа и открыть доступный рапорт. Не менять существующую разную сетку колонок по датам.

- [ ] **Step 4: Запустить GREEN и полный planning regression**

Run: `cd Wintime-Control-Frontend; npm test -- src/views/planning src/components/planning`
Expected: PASS; разное число колонок, прошлый/current/future styling и старые lead actions не сломаны.

- [ ] **Step 5: Commit**

```powershell
git add Wintime-Control-Frontend/src/views/planning/ShiftPlanView.vue Wintime-Control-Frontend/src/views/planning/__tests__/ShiftPlanView.spec.js Wintime-Control-Frontend/src/components/planning/ShiftPlanItemDialog.vue Wintime-Control-Frontend/src/components/planning/__tests__/ShiftPlanItemDialog.spec.js
git commit -m "feat(shift-plan-ui): показать закрытие и отсутствие старшего (PZP-31)" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 18: ADR-0030, CLAUDE.md, VERSION и полная проверка

**Files:**

- Create: `docs/adr/0030-shift-closure-report.md`
- Modify: `docs/adr/README.md`
- Modify: `CLAUDE.md`
- Modify: `VERSION`
- Verify: все изменённые исходники и tests Tasks 1–17

**Interfaces:**

- Produces: зафиксированные инварианты фактических интервалов, динамического черновика, закрытия, снимка, распределения и corrections.
- Produces: `VERSION` ровно `1.34.0` при исходной `1.33.1`; если ветка получила новый master, пересчитать минор от актуальной версии вместо blind overwrite.

- [ ] **Step 1: Написать ADR-0030 по MADR, не редактируя принятый ADR-0027**

Поля `Status: Accepted`, `Date: 2026-11-18`, `Related: ADR-0027, ADR-0016, ADR-0029`. Зафиксировать:

- фактические границы + отдельный Created/Updated audit, temporal exclusion constraints и `23P01 → 409`;
- динамическую read model вместо хранения вычисляемого черновика; pending отдельно от nearest;
- единый lock/transaction close, `FactEnd`, early confirmation и полный план при early;
- неизменяемый typed snapshot и Shares, EndTime attribution, объединение до округления, Slot на начало merged interval;
- raw Offline fallback, orphan block, отсутствие сверки ручного/автоматического выпуска;
- typed corrections + JSON audit без reopen; два PDF режима.

Отвергнутые альтернативы: изменение итогов `ShiftTask`; background worker; сохранение вычисляемых draft-колонок; независимое округление elementary intervals; пересчёт Closed при чтении; вывод Offline из отсутствия циклов/effective gaps; reopen вместо correction audit.

Добавить ссылку в `docs/adr/README.md` после ADR-0029. ADR-0027 не переписывать по существу.

- [ ] **Step 2: Обновить CLAUDE.md правилом-итогом**

В PZP-32 заменить устаревшие правила: historical до closure не read-only; AssignedAt/UnassignedAt — фактические границы, default start/end, отдельный audit, temporal exclusions вместо partial unique; nearest не управляет pending. Добавить компактный подраздел PZP-31: dynamic draft, task selection, FactEnd/early, automatic metrics, merged intervals/split, orphan/Offline, required fields, snapshot/corrections/PDF, lock и запрет менять ShiftTask. Сослаться на ADR-0030, не дублировать всю спецификацию.

- [ ] **Step 3: Выполнить self-review реализации против спецификации**

Проверить по коду и tests каждый UC-1…UC-14, все поля модели и API routes. Отдельно убедиться:

- нет переноса брака/изменения итогов ShiftTask, PZP-36/PZP-37, worker или распределения manual output;
- Closed GET/PDF не обращаются к актуальным справочникам/циклам/assignments;
- Shares включают нули и сумма долей+сирот равна automatic;
- blank очищает даже сохранённые manual fields; correction — единственный путь изменить Closed manual values;
- ShiftLead не вызывает общие API и не корректирует Closed; Manager self-assign ограничен текущей lead-less сменой;
- все DateTime UTC, время UI/PDF в правильных поясах, нет N+1 closure GET в shift-plan indicators.

Run: `git diff --check`
Expected: нет whitespace errors.

- [ ] **Step 4: Запустить полную backend verification**

Run: `dotnet test Wintime.Control.sln --configuration Release`
Expected: все unit и integration tests PASS, включая Testcontainers/PostgreSQL 16; failed=0. Если Docker недоступен, не объявлять integration проверку выполненной: зафиксировать ошибку и повторить после восстановления Docker.

Run: `dotnet ef migrations has-pending-model-changes --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`
Expected: pending model changes отсутствуют.

- [ ] **Step 5: Запустить полную frontend verification и production build**

Run: `cd Wintime-Control-Frontend; npm test`
Expected: все Vitest files/tests PASS.

Run: `cd Wintime-Control-Frontend; npm run build`
Expected: Vite build успешен; lazy routes и closure components включены без unresolved imports.

- [ ] **Step 6: Поднять VERSION только после зелёной проверки**

Если текущий файл всё ещё `1.33.1`, записать:

```text
1.34.0
```

Если base изменился, поднять минор от актуального `X.Y.Z` до `X.(Y+1).0`, не понижать версию. Повторно выполнить `git diff --check` и убедиться, что изменён только ожидаемый release файл сверх Tasks 1–18.

- [ ] **Step 7: Commit документации и версии**

```powershell
git add docs/adr/0030-shift-closure-report.md docs/adr/README.md CLAUDE.md VERSION
git commit -m "docs(pzp-31): зафиксировать архитектуру сменного рапорта" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

После этого не создавать PR без отдельного указания пользователя. Ветка готова к review; в итоговом отчёте перечислить выполненные команды и фактические результаты, не объявлять пропущенные проверки успешными.
