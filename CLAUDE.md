# CLAUDE.md

## Что это
Wintime Control — веб-система управления производством для термопластавтоматов (ТПА):
сбор телеметрии по MQTT, производственные задания, отчёты.
Backend: ASP.NET Core 9 (`API` хостит SPA + `Core` + `Infrastructure` + `Shared`,
`Emulator` — автономный MQTT-эмулятор оборудования). Frontend: Vue 3 + Vite
(в проде встроен в `wwwroot` API). БД: PostgreSQL 16 (EF Core 9 + Npgsql).

## Команды (только неочевидное)
```powershell
dotnet run --project Wintime.Control.API        # API на https://localhost:7001
dotnet run --project Wintime.Control.Emulator   # MQTT-эмулятор оборудования

# Миграции — важны ОБА флага:
dotnet ef migrations add <Name> --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
dotnet ef database update       --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API

docker-compose up   # PostgreSQL + Mosquitto + API

.\build.ps1          # публикация образов; ПЕРЕД ней поднять номер в VERSION (SemVer)
```
Frontend: `cd Wintime-Control-Frontend && npm run dev` — порт 3000, проксирует `/api` → `https://localhost:5001`.

## Инварианты домена (нарушение = баг)

### Роль — только `User.Role`
Единственный источник правды по роли — `User.Role` (enum `UserRole`), одна роль на пользователя.
JWT-клейм `role` и все `[Authorize(Roles=…)]`/политики опираются на него.
Identity-роли (`AspNetRoles`/`AspNetUserRoles`) **не используются** — никогда не вызывай
`AddToRoleAsync`/`RemoveFromRolesAsync`/`GetRolesAsync` для прав доступа.
Первый админ в проде создаётся при старте из секрета `Bootstrap__AdminPassword`, не из исходников.
Политики: `AdminOnly`, `ManagerOrAdmin`, `AdjusterOrHigher` (Adjuster < Manager < Admin). См. ADR-0004.

### `IsActive` — архивный флаг (мягкое удаление)

На `Imm`/`Mold`/`User` `IsActive` — вывод из оборота, **не** признак текущей активности. `false` → в архиве: скрыт из списков выбора, нельзя назначить в новое СЗ, история сохраняется и доступна в отчётах.
Никогда не удалять сущности физически — только `IsActive = false`.

### Тип изделия (`ProductType`) обязателен для новых ПФ и заданий

`ProductType` — каталог изделий (номенклатура), связь `ProductType → Mold` = 1:N (`Mold.ProductTypeId`
nullable — старые ПФ без типа). Правила (ADR-0007): создать ПФ без типа нельзя; создать задание для
ПФ без типа нельзя; у ПФ с типом сменить тип нельзя, если по ней есть хоть одно задание (любой статус)
— **исключение** `null → value` (впервые присвоить тип legacy-ПФ можно всегда). Уникальный `Article`
(человекочитаемый, вводится заглавными). Архивный тип (`IsActive=false`) нельзя привязать к ПФ;
ранее привязанные ПФ ссылку сохраняют.

### Плановое начало и выдача задания

`ShiftTask.PlannedDate` (`DateOnly`, обязательна) + `PlannedSetupStartTime` (`TimeOnly?`, по часам
завода). Эффективное начало — `PlannedTaskStart.Resolve` (без времени — начало первой смены),
**не хранится**, в API — `TaskDto.PlannedStartAt`. Менять план и наладчика можно только в
`Planned`/`Issued`. Выдать задание (`Issue`) без наладчика нельзя; в `Issued` наладчика можно
переназначить, но не снять. См. ADR-0014.

**Норматив наладки и буфер выпуска** (PZP-14) — снапшот умолчаний `PlanningDefaults` в задании:
`PlannedSetupMinutes`, `ReleaseBufferFixedMinutes` (минуты 0..1440), `ReleaseBufferPercent`
(целое 0..100, % от плановой длительности **выпуска**, без наладки). Смена умолчаний задания не
трогает. Норматив правится в `Planned`/`Issued`, буфер — до завершения выпуска
(`Planned`…`InProgress`). План задания (выпуск `⌈План/Гнёзда⌉ × PlannedFullCycleSeconds`, буфер,
`TaskDto.PlannedEndAt`) считает `TaskPlanCalculator`, **не хранится**; гнёзда — текущие
`Mold.Cavities` (это прогноз, не история). Настройки приложения — по ADR-0015.

### Задание длиннее смены — окно активности

Задание может идти через несколько смен и суток; смена/сутки — только ось отчётности (ADR-0016).
Окно активности `[SetupStartedAt, CompletedAt ?? ClosedAt ?? now)` (окно выпуска — от `StartedAt`)
— `TaskActivityWindow`. Задание относится к периоду **только** по пересечению окна
(`TaskActivityWindow.OverlapsPeriod` в EF), никогда по одной точке (`StartedAt`/`IssuedAt`).
План за период — нормативный (`TaskPeriodPlan`, сумма по дням ≤ плана задания (= min(P, норматив за весь выпуск)), не зависит от нарезки периода). Наладка относится к
периоду своего завершения. Статусы задания сравнивать только `==` (`Setup = 5` вне порядка ЖЦ).
`GET /api/tasks`: фильтры `issued*`/`planned*`/`active*` через И; без `status` архив скрыт, кроме `active*`.

### `Cavities` — снапшот, не текущее значение

