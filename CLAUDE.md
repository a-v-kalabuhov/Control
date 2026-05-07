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

Tables: `Users` (Identity), `Imms`, `Molds`, `Tasks`, `Templates`, `MoldUsages`, `Events`, `DowntimeReasons`, `Telemetry`.

`Telemetry` table has indexes on `ImmId + Timestamp` for time-range queries.

## Frontend Dev Proxy

`vite.config.js` proxies `/api` → `https://localhost:5001`. The production SPA is embedded in the API's `wwwroot` and served via `UseSpaStaticFiles` / SPA fallback middleware.

## No Tests Yet

There are no test projects. When adding tests, use xUnit for .NET and Vitest for the frontend.
