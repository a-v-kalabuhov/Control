# Shift Default Lead Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Реализовать цепочку `Explicit → Default → первый активный Manager` для старшего смены во всех API и UI.

**Architecture:** `ShiftDefaultLead` хранит default по позиции графика. Чистый `EffectiveShiftLeadResolver` и один fallback provider используются планом, staffing и closures; виртуальные смены лениво материализуются, при закрытии effective lead замораживается в `ShiftPlanItem.ShiftLeadId`.

**Tech Stack:** ASP.NET Core 9, EF Core 9, PostgreSQL 16, xUnit/Testcontainers, Vue 3, Vitest.

**Spec:** `docs/superpowers/specs/2026-11-25-shift-default-lead-design.md`.

## Global Constraints

- Работа в текущей ветке `feature/shift-lead-planning-ui`, без worktree.
- Каждая задача: failing test → реализация → focused regression → коммит с `Co-Authored-By: Claude Code <noreply@anthropic.com>`.
- Роль только `User.Role`; Identity-роли не использовать.
- Все DateTime UTC; `User.CreatedAt` только серверный.
- Изменения default, графика, staffing, close и активности/роли Manager сериализуются через `ShiftScheduleLock`.
- Не добавлять default в общий `GET /api/shifts` или payload полного редактирования графика.
- Не создавать background worker и не материализовать календарь массово.
- Closed неизменяем при последующих изменениях default или состава менеджеров.
- Не ослаблять существующие тесты: сценарии без старшего должны явно исключать активных Manager.

## Общие интерфейсы

```csharp
public enum ShiftLeadSource { Explicit, Default, ManagerFallback }
public sealed record EffectiveShiftLead(string? Id, ShiftLeadSource? Source);
public sealed record ShiftLeadFallbackSnapshot(
    IReadOnlyDictionary<int, string> DefaultLeadIdsByPosition,
    string? FirstActiveManagerId,
    IReadOnlyDictionary<string, User> Users);
public interface IShiftLeadFallbackProvider
{
    Task<ShiftLeadFallbackSnapshot> GetAsync(CancellationToken ct);
}
```

`EffectiveShiftLeadResolver.Resolve(ShiftPlanItem item, ShiftLeadFallbackSnapshot fallback)` — чистая политика без БД. Provider загружает defaults, пользователей и первого Manager один раз на запрос.

---

### Task 1: User.CreatedAt

**Files:**
- Modify: `Wintime.Control.Core/Entities/User.cs`
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs`
- Create: migration `AddUserCreatedAt` + Designer/snapshot
- Modify: `Wintime.Control.Tests.Integration/Auth/PersonnelManagementTests.cs`
- Create: `Wintime.Control.Tests.Integration/Migrations/UserCreatedAtMigrationTests.cs`

- [ ] Написать failing tests: API create заполняет UTC CreatedAt; клиентский createdAt игнорируется; bootstrap/seed покрываются инициализатором; migration backfill/index/Down.
- [ ] Добавить `public DateTime CreatedAt { get; set; } = DateTime.UtcNow;`, EF `timestamptz` и index `(IsActive, Role, CreatedAt, Id)`.
- [ ] `dotnet ef migrations add AddUserCreatedAt --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`; в migration `defaultValueSql: "now()"`.
- [ ] Не менять CreatePersonnelRequestDto/PersonnelDto.
- [ ] `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~UserCreatedAt|FullyQualifiedName~PersonnelManagementTests"` — 0 failed.
- [ ] Commit `feat(users): добавить серверную дату создания пользователя`.

### Task 2: Defaults, resolver, fallback provider и settings API

**Files:**
- Create: `Core/Entities/ShiftDefaultLead.cs`
- Create: `Core/Policies/EffectiveShiftLeadResolver.cs`
- Create: `Core/Interfaces/IShiftLeadFallbackProvider.cs`, `IShiftDefaultLeadService.cs`
- Create: `Core/DTOs/Shifts/ShiftDefaultLeadDtos.cs`
- Create: `Infrastructure/Services/ShiftLeadFallbackProvider.cs`, `ShiftDefaultLeadService.cs`
- Create: migration `AddShiftDefaultLeads`
- Modify: `ControlDbContext.cs`, `ShiftsController.cs`, `ShiftService.cs`, `API/Program.cs`
- Create: `Unit/Policies/EffectiveShiftLeadResolverTests.cs`
- Create: `Integration/Planning/ShiftDefaultLeadsApiTests.cs`
- Create: `Integration/Migrations/ShiftDefaultLeadMigrationTests.cs`

