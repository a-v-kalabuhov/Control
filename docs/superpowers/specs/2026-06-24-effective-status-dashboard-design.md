# Эффективное состояние ТПА на дашборде

> Дата: 2026-06-24
> Статус: дизайн утверждён, готов к написанию плана реализации
> Ветка: `feature/downtime-ui`
> Связано: [[0006-effective-imm-state]] (ADR — модель состояния),
> предыдущий дизайн `2026-06-23-effective-imm-state-and-auto-downtime-design.md` (бэкенд),
> матрица сценариев `docs/details/Состояния_ТПА.xlsx`, [[feature_backlog]] (MUN-03, BL-17..BL-20)

## Проблема

Бэкенд уже определяет авто-простои и гейтит обработку циклов по «эффективному состоянию», но
**дашборд по-прежнему показывает сырой статус** ТПА (`mode` из MQTT: Auto/Manual/Alarm/Idle/Offline).
Из-за этого пользователь видит «Авто», когда станок гонит программу без выданного задания, и не
отличает наладку от производства. Чистая функция `ImmEffectiveStatus.Resolve` в ADR-0006 была
осознанно отложена — у неё не было потребителя. Теперь потребитель появился: дашборд и таймлайн
смены должны показывать эффективное состояние.

## Цель

Показать **эффективное состояние ТПА** (6 значений) везде на десктоп-дашборде: плитки, бейджи,
рамки карточек, фильтр, счётчики, метрика загрузки, и таймлайн смены в окне деталей ТПА.
Логику состояния держать одной чистой функцией в Core, вычислять **на лету** (без хранения в БД),
для истории — реконструировать наложением уже историзированных рядов.

Журнал простоев (MUN-03) — **следующая** фронт-задача, в этот объём не входит.

## Эффективное состояние — 6 значений

`Stopped` из исходной матрицы (7 значений) **убран как пользовательское состояние**: до истечения
порога простоя ТПА показывается как «Работа» (Production). `Stopped` остаётся лишь внутренней
стадией детектирования простоя на бэкенде, наружу не выходит. Сырой `Alarm` **растворяется**
(не отдельное состояние) — заметность аварии вернёт BL-19.

| Состояние | Подпись | Цвет (Tailwind / hex) | Смысл |
|---|---|---|---|
| `Production` | Работа | green-500 `#22c55e` | Полезная работа по заданию (вкл. кратковременный выход из Auto до порога) |
| `Setup` | Наладка | yellow-500 `#eab308` | Активное задание в статусе Setup |
| `Downtime` | Простой | red-500 `#ef4444` | Зафиксированный простой (порог пройден или открыт простой) |
| `Unplanned` | Работа без задания | purple-500 `#a855f7` | Auto без выданного задания — аномалия (BL-17) |
| `NoTask` | Без задания | blue-500 `#3b82f6` | Нет задания и не-Auto — станок свободен (норма) |
| `Offline` | Нет связи | gray-400 `#9ca3af` | Нет MQTT-сообщений и нет активного задания |

## Ключевые решения (зафиксированы при обсуждении)

| # | Решение | Выбор |
|---|---|---|
| 1 | Объём | Сначала эффективное состояние на дашборде (вариант A); журнал простоев MUN-03 — потом |
| 2 | Охват дашборда | «Drive everything»: бейдж, рамка, фильтр, счётчики, метрика загрузки — все на эффективном |
| 3 | Авария (`Alarm`) | Растворить полностью; баннер аварии — отдельная задача BL-19 |
| 4 | KPI-панель | Вариант B: 7 карточек (Всего/Работа/Наладка/Простой/Без задания/Нет связи/Загрузка) |
| 5 | Метрика «Текущая загрузка» | `(Production + Setup) / всего` — та же формула, но на эффективных состояниях |
| 6 | Где вычислять | Чистая функция в Core, вызов на бэкенде; на лету, **без хранения в БД** |
| 7 | Таймлайн истории | Реконструкция наложением рядов (сырой статус + интервалы заданий + простои) |
| 8 | Тесты фронта | Настроить Vitest впервые; тесты чистой JS-логики (без рендера компонентов) |

