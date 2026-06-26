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

На `Imm`/`Mold`/`User` `IsActive` — вывод из оборота, **не** признак текущей активности.`false` → в архиве: скрыт из списков выбора, нельзя назначить в новое СЗ, история сохраняется и доступна в отчётах.
Никогда не удалять сущности физически — только `IsActive = false`.

### `Cavities` — снапшот, не текущее значение

`Mold.Cavities` изменяемо (гнёзда заглушаются при ремонте). Для пересчёта исторических циклов
бери `ImmCycle.Cavities` (снапшот на момент записи цикла); fallback для старых записей (= 0) — `Mold.Cavities`.
Не использовать текущий `Mold.Cavities` для истории. (Будущее — `ImmCycle.MoldVersionId` → `MoldVersion`, см. ADR.)

### Статусы ТПА

Статус — строка по MQTT, хранится в `ImmStatusHistory.Status`, кешируется в `IImmStatusCache`.
Единственная точка записи — `IImmStatusService.UpdateStatusAsync` (обновляет и БД, и кеш):
- `Auto` — работа по программе · `Manual` — наладка · `Idle` — включён, простой · `Alarm` — авария · `Offline` — нет MQTT
Дашборд показывает **эффективное состояние** — чистая функция `ImmEffectiveStatus.Resolve`
(`Core/Policies`), **не хранится**, считается на лету; история — `EffectiveStatusTimeline.Build`.
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
Заводи новый ADR, когда решение трудно откатить (схема БД, контракт API, выбор слоя) или задаёт паттерн.
Баг-фиксы и косметика — без ADR. Принятые ADR не правят по существу — пишут заменяющий (`Superseded by`).

## Коннекторы (платные модули)
Интеграция с приватными репозиториями-коннекторами (поля `Template.ConnectorType`/`Imm.ConnectorAlias`,
endpoint `/api/connectors/{type}/machines`, формат `JsonConfig`, OPC-пути) — в скиле `connector-integration`.
Грузится автоматически при работе с коннекторами/OPC.