- [ ] Failing unit tests: Explicit > Default > ManagerFallback > none.
- [ ] Failing integration: GET/PUT round-trip, fallbackManager CreatedAt/Id ordering, candidates/roles, invalid 400, stale positions 409, atomicity, один пользователь на нескольких позициях.
- [ ] ShiftDefaultLead: Position PK >=1; required ShiftLeadId FK Users Restrict; no Shift FK; index ShiftLeadId.
- [ ] GET/PUT `/api/shifts/default-leads` ManagerOrAdmin. Response items + fallbackManager(id/fullName/employeeId/createdAt).
- [ ] PUT в транзакции под ShiftScheduleLock: полный набор позиций, повторная проверка после lock, active ShiftLead/Manager только; замена разрешена; очистка переходит к Manager; без Manager нельзя оставить assignments/closure без lead.
- [ ] Синхронизация с `ShiftService.SaveShiftsAsync` в той же транзакции: defaults 1..N сохранить, >N удалить, rollback вместе с графиком/планом.
- [ ] Зарегистрировать DI и создать миграцию обязательно с project/startup-project.
- [ ] `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~EffectiveShiftLeadResolverTests"` и `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftDefaultLead"` — 0 failed.
- [ ] Commit `feat(shifts): добавить старших по умолчанию и fallback менеджера`.

### Task 3: Effective lead в плане и индикаторах

**Files:**
- Modify: `Core/DTOs/ShiftPlans/ShiftPlanDtos.cs`
- Modify: `Core/Policies/NearestShiftSelector.cs`
- Modify: `Infrastructure/Services/ShiftPlanService.cs`, `ShiftClosureIndicatorService.cs`
- Modify: `Unit/Policies/NearestShiftSelectorTests.cs`
- Modify: `Integration/Planning/ShiftPlanApiTests.cs`, `ShiftPlanClosureIndicatorTests.cs`, `ShiftPlanScheduleSynchronizationTests.cs`

- [ ] Failing tests virtual/saved: effective Id/Name, source Explicit/Default/ManagerFallback, warning только none, Draft indicator при fallback, clear explicit с fallback сохраняет данные, без fallback прежние guards.
- [ ] В старых no-lead fixtures явно исключить активных Manager; не ослаблять assertions.
- [ ] Добавить `ShiftLeadSource? ShiftLeadSource` в ShiftPlanItemDto.
- [ ] NearestShiftSelector.ForLead/IsNearestForManager принимает effective-ID selector; ShiftPlanService Get/Set/Nearest/projection использует provider/resolver.
- [ ] Day cleanup учитывает effective lead + assignments + closure. Indicator использует resolver.
- [ ] До Task 4 CanEditOperators виртуальных остаётся false.
- [ ] `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~NearestShiftSelectorTests"` и `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftPlan"` — 0 failed.
- [ ] Commit `feat(shift-plan): показывать эффективного старшего смены`.

### Task 4: Общий materializer и materialize API

**Files:**
- Create: `Core/Interfaces/IShiftPlanMaterializer.cs`
- Create: `Core/Policies/ShiftPlanCandidateBuilder.cs`
- Create: `Infrastructure/Services/ShiftPlanMaterializer.cs`
- Create: `Integration/Planning/ShiftPlanMaterializationApiTests.cs`
- Modify: `Core/Interfaces/IShiftPlanService.cs`, `Infrastructure/Services/ShiftPlanService.cs`, `API/Controllers/ShiftPlanController.cs`, `API/Program.cs`