Обоснование «на лету» (согласуется с ADR-0006): все три входа уже историзированы порознь — сырой
режим в `ImmStatusHistory`, моменты переходов задания в метках `ShiftTask`, простои в `Event`.
Эффективное состояние на любой момент реконструируемо. Хранение потребовало бы записи при каждом
изменении любого из трёх входов + отложенного перехода по порогу — много точек, риск рассинхрона
(отвергнутая в ADR-0006 альтернатива).

## Архитектура

```
Core (чистые функции, тестируются по матрице xlsx)
  ├─ ImmEffectiveStatus.Resolve(rawMode, task, hasOpenDowntime, thresholdPassed) → string  (★ новое)
  └─ EffectiveStatusTimeline.Build(raw[], tasks[], downtimes[], from, to) → EffectiveSegment[]  (★ новое)

API
  ├─ ImmController.GetImmList   → ImmDto.EffectiveStatus (live: Resolve по кешу+порогу+простоям)
  ├─ ImmController.GetImmStatus → ImmStatusDto.EffectiveStatus (live, одиночный)
  └─ ImmController.GetImmEffectiveStatusHistory (★ новый endpoint, история через Build)

Frontend (десктоп-дашборд)
  ├─ constants/effectiveStatus.js  (★ единый источник палитры: label/bg/text/dot/border/hex)
  ├─ ImmStatusBadge.vue, ImmCard.vue            (эффективный статус)
  ├─ stores/dashboard.js                        (геттеры, фильтр, метрика загрузки)
  ├─ DashboardView.vue                          (KPI вариант B, фильтр 6 опций)
  ├─ ImmDetailModal.vue, ShiftTimeline.vue      (таймлайн на эффективных состояниях)
  └─ api/dashboard.js                           (getImmEffectiveStatusHistory)

Скрипт: Start-DevEnv.ps1 — добавить прогон Vitest в шаг «Тесты»
```

## Секция 1 — `ImmEffectiveStatus` (Core, чистая функция)

Новый класс `Wintime.Control.Core.Policies.ImmEffectiveStatus` рядом с `CycleProcessingPolicy`/
`DowntimeDecision`. Константы состояний — в `Core.Constants` (по образцу `ImmMode`/`ImmStatus`):

```csharp
public static class EffectiveStatus   // 6 значений
{
    public const string Production = "Production";
    public const string Setup      = "Setup";
    public const string Downtime   = "Downtime";
    public const string Unplanned  = "Unplanned";
    public const string NoTask     = "NoTask";
    public const string Offline    = "Offline";
}

public static class ImmEffectiveStatus
{
    public static string Resolve(string rawMode, ActiveTaskStatus task,
                                 bool hasOpenDowntime, bool thresholdPassed);
}
```

Нормализация `rawMode` через существующий `ImmMode.Normalize`. Порядок приоритетов:

| # | Условие | Результат |
|---|---|---|
| 1 | `task == Setup` | `Setup` (наладка доминирует над сигналом) |
| 2 | `task == InProgress` && `mode == Auto` | `Production` |
| 3 | `task == InProgress` && `mode != Auto` && (`hasOpenDowntime` \|\| `thresholdPassed`) | `Downtime` |
| 4 | `task == InProgress` && `mode != Auto` && иначе | `Production` (до порога) |
| 5 | `task == None` && `mode == Auto` | `Unplanned` |
| 6 | `task == None` && `mode == Offline` | `Offline` |
| 7 | `task == None` && иначе (Idle/Manual/Alarm) | `NoTask` |

