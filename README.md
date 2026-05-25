# Wintime Control

> **EN** — Web-based manufacturing management system for real-time monitoring and control of injection molding machines (IMM).
>
> **RU** — Веб-система управления производством для мониторинга и контроля термопластавтоматов (ТПА) в реальном времени.

---

## Table of Contents / Содержание

- [Overview / Обзор](#overview--обзор)
- [Features / Функциональность](#features--функциональность)
- [Architecture / Архитектура](#architecture--архитектура)
- [Tech Stack / Технологии](#tech-stack--технологии)
- [Quick Start / Быстрый старт](#quick-start--быстрый-старт)
- [Development Setup / Настройка разработки](#development-setup--настройка-разработки)
- [Configuration / Конфигурация](#configuration--конфигурация)
- [API Overview / Обзор API](#api-overview--обзор-api)
- [Roles & Access / Роли и доступ](#roles--access--роли-и-доступ)
- [Database / База данных](#database--база-данных)

---

## Overview / Обзор

**EN**

Wintime Control is a full-stack production management platform designed for injection molding workshops. It provides real-time telemetry collection over MQTT, task and shift management, mold tracking with QR-code verification, and automated PDF/Excel report generation.

**RU**

Wintime Control — полнофункциональная система управления производством для цехов литья под давлением. Обеспечивает сбор телеметрии в реальном времени через MQTT, управление заданиями и сменами, учёт пресс-форм с QR-верификацией и автоматическую генерацию отчётов в форматах PDF и Excel.

---

## Features / Функциональность

| Feature | Функция |
|---|---|
| Real-time machine telemetry via MQTT | Телеметрия ТПА в реальном времени (MQTT) |
| Production task management | Управление производственными заданиями |
| Mold inventory & QR-code verification | Учёт пресс-форм и QR-верификация |
| Shift scheduling & personnel management | Управление сменами и персоналом |
| Downtime tracking & reason categorization | Учёт простоев с категоризацией причин |
| Machine status history & statistics | История статусов и статистика по ТПА |
| PDF & Excel report generation | Генерация отчётов (PDF, Excel) |
| Role-based access control | Разграничение доступа по ролям |
| Equipment configuration templates | Шаблоны конфигурации оборудования |
| MQTT emulator for development & testing | Эмулятор MQTT-оборудования для разработки |

---

## Architecture / Архитектура

### Solution Structure / Структура решения

```
Wintime.Control.sln
├── Wintime.Control.API          # ASP.NET Core 9 — HTTP API + SPA host
├── Wintime.Control.Core         # Domain entities, DTOs, service interfaces
├── Wintime.Control.Infrastructure  # EF Core, MQTT, JWT, PDF/Excel reports
├── Wintime.Control.Shared       # Configuration POCOs, role constants
├── Wintime.Control.Emulator     # Standalone MQTT equipment emulator
├── Wintime.Control.Tests.Unit   # xUnit unit tests
├── Wintime.Control.Tests.Integration  # xUnit integration tests
└── Wintime-Control-Frontend     # Vue 3 + Vite SPA
```

### Layered Architecture

```
HTTP Request
  └── API Controllers
        └── Application Services (Core interfaces / Infrastructure implementations)
              └── EF Core DbContext → PostgreSQL
```

### MQTT Telemetry Pipeline

```
MQTT Broker (Mosquitto)
  └── MqttService  (topics: control/imm/+/telemetry | .../events | .../status)
        └── MessageProcessor
              └── DecodeTelemetryHandler
                    └── ValidateTelemetryDataHandler
                          └── StoreTelemetryHandler   ← COV filtering
```

COV (Change of Value) filtering prevents storing redundant telemetry when values have not changed.

---

## Tech Stack / Технологии

### Backend

| Technology | Version | Purpose |
|---|---|---|
| ASP.NET Core | 9 | Web API framework |
| Entity Framework Core | 9 | ORM |
| PostgreSQL | 16 | Primary database |
| MQTTnet | 5.1 | MQTT client |
| ASP.NET Core Identity | 9 | User management |
| JWT Bearer | — | Authentication |
| QuestPDF | 2026.x | PDF report generation |
| ClosedXML | 0.105 | Excel report generation |
| Serilog | 9 | Structured logging |

### Frontend

| Technology | Version | Purpose |
|---|---|---|
| Vue 3 | ^3.5 | UI framework |
| Vite | ^8.0 | Build tool & dev server |
| Element Plus | ^2.13 | UI component library |
| ECharts | ^6.0 | Charts & visualizations |
| Pinia | ^3.0 | State management |
| Vue Router | ^4.6 | Client-side routing |
| Tailwind CSS | ^3.4 | Utility-first styling |
| Axios | ^1.14 | HTTP client |
| html5-qrcode | ^2.3 | QR code scanning |

### Infrastructure

| Component | Technology |
|---|---|
| Message broker | Eclipse Mosquitto 2.0 |
| Container runtime | Docker & Docker Compose |
| Database | PostgreSQL 16 (Alpine) |

---

## Quick Start / Быстрый старт

**EN** — The fastest way to run the full stack is Docker Compose.

**RU** — Самый быстрый способ запустить весь стек — Docker Compose.

### Prerequisites / Требования

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (with Compose v2)

### Run / Запуск

```powershell
git clone https://github.com/a-v-kalabuhov/Control.git
cd Control
docker-compose up
```

Services started / Запускаемые сервисы:

| Service | URL / Port |
|---|---|
| API (HTTP) | http://localhost:5000 |
| API (HTTPS) | https://localhost:5001 |
| PostgreSQL | localhost:5432 |
| Mosquitto MQTT | localhost:1883 |
| Mosquitto WebSocket | localhost:9001 |

The database is migrated and seeded with default users on first run.

При первом запуске база данных создаётся автоматически и заполняется пользователями по умолчанию.

---

## Development Setup / Настройка разработки

### Prerequisites / Требования

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 18+](https://nodejs.org/) with npm 9+
- [PostgreSQL 16](https://www.postgresql.org/download/) or Docker
- [Mosquitto](https://mosquitto.org/download/) or Docker

### 1. Backend

```powershell
dotnet restore
dotnet build

# Apply database migrations
dotnet ef database update `
  --project Wintime.Control.Infrastructure `
  --startup-project Wintime.Control.API

# Start API (https://localhost:7001)
dotnet run --project Wintime.Control.API

# Start MQTT emulator (optional)
dotnet run --project Wintime.Control.Emulator
```

### 2. Frontend

```powershell
cd Wintime-Control-Frontend
npm install
npm run dev      # Dev server → http://localhost:3000 (proxies /api to https://localhost:5001)
```

### 3. Production build / Продакшн-сборка

```powershell
cd Wintime-Control-Frontend
npm run build    # Outputs to Wintime.Control.API/wwwroot
```

The frontend is served by the API as a SPA with fallback routing.

Frontend встраивается в API и раздаётся как статика с поддержкой SPA-роутинга.

### 4. Database migrations / Миграции БД

```powershell
# Add new migration
dotnet ef migrations add <MigrationName> `
  --project Wintime.Control.Infrastructure `
  --startup-project Wintime.Control.API

# Apply migrations
dotnet ef database update `
  --project Wintime.Control.Infrastructure `
  --startup-project Wintime.Control.API
```

---

## Configuration / Конфигурация

Key settings are defined in `Wintime.Control.API/appsettings.json` and can be overridden in `appsettings.Development.json` or via environment variables.

Основные настройки задаются в `Wintime.Control.API/appsettings.json` и могут быть переопределены в `appsettings.Development.json` или через переменные окружения.

### Connection String

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Database=WintimeControlDb;Username=postgres;Password=password;"
}
```

### JWT Settings

```json
"JwtSettings": {
  "SecretKey": "YourSuperSecretKeyForJWTTokenGenerationMustBeLongEnough2026!",
  "Issuer": "Wintime.Control.API",
  "Audience": "Wintime.Control.Client",
  "ExpirationMinutes": 60,
  "RefreshTokenExpirationDays": 7
}
```

### MQTT Settings

```json
"MqttSettings": {
  "BrokerUrl": "localhost",
  "Port": 1883,
  "ClientId": "ControlServer",
  "TelemetryTopic": "control/imm/+/telemetry",
  "EventsTopic":    "control/imm/+/events",
  "StatusTopic":    "control/imm/+/status"
}
```

---

## API Overview / Обзор API

Swagger UI is available at `/swagger` in development mode.

Swagger UI доступен по адресу `/swagger` в режиме разработки.

| Controller | Prefix | Description / Описание |
|---|---|---|
| `AuthController` | `/api/auth` | Login, logout, token refresh, profile |
| `ImmController` | `/api/imm` | Machine CRUD, telemetry, statistics |
| `MoldsController` | `/api/molds` | Mold / press-form management |
| `TasksController` | `/api/tasks` | Production task lifecycle |
| `ShiftsController` | `/api/shifts` | Shift scheduling |
| `PersonnelController` | `/api/personnel` | Staff management |
| `DowntimeController` | `/api/downtime` | Downtime reasons |
| `TemplatesController` | `/api/templates` | Equipment configuration templates |
| `ReportsController` | `/api/reports` | PDF & Excel report generation |
| `AdminController` | `/api/admin` | Admin-only operations |

All endpoints (except `/api/auth/login`) require a valid JWT Bearer token.

Все эндпоинты (кроме `/api/auth/login`) требуют действующий JWT Bearer токен.

---

## Roles & Access / Роли и доступ

| Role / Роль | Policy | Description / Описание |
|---|---|---|
| `Admin` | `AdminOnly` | Full system access / Полный доступ |
| `Manager` | `ManagerOrAdmin` | Task & personnel management / Управление заданиями и персоналом |
| `Adjuster` | `AdjusterOrHigher` | Machine setup, mold verification / Наладка, верификация пресс-форм |
| `Operator` | — | Basic monitoring / Базовый мониторинг |

---

## Database / База данных

Main tables / Основные таблицы:

| Table | Description / Описание |
|---|---|
| `Users` | ASP.NET Identity users with roles |
| `Imms` | Injection molding machines (ТПА) |
| `Molds` | Press forms with resource tracking |
| `Tasks` | Production tasks with full lifecycle |
| `Templates` | Machine configuration templates |
| `Events` | Equipment event log |
| `DowntimeReasons` | Categorized downtime catalog |
| `Telemetry` | Time-series sensor data (indexed on ImmId + Timestamp) |
| `Shifts` | Shift definitions |
| `ImmStatusHistory` | Historical machine status |

---

## Testing / Тестирование

```powershell
# Run all tests
dotnet test

# Unit tests only
dotnet test Wintime.Control.Tests.Unit

# Integration tests only
dotnet test Wintime.Control.Tests.Integration
```

Unit tests use mocked dependencies; integration tests hit a real PostgreSQL database.

Юнит-тесты используют моки; интеграционные тесты работают с реальной БД PostgreSQL.

---

## License / Лицензия

This project is proprietary software. All rights reserved.

Данный проект является проприетарным программным обеспечением. Все права защищены.
