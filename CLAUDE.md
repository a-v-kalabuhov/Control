# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Wintime Control** — a web-based manufacturing management system for monitoring injection molding machines (IMM). It collects real-time telemetry via MQTT, manages production tasks, and generates reports.

## Solution Structure

| Project | Type | Role |
|---|---|---|
| `Wintime.Control.API` | ASP.NET Core 9 Web API | Main HTTP API + SPA host |
| `Wintime.Control.Core` | Class Library | Domain entities, DTOs, service interfaces |
| `Wintime.Control.Infrastructure` | Class Library | EF Core, MQTT, JWT, PDF reports |
| `Wintime.Control.Shared` | Class Library | Configuration POCOs, role constants |
| `Wintime.Control.Emulator` | ASP.NET Core 9 Web API | Standalone MQTT equipment emulator |
| `Wintime-Control-Frontend` | Vue 3 + Vite SPA | Frontend (served from API in production) |

## Build & Run Commands

### Backend
```powershell
dotnet restore
dotnet build
dotnet run --project Wintime.Control.API           # API on https://localhost:7001
dotnet run --project Wintime.Control.Emulator      # Emulator on its own port
```

### Frontend
```powershell
cd Wintime-Control-Frontend
npm install
npm run dev        # Dev server at http://localhost:3000 (proxies API to :5001)
npm run build      # Production build into wwwroot of API
```

### Docker (full stack)
```powershell
docker-compose up  # PostgreSQL + Mosquitto + API
```

### Database migrations
```powershell
dotnet ef migrations add <Name> --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
dotnet ef database update --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```

## Architecture

### Layered Architecture
```
API Controllers
    → Application Services (Core interfaces, Infrastructure implementations)
    → EF Core DbContext (PostgreSQL via Npgsql)
```

### MQTT Message Processing Pipeline
Real-time telemetry flows through a handler chain:

```
MQTT Broker (Mosquitto)
    → MqttService (subscribes to control/imm/+/telemetry, .../events, .../status)
    → MessageProcessor
    → DecodeTelemetryHandler → ValidateTelemetryDataHandler → StoreTelemetryHandler
```

Handlers are chained and each calls `next()`. COV (Change of Value) filtering is applied to avoid storing redundant telemetry.

### Authorization Policies
Defined in `Wintime.Control.Shared` constants and registered in `Program.cs`:
- `AdminOnly` — Admin role
- `ManagerOrAdmin` — Manager + Admin
- `AdjusterOrHigher` — Adjuster + Manager + Admin

JWT tokens are issued by `IJwtTokenService` (Infrastructure). Identity is ASP.NET Core Identity on top of EF Core.

### Configuration (Options Pattern)
Strongly-typed settings classes live in `Wintime.Control.Shared`:
- `JwtSettings` — secret key, issuer, audience, expiration
- `MqttSettings` — broker host/port, topics
- `CorsSettings` — allowed origins

Bound from `appsettings.json` / `appsettings.Development.json`. Development overrides use `localhost`.

## Key Technologies

- **ORM**: EF Core 9 + Npgsql (PostgreSQL 16)
- **Messaging**: MQTTnet 5.1 (client, not embedded broker)
- **Auth**: ASP.NET Core Identity + JWT Bearer
- **PDF**: QuestPDF
- **Excel**: ClosedXML
- **Logging**: Serilog (structured, request logging middleware)
- **Validation**: FluentValidation (used in Emulator)
- **HTTP client**: Refit (Emulator → API)
- **Frontend UI**: Element Plus + ECharts + Tailwind CSS
- **State**: Pinia
- **Routing**: Vue Router

## Database

Tables: `Users` (Identity), `Imms`, `Molds`, `Tasks`, `Templates`, `Events`, `DowntimeReasons`, `Telemetry`.

`Telemetry` table has indexes on `ImmId + Timestamp` for time-range queries.

## Коннекторы (платные модули)

Коннекторы — отдельные приватные репозитории (Worker Service, .NET 9), которые читают данные с оборудования и публикуют телеметрию в MQTT в формат Wintime Control. Основной API предоставляет коннекторам endpoint для получения списка ТПА.

### Поля для интеграции с коннекторами

**`Template.ConnectorType`** (string?, nullable) — дискриминатор типа коннектора:

- `null` / `"emulator"` — эмулятор, универсальные шаблоны
- `"kemro-opc"` — KEBA Kemro через OPC DA
- (будущие) `"modbus"`, `"fanuc-focas"` и др.

**`Imm.ConnectorAlias`** (string?, nullable) — псевдоним машины в дереве OPC-сервера (например `"TPA-06"`). Используется коннектором для построения OPC-путей к переменным.

### API для коннекторов

```http
GET /api/connectors/{connectorType}/machines
X-Api-Key: {ConnectorApiKey}
```

Возвращает список активных ТПА (`IsActive = true`) с шаблоном `ConnectorType == connectorType`. Ответ включает `ImmId`, `ImmName`, `ConnectorAlias` и `TemplateConfig` (разобранный `Template.JsonConfig`).