Следствия: `Offline` как пользовательское состояние возникает только при `None`+нет связи; при
`InProgress`+Offline дольше порога — `Downtime` (согласуется с авто-простоями воркера). `Alarm`
растворяется (строки 3/4/7). Существующий `ActiveTaskStatusMap.From(TaskStatus?)` проецирует статус
задания в `ActiveTaskStatus`.

## Секция 2 — Live-контракт: `ImmController` + DTO

`ImmDto` и `ImmStatusDto` получают поле `EffectiveStatus` (string). Сырой `Status` остаётся
(пригодится BL-19 и для отладки).

`GetImmList`:
- в проекцию добавить статус активного задания (рядом с уже выбираемым `CurrentTaskId`):
  ```csharp
  ActiveTaskRawStatus = i.ShiftTasks
      .Where(t => t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Setup)
      .Select(t => (TaskStatus?)t.Status).FirstOrDefault()
  ```
  (внутреннее поле DTO; скрыть из JSON через `[JsonIgnore]` либо отдельной проекцией — решить в плане);
- один батч-запрос открытых простоев на весь список:
  ```csharp
  var openDowntimeImmIds = (await _context.Events
      .Where(e => e.EventType == EventType.Downtime && e.EndTime == null)
      .Select(e => e.ImmId).Distinct().ToListAsync()).ToHashSet();
  ```
- в существующем `foreach (var dto in imms)` (где заполняются `Status`/`LastUpdate`):
  ```csharp
  var entry = _statusCache.GetEntry(dto.Id);
  var raw   = entry?.Status ?? ImmStatus.Offline;
  var task  = ActiveTaskStatusMap.From(dto.ActiveTaskRawStatus);
  var hasOpenDt = openDowntimeImmIds.Contains(dto.Id);
  var thresholdPassed = entry != null &&
      (DateTime.UtcNow - entry.SinceUtc).TotalSeconds >= _downtime.IdleThresholdSeconds;
  dto.EffectiveStatus = ImmEffectiveStatus.Resolve(raw, task, hasOpenDt, thresholdPassed);
  ```
- контроллер получает `IOptions<DowntimeSettings>` через DI.

`GetImmStatus` (одиночный): то же — `EffectiveStatus` по `currentTask`, точечный `AnyAsync` для
открытого простоя, `thresholdPassed` по `entry.SinceUtc`. Нужен, т.к. `dashboard store.refreshImmStatus`
использует этот endpoint.

**Расхождение live vs история:** на дашборде `thresholdPassed` может показать `Downtime` на
~`PollingIntervalSeconds` раньше, чем воркер физически создаст `Event(Downtime)`. Это осознанно —
пользователь видит простой без задержки опроса; в истории сегмент строится ровно по факту `Event`.

## Секция 3 — История: endpoint + наложение интервалов

Новый endpoint (старый сырой `/status-history` не трогаем):

```http
GET /api/imm/{id}/effective-status-history?from&to
→ EffectiveStatusSegmentDto[]   { EffectiveStatus, ChangedAt, EndedAt }
```

Роли — как у `/status-history`: `Admin,Manager,Adjuster,Observer`. `from`/`to` → `Kind=Utc`.

Контроллер собирает три ряда за `[from, to]` (пересекающиеся с периодом) и передаёт в чистую функцию:
1. **Сырой статус** — `ImmStatusHistory`: `(Status, ChangedAt, EndedAt)`.
2. **Интервалы задания** — `ShiftTask` по `ImmId`: Наладка `[SetupStartedAt, StartedAt ?? конец]`,
   Работа `[StartedAt, CompletedAt ?? ClosedAt ?? to]`. Одновременно активно ≤1 задания.
3. **Простои** — `Event(Downtime)` по `ImmId`: `[StartTime, EndTime ?? to]` (авто и ручные).

Чистая функция в Core (без EF):