`Mold.Cavities` изменяемо (гнёзда заглушаются при ремонте). Для пересчёта исторических циклов
бери `ImmCycle.Cavities` (снапшот на момент записи цикла); fallback для старых записей (= 0) — `Mold.Cavities`.
Не использовать текущий `Mold.Cavities` для истории. (Будущее — `ImmCycle.MoldVersionId` → `MoldVersion`, см. ADR.)

### Рабочий режим — параметр задания, не телеметрия

`ShiftTask.WorkMode` (`Auto`/`SemiAuto`) — как технолог назначил работать; ручного режима
нет, он же наладка. Не путать с `ImmMode` (`auto`/`manual`/`idle`/`alarm`) — это состояние
ТПА из телеметрии. Режим влияет только на учёт выпуска: в полуавтомате условие
`mode == auto` в `ShouldCountOutput` снято, потому что ТПА между циклами ждёт оператора.
Цикл засчитывается только если закрыт коннектором (`endedByCounter`, под контрактом v2 всегда
`true` — цикл закрывается исключительно по `lastCycle`, старой развилки "закрыт счётчиком vs
закрыт сменой режима" из MQTT-контракта v1 больше нет; параметр в `CompletedCycle` сохранён
для формы, не за логику).
На детект простоя режим **не влияет** — порог единый и высокий (900 с), см. ADR-0010.
Эталоны `PlannedFullCycleSeconds` (обязателен) и `PlannedInjectionCycleSeconds` хранятся
для планирования и аналитики.

### Цикл литья — коннектор детектирует, Control открывает/закрывает одну строку

Коннектор сам ведёт автомат состояний цикла литья и публикует в каждом MQTT-сообщении
структурированные блоки `currentCycle`/`lastCycle` (контракт v2, ADR-0012, отменяет
ADR-0011). Control не детектирует границы — `CycleProcessingHandler` открывает
`ImmCycle` (`EndTime = null`) по `currentCycle`, закрывает (`EndTime`,
`DurationSeconds`, `InjectionDurationMs`, `Cushion`) по совпавшему `lastCycle`.
Идентичность цикла — составной ключ `(CycleNumber, StartTime)`, **не** один номер:
счётчик коннектора легитимно обнуляется между сериями выпуска без разрыва связи,
поэтому номер может повторяться для разных физических циклов. Открытые
(`EndTime = null`) циклы уже учитываются в износе формы (`Cavities`), но исключены из
агрегатов длительности/среднего цикла (`ReportService`) до закрытия.
`ImmCycle.PauseDurationMs` в v2 контракте ничем не заполняется (пауза между циклами
вычисляется потребителем данных, не Control) — поле сохранено в схеме, но пока
всегда `null`.

### Статусы ТПА

Статус — строка по MQTT, хранится в `ImmStatusHistory.Status`, кешируется в `IImmStatusCache`.
Единственная точка записи — `IImmStatusService.UpdateStatusAsync` (обновляет и БД, и кеш):
- `Auto` — работа по программе · `Manual` — наладка · `Idle` — включён, простой · `Alarm` — авария · `Offline` — нет MQTT
Дашборд показывает **эффективное состояние** — чистая функция `ImmEffectiveStatus.Resolve`
(`Core/Policies`), **не хранится**, считается на лету; история — `EffectiveStatusTimeline.Build`.
6 значений: `Production` (Работа) · `Setup` (Наладка) · `Downtime` (Простой) ·
`Unplanned` (Работа без задания) · `NoTask` (Без задания) · `Offline` (Нет связи).
Палитра/подписи на фронте — `src/constants/effectiveStatus.js`.
Дизайн: `docs/superpowers/specs/2026-06-24-effective-status-dashboard-design.md`.

## Правила кода

### DateTime → PostgreSQL (Npgsql)

Все `DateTime` в EF-запросах к Postgres обязаны иметь `Kind=Utc` (колонки `timestamptz`, иначе
исключение `Cannot write DateTime with Kind=Unspecified`). ASP.NET биндит даты из query string как
`Unspecified`; `.Date`/`.AddDays` сохраняют Kind. Конвертируй сразу после получения параметра:
```csharp
var dateUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc); // AddDays/AddMinutes от Utc безопасны
```
Никогда не передавай `date.Date` в `.Where()` напрямую — только через `SpecifyKind`-переменную.

### Тесты
Добавляешь — xUnit для .NET, Vitest для фронта.

## ADR
Архитектурные решения — в `docs/adr/` (формат MADR, см. `docs/adr/README.md`).
ADR хранят *почему* решили так и какие альтернативы отвергли; CLAUDE.md — правило-итог (*как*); git — *что* изменилось.
Доменный словарь и описание сущностей (*что это за понятие*) — `docs/domain/` (необязательно к загрузке).
Заводи новый ADR, когда решение трудно откатить (схема БД, контракт API, выбор слоя) или задаёт паттерн.
Баг-фиксы и косметика — без ADR. Принятые ADR не правят по существу — пишут заменяющий (`Superseded by`).

## Коннекторы (платные модули)
Интеграция с приватными репозиториями-коннекторами (поля `Template.ConnectorType`/`Imm.ConnectorAlias`,
endpoint `/api/connectors/{type}/machines`, формат `JsonConfig`, OPC-пути) — в скиле `connector-integration`.
Грузится автоматически при работе с коннекторами/OPC.