- [ ] Failing tests: virtual default/fallback → stable itemId и полный день; повтор идемпотентен; fallback не копируется в ShiftLeadId; нет closure/assignments; no lead 400; missing 404; role 403; race согласованность/409.
- [ ] Перенести BuildVirtualDay и nearest candidates в ShiftPlanCandidateBuilder без изменения границ.
- [ ] Materializer: transaction + ShiftScheduleLock, re-read schedule/default/Manager, saved или virtual day, effective lead check, idempotent save. Shared tracked-load использовать SetLeadAsync.
- [ ] POST `/api/shift-plan/days/{date}/items/{position}/materialize` возвращает ShiftPlanDayDto.
- [ ] После реализации CanEditOperators virtual по effective lead и nearest/ended.
- [ ] `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftPlan"` — 0 failed.
- [ ] Commit `feat(shift-plan): материализовать виртуальные fallback-смены`.

### Task 5: Staffing/nearest/history через resolver

**Files:**
- Modify: `Infrastructure/Services/ShiftStaffingService.cs`
- Modify: `Integration/ShiftStaffing/*ApiTests.cs`
- Modify: `Unit/Policies/NearestShiftSelectorTests.cs`

- [ ] Failing tests: virtual default current/earliest future materialize; ManagerFallback не даёт ShiftLead чужую смену; history effective; замена default передаёт доступ и сохраняет assignments; manager nearest effective; stale lead 403; history не материализует прошлое массово.
- [ ] GetNearestAsync использует ShiftPlanCandidateBuilder + resolver + materializer; после lock повторный resolve, смена выбора 409.
- [ ] Заменить прямые проверки ShiftLeadId во всех read/mutations/CanEdit paths и DTO name projection.
- [ ] History фильтрует materialized items после загрузки по effective lead.
- [ ] `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~NearestShiftSelectorTests"` и `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftStaffing"` — 0 failed.
- [ ] Commit `feat(shift-staffing): разрешать доступ через default и fallback`.

### Task 6: Closures через resolver и freeze при close

**Files:**
- Modify: `Infrastructure/Services/ShiftClosureService.cs`, `ShiftClosureReadModelLoader.cs`
- Modify: `Integration/ShiftClosures/ShiftClosureCloseApiTests.cs`, `ShiftClosureReadApiTests.cs`, `ShiftClosureDraftApiTests.cs`, `ShiftClosurePdfApiTests.cs`

- [ ] Failing E2E: default и ManagerFallback → draft → pending → close; DTO effective Id/Name; pending ownership; close сохраняет effective ID; поздние изменения default/Manager не меняют Closed/PDF/history; validation failure не фиксирует lead.
- [ ] EnsureAccess после locks использует resolver; pending фильтрует ended nonclosed по effective lead, не прежним SQL ShiftLeadId != null.
- [ ] Close после всех проверок перед SaveChanges: `item.ShiftLeadId ??= readModel.ShiftLeadId;` в существующей транзакции.
- [ ] Draft/corrections/PDF используют resolver; Closed становится Explicit через frozen ID.
- [ ] `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ShiftClosure"` — 0 failed.
- [ ] Commit `feat(shift-closures): фиксировать fallback старшего при закрытии`.

### Task 7: Сериализация изменений Manager в персонале

**Files:**
- Create: `Core/Interfaces/IPersonnelService.cs`, `Infrastructure/Services/PersonnelService.cs`
- Create: `Integration/Planning/ShiftManagerFallbackTests.cs`
- Modify: `API/Controllers/PersonnelController.cs`, `API/Program.cs`, `Integration/Auth/PersonnelManagementTests.cs`

- [ ] Сначала зафиксировать существующие BadRequest/Forbid/NoContent/response shapes тестами.
- [ ] Failing tests: archive первого Manager → следующий; restore более раннего → возврат; Manager→other исключает; other→Manager возвращает; поздний CreatedAt не меняет fallback; нет Manager → no lead; race с materialize/staffing/close сериализован.
- [ ] Перенести update workflow в Infrastructure service, т.к. ShiftScheduleLock internal. Для old/new Manager или IsActive: transaction + lock + re-read + повторные guards + save + commit.
- [ ] Сохранить последнего admin, password/login rules, response contracts; refresh revocation только после commit. Не очищать defaults и не материализовать дни.
- [ ] `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~PersonnelManagementTests|FullyQualifiedName~ShiftManagerFallbackTests"` и close suite — 0 failed.
- [ ] Commit `fix(personnel): сериализовать изменения резервного менеджера`.

