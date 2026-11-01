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

.\deploy\demo\deploy.ps1   # деплой демо-VPS (GHCR + ssh); ПЕРЕД ним поднять номер в VERSION (SemVer); -ResetDbOnly — сброс БД
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
Никогда не удалять сущности физически — только `IsActive = false` (исключения: изделие и материал без ссылок, см. ниже). ТПА/ПФ с незавершёнными заданиями (`Planned`…`InProgress`) в архив перевести нельзя (PZP-07b).

### Тип изделия и изделие (`ProductType` → `Product`)

Двухуровневый каталог (ADR-0020, заменяет ADR-0007): ПФ → **тип** (`Mold.ProductTypeId`, 1:N;
nullable только у старых ПФ), заказ и задание → **изделие** (`ProductId` обязателен, `Order.ProductTypeId`
нет). Создать ПФ без типа, задание для ПФ без типа нельзя; тип ПФ с заданиями (любой статус) менять
нельзя — **исключение** `null → value`. Полный артикул `FullArticle` = `ТИП.ЧАСТЬ` (`ProductArticle`,
Trim + заглавные), уникален и не совпадает ни с одним артикулом типа (и наоборот); правка артикула типа
пересчитывает полные артикулы изделий в одной транзакции. У типа всегда ≥1 изделия (по умолчанию —
часть `0`, «По умолчанию»), у активного типа ≥1 активного. Изделие задания — из типа его ПФ, меняется
только в `Planned` без заказа; привязка к заказу — `task.ProductId == order.ProductId`
(`OrderTaskBinding`). Архивное изделие/тип выбрать нельзя, ссылки сохраняются. Физически удаляется
только изделие без заказов и заданий и не последнее у типа — единственное исключение из мягкого
удаления.

### Материалы (`Material`)

Справочник сырья — одна таблица, тип `MaterialType` (`Plastic`/`Colorant`); в UI два справочника.
Тип задаётся при создании и **не меняется**. Уникальность внутри типа без учёта регистра:
`(Type, Article)` и `(Type, Manufacturer, Name)` (пустой производитель — одно значение; индекс на
выражении в миграции `AddMaterials`). У красителя обязателен цвет. Физически удаляется только
материал без ссылок (рецепты и их копии в заданиях) — второе исключение из мягкого удаления.

### Совместимые ТПА пресс-формы

`Mold.CompatibleImms` (таблица `MoldCompatibleImms`) — ручной список ТПА, на которые ставится ПФ.
**Пусто = совместимость не указана = любой ТПА.** Задание с ТПА вне непустого списка создать нельзя
(`MoldImmCompatibility.IsCompatible`, проверка в `CreateTask`). Архивный ТПА в список не добавить,
ранее привязанный остаётся. Существующие задания при правке списка не трогаются. См. ADR-0018.

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

### Конфликты плана — подсвечивают, не блокируют

Ресурсы — ТПА и ПФ (на любом ТПА); участвуют `Planned`/`Issued`/`Setup`/`InProgress`. Отрезки —
`TaskOccupancy` (план с буфером до начала; дальше факт + прогноз конца выпуска без буфера по стадиям,
задержка 10 мин — константа), пары — `PlanConflictDetector`: пересечение > `PlanningDefaults.ConflictThresholdMinutes`
(20 мин, не снапшот). Вид: только план — конфликт плана, иначе — конфликт отклонения. Не хранятся, считает
`IPlanConflictService` (`GET /api/planning/conflicts`). Сохранение, выдача и статусы **не блокируются**; старт
наладки с ПФ, занятой на другом ТПА, — 409 `moldBusy` + `confirmMoldBusy`. ТПА/ПФ с незавершёнными заданиями в
архив не переводятся. См. ADR-0022.

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
`ImmCycle.PauseDurationMs` — пауза перед циклом (от раскрытия предыдущего цикла ТПА), пишет Control
при создании строки (`CyclePause`); `null` — нет предыдущего, предыдущий цикл ещё открыт, пауза ≥ порога простоя или в паузе был
статус кроме `Auto`/`Offline`. Темп и прогноз окончания выпуска на плитке ТПА — `ReleaseForecast`
по среднему полному периоду (`AVG(DurationSeconds) + AVG(PauseDurationMs)` по закрытым циклам
задания, < 3 циклов — эталон), не хранятся; наладка и простои в прогноз не входят. См. ADR-0017.

### Статусы ТПА

Статус — строка по MQTT, хранится в `ImmStatusHistory.Status`, кешируется в `IImmStatusCache`.
Единственная точка записи — `IImmStatusService.UpdateStatusAsync` (обновляет и БД, и кеш):
- `Auto` — работа по программе · `Manual` — наладка · `Idle` — включён, простой · `Alarm` — авария · `Offline` — нет MQTT
Дашборд показывает **эффективное состояние** — чистая функция `ImmEffectiveStatus.Resolve`
(`Core/Policies`), **не хранится**, считается на лету; история — `EffectiveStatusTimeline.Build`.
6 значений: `Production` (Работа) · `Setup` (Наладка) · `Downtime` (Простой) ·
`Unplanned` (Работа без задания) · `NoTask` (Без задания) · `Offline` (Нет связи).
Сырой `Offline` → **всегда** `Offline` («Нет связи»): доминирует над наладкой, простоем (авто и ручным)
и порогом — нет данных, значит нет ни «работы», ни «простоя». Авто-простой по `Offline` не открывается;
открытый до обрыва — не закрывается. См. ADR-0021.
Палитра/подписи на фронте — `src/constants/effectiveStatus.js`.
Дизайн: `docs/superpowers/specs/2026-06-24-effective-status-dashboard-design.md`.

Все отчёты по ТПА («Картина рабочего дня», «Производительность оборудования») и карточка ТПА показывают
**эффективное состояние** (одна палитра `effectiveStatus.js`); сырой режим — только `ImmStatusHistory`/телеметрия.

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
