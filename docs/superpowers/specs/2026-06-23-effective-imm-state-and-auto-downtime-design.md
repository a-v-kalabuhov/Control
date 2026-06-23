# Эффективное состояние ТПА, гейтинг обработки циклов и автоматические простои

> Дата: 2026-06-23
> Статус: дизайн утверждён, готов к написанию плана реализации
> Связано: [[project_requirements]] (жизненный цикл СЗ), [[feature_backlog]] (MUN-03, BL-16, BL-17),
> матрица сценариев: `docs/details/Состояния_ТПА.xlsx`

## Проблема

Сейчас статус ТПА и вся обработка телеметрии завязаны на единственный источник —
поле `mode` из MQTT-сообщения:

- `UpdateImmStatusHandler.MapModeToStatus` выводит статус только из `data.Mode`; статус
  активного задания (`ShiftTask.Status`) не учитывается.
- `CycleProcessingHandler` детектирует циклы по `mode == Auto` и считает выпуск, если есть
  задание `InProgress`; статус `Setup` (наладка) от `InProgress` не отделяется — во время
  наладки тестовые прогоны ошибочно записываются как циклы.
- Автоматического создания простоев (`Event` типа `Downtime`) нет вообще: единственная точка
  создания — ручной эндпоинт `DowntimeController.StartDowntime` («Начать простой»).

Из-за этого:

1. Наладчик не видит автоматических простоев — их некому создавать.
2. Циклы/выпуск считаются неверно, потому что решение не учитывает, что делает наладчик
   (наладка / производство / ручной простой / нет задания).

## Цель

Ввести **эффективное состояние ТПА** = функция от (сырой `mode` + статус активного задания +
наличие открытого простоя) и подчинить ему три решения:

1. **Обработка циклов** — писать ли `ImmCycle`.
2. **Учёт выпуска** — увеличивать ли `ActualQuantity` / `ActualMaterialWeightGrams` задания.
3. **Автоматические простои** — создавать/закрывать `Event` типа `Downtime`.

Авторитетный источник правил обработки циклов/выпуска — `docs/details/Состояния_ТПА.xlsx`
(16 сценариев). Этот дизайн переносит документ в исполняемую спецификацию.

## Ключевые решения (зафиксированы при обсуждении)

| # | Решение | Выбор |
|---|---|---|
| 1 | Охват | A (эффективное состояние + гейтинг) и B (авто-простои) вместе |
| 2 | Хранение состояния | Два слоя: сырой `mode` в `ImmStatusHistory`, эффективное **вычисляется на лету** |
| 3 | Триггер авто-простоя | Любой не-Auto режим (Idle/Manual/Alarm) **и Offline** при `InProgress` дольше порога |
| 4 | Порог и период опроса | Глобально в `DowntimeSettings` (`appsettings.json`) |
| 5 | Механика детектирования | Polling-воркер по образцу `ImmOfflineWorker` |
| 6 | Связь простоя с заданием | Новое поле `Event.TaskId` (nullable) |
| 7 | Дискриминатор авто/ручной | Новое поле `Event.IsAuto` (bool, default false) |
| 8 | Комментарий простоя | Новое поле `Event.Comment` (string?) |

Обоснование «вычислять на лету»: оба входа эффективного состояния уже историзированы
независимо — сырой `mode` в `ImmStatusHistory` (с `ChangedAt`/`EndedAt`), а моменты переходов
задания в метках `ShiftTask` (`IssuedAt`, `SetupStartedAt`, `StartedAt`, `CompletedAt`,
`ClosedAt`). Поэтому эффективное состояние на любой прошлый момент реконструируется из
существующих данных, а доказательная история простоев лежит в `Event`. Отдельная таблица не нужна.

## Матрица обработки (из `Состояния_ТПА.xlsx`)

Входы:

- **signal** — режим контроллера из MQTT: `auto` / `idle` / `manual` / `alarm` (плюс `offline`,
  когда сообщения не приходят).
- **taskStatus** — статус активного задания на ТПА: `None` (нет активного) / `Setup` / `InProgress`.
  Активное задание — задание на этом ТПА в статусе `Setup` **или** `InProgress` (по доменному
  правилу оно не более одного).
- **hasOpenDowntime** — есть ли открытый `Event` типа `Downtime` (`EndTime == null`) на этом ТПА.

### Правило 1 — обработка циклов (`ShouldProcessCycle`)

| taskStatus | signal | Циклы |
|---|---|---|
| `Setup` | любой | **НЕ обрабатывать** |
| `InProgress` | любой | обрабатывать |
| `None` | `auto` | обрабатывать (без привязки к ПФ/заданию: `TaskId=null`, `MoldId=null`) |
| `None` | `idle`/`manual`/`alarm`/`offline` | **НЕ обрабатывать** |