```csharp
public static class EffectiveStatusTimeline
{
    public static IReadOnlyList<EffectiveSegment> Build(
        IReadOnlyList<RawSegment> raw,        // (string Status, DateTime Start, DateTime End)
        IReadOnlyList<TaskInterval> tasks,    // (ActiveTaskStatus Status, DateTime Start, DateTime End)
        IReadOnlyList<Interval> downtimes,    // (DateTime Start, DateTime End)
        DateTime from, DateTime to);
}
```

Алгоритм (sweep-line):
- собрать все границы (обрезанные к `[from,to]`) из трёх рядов → отсортировать уникальные;
- на каждом под-интервале определить активный `rawMode`, `ActiveTaskStatus`, покрытие `Downtime`;
- `effective = ImmEffectiveStatus.Resolve(rawMode, task, hasDowntime, thresholdPassed: false)`;
- слить смежные сегменты с одинаковым `effective`.

`thresholdPassed: false` для истории намеренно — факт простоя уже материализован в `Event(Downtime)`
(воркер бэкдейтит `StartTime` на реальное начало не-Auto), поэтому вне Downtime-интервала не-Auto при
`InProgress` корректно даёт `Production`, а покрытое — `Downtime`. Живой таймер для прошлого не нужен.

## Секция 4 — Фронт: дашборд

Единый источник палитры — `src/constants/effectiveStatus.js`:

```js
export const EFFECTIVE_STATUS = {
  Production: { label:'Работа',             bg:'bg-green-100',  text:'text-green-800',  dot:'bg-green-500',  border:'border-green-500',  hex:'#22c55e' },
  Setup:      { label:'Наладка',            bg:'bg-yellow-100', text:'text-yellow-800', dot:'bg-yellow-500', border:'border-yellow-500', hex:'#eab308' },
  Downtime:   { label:'Простой',            bg:'bg-red-100',    text:'text-red-800',    dot:'bg-red-500',    border:'border-red-500',    hex:'#ef4444' },
  Unplanned:  { label:'Работа без задания', bg:'bg-purple-100', text:'text-purple-800', dot:'bg-purple-500', border:'border-purple-500', hex:'#a855f7' },
  NoTask:     { label:'Без задания',        bg:'bg-blue-100',   text:'text-blue-800',   dot:'bg-blue-500',   border:'border-blue-500',   hex:'#3b82f6' },
  Offline:    { label:'Нет связи',          bg:'bg-gray-100',   text:'text-gray-800',   dot:'bg-gray-500',   border:'border-gray-400',   hex:'#9ca3af' },
}
```

- **`ImmStatusBadge.vue`** — читает из `EFFECTIVE_STATUS` (validator → 6 ключей, fallback `Offline`).
- **`ImmCard.vue`** — `borderColor` и бейдж по `imm.effectiveStatus`; **красный баннер аварии убрать**
  (раствор `Alarm` → BL-19).
- **`stores/dashboard.js`**:
  - `loadImms` маппит `status: imm.effectiveStatus || 'Offline'`, сохраняя сырой `rawStatus = imm.status`
    про запас (BL-19);
  - геттеры на эффективные ключи: `workingImms→Production`, `setupImms→Setup`, `downtimeImms→Downtime`,
    `noTaskImms→NoTask`, `unplannedImms→Unplanned`, `offlineImms→Offline`;
  - `overallEfficiency = (Production + Setup) / total`.
- **`DashboardView.vue`**:
  - KPI-панель — **вариант B (7 карточек):** Всего · Работа · Наладка · Простой · Без задания ·
    Нет связи · Текущая загрузка. «Работа без задания» (Unplanned) при `>0` подсветить числом внутри
    карточки «Работа»;
  - фильтр «Статус» — 6 опций из `EFFECTIVE_STATUS`; `filteredImms` фильтрует по `effectiveStatus`.

## Секция 5 — Фронт: таймлайн в `ImmDetailModal`

- **`api/dashboard.js`** — новый `getImmEffectiveStatusHistory(id, { from, to })`; старый
  `getImmStatusHistory` оставляем.