### Task 8: Панель default-настроек в справочнике смен

**Files:**
- Create: `Frontend/src/components/shifts/ShiftDefaultLeadsDialog.vue`
- Create: `Frontend/src/components/shifts/__tests__/ShiftDefaultLeadsDialog.spec.js`
- Create: `Frontend/src/views/dictionary/__tests__/ShiftsDictionary.spec.js`
- Create: `Frontend/src/api/__tests__/shifts.spec.js`
- Modify: `Frontend/src/api/shifts.js`, `Frontend/src/views/dictionary/ShiftsDictionary.vue`

- [ ] Failing tests: GET/PUT `/shifts/default-leads`; полный payload с null; кнопка только Manager/Admin; Observer не вызывает APIs; временные колонки + default; один кандидат в нескольких строках; archived option; fallbackManager; 400 сохраняет ввод; 409 reload; Cancel без сохранения.
- [ ] Отдельный dialog/panel, не основной диалог редактирования графика. Кандидаты lazy через getLeadCandidates. После save использовать серверный response.
- [ ] Основной PUT `/shifts` не менять.
- [ ] `npm test -- src/api/__tests__/shifts.spec.js src/components/shifts/__tests__/ShiftDefaultLeadsDialog.spec.js src/views/dictionary/__tests__/ShiftsDictionary.spec.js` и `npm run build` из Wintime-Control-Frontend — PASS.
- [ ] Commit `feat(shifts-ui): добавить панель старших по умолчанию`.

### Task 9: Default/fallback в плане UI и materialize перед переходом

**Files:**
- Modify: `Frontend/src/api/shiftPlans.js`, `views/planning/ShiftPlanView.vue`, `components/planning/ShiftPlanItemDialog.vue`
- Modify: `views/planning/__tests__/ShiftPlanView.spec.js`, `ShiftWorkPlanView.spec.js`, `components/planning/__tests__/ShiftPlanItemDialog.spec.js`

- [ ] Failing tests: source badges; warning только none; fallback select имеет empty explicit value; save не делает fallback explicit; clear только Explicit; Manager self-assign поверх fallback; virtual open сначала materialize; ошибка не роутит; saved day reselect.
- [ ] Добавить materializeShift API. В диалоге selectedExplicitLeadId: Explicit→DTO id, Default/ManagerFallback→null. Пустой option показывает следующий fallback.
- [ ] Права только server-driven canEdit*, source не используется для permissions. Перед staffing/closure virtual: materialize → emit saved day → route по returned itemId.
- [ ] ShiftWorkPlanView продолжает требовать materialized id. Mobile Home/History/Closure/store не менять.
- [ ] Focused Vitest + shift-lead/store regression + `npm run build` — PASS.
- [ ] Commit `feat(shift-plan-ui): поддержать default и fallback смены`.

### Task 10: ADR/docs/VERSION и полный verification

**Files:**
- Create: `docs/adr/0032-shift-default-lead.md`, `docs/user-guide/shift-lead.md`
- Modify: `docs/adr/README.md`, `CLAUDE.md`, `docs/user-guide/manager.md`, spec status, `VERSION` 1.36.0→1.37.0

- [ ] ADR: default по Position, resolver, ManagerFallback CreatedAt/Id, backfill, lazy materialize, no worker, Closed freeze, personnel lock, отвергнутые альтернативы.
- [ ] CLAUDE/user guides/spec status обновить по фактической реализации.
- [ ] `dotnet restore`; `dotnet build Wintime.Control.sln --configuration Release --no-restore`; unit/integration Release — 0 failed.
- [ ] `npm test`; `npm run build`; `git diff --check` — PASS.
- [ ] Commit `docs: оформить ADR и выпуск 1.37.0`.

## Final Gate

Все UC-1…UC-15 покрыты; ни один доступ не считает ShiftPlanItem.ShiftLeadId единственным lead; virtual nearest стабилен; default replacement сохраняет данные; Manager changes не влияют на Closed; общий GET /api/shifts неизменен; все тесты/build зелёные.