Авторизация — статичный API-ключ из `appsettings.json` → `ConnectorApiKey`. Не использует JWT.

### Формат JsonConfig шаблона коннектора

Поля `mode_opc_path` и `sensors[].opc_path` — специфичны для OPC-коннекторов, основным сервером игнорируются:

```json
{
  "mode_opc_path": "SVs.OperationMode",
  "sensors": [
    { "field": "iCycleCounter", "opc_path": "SVs.ShotCounter.sv_iShotCounter", "type": "cycleCounter" },
    { "field": "temp_zone_1",   "opc_path": "SVs.Heating.sv_Zone[1].rActualTemp", "type": "float" }
  ]
}
```

## Frontend Dev Proxy

`vite.config.js` proxies `/api` → `https://localhost:5001`. The production SPA is embedded in the API's `wwwroot` and served via `UseSpaStaticFiles` / SPA fallback middleware.

## No Tests Yet

There are no test projects. When adding tests, use xUnit for .NET and Vitest for the frontend.

## Domain Rules

### IsActive — архивный флаг (мягкое удаление)

`IsActive` на сущностях `Imm`, `Mold`, `User` — это флаг вывода из оборота, **не** признак текущей активности.

- `IsActive = true` → сущность в работе, доступна для назначения в СЗ
- `IsActive = false` → сущность в архиве: скрыта из списков выбора, нельзя назначить в новое СЗ, исторические данные сохраняются и доступны в отчётах

Никогда не удалять сущности физически — только `IsActive = false`.

### Статусы ТПА

Статус ТПА передаётся как строка через MQTT, хранится в `ImmStatusHistory.Status` и кешируется в `IImmStatusCache`. Фронтенд интерпретирует строку и показывает пользователю локализованное название и цвет.

| Строка в коде | Отображение пользователю | Смысл                                        |
| ------------- | ------------------------ | -------------------------------------------- |
| `"Auto"`      | Авто                     | ТПА работает по программе (полезная работа)  |
| `"Manual"`    | Наладка                  | ТПА в ручном режиме (наладка перед запуском) |
| `"Idle"`      | Простой                  | ТПА включён, но не работает и не в аварии    |
| `"Alarm"`     | Авария                   | Аварийное прерывание работы по программе     |
| `"Offline"`   | Нет связи                | От ТПА не поступают MQTT-сообщения           |

**Инфраструктура статусов:**
- `ImmStatusHistory` — таблица в БД, хранит историю переходов статусов (открытая запись = текущий статус)
- `IImmStatusCache` / `MemoryImmStatusCache` — in-memory singleton-кеш текущих статусов, заполняется при старте
- `ImmStatusStartupService` — при старте приложения закрывает незавершённые записи истории и заполняет кеш из БД
- `ImmOfflineWorker` — фоновый сервис, каждые 5 сек переводит ТПА в `Offline` если от него нет сообщений
- `IImmStatusService.UpdateStatusAsync` — единственная точка записи нового статуса (обновляет и БД, и кеш)

### Гнёздность (Cavities) и история версий пресс-формы

`Mold.Cavities` — изменяемое поле (при ремонте гнёзда могут заглушаться). Нельзя использовать текущее значение для пересчёта исторических циклов.

**MVP Мун:** `ImmCycle.Cavities` (int) — снапшот на момент записи цикла. Заполняется из `Mold.Cavities` при создании цикла. Fallback для старых записей (= 0) — брать из `Mold.Cavities`.

**РОСОМС и далее:** заменить на `ImmCycle.MoldVersionId` → сущность `MoldVersion`, которая хранит полную конфигурацию ПФ (Cavities, веса, статус) с датой вступления в силу и причиной изменения. `MoldVersion` — фундамент журнала ремонтов и обслуживания.

## Coding Rules

### DateTime и PostgreSQL (Npgsql)

**Правило:** все `DateTime`-значения, передаваемые в EF Core запросы к PostgreSQL, обязаны иметь `Kind=Utc`. Npgsql отклоняет `DateTimeKind.Unspecified` с исключением `Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'`.

Причина: колонки, объявленные как `timestamp with time zone` (`timestamptz`), требуют явного UTC.

**Как это возникает:** ASP.NET Core биндит `DateTime` из query string (например, `?date=2026-05-23`) с `Kind=Unspecified`. Далее `.Date`, `.AddDays()`, `.AddMinutes()` сохраняют тот же `Kind`.

**Обязательный паттерн** — конвертировать в UTC сразу после получения параметра:

```csharp
// В начале метода сервиса, до любых запросов к БД:
var dateUtc      = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
var dateFromUtc  = DateTime.SpecifyKind(dateFrom.Date, DateTimeKind.Utc);
var dateToUtc    = DateTime.SpecifyKind(dateTo.Date, DateTimeKind.Utc);
// AddDays/AddMinutes от Utc-значения сохраняют Kind=Utc — безопасно
var periodEnd    = dateUtc.AddDays(1);
```

Никогда не передавать `date.Date` напрямую в `.Where()`-условие — только через `SpecifyKind`-переменную.