```
ShouldProcessCycle = (taskStatus == InProgress)
                  || (taskStatus == None && signal == auto)
// taskStatus == Setup → всегда false
```

### Правило 2 — учёт выпуска (`ShouldCountOutput`)

Выпуск (`ActualQuantity += cavities`, `ActualMaterialWeightGrams += …`) учитывается **только** при:

```
ShouldCountOutput = (taskStatus == InProgress)
                 && (signal == auto)
                 && !hasOpenDowntime
```

Это единственный сценарий из документа (строка 11), где «Обработка: выпуск» = «учитывается».
Открытый ручной простой при `InProgress`+`auto` (строка 12 документа) подавляет учёт выпуска —
циклы при этом всё равно пишутся (с `TaskId`), но `ActualQuantity` не растёт.

### Эффективное состояние (`ImmEffectiveStatus.Resolve`)

Производное значение для дашборда/отчётов и воркера:

| Эффективное состояние | Условие |
|---|---|
| `Offline` | связи нет |
| `Setup` (Наладка) | активное задание `Setup` (любой signal) |
| `Production` (Работа) | `InProgress` + `auto` + нет открытого простоя |
| `Downtime` (Простой) | `InProgress` + не-Auto/Offline дольше порога, **или** открытый простой |
| `Stopped` (Остановлен) | `InProgress` + не-Auto, порог ещё не пройден |
| `Unplanned` (Работа без задания) | нет задания + `auto` (аномалия → BL-17) |
| `NoTask` (Без задания) | нет задания + не-Auto |

## Архитектура

```
MQTT → MessageProcessingPipeline
        → DecodeTelemetryHandler
        → ValidateTelemetryDataHandler
        → StoreTelemetryHandler                  (сырьё пишется всегда)
        → UpdateImmStatusHandler                 (сырой статус в ImmStatusHistory — как сейчас)
        → CycleProcessingHandler                 (★ гейтинг по taskStatus + правила 1/2)

DowntimeDetectionWorker (BackgroundService, ★ новый)
        → каждые PollingIntervalSeconds читает кеш статусов + активные задания
        → создаёт/закрывает Event(Downtime, IsAuto=true)

ImmEffectiveStatus.Resolve (★ pure-функция в Core)
        → используется воркером и эндпоинтом статусов дашборда
```

### Компонент: `CycleProcessingPolicy` (новый, Core, pure)

Статические pure-функции, инкапсулирующие правила 1 и 2:

```csharp
public static class CycleProcessingPolicy
{
    public static bool ShouldProcessCycle(string signal, ActiveTaskStatus taskStatus);
    public static bool ShouldCountOutput(string signal, ActiveTaskStatus taskStatus, bool hasOpenDowntime);
}
```

`ActiveTaskStatus` — enum `{ None, Setup, InProgress }` (проекция `TaskStatus` на «активность» для
конвейера). Решение, что считать активным, остаётся в одном месте.

### Компонент: `ImmEffectiveStatus` (новый, Core, pure)

```csharp
public static class ImmEffectiveStatus
{
    public static string Resolve(string rawStatus, ActiveTaskStatus taskStatus, bool hasOpenDowntime, bool thresholdPassed);
}
```

### Изменения: `CycleProcessingHandler`

- Запрос активного задания расширяется: `Status == Setup || Status == InProgress`
  (сейчас — только `InProgress`).
- В начале: если `ShouldProcessCycle(signal, taskStatus) == false` → выйти, не трогая трекер
  (для `Setup` и для «нет задания при не-auto»). Поведение трекера в этих случаях
  детализируется в плане (наладка не должна «склеивать» цикл через границу `Setup→InProgress`).
- Создание `ImmCycle`: при `InProgress` — с `TaskId`/`MoldId`; при «нет задания + auto» — без них.
- Учёт выпуска: блок инкремента выполняется только если
  `ShouldCountOutput(signal, InProgress, hasOpenDowntime) == true`. Добавляется проверка
  открытого простоя (запрос `Event` с `EndTime == null` по `ImmId`).

### Компонент: `DowntimeDetectionWorker` (новый, Infrastructure)

`BackgroundService` по образцу `ImmOfflineWorker`. Каждые `PollingIntervalSeconds`:

1. `var entries = _statusCache.GetAll();` — для каждого `ImmStatusEntry(ImmId, Status, SinceUtc)`.
2. Получить активное задание `InProgress` для `ImmId` (scoped-сервис в DI-скоупе).
3. Решение через pure-метод `DowntimeDecision.Evaluate(...)`:
   - **Open(start):** `taskStatus == InProgress` && `raw != Auto` (включая `Offline`) &&
     `(now - max(SinceUtc, task.StartedAt)) >= IdleThresholdSeconds` && нет открытого авто-простоя
     → создать `Event { EventType=Downtime, ImmId, TaskId=task.Id, StartTime=max(SinceUtc, task.StartedAt), ReasonId=null, EndTime=null, IsAuto=true }`.
   - **Close(end):** есть открытый **авто**-простой и (`raw == Auto` || задание больше не `InProgress`)
     → `EndTime = SinceUtc` (бэкдейт на момент возврата в Auto).
   - **None:** иначе.