- **`ImmDetailModal.vue`** — вызывать новый метод; `statusSegments` теперь
  `{ effectiveStatus, changedAt, endedAt }`; `STATUS_LEGEND` пересобрать на 6 эффективных состояний из
  `EFFECTIVE_STATUS`; агрегаты `totals` пересчитать по эффективным ключам (сводка по смене:
  Работа/Наладка/Простой/Без задания (+ Работа без задания, если была) в часах/процентах).
- **`ShiftTimeline.vue`** — принимать сегменты с полем `effectiveStatus`, цвет из
  `EFFECTIVE_STATUS[seg.effectiveStatus].hex`.

## Секция 6 — Тестирование и инфраструктура

**Backend (xUnit):**
- `ImmEffectiveStatusTests` — табличные `[Theory]` по матрице `Состояния_ТПА.xlsx`: все комбинации
  `(rawMode × taskStatus × hasOpenDowntime × thresholdPassed)` → ожидаемое из 6 состояний.
- `EffectiveStatusTimelineTests` — сценарии наложения: Наладка→Работа без разрыва; не-Auto до порога =
  Работа, покрытый `Event(Downtime)` = Простой; слияние смежных; обрезка по `[from,to]`; открытые
  интервалы; «дыры» в данных.
- Интеграционные: новый endpoint `effective-status-history` (роли + форма ответа) и `effectiveStatus`
  в `GET /imm`.

**Frontend (Vitest — впервые):**
- Настройка: `vitest` в `devDependencies`, npm-скрипт `"test": "vitest run"`, конфиг.
- Тесты чистой логики: геттеры стора (`overallEfficiency`, группировки), целостность
  `EFFECTIVE_STATUS` (6 ключей, все поля). Без рендера компонентов (`jsdom` не нужен).

**`Start-DevEnv.ps1`:** в шаг 3 «Тесты» добавить прогон Vitest под тем же флагом `-SkipTests`, после
backend-тестов:

```powershell
Write-Step "Запуск frontend-тестов (Vitest)..."
Push-Location "$root\Wintime-Control-Frontend"
try {
    if (-not (Test-Path 'node_modules')) { npm install }
    npm run test
    if ($LASTEXITCODE -ne 0) { Write-Fail "Frontend-тесты упали"; exit 1 }
} finally { Pop-Location }
Write-Ok "Frontend-тесты прошли"
```

**`CLAUDE.md`:** расширить раздел «Статусы ТПА» подсекцией про эффективное состояние (6 значений,
правило-итог: дашборд показывает эффективное, сырой `mode` остаётся в `ImmStatusHistory`).

## Что НЕ входит (YAGNI / следующие шаги)

- **MUN-03** — журнал простоев (следующая фронт-задача).
- **BL-19** — баннер аварии в карточке (нужен сырой `Alarm` отдельно от эффективного).
- **BL-20** — donut-диаграмма состояния цеха вместо KPI-карточек.
- **BL-17** — уведомление менеджеру при `Unplanned`.
- **MUN-02** — длительность цикла → подавление ложных простоев от дребезга связи (особый случай
  Offline-инициированного простоя). Зафиксировано в бэклоге.
- Сырой `/status-history`, `MobileStatusBadge` (статус задания), мобильный интерфейс — не трогаем.
- Отдельный ADR не нужен: ADR-0006 покрывает модель и предусматривал реализацию `ImmEffectiveStatus`
  вместе с появлением потребителя.

## Открытые детали для плана реализации

- Способ скрыть внутреннее `ActiveTaskRawStatus` из JSON (`[JsonIgnore]` vs отдельная проекция).
- Точные имена/расположение Core-типов `RawSegment`/`TaskInterval`/`Interval`/`EffectiveSegment`.
- Минимальный конфиг Vitest для проекта на Vite (отдельный `vitest.config` или секция в `vite.config`).
- Поведение `Build` при `to` в будущем (открытые интервалы обрезать к `min(to, now)`).