`StartTime` бэкдейтится на реальное начало не-Auto (`SinceUtc`), поэтому период опроса влияет
только на задержку обнаружения (~`PollingIntervalSeconds`), но не на точность `DurationSeconds`.

Воркер управляет только записями `IsAuto = true`; ручные простои (`IsAuto = false`) закрывает
наладчик. Правило «один открытый простой на ТПА» предотвращает дублирование: если открыт ручной
простой, авто-создание блокируется.

### Изменения схемы (`Event` + миграция)

```csharp
public class Event : BaseEntity
{
    // ... существующие поля ...
    public Guid? TaskId { get; set; }      // ★ связь с активным заданием
    public string? Comment { get; set; }   // ★ комментарий наладчика/менеджера
    public bool IsAuto { get; set; }       // ★ авто (воркер) vs ручной (наладчик); default false

    public ShiftTask? Task { get; set; }   // ★ navigation
}
```

Миграция: `dotnet ef migrations add AddEventTaskIdCommentIsAuto`. FK `Event.TaskId → ShiftTasks.Id`
(nullable, `OnDelete: SetNull` или `Restrict` — уточнить в плане согласно существующим связям).

### Конфигурация: `DowntimeSettings` (новый, `Wintime.Control.Shared`)

```csharp
public class DowntimeSettings
{
    public int IdleThresholdSeconds { get; set; } = 120;
    public int PollingIntervalSeconds { get; set; } = 10;
}
```

Биндинг из `appsettings.json` (`Downtime` секция) в `Program.cs` через Options pattern;
`DowntimeDetectionWorker` регистрируется как `AddHostedService`.

### Изменения API (`DowntimeController`)

- `PATCH events/{id}` (`UpdateDowntimeEvent`): сейчас меняет только `ReasonId` и доступен
  `Admin,Manager`. Переделать:
  - добавить роль `Adjuster` (наладчик редактирует свои простои);
  - менять `ReasonId` **+ `EndTime` + `Comment`** (все опционально);
  - `EndTime` приводить к `Kind=Utc` (правило проекта по Npgsql).
- `EventDto` — добавить `Comment`, `TaskId`, `IsAuto`.
- `UpdateDowntimeEventRequestDto` — добавить `EndTime?`, `Comment?`; `ReasonId` сделать опциональным.
- `StartDowntime` — выставлять `IsAuto = false` (дефолт, без изменения сигнатуры).

### Фронтенд (следующий шаг, не блокирует A/B)

Отображение эффективного состояния на дашборде и журнал простоев (MUN-03) — отдельная задача
после бэкенда. Эндпоинт статусов начинает отдавать эффективное состояние; UI адаптируется позже.

## Тестирование (xUnit, существующий набор 125 unit + 21 integration)

- **Табличные `[Theory]` по 16 строкам `Состояния_ТПА.xlsx`:**
  - `CycleProcessingPolicy.ShouldProcessCycle` / `ShouldCountOutput`;
  - `ImmEffectiveStatus.Resolve`.
  Документ становится исполняемой спецификацией.
- **`DowntimeDecision.Evaluate`** — табличные тесты: порог не пройден / пройден / возврат в Auto /
  уход из `InProgress` / `Offline` при `InProgress` / уже открыт ручной простой.
- **`CycleProcessingHandler`** — новые кейсы: `Setup` (циклы не пишутся); открытый ручной простой
  подавляет выпуск, но цикл пишется.
- **Интеграционный тест** `PATCH events/{id}`: наладчик меняет причину + `EndTime` + комментарий;
  проверка допуска роли `Adjuster`.

## Что НЕ входит (YAGNI / следующие шаги)

- Историзация эффективного состояния в отдельную таблицу — не нужна (вычисляем на лету).
- Уведомление менеджеру «работа без задания» — отдельная задача BL-17 (зависит от MUN-09).
- Порог простоя на уровне ТПА — пока только глобальный.
- UI дашборда/журнала простоев — отдельная фронтенд-задача (MUN-03).
- ADR — завести запись о смене модели определения состояния (mode → эффективное состояние)
  при реализации.

## Открытые детали для плана реализации

- Точное поведение `CycleTracker` при входе/выходе из `Setup` (сброс vs удержание состояния),
  чтобы цикл не «склеивался» через границу наладки.
- Стратегия `OnDelete` для FK `Event.TaskId`.
- Имя секции конфигурации (`Downtime`) и значения дефолтов под конкретный стенд.
