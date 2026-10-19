# Деплой демо-VPS (`deploy/demo/`) — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** собрать в `deploy/demo/` всё для обновления публичного демо `control-demo.ru`: образ API в GHCR,
compose с HTTPS через Kestrel, сброс БД новым `seed_demo.sql` (только прошлое, даты от момента загрузки),
плюс подпись «демо-версия» в приложении.

**Architecture:** `deploy.ps1` на машине автора собирает и пушит `api:<VERSION>`, копирует пакет на VPS
по `scp` и через `ssh` делает `compose pull && up -d`; `reset-db.sh` на VPS пересоздаёт БД и грузит сид.
Сид — один SQL-файл (PL/pgSQL), проверяется локально `test-seed.ps1` + `check_seed.sql` на временном
postgres со схемой из текущих миграций. Признак демо фронт берёт из анонимного `GET /api/app-info`.

**Tech Stack:** ASP.NET Core 9, xUnit + FluentAssertions + Testcontainers, Vue 3 + Pinia + Vitest,
PostgreSQL 16 (PL/pgSQL), Docker Compose v2, PowerShell 7, bash, OpenSSH.

**Spec:** `docs/superpowers/specs/2026-10-19-demo-vps-deploy-design.md`

## Global Constraints

- VPS: `deploy@195.208.2.44`, домен `control-demo.ru`, папка на VPS `~/wtctrl-demo`; VPS 1 ГБ ОЗУ / 1 ядро / 8,6 ГБ диска.
- `DemoMode=true` на демо — намеренно; демо-аккаунты создаёт API, сид их не создаёт и не меняет.
- Эмулятора (`Wintime.Control.Emulator`) в деплое нет; образ эмулятора больше не публикуется.
- Образ: `ghcr.io/a-v-kalabuhov/control/api:<VERSION>` и `:latest`.
- Compose-проект `wtctrl-demo`, контейнеры `wtctrl-demo-postgres`, `wtctrl-demo-mosquitto`, `wtctrl-demo-api`, volume `wtctrl-demo_postgres_data`, БД `WintimeControlDb`.
- HTTPS — Kestrel: `443→8443`, `80→8080` с редиректом; сертификат `certs/fullchain.pem` (= `.crt` домена + `.crt` УЦ) и `certs/privkey.pem`.
- Наружу открыты только 80/443; mosquitto и postgres — только во внутренней сети compose.
- Логи всех сервисов: `json-file`, `max-size 10m`, `max-file 3`.
- Сид: ничего позже `t0 = now()` в момент загрузки; история 30 суток (`days`), `points_per_cycle` = 6 (1..6); 12 ТПА (ТПА-12 в архиве); у демо-`adjuster` ровно 7 заданий `Issued`.
- Свободное место: `deploy.ps1` перед `pull` — ≥ 1,5 ГБ (1536 МБ); `reset-db.sh` перед сидом — ≥ 5 ГБ.
- `VERSION` → `1.18.0`.
- Подпись версии: `v<версия> · <ДД.ММ.ГГГГ>` + ` · демо-версия` при `demoMode`.
- SQL/sh/md-файлы — UTF-8 без BOM; `*.sh` — LF (`.gitattributes`).
- Комментарии и сообщения — по-русски, как в остальном репозитории.
- Ветка `feature/demo-deploy`; прямой push в master запрещён; PR через `gh pr create`.

## Структура файлов

| Файл | Ответственность |
| --- | --- |
| `Wintime.Control.Core/DTOs/AppInfo/AppInfoDto.cs` | DTO публичных сведений о стенде |
| `Wintime.Control.API/Controllers/AppInfoController.cs` | `GET /api/app-info` (анонимный) |
| `Wintime.Control.Tests.Integration/AppInfo/AppInfoTests.cs` | интеграционный тест endpoint |
| `Wintime-Control-Frontend/src/utils/appVersion.js` | формат подписи версии (+ `demo`) |
| `Wintime-Control-Frontend/src/utils/buildInfo.js` | `appVersionFor(demo)` из констант сборки |
| `Wintime-Control-Frontend/src/api/appInfo.js` | клиент `GET /app-info` |
| `Wintime-Control-Frontend/src/stores/appInfo.js` | стор `demoMode` (грузится один раз) |
| `Wintime-Control-Frontend/src/layouts/DefaultLayout.vue` | подпись версии в меню |
| `Wintime-Control-Frontend/src/views/auth/LoginView.vue` | подпись версии + демо-учётки только в демо |
| `deploy/demo/seed/check_seed.sql` | SQL-проверки инвариантов сида |
| `deploy/demo/test-seed.ps1` | локальный прогон сида и проверок |
| `deploy/demo/seed/seed_demo.sql` | новый сид |
| `deploy/demo/package/docker-compose.yml` | стек демо |
| `deploy/demo/package/mosquitto.conf`, `.env.example` | конфиг брокера, шаблон секретов |
| `deploy/demo/package/reset-db.sh` | сброс БД на VPS |
| `deploy/demo/deploy.ps1`, `deploy.local.example.psd1` | деплой с машины автора |
| `deploy/demo/ASSEMBLE.md` | инструкция автору |

---

### Task 1: `GET /api/app-info` — признак демо-режима

**Files:**
- Create: `Wintime.Control.Core/DTOs/AppInfo/AppInfoDto.cs`
- Create: `Wintime.Control.API/Controllers/AppInfoController.cs`
- Test: `Wintime.Control.Tests.Integration/AppInfo/AppInfoTests.cs`

**Interfaces:**
- Produces: `GET /api/app-info` → `200 { "demoMode": bool }` без авторизации (используется в Task 2).

- [ ] **Step 1: Написать падающий тест**

`Wintime.Control.Tests.Integration/AppInfo/AppInfoTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.AppInfo;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.AppInfo;

[Collection("Integration")]
public class AppInfoTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public AppInfoTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetAppInfo_WithoutToken_ReturnsDemoModeFromConfiguration()
    {
        var expected = _factory.Services.GetRequiredService<IConfiguration>().GetValue<bool>("DemoMode");
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/app-info");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AppInfoDto>();
        dto!.DemoMode.Should().Be(expected);
    }
}
```

- [ ] **Step 2: Убедиться, что тест не компилируется**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~AppInfoTests"`
Expected: ошибка сборки `The type or namespace name 'AppInfo' does not exist in the namespace 'Wintime.Control.Core.DTOs'`.

- [ ] **Step 3: DTO и контроллер**

`Wintime.Control.Core/DTOs/AppInfo/AppInfoDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.AppInfo;

/// <summary>Публичные сведения о стенде для фронта (без авторизации).</summary>
public class AppInfoDto
{
    /// <summary>Демо-стенд (флаг DemoMode): открытые демо-аккаунты и подпись «демо-версия».</summary>
    public bool DemoMode { get; set; }
}
```

`Wintime.Control.API/Controllers/AppInfoController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.AppInfo;

namespace Wintime.Control.API.Controllers;

/// <summary>
/// Публичные сведения о стенде: по ним фронт показывает подпись «демо-версия» и демо-учётки
/// на экране входа. Режим определяется во время работы, а не при сборке — один образ корректен
/// и на демо, и у клиента.
/// </summary>
[ApiController]
[Route("api/app-info")]
[AllowAnonymous]
public class AppInfoController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AppInfoController(IConfiguration configuration) => _configuration = configuration;

    /// <summary>Признак демо-режима — тот же флаг DemoMode, что создаёт демо-аккаунты.</summary>
    [HttpGet]
    public ActionResult<AppInfoDto> Get() =>
        Ok(new AppInfoDto { DemoMode = _configuration.GetValue<bool>("DemoMode") });
}
```

- [ ] **Step 4: Тест проходит**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~AppInfoTests"`
Expected: `Passed: 1, Failed: 0` (нужен запущенный Docker — Testcontainers).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/DTOs/AppInfo Wintime.Control.API/Controllers/AppInfoController.cs Wintime.Control.Tests.Integration/AppInfo
git commit -m "feat(api): anonymous GET /api/app-info with demo mode flag"
```

---

### Task 2: Подпись «демо-версия» и демо-учётки только на демо

**Files:**
- Modify: `Wintime-Control-Frontend/src/utils/appVersion.js`
- Modify: `Wintime-Control-Frontend/src/utils/buildInfo.js`
- Create: `Wintime-Control-Frontend/src/api/appInfo.js`
- Create: `Wintime-Control-Frontend/src/stores/appInfo.js`
- Modify: `Wintime-Control-Frontend/src/layouts/DefaultLayout.vue:11,171`
- Modify: `Wintime-Control-Frontend/src/views/auth/LoginView.vue:5-8,62-70,74-79`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/appVersion.spec.js`
- Test: `Wintime-Control-Frontend/src/stores/__tests__/appInfo.spec.js`

**Interfaces:**
- Consumes: `GET /api/app-info` → `{ demoMode }` (Task 1).
- Produces: `formatAppVersion({ version, buildDate, commit, demo = false }) → { label, tooltip }`;
  `appVersionFor(demo: boolean) → { label, tooltip }`; `useAppInfoStore()` с `demoMode`, `loaded`, `load()`.

- [ ] **Step 1: Падающие тесты**

Добавить в конец `describe` в `src/utils/__tests__/appVersion.spec.js`:

```js
  it('демо-стенд: дописывает «демо-версия»', () => {
    const r = formatAppVersion({ version: '1.18.0', buildDate: '2026-10-19T12:00:00Z', commit: 'abc1234', demo: true })
    expect(r.label).toBe('v1.18.0 · 19.10.2026 · демо-версия')
    expect(r.tooltip).toBe('commit abc1234')
  })
```

Создать `src/stores/__tests__/appInfo.spec.js`:

```js
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// Стор тянет apiClient → authStore → router (нужен window) — api-модуль мокаем.
const { get } = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock('@/api/appInfo', () => ({ appInfoApi: { get } }))

const { useAppInfoStore } = await import('@/stores/appInfo')

describe('appInfo store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    get.mockReset()
  })

  it('demoMode берётся из ответа сервера', async () => {
    get.mockResolvedValue({ data: { demoMode: true } })
    const store = useAppInfoStore()
    await store.load()
    expect(store.demoMode).toBe(true)
    expect(store.loaded).toBe(true)
  })

  it('ошибка запроса — не демо', async () => {
    get.mockRejectedValue(new Error('network'))
    const store = useAppInfoStore()
    await store.load()
    expect(store.demoMode).toBe(false)
    expect(store.loaded).toBe(true)
  })

  it('сведения грузятся один раз', async () => {
    get.mockResolvedValue({ data: { demoMode: false } })
    const store = useAppInfoStore()
    await store.load()
    await store.load()
    expect(get).toHaveBeenCalledTimes(1)
  })
})
```

- [ ] **Step 2: Тесты падают**

Run: `cd Wintime-Control-Frontend && npx vitest run src/utils/__tests__/appVersion.spec.js src/stores/__tests__/appInfo.spec.js`
Expected: FAIL — подпись без « · демо-версия»; `Failed to resolve import "@/stores/appInfo"`.

- [ ] **Step 3: Реализация**

`src/utils/appVersion.js` (весь файл):

```js
// Подпись версии приложения под логотипом. Дата — в поясе браузера.
// demo — демо-стенд (GET /api/app-info): к подписи дописывается «демо-версия».
export function formatAppVersion({ version, buildDate, commit, demo = false }) {
  let date = 'dev'
  if (buildDate) {
    const d = new Date(buildDate)
    const pad = (n) => String(n).padStart(2, '0')
    date = `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`
  }
  return {
    label: `v${version} · ${date}${demo ? ' · демо-версия' : ''}`,
    tooltip: commit ? `commit ${commit}` : '',
  }
}
```

`src/utils/buildInfo.js` (весь файл):

```js
/* global __APP_VERSION__, __BUILD_DATE__, __GIT_COMMIT__ */
// Константы вшиваются Vite (define в vite.config.js) при сборке.
import { formatAppVersion } from './appVersion'

const build = {
  version: __APP_VERSION__,
  buildDate: __BUILD_DATE__,
  commit: __GIT_COMMIT__,
}

// Подпись версии; demo — признак демо-стенда из стора appInfo.
export const appVersionFor = (demo) => formatAppVersion({ ...build, demo })
```

`src/api/appInfo.js`:

```js
import apiClient from './client'

// Публичные сведения о стенде (без авторизации): сейчас — только признак демо-режима.
export const appInfoApi = {
  get() {
    return apiClient.get('/app-info')
  }
}
```

`src/stores/appInfo.js`:

```js
import { defineStore } from 'pinia'
import { appInfoApi } from '@/api/appInfo'

// Сведения о стенде с сервера (GET /api/app-info). Грузятся один раз; при ошибке — не демо.
export const useAppInfoStore = defineStore('appInfo', {
  state: () => ({
    demoMode: false,
    loaded: false
  }),

  actions: {
    async load() {
      if (this.loaded) return
      try {
        const { data } = await appInfoApi.get()
        this.demoMode = data?.demoMode === true
      } catch {
        this.demoMode = false
      } finally {
        this.loaded = true
      }
    }
  }
})
```

`src/layouts/DefaultLayout.vue` — в `<script setup>` заменить строку
`import { appVersion } from '@/utils/buildInfo'` на:

```js
import { appVersionFor } from '@/utils/buildInfo'
import { useAppInfoStore } from '@/stores/appInfo'
```

после `const { canAccess } = usePermissions()` добавить:

```js
const appInfo = useAppInfoStore()
const appVersion = computed(() => appVersionFor(appInfo.demoMode))
```

и первой строкой в существующий `onMounted(() => {` добавить `appInfo.load()`. Шаблон (строка 11,
`{{ appVersion.label }}` и `:title="appVersion.tooltip"`) не меняется — computed разворачивается в шаблоне.

`src/views/auth/LoginView.vue`:
- после `<p class="text-gray-600">Управление цехом ТПА</p>` добавить
  `<p class="text-xs text-gray-400 mt-1" :title="appVersion.tooltip">{{ appVersion.label }}</p>`;
- блок демо-учёток:

```html
      <!-- Демо-учётки: только на демо-стенде (DemoMode), у клиентов таких аккаунтов нет -->
      <div v-if="appInfo.demoMode" class="mt-6 p-4 bg-gray-50 rounded-lg text-sm">
```

  (вместо `<!-- Демо-учётки (для разработки) -->` и `<div class="mt-6 p-4 bg-gray-50 rounded-lg text-sm">`);
- в `<script setup>` заменить `import { ref, reactive } from 'vue'` на
  `import { ref, reactive, computed, onMounted } from 'vue'`, после импорта `ElMessage` добавить

```js
import { appVersionFor } from '@/utils/buildInfo'
import { useAppInfoStore } from '@/stores/appInfo'
```

  и после `const authStore = useAuthStore()`:

```js
const appInfo = useAppInfoStore()
const appVersion = computed(() => appVersionFor(appInfo.demoMode))
onMounted(() => appInfo.load())
```

- [ ] **Step 4: Тесты и сборка проходят**

Run: `cd Wintime-Control-Frontend && npx vitest run && npm run build`
Expected: все тесты PASS; сборка без ошибок (`grep -rn "appVersion }" src` — пусто: старый экспорт больше нигде не импортируется).

- [ ] **Step 5: Commit**

```bash
git add Wintime-Control-Frontend/src
git commit -m "feat(ui): demo-version label and demo accounts only in demo mode"
```

---

### Task 3: Проверки сида и локальный прогон (красная фаза)

**Files:**
- Create: `deploy/demo/seed/check_seed.sql`
- Create: `deploy/demo/test-seed.ps1`

**Interfaces:**
- Produces: `pwsh deploy/demo/test-seed.ps1` — поднимает временный postgres, применяет схему,
  вставляет пользователя `adjuster`, грузит `deploy/demo/seed/seed_demo.sql`, печатает строки
  `проверка|число` из `check_seed.sql` и завершается ошибкой при любом числе ≠ 0. Используется в Task 4.

- [ ] **Step 1: `check_seed.sql`**

```sql
-- Проверки инвариантов демо-сида (deploy/demo/test-seed.ps1). Каждая строка — «проверка|нарушений»,
-- всё должно быть 0. Запускать сразу после сида: «будущее» — относительно now() проверки.
SELECT 'future_rows', (
      (SELECT count(*) FROM "ShiftTasks"
        WHERE GREATEST("IssuedAt","SetupStartedAt","MoldVerifiedAt","StartedAt","CompletedAt","ClosedAt",
                       "CreatedAt","UpdatedAt") > now()
           OR ("PlannedDate" + COALESCE("PlannedSetupStartTime", time '00:00')) AT TIME ZONE 'Europe/Moscow' > now())
    + (SELECT count(*) FROM "ImmCycles"        WHERE GREATEST("StartTime","EndTime","CreatedAt") > now())
    + (SELECT count(*) FROM "ImmStatusHistory" WHERE GREATEST("ChangedAt","EndedAt") > now())
    + (SELECT count(*) FROM "Events"           WHERE GREATEST("StartTime","EndTime","CreatedAt") > now())
    + (SELECT count(*) FROM "Telemetry"        WHERE "Timestamp" > now())
    + (SELECT count(*) FROM "Orders"           WHERE GREATEST("OrderDate","DueDate","CreatedAt") > now())
    + (SELECT count(*) FROM "UnplannedRuns"    WHERE GREATEST("StartTime","ClosedAt","CreatedAt") > now()))
UNION ALL SELECT 'type_without_product', count(*) FROM "ProductTypes" pt
  WHERE NOT EXISTS (SELECT 1 FROM "Products" p WHERE p."ProductTypeId" = pt."Id")
UNION ALL SELECT 'active_type_without_active_product', count(*) FROM "ProductTypes" pt
  WHERE pt."IsActive" AND NOT EXISTS (SELECT 1 FROM "Products" p WHERE p."ProductTypeId" = pt."Id" AND p."IsActive")
UNION ALL SELECT 'full_article_clash', count(*) FROM "Products" p JOIN "ProductTypes" pt ON pt."Article" = p."FullArticle"
UNION ALL SELECT 'mold_without_type', count(*) FROM "Molds" WHERE "ProductTypeId" IS NULL
UNION ALL SELECT 'task_product_not_of_mold_type', count(*) FROM "ShiftTasks" t
  JOIN "Molds" m ON m."Id" = t."MoldId" JOIN "Products" p ON p."Id" = t."ProductId"
  WHERE p."ProductTypeId" <> m."ProductTypeId"
UNION ALL SELECT 'task_order_product_mismatch', count(*) FROM "ShiftTasks" t
  JOIN "Orders" o ON o."Id" = t."OrderId" WHERE o."ProductId" <> t."ProductId"
UNION ALL SELECT 'task_imm_incompatible', count(*) FROM "ShiftTasks" t
  WHERE EXISTS (SELECT 1 FROM "MoldCompatibleImms" c WHERE c."MoldId" = t."MoldId")
    AND NOT EXISTS (SELECT 1 FROM "MoldCompatibleImms" c WHERE c."MoldId" = t."MoldId" AND c."ImmId" = t."ImmId")
UNION ALL SELECT 'task_without_cycle_norm', count(*) FROM "ShiftTasks" WHERE COALESCE("PlannedFullCycleSeconds", 0) <= 0
-- ТПА останавливается по числу смыканий ⌈План/Гнёзда⌉, поэтому выпуск может превысить план не больше чем на гнёзда − 1
UNION ALL SELECT 'output_over_plan', count(*) FROM "ShiftTasks" t JOIN "Molds" m ON m."Id" = t."MoldId"
  WHERE t."ActualQuantity" >= t."PlanQuantity" + m."Cavities"
UNION ALL SELECT 'imm_multiple_active_tasks', count(*) FROM
  (SELECT "ImmId" FROM "ShiftTasks" WHERE "Status" IN (2, 5) GROUP BY "ImmId" HAVING count(*) > 1) x
UNION ALL SELECT 'issued_without_adjuster', count(*) FROM "ShiftTasks" WHERE "Status" = 1 AND "PersonnelId" IS NULL
UNION ALL SELECT 'demo_adjuster_issued_not_7', abs(7 - (SELECT count(*) FROM "ShiftTasks" t
  JOIN "Users" u ON u."Id" = t."PersonnelId" WHERE u."UserName" = 'adjuster' AND t."Status" = 1))
UNION ALL SELECT 'imm_open_status_not_1', count(*) FROM "Imms" i
  WHERE (SELECT count(*) FROM "ImmStatusHistory" h WHERE h."ImmId" = i."Id" AND h."EndedAt" IS NULL) <> 1
UNION ALL SELECT 'status_overlap', count(*) FROM
  (SELECT "EndedAt", lead("ChangedAt") OVER (PARTITION BY "ImmId" ORDER BY "ChangedAt") AS nxt
   FROM "ImmStatusHistory") s
  WHERE s.nxt < COALESCE(s."EndedAt", 'infinity')
UNION ALL SELECT 'cycle_outside_task_window', count(*) FROM "ImmCycles" c JOIN "ShiftTasks" t ON t."Id" = c."TaskId"
  WHERE c."StartTime" < t."StartedAt" OR c."EndTime" > COALESCE(t."CompletedAt", now())
UNION ALL SELECT 'completed_order_with_open_task', count(*) FROM "Orders" o
  JOIN "ShiftTasks" t ON t."OrderId" = o."Id" WHERE o."Status" = 1 AND t."Status" NOT IN (3, 4)
UNION ALL SELECT 'active_imm_without_recent_telemetry', count(*) FROM "Imms" i
  WHERE i."IsActive" AND NOT EXISTS
    (SELECT 1 FROM "Telemetry" tl WHERE tl."ImmId" = i."Id" AND tl."Timestamp" > now() - interval '3 days')
UNION ALL SELECT 'no_recent_unclassified_downtime',
  CASE WHEN EXISTS (SELECT 1 FROM "Events" WHERE "ReasonId" IS NULL AND "StartTime" > now() - interval '3 hours')
       THEN 0 ELSE 1 END
UNION ALL SELECT 'too_few_orders', CASE WHEN (SELECT count(*) FROM "Orders") >= 10 THEN 0 ELSE 1 END
UNION ALL SELECT 'heartbeat_not_t0',
  CASE WHEN (SELECT "LastHeartbeatAt" FROM "AppHeartbeat" WHERE "Id" = 1) <= now() THEN 0 ELSE 1 END;
```

- [ ] **Step 2: `test-seed.ps1`**

```powershell
# Локальная проверка демо-сида: временный postgres -> схема из миграций текущих исходников ->
# пользователь adjuster (как его создаёт API в DemoMode) -> seed\seed_demo.sql -> seed\check_seed.sql.
# Каждая проверка должна дать 0 нарушений. Контейнер удаляется в любом случае.
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$ScriptDir   = $PSScriptRoot
$ControlRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$SeedDir     = Join-Path $ScriptDir "seed"
$Container   = "wtctrl-demo-seed-test"
$Db          = "WintimeControlDb"
$Tmp         = Join-Path ([IO.Path]::GetTempPath()) "wtctrl-demo-seed-test"

New-Item -ItemType Directory -Force $Tmp | Out-Null

Write-Host "==> 1/5 SQL-скрипт миграций..."
Push-Location $ControlRoot
dotnet ef migrations script --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API `
    -o (Join-Path $Tmp "schema.sql")
Pop-Location

# Демо-аккаунт adjuster: в проде его создаёт API при DemoMode=true, сид ищет его по UserName
$adjusterSql = @'
INSERT INTO "Users" ("Id","FullName","Role","IsActive","UserName","NormalizedUserName","EmailConfirmed",
                     "PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount")
VALUES ('test-demo-adjuster','Наладчик Тестовый',2,true,'adjuster','ADJUSTER',false,false,false,true,0);
'@
[IO.File]::WriteAllText((Join-Path $Tmp "adjuster.sql"), $adjusterSql)

Write-Host "==> 2/5 Временный postgres..."
$PSNativeCommandUseErrorActionPreference = $false   # контейнера может не быть — это норма
docker rm -f $Container 2>$null | Out-Null
$PSNativeCommandUseErrorActionPreference = $true
docker run -d --name $Container -e POSTGRES_PASSWORD=test -e POSTGRES_DB=$Db postgres:16-alpine `
    postgres -c shared_buffers=128MB | Out-Null
try {
    # -h 127.0.0.1: на время init-скриптов образ слушает только unix-сокет, по TCP — уже рабочий сервер
    $ready = $false
    $PSNativeCommandUseErrorActionPreference = $false
    for ($i = 0; $i -lt 60; $i++) {
        docker exec $Container pg_isready -h 127.0.0.1 -U postgres -d $Db 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    $PSNativeCommandUseErrorActionPreference = $true
    if (-not $ready) { throw "postgres не поднялся за 60 с" }

    docker cp (Join-Path $Tmp "schema.sql")         "${Container}:/tmp/schema.sql"
    docker cp (Join-Path $Tmp "adjuster.sql")       "${Container}:/tmp/adjuster.sql"
    docker cp (Join-Path $SeedDir "seed_demo.sql")  "${Container}:/tmp/seed.sql"
    docker cp (Join-Path $SeedDir "check_seed.sql") "${Container}:/tmp/check.sql"

    Write-Host "==> 3/5 Схема и демо-аккаунт adjuster..."
    docker exec $Container psql -q -U postgres -d $Db -v ON_ERROR_STOP=1 -f /tmp/schema.sql | Out-Null
    docker exec $Container psql -q -U postgres -d $Db -v ON_ERROR_STOP=1 -f /tmp/adjuster.sql | Out-Null

    Write-Host "==> 4/5 Сид..."
    $sw = [Diagnostics.Stopwatch]::StartNew()
    docker exec $Container psql -q -U postgres -d $Db -v ON_ERROR_STOP=1 -f /tmp/seed.sql
    $size = docker exec $Container psql -U postgres -d $Db -At -c "SELECT pg_size_pretty(pg_database_size(current_database()))"
    Write-Host ("    сид за {0:mm\:ss}, размер БД {1}" -f $sw.Elapsed, $size)

    Write-Host "==> 5/5 Проверки..."
    $rows = docker exec $Container psql -U postgres -d $Db -At -F "|" -v ON_ERROR_STOP=1 -f /tmp/check.sql
    $failed = @()
    foreach ($row in $rows) {
        $name, $count = $row -split '\|'
        Write-Host ("    {0,-40} {1}" -f $name, $count)
        if ([long]$count -ne 0) { $failed += $name }
    }
    if ($failed.Count -gt 0) { throw "Нарушены проверки: $($failed -join ', ')" }
    Write-Host "Сид в порядке."
}
finally {
    docker rm -f $Container | Out-Null
}
```

- [ ] **Step 3: Красная фаза — сида ещё нет**

Run: `pwsh deploy/demo/test-seed.ps1`
Expected: FAIL на шаге `docker cp ... seed_demo.sql` — файла нет. Контейнер удалён (`docker ps -a --filter name=wtctrl-demo-seed-test` — пусто).

- [ ] **Step 4: Commit**

```bash
git add deploy/demo/seed/check_seed.sql deploy/demo/test-seed.ps1
git commit -m "test(demo): seed invariant checks and local seed test harness"
```

---

### Task 4: `seed_demo.sql` (зелёная фаза)

**Files:**
- Create: `deploy/demo/seed/seed_demo.sql`

**Interfaces:**
- Consumes: `test-seed.ps1`, `check_seed.sql` (Task 3); схема из текущих миграций; пользователь `UserName = 'adjuster'`.
- Produces: `seed_demo.sql` для `reset-db.sh` (Task 5). Параметры — в `_cfg` в начале файла (`days`, `ppc`).

**Модель данных (для понимания кода ниже):**
- `t0` — начало загрузки (до минуты); `p_start` — местная полночь `t0 − days` (Europe/Moscow).
- ТПА: 1–4 `run` (идёт выпуск), 5 `setup`, 6–11 `free`, 12 `arch` (история 18 суток, потом «Нет связи», в архиве).
- Пулы ПФ на ТПА — `_pool`; у ПФ 1–7 список совместимости = ТПА их пулов, у ПФ 8–15 пусто (любой ТПА).
- История: пауза 30–300 мин (иногда «работа без задания») → задание: наладка 60–150 мин (`Manual`) →
  блоки `Auto` 40–240 мин с циклами, между ними короткие остановки / аварии / долгие простои (с событиями) →
  остановка по плану (`Idle` 5–40 мин, > 15 мин — неразмеченный простой) → `CompletedAt`.
  Завершено > 1 суток назад — `Closed`, иначе `Completed`.
- Срез «сейчас»: `run` — текущее `InProgress` (ТПА-1 — у демо-`adjuster`; на ТПА-2 за ~1,5 ч до `t0`
  неразмеченный простой 20 мин); `setup` — `Setup` у демо-`adjuster`; `free` — последняя запись статуса открыта;
  очередь `_queue`: 7 `Issued` демо-`adjuster` на ТПА-6…11 + 3 задания других наладчиков на ТПА-1…3.
- Выпуск/брак/вес заданий — из циклов; заказы — по изделиям заданий (две «волны» на изделие, ~¾ заданий
  с заказом) + отменённый и новый без заданий; телеметрия — `points_per_cycle` точек × 5 датчиков на цикл.
- `AppHeartbeat` = `t0`: при старте API `ImmStatusStartupService` закроет открытые статусы записью
  `Offline` сразу после `t0`, а не задним числом.

- [ ] **Step 1: Написать сид**

`deploy/demo/seed/seed_demo.sql`:

```sql
-- ============================================================
-- Wintime Control — данные публичного демо (control-demo.ru)
-- Период   : последние _cfg.days суток до t0 = момента загрузки. Ничего позже t0.
-- Состав   : 2 шаблона, 12 ТПА (ТПА-12 в архиве), 15 ПФ, 10 типов изделий (тип 10 в архиве),
--            6 сотрудников сида (без пароля — войти нельзя), заказы, задания, циклы, простои, телеметрия.
-- Логины   : только демо-аккаунты API (admin / manager / adjuster / emulator). Сид их не создаёт;
--            демо-adjuster получает задания (ищется по UserName).
-- Запуск   : свежая БД после первого старта API (миграции + демо-аккаунты), API остановлен — reset-db.sh:
--   docker exec -i wtctrl-demo-postgres psql -U postgres -d WintimeControlDb -v ON_ERROR_STOP=1 -q < seed_demo.sql
-- Параметры: _cfg ниже — days (история, сутки), ppc (точек телеметрии на датчик на цикл, 1..6).
--            Объём при 30/6: ~0,45 млн циклов, ~13 млн строк телеметрии, БД ~3 ГБ.
-- ============================================================
SET synchronous_commit = off;

CREATE TEMP TABLE _cfg AS
SELECT t0,
       ((date_trunc('day', t0 AT TIME ZONE 'Europe/Moscow') - days * interval '1 day') AT TIME ZONE 'Europe/Moscow') AS p_start,
       days, ppc
FROM (SELECT date_trunc('minute', now()) AS t0, 30 AS days, 6 AS ppc) s;

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM "Imms") OR EXISTS (SELECT 1 FROM "ShiftTasks") THEN
    RAISE EXCEPTION 'БД не пустая — сид грузится только в свежую БД (reset-db.sh)';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "UserName" = 'adjuster') THEN
    RAISE EXCEPTION 'Нет демо-аккаунта adjuster — сначала запустите API с DemoMode=true';
  END IF;
END $$;

-- ============================================================
-- Справочники
-- ============================================================
INSERT INTO "Templates" ("Id","Name","Manufacturer","Model","Version","Author","JsonConfig","IsActive","CreatedAt","UpdatedAt")
SELECT v.id::uuid, v.name, v.maker, v.model, '1.0', 'Wintime',
       '{"device_timeout_seconds":30,"sensors":['
       || '{"name":"Температура расплава, °C","field":"MeltTemp","type":"float","threshold":0.5},'
       || '{"name":"Давление впрыска, бар","field":"InjectionPressure","type":"float","threshold":5},'
       || '{"name":"Положение шнека, мм","field":"ScrewPosition","type":"float","threshold":0.5},'
       || '{"name":"Температура формы, °C","field":"MoldTemp","type":"float","threshold":0.2},'
       || '{"name":"Усилие смыкания, кН","field":"ClampForce","type":"float","threshold":10}]}',
       true, c.t0 - interval '90 days', c.t0 - interval '90 days'
FROM _cfg c,
     (VALUES ('a3000000-0000-0000-0000-000000000001','Haitian Mars III','Haitian','MA III'),
             ('a3000000-0000-0000-0000-000000000002','KraussMaffei CX','KraussMaffei','CX')) v(id, name, maker, model);

CREATE TEMP TABLE _imm AS
SELECT n, ('b3000000-0000-0000-0000-0000000000' || lpad(n::text, 2, '0'))::uuid AS id,
       name, tpl, kind, cur_mold, p_short, p_alarm
FROM (VALUES
  (1, 'ТПА-01 Haitian MA1600',      1, 'run',   2,    0.80, 0.97),
  (2, 'ТПА-02 Haitian MA1600',      1, 'run',   4,    0.55, 0.88),
  (3, 'ТПА-03 Haitian MA2500',      1, 'run',   5,    0.75, 0.95),
  (4, 'ТПА-04 Haitian MA2500',      1, 'run',   7,    0.75, 0.95),
  (5, 'ТПА-05 Haitian MA3600',      1, 'setup', 8,    0.75, 0.95),
  (6, 'ТПА-06 Haitian MA1200',      1, 'free',  NULL, 0.80, 0.96),
  (7, 'ТПА-07 Haitian MA1200',      1, 'free',  NULL, 0.75, 0.95),
  (8, 'ТПА-08 Haitian MA900',       1, 'free',  NULL, 0.70, 0.93),
  (9, 'ТПА-09 KraussMaffei CX 160', 2, 'free',  NULL, 0.60, 0.90),
  (10,'ТПА-10 KraussMaffei CX 200', 2, 'free',  NULL, 0.80, 0.97),
  (11,'ТПА-11 KraussMaffei CX 300', 2, 'free',  NULL, 0.75, 0.95),
  (12,'ТПА-12 KraussMaffei CX 80',  2, 'arch',  NULL, 0.75, 0.95)) v(n, name, tpl, kind, cur_mold, p_short, p_alarm);

INSERT INTO "Imms" ("Id","Name","InventoryNumber","TemplateId","IsActive","CommissioningDate","CreatedAt")
SELECT i.id, i.name, 'ИН-' || lpad(i.n::text, 3, '0'),
       ('a3000000-0000-0000-0000-00000000000' || i.tpl)::uuid, i.kind <> 'arch',
       timestamptz '2018-03-01 00:00+00' + i.n * interval '150 days', c.t0 - interval '90 days'
FROM _imm i, _cfg c;

INSERT INTO "ProductTypes" ("Id","Article","Name","IsActive","CreatedAt")
SELECT ('e3000000-0000-0000-0000-0000000000' || lpad(v.n::text, 2, '0'))::uuid, v.art, v.name, v.n <> 10,
       c.t0 - interval '90 days'
FROM _cfg c,
     (VALUES (1,'КР28','Крышка флип-топ 28 мм'),(2,'КП38','Колпачок 38 мм'),(3,'КФ01','Корпус фильтра'),
             (4,'РВ05','Ручка ведра'),(5,'ВД05','Ведро 5 л'),(6,'КВ05','Крышка ведра 5 л'),
             (7,'ЯО01','Ящик овощной'),(8,'ВТ20','Втулка d20'),(9,'ЗГ20','Заглушка 20×20'),
             (10,'ШБ08','Шайба 8 мм')) v(n, art, name);

-- У каждого типа есть изделие «0» (по умолчанию); у части — варианты. Полный артикул ТИП.ЧАСТЬ (ADR-0020).
INSERT INTO "Products" ("Id","ProductTypeId","Article","Name","FullArticle","IsActive","CreatedAt")
SELECT gen_random_uuid(), pt."Id", v.part, v.name, pt."Article" || '.' || v.part, pt."IsActive", pt."CreatedAt"
FROM "ProductTypes" pt
JOIN (VALUES (1,'0','По умолчанию'),(1,'Б','Белая'),(1,'Ч','Чёрная'),
             (2,'0','По умолчанию'),(2,'К','Красный'),
             (3,'0','По умолчанию'),
             (4,'0','По умолчанию'),(4,'С','Серая'),
             (5,'0','По умолчанию'),(6,'0','По умолчанию'),(7,'0','По умолчанию'),
             (8,'0','По умолчанию'),(8,'Н','Натуральная'),
             (9,'0','По умолчанию'),(10,'0','По умолчанию')) v(n, part, name)
  ON pt."Id" = ('e3000000-0000-0000-0000-0000000000' || lpad(v.n::text, 2, '0'))::uuid;

-- ct — длительность цикла, с; inj — время литья, с; pw/rw — вес детали/литника, г
CREATE TEMP TABLE _mold AS
SELECT n, ('c3000000-0000-0000-0000-0000000000' || lpad(n::text, 2, '0'))::uuid AS id,
       ('e3000000-0000-0000-0000-0000000000' || lpad(t::text, 2, '0'))::uuid AS type_id,
       name, cav, ct, inj, pw, rw
FROM (VALUES
  (1, 1, 'Крышка флип-топ 28 мм',         8, 26,  4,   3.2,  4.0),
  (2, 1, 'Крышка флип-топ 28 мм (16 гн.)',16, 30,  4,   3.2,  6.0),
  (3, 2, 'Колпачок 38 мм',               12, 28,  4,   4.1,  6.0),
  (4, 3, 'Корпус фильтра',                2, 48,  8,  38.0,  9.0),
  (5, 4, 'Ручка ведра',                   4, 34,  5,   6.5,  5.0),
  (6, 5, 'Ведро 5 л',                     1, 55,  9, 210.0, 12.0),
  (7, 6, 'Крышка ведра 5 л',              1, 42,  7,  95.0,  8.0),
  (8, 7, 'Ящик овощной',                  1, 70, 12, 820.0, 25.0),
  (9, 8, 'Втулка d20',                   32, 25,  3,   1.9,  7.0),
  (10,9, 'Заглушка 20×20',               24, 24,  3,   1.5,  5.0),
  (11,2, 'Колпачок 38 мм (8 гн.)',        8, 27,  4,   4.1,  5.0),
  (12,3, 'Корпус фильтра (4 гн.)',        4, 52,  8,  38.0, 14.0),
  (13,8, 'Втулка d20 (16 гн.)',          16, 24,  3,   1.9,  5.0),
  (14,9, 'Заглушка 20×20 (32 гн.)',      32, 26,  3,   1.5,  6.0),
  (15,10,'Шайба 8 мм',                   48, 22,  3,   0.4,  3.0)) v(n, t, name, cav, ct, inj, pw, rw);

INSERT INTO "Molds" ("Id","FormId","Name","Cavities","PartWeightGrams","RunnerWeightGrams","MaxResourceCycles",
                     "To1Cycles","To2Cycles","IsActive","MoldStatus","ProductTypeId","CreatedAt")
SELECT m.id, 'ПФ-' || (200 + m.n), m.name, m.cav, m.pw, m.rw, 1000000, 300000, 600000, m.n <> 15, 0, m.type_id,
       c.t0 - interval '90 days'
FROM _mold m, _cfg c;

-- Какие ПФ ставятся на какой ТПА (по ним идёт история)
CREATE TEMP TABLE _pool (imm int, mold int);
INSERT INTO _pool VALUES (1,1),(1,2),(2,3),(2,4),(3,5),(4,6),(4,7),(5,8),(6,9),(6,1),(7,10),(8,11),(8,3),
                         (9,12),(10,13),(10,6),(11,14),(12,15),(12,12);

-- Совместимость (ADR-0018): у ПФ 1–7 список = ТПА их пулов, у остальных пусто (любой ТПА)
INSERT INTO "MoldCompatibleImms" ("MoldId","ImmId")
SELECT DISTINCT m.id, i.id FROM _pool p JOIN _mold m ON m.n = p.mold JOIN _imm i ON i.n = p.imm WHERE p.mold <= 7;

INSERT INTO "DowntimeReasons" ("Id","Name","Type","IsActive","CreatedAt")
SELECT v.id::uuid, v.name, v.type, true, c.t0 - interval '90 days'
FROM _cfg c,
     (VALUES ('d3000000-0000-0000-0000-000000000001','Неисправность оборудования',   'Emergency'),
             ('d3000000-0000-0000-0000-000000000002','Неисправность пресс-формы',    'Emergency'),
             ('d3000000-0000-0000-0000-000000000003','Ожидание сырья',               'Planned'),
             ('d3000000-0000-0000-0000-000000000004','Отсутствие оператора',         'Planned'),
             ('d3000000-0000-0000-0000-000000000005','Подналадка технологии',        'Planned'),
             ('d3000000-0000-0000-0000-000000000006','Смена материала / цвета',      'Planned')) v(id, name, type);

-- Две 12-часовые смены 08:00–20:00 и 20:00–08:00 по Москве
INSERT INTO "Shifts" ("Id","StartMinutes","DurationMinutes","BreakStartMinutes","BreakDurationMinutes","TimeZoneId","CreatedAt")
SELECT gen_random_uuid(), v.s, 720, v.b, 30, 'Europe/Moscow', c.t0 - interval '90 days'
FROM _cfg c, (VALUES (480, 720), (1200, 0)) v(s, b);

UPDATE "PlanningDefaults" SET "SetupMinutes" = 90, "ReleaseBufferFixedMinutes" = 30, "ReleaseBufferPercent" = 10,
                              "UpdatedAt" = (SELECT t0 FROM _cfg)
WHERE "Id" = 1;

-- Сотрудники сида: без пароля (PasswordHash = NULL) — войти под ними нельзя, нужны для отчётов
INSERT INTO "Users" ("Id","FullName","Role","EmployeeId","IsActive","UserName","NormalizedUserName","Email","NormalizedEmail",
                     "EmailConfirmed","PasswordHash","SecurityStamp","ConcurrencyStamp",
                     "PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount")
SELECT v.id, v.fio, v.role, v.emp, true, v.login, upper(v.login),
       v.login || '@control-demo.local', upper(v.login || '@control-demo.local'), true,
       NULL, gen_random_uuid()::text, gen_random_uuid()::text, false, false, true, 0
FROM (VALUES ('f3000000-0000-0000-0000-000000000001','Ковалёв Сергей Петрович',  1,'М-001','kovalev'),
             ('f3000000-0000-0000-0000-000000000002','Сидоров Олег Иванович',    2,'Н-002','sidorov'),
             ('f3000000-0000-0000-0000-000000000003','Гусев Павел Андреевич',    2,'Н-003','gusev'),
             ('f3000000-0000-0000-0000-000000000004','Морозов Илья Викторович',  2,'Н-004','morozov'),
             ('f3000000-0000-0000-0000-000000000005','Лебедев Артём Сергеевич',  2,'Н-005','lebedev'),
             ('f3000000-0000-0000-0000-000000000006','Орлов Денис Николаевич',   2,'Н-006','orlov')) v(id, fio, role, emp, login);

-- Окна без связи (часы от p_start, длительность, ч)
CREATE TEMP TABLE _win AS
SELECT v.imm, c.p_start + v.h * interval '1 hour' AS a, c.p_start + (v.h + v.dur) * interval '1 hour' AS b,
       'Offline'::text AS status
FROM _cfg c, (VALUES (3, 12 * 24 + 10, 4), (7, 20 * 24, 20), (9, 5 * 24 + 14, 3)) v(imm, h, dur);

-- Очередь заданий «на сейчас»: who = 'demo' — демо-adjuster; ago — плановое начало, мин до t0
CREATE TEMP TABLE _queue (imm int, mold int, who text, status int, ago int, shots int);
INSERT INTO _queue VALUES
  (6, 9, 'demo', 1, 300,  800), (6, 1, 'demo', 1, 240, 1200), (7, 10, 'demo', 1, 200,  900),
  (8, 11,'demo', 1, 150, 1500), (9, 12,'demo', 1, 120,  600), (10, 13,'demo', 1,  90, 1000),
  (11,14,'demo', 1,  45,  700),
  (1, 2, 'f3000000-0000-0000-0000-000000000002', 0, 30, 1500),
  (2, 3, 'f3000000-0000-0000-0000-000000000003', 1, 60, 1000),
  (3, 5, 'f3000000-0000-0000-0000-000000000004', 0, 20,  800);

-- ============================================================
-- Вспомогательные функции (живут только в этой сессии)
-- ============================================================
CREATE FUNCTION pg_temp.rmin(lo int, hi int) RETURNS interval LANGUAGE sql VOLATILE AS
$$ SELECT ((lo + random() * (hi - lo))::int) * interval '1 minute' $$;

-- Запись статуса; b = NULL — открытая (текущая) запись
CREATE FUNCTION pg_temp.st(p_imm uuid, p_status text, a timestamptz, b timestamptz) RETURNS void LANGUAGE sql AS
$$ INSERT INTO "ImmStatusHistory" ("ImmId","Status","ChangedAt","EndedAt")
   SELECT p_imm, p_status, a, b WHERE b IS NULL OR b > a $$;

-- Простой; p_reason = NULL — неразмеченный (причину укажет наладчик)
CREATE FUNCTION pg_temp.ev(p_imm uuid, p_task uuid, p_adj text, p_reason int, a timestamptz, b timestamptz)
RETURNS void LANGUAGE sql AS
$$ INSERT INTO "Events" ("Id","ImmId","EventType","ReasonId","ReasonName","StartTime","EndTime","PersonnelId","TaskId","IsAuto","CreatedAt")
   SELECT gen_random_uuid(), p_imm, 0, r."Id", r."Name", a, b, p_adj, p_task, true, a
   FROM (SELECT 1) one
   LEFT JOIN "DowntimeReasons" r ON r."Id" = ('d3000000-0000-0000-0000-00000000000' || p_reason)::uuid
   WHERE b > a $$;

-- Циклы подряд с a, период p_ct + p_gap с, не больше p_max и не дальше b. Возвращает число циклов.
-- PauseDurationMs — от конца предыдущего цикла серии (у первого — NULL, ADR-0017).
CREATE FUNCTION pg_temp.cyc(p_imm uuid, p_task uuid, p_mold uuid, p_cav int, p_ct int, p_inj int, p_gap int,
                            a timestamptz, b timestamptz, p_no int, p_max int) RETURNS int LANGUAGE plpgsql AS
$$
DECLARE n int := LEAST(floor(extract(epoch FROM (b - a)) / (p_ct + p_gap))::int, p_max);
BEGIN
  IF n <= 0 THEN RETURN 0; END IF;
  INSERT INTO "ImmCycles" ("Id","ImmId","TaskId","MoldId","StartTime","EndTime","DurationSeconds","IsSuccessful",
                           "Cavities","InjectionDurationMs","InjectionStartTime","PauseDurationMs","Cushion",
                           "CycleNumber","CreatedAt")
  SELECT gen_random_uuid(), p_imm, p_task, p_mold,
         s.st, s.st + s.d * interval '1 second', s.d, random() > 0.005, p_cav,
         p_inj * 1000 - 200 + (random() * 400)::int,
         s.st + interval '1 second',
         CASE WHEN s.g = 0 THEN NULL ELSE ((p_ct + p_gap - s.prev_d) * 1000)::int END,
         round((3 + random() * 3)::numeric, 2),
         p_no + s.g + 1,
         s.st + s.d * interval '1 second'
  FROM (SELECT g, st, d, lag(d) OVER (ORDER BY g) AS prev_d
        FROM (SELECT g, a + g * (p_ct + p_gap) * interval '1 second' AS st,
                     p_ct - (random() < 0.3)::int AS d
              FROM generate_series(0, n - 1) g) x) s;
  RETURN n;
END $$;

-- Задание. Изделие — случайное из типа ПФ; плановое начало = p_planned (дата + время по часам завода);
-- норматив наладки/буфер — снапшот PlanningDefaults (90 / 30 / 10).
CREATE FUNCTION pg_temp.task(p_imm uuid, p_mold int, p_adj text, p_plan int, p_status int, p_mode int,
                             p_planned timestamptz, p_issued timestamptz, p_setup timestamptz, p_started timestamptz,
                             p_completed timestamptz, p_closed timestamptz) RETURNS uuid LANGUAGE plpgsql AS
$$
DECLARE
  v_id      uuid := gen_random_uuid();
  m         record;
  v_prod    uuid;
  loc       timestamp := p_planned AT TIME ZONE 'Europe/Moscow';
  v_created timestamptz := COALESCE(p_issued, p_planned) - interval '1 day';
BEGIN
  SELECT * INTO m FROM _mold WHERE n = p_mold;
  SELECT "Id" INTO v_prod FROM "Products" WHERE "ProductTypeId" = m.type_id ORDER BY random() LIMIT 1;
  INSERT INTO "ShiftTasks" ("Id","ImmId","MoldId","ProductId","PersonnelId","PlanQuantity","ActualQuantity",
                            "ActualMaterialWeightGrams","Status","WorkMode","PlannedFullCycleSeconds",
                            "PlannedInjectionCycleSeconds","DefectQuantity","PlannedDate","PlannedSetupStartTime",
                            "PlannedSetupMinutes","ReleaseBufferFixedMinutes","ReleaseBufferPercent",
                            "IssuedAt","SetupStartedAt","MoldVerifiedAt","StartedAt","CompletedAt","ClosedAt",
                            "CreatedAt","UpdatedAt")
  VALUES (v_id, p_imm, m.id, v_prod, p_adj, p_plan, 0, 0, p_status, p_mode, m.ct, m.inj, 0,
          loc::date, loc::time, 90, 30, 10,
          p_issued, p_setup,
          CASE WHEN p_setup IS NOT NULL AND random() < 0.8 THEN p_setup + interval '10 minutes' END,
          p_started, p_completed, p_closed, v_created,
          COALESCE(p_closed, p_completed, p_started, p_setup, p_issued, v_created));
  RETURN v_id;
END $$;

-- ============================================================
-- Генерация истории и среза «сейчас»
-- ============================================================
DO $$
DECLARE
  cfg record; im record; m record;
  v_adjuster text; adj text[]; v_adj text;
  n int; cno int; need int; done int; v_plan int; v_shots int; v_gap int; v_mode int; v_period int;
  r float; avail_s float;
  cur timestamptz; lim timestamptz; hist_end timestamptz; g_end timestamptz; u_a timestamptz; u_b timestamptz;
  s_beg timestamptz; s_end timestamptz; t timestamptz; b_end timestamptz; c_end timestamptz; x_end timestamptz;
  v_planned timestamptz;
  w_a timestamptz; w_b timestamptz; w_st text;
  v_task uuid; v_mold int; last_mold int; v_dt_done boolean;
BEGIN
  PERFORM setseed(0.19);
  SELECT * INTO cfg FROM _cfg;
  SELECT "Id" INTO v_adjuster FROM "Users" WHERE "UserName" = 'adjuster';
  adj := ARRAY(SELECT "Id" FROM "Users" WHERE "Id" LIKE 'f3000000%' AND "Role" = 2 ORDER BY "Id") || v_adjuster;

  FOR im IN SELECT * FROM _imm ORDER BY n LOOP
    cur := cfg.p_start; cno := 0; last_mold := NULL;
    hist_end := CASE im.kind
      WHEN 'run'   THEN cfg.t0 - pg_temp.rmin(180, 1200)
      WHEN 'setup' THEN cfg.t0 - interval '40 minutes'
      WHEN 'free'  THEN cfg.t0
      ELSE              cfg.p_start + interval '18 days' END;

    WHILE cur < hist_end LOOP
      -- Ближайшее окно без связи
      SELECT a, b, status INTO w_a, w_b, w_st FROM _win WHERE imm = im.n AND b > cur ORDER BY a LIMIT 1;
      IF NOT FOUND THEN w_a := 'infinity'; END IF;
      IF w_a <= cur THEN
        PERFORM pg_temp.st(im.id, w_st, cur, LEAST(w_b, hist_end));
        cur := LEAST(w_b, hist_end);
        CONTINUE;
      END IF;
      lim := LEAST(w_a, hist_end);

      -- Пауза между заданиями: «Без задания», иногда «Работа без задания»
      g_end := LEAST(cur + pg_temp.rmin(30, 300), lim);
      n := 0;
      IF last_mold IS NOT NULL AND random() < 0.2 THEN
        SELECT * INTO m FROM _mold WHERE _mold.n = last_mold;
        u_a := cur + (g_end - cur) * 0.3;
        n := pg_temp.cyc(im.id, NULL, m.id, m.cav, m.ct, m.inj, 1, u_a, LEAST(u_a + pg_temp.rmin(20, 90), g_end), cno, 1000000);
      END IF;
      IF n > 0 THEN
        u_b := u_a + n * (m.ct + 1) * interval '1 second';
        PERFORM pg_temp.st(im.id, 'Idle', cur, u_a);
        PERFORM pg_temp.st(im.id, 'Auto', u_a, u_b);
        INSERT INTO "UnplannedRuns" ("Id","ImmId","StartTime","ClosedAt","CreatedAt")
        VALUES (gen_random_uuid(), im.id, u_a, u_b, u_a);
        PERFORM pg_temp.st(im.id, 'Idle', u_b, g_end);
        cno := cno + n;
      ELSE
        PERFORM pg_temp.st(im.id, 'Idle', cur, g_end);
      END IF;
      cur := g_end;

      IF lim - cur < interval '4 hours' THEN
        PERFORM pg_temp.st(im.id, 'Idle', cur, lim);
        cur := lim;
        CONTINUE;
      END IF;

      -- Задание: план считается из доступного времени, ТПА остановится по плану
      SELECT p.mold INTO v_mold FROM _pool p WHERE p.imm = im.n ORDER BY random() LIMIT 1;
      SELECT * INTO m FROM _mold WHERE _mold.n = v_mold;
      v_mode   := CASE WHEN random() < 0.2 THEN 1 ELSE 0 END;          -- 20% — полуавтомат
      v_gap    := CASE WHEN v_mode = 1 THEN 6 ELSE 1 END;              -- пауза между циклами, с
      v_period := m.ct + v_gap;
      v_adj    := adj[1 + floor(random() * array_length(adj, 1))::int];
      s_beg    := cur;
      s_end    := s_beg + pg_temp.rmin(60, 150);
      avail_s  := extract(epoch FROM (lim - s_end)) - 3600;            -- запас на ожидание наладчика
      v_shots  := floor(LEAST((8 + random() * 52) * 3600, avail_s) * 0.75 / v_period)::int;
      v_plan   := (floor(v_shots * m.cav / 100.0) * 100)::int;
      IF v_plan < 100 THEN
        PERFORM pg_temp.st(im.id, 'Idle', cur, lim);
        cur := lim;
        CONTINUE;
      END IF;
      need := ceil(v_plan::numeric / m.cav)::int;                     -- смыканий до остановки по плану

      v_planned := date_bin('15 minutes', s_beg - pg_temp.rmin(0, 90), cfg.p_start);
      v_task := pg_temp.task(im.id, v_mold, v_adj, v_plan, 3, v_mode, v_planned,
                             v_planned - pg_temp.rmin(60, 720), s_beg, s_end, NULL, NULL);
      PERFORM pg_temp.st(im.id, 'Manual', s_beg, s_end);

      t := s_end; done := 0;
      WHILE done < need AND t < lim LOOP
        b_end := LEAST(t + pg_temp.rmin(40, 240), lim);
        n := pg_temp.cyc(im.id, v_task, m.id, m.cav, m.ct, m.inj, v_gap, t, b_end, cno, need - done);
        EXIT WHEN n = 0;
        c_end := t + n * v_period * interval '1 second';
        PERFORM pg_temp.st(im.id, 'Auto', t, c_end);
        cno := cno + n; done := done + n; t := c_end;
        EXIT WHEN done >= need OR t >= lim;

        r := random();
        IF r < im.p_short THEN                -- короткая остановка, простой не фиксируется
          x_end := LEAST(t + pg_temp.rmin(2, 10), lim);
          PERFORM pg_temp.st(im.id, 'Idle', t, x_end);
        ELSIF r < im.p_alarm THEN             -- авария 15–90 мин
          x_end := LEAST(t + pg_temp.rmin(15, 90), lim);
          PERFORM pg_temp.st(im.id, 'Alarm', t, x_end);
          PERFORM pg_temp.ev(im.id, v_task, v_adj, CASE WHEN random() < 0.5 THEN 1 ELSE 2 END, t, x_end);
        ELSE                                  -- долгий простой 1–3 ч, 20% — без причины
          x_end := LEAST(t + pg_temp.rmin(60, 180), lim);
          PERFORM pg_temp.st(im.id, 'Idle', t, x_end);
          PERFORM pg_temp.ev(im.id, v_task, v_adj,
                             CASE WHEN random() < 0.2 THEN NULL ELSE 3 + floor(random() * 4)::int END, t, x_end);
        END IF;
        t := x_end;
      END LOOP;

      -- Остановка по плану: ТПА стоит, пока наладчик не придёт и не завершит задание
      x_end := LEAST(t + pg_temp.rmin(5, 40), lim);
      PERFORM pg_temp.st(im.id, 'Idle', t, x_end);
      IF x_end - t > interval '15 minutes' THEN
        PERFORM pg_temp.ev(im.id, v_task, v_adj, CASE WHEN random() < 0.5 THEN NULL ELSE 4 END, t, x_end);
      END IF;
      UPDATE "ShiftTasks"
         SET "CompletedAt" = x_end,
             "Status"      = CASE WHEN x_end < cfg.t0 - interval '1 day' THEN 4 ELSE 3 END,
             "ClosedAt"    = CASE WHEN x_end < cfg.t0 - interval '1 day' THEN x_end + pg_temp.rmin(30, 240) END,
             "UpdatedAt"   = x_end
       WHERE "Id" = v_task;
      UPDATE "ShiftTasks" SET "UpdatedAt" = "ClosedAt" WHERE "Id" = v_task AND "ClosedAt" IS NOT NULL;

      last_mold := v_mold;
      cur := x_end;
    END LOOP;

    -- Срез «сейчас»
    IF im.kind = 'arch' THEN
      PERFORM pg_temp.st(im.id, 'Offline', hist_end, NULL);

    ELSIF im.kind = 'free' THEN
      UPDATE "ImmStatusHistory" SET "EndedAt" = NULL
       WHERE "Id" = (SELECT "Id" FROM "ImmStatusHistory" WHERE "ImmId" = im.id ORDER BY "ChangedAt" DESC LIMIT 1);

    ELSIF im.kind = 'setup' THEN
      SELECT * INTO m FROM _mold WHERE _mold.n = im.cur_mold;
      v_planned := date_bin('15 minutes', hist_end - interval '30 minutes', cfg.p_start);
      PERFORM pg_temp.task(im.id, im.cur_mold, v_adjuster, m.cav * 500, 5, 0, v_planned,
                           v_planned - interval '3 hours', hist_end, NULL, NULL, NULL);
      PERFORM pg_temp.st(im.id, 'Manual', hist_end, NULL);

    ELSE -- run: текущее задание в работе
      SELECT * INTO m FROM _mold WHERE _mold.n = im.cur_mold;
      v_adj := CASE WHEN im.n = 1 THEN v_adjuster ELSE adj[1 + floor(random() * (array_length(adj, 1) - 1))::int] END;
      s_beg := hist_end;
      s_end := s_beg + pg_temp.rmin(60, 120);
      v_planned := date_bin('15 minutes', s_beg - pg_temp.rmin(0, 60), cfg.p_start);
      v_task := pg_temp.task(im.id, im.cur_mold, v_adj,
                             GREATEST(floor(60 * 3600 / (m.ct + 1) * m.cav / 100.0) * 100, 100)::int, 2, 0,
                             v_planned, v_planned - pg_temp.rmin(60, 600), s_beg, s_end, NULL, NULL);
      PERFORM pg_temp.st(im.id, 'Manual', s_beg, s_end);
      t := s_end; v_dt_done := false;
      LOOP
        -- ТПА-2: неразмеченный простой ~1,5 ч назад — наладчику есть что разметить на мобильном
        IF im.n = 2 AND NOT v_dt_done AND t > cfg.t0 - interval '100 minutes' AND t < cfg.t0 - interval '30 minutes' THEN
          x_end := t + interval '20 minutes';
          PERFORM pg_temp.st(im.id, 'Idle', t, x_end);
          PERFORM pg_temp.ev(im.id, v_task, v_adj, NULL, t, x_end);
          v_dt_done := true;
          t := x_end;
        END IF;
        b_end := LEAST(t + pg_temp.rmin(40, 240), cfg.t0,
                       CASE WHEN im.n = 2 AND NOT v_dt_done AND t < cfg.t0 - interval '100 minutes'
                            THEN cfg.t0 - interval '90 minutes' ELSE 'infinity'::timestamptz END);
        n := pg_temp.cyc(im.id, v_task, m.id, m.cav, m.ct, m.inj, 1, t, b_end, cno, 1000000);
        cno := cno + n;
        IF b_end >= cfg.t0 THEN
          PERFORM pg_temp.st(im.id, 'Auto', t, NULL);
          EXIT;
        END IF;
        c_end := t + n * (m.ct + 1) * interval '1 second';
        PERFORM pg_temp.st(im.id, 'Auto', t, c_end);
        x_end := c_end + pg_temp.rmin(2, 10);
        IF x_end >= cfg.t0 THEN
          PERFORM pg_temp.st(im.id, 'Idle', c_end, NULL);
          EXIT;
        END IF;
        PERFORM pg_temp.st(im.id, 'Idle', c_end, x_end);
        t := x_end;
      END LOOP;
    END IF;

    RAISE NOTICE '% — готово', im.name;
  END LOOP;
END $$;

-- Очередь «на сейчас»: выданные демо-adjuster и задания других наладчиков
DO $$
DECLARE cfg record; v_adjuster text; q record; v_planned timestamptz; m record;
BEGIN
  SELECT * INTO cfg FROM _cfg;
  SELECT "Id" INTO v_adjuster FROM "Users" WHERE "UserName" = 'adjuster';
  FOR q IN SELECT * FROM _queue ORDER BY imm, ago DESC LOOP
    SELECT * INTO m FROM _mold WHERE n = q.mold;
    v_planned := date_bin('15 minutes', cfg.t0 - q.ago * interval '1 minute', cfg.p_start);
    PERFORM pg_temp.task((SELECT id FROM _imm WHERE n = q.imm), q.mold,
                         CASE WHEN q.who = 'demo' THEN v_adjuster ELSE q.who END,
                         m.cav * q.shots, q.status, 0, v_planned,
                         CASE WHEN q.status = 1 THEN v_planned - interval '2 hours' END, NULL, NULL, NULL, NULL);
  END LOOP;
END $$;

-- Выпуск, брак и материал — из фактических циклов
UPDATE "ShiftTasks" t
SET "ActualQuantity"            = q.qty,
    "DefectQuantity"            = CASE WHEN t."Status" IN (3, 4) THEN (q.qty * random() * 0.02)::int ELSE 0 END,
    "ActualMaterialWeightGrams" = round(q.qty * m."PartWeightGrams" + q.n * m."RunnerWeightGrams", 2)
FROM (SELECT "TaskId", count(*) AS n, COALESCE(sum("Cavities") FILTER (WHERE "IsSuccessful"), 0) AS qty
      FROM "ImmCycles" WHERE "TaskId" IS NOT NULL GROUP BY "TaskId") q,
     "Molds" m
WHERE q."TaskId" = t."Id" AND m."Id" = t."MoldId";

-- ============================================================
-- Заказы: по изделию — две «волны» (первые 15 суток и остальное), ~¾ заданий с заказом
-- ============================================================
CREATE TEMP TABLE _tb AS
SELECT t."Id" AS task_id, t."ProductId" AS product_id,
       CASE WHEN t."PlannedDate" < (SELECT (p_start AT TIME ZONE 'Europe/Moscow')::date + 15 FROM _cfg) THEN 1 ELSE 2 END AS bucket
FROM "ShiftTasks" t
JOIN "Products" p ON p."Id" = t."ProductId"
WHERE p."IsActive" AND abs(hashtext(t."Id"::text)) % 4 <> 0;

CREATE TEMP TABLE _ord AS
SELECT gen_random_uuid() AS id, product_id, bucket, row_number() OVER (ORDER BY bucket, product_id) AS rn
FROM (SELECT DISTINCT product_id, bucket FROM _tb) d;

INSERT INTO "Orders" ("Id","Number","OrderDate","DueDate","Quantity","Status","Note","CreatedAt","ProductId")
SELECT o.id, 'З-' || lpad(o.rn::text, 3, '0'),
       min(t."CreatedAt") - interval '2 days',
       LEAST((max(t."PlannedDate") + 5)::timestamp AT TIME ZONE 'Europe/Moscow', (SELECT t0 FROM _cfg)),
       (ceil(sum(t."PlanQuantity") / 1000.0) * 1000)::int,
       CASE WHEN bool_and(t."Status" IN (3, 4)) THEN 1 ELSE 0 END,
       NULL, min(t."CreatedAt") - interval '2 days', o.product_id
FROM _ord o
JOIN _tb b ON b.product_id = o.product_id AND b.bucket = o.bucket
JOIN "ShiftTasks" t ON t."Id" = b.task_id
GROUP BY o.id, o.rn, o.product_id;

UPDATE "ShiftTasks" t SET "OrderId" = o.id
FROM _tb b JOIN _ord o ON o.product_id = b.product_id AND o.bucket = b.bucket
WHERE t."Id" = b.task_id;

INSERT INTO "Orders" ("Id","Number","OrderDate","DueDate","Quantity","Status","Note","CreatedAt","ProductId")
SELECT gen_random_uuid(), v.num, c.t0 - v.ago * interval '1 day', c.t0 - v.due * interval '1 day', v.qty, v.status,
       v.note, c.t0 - v.ago * interval '1 day', (SELECT "Id" FROM "Products" WHERE "FullArticle" = v.art)
FROM _cfg c,
     (VALUES ('З-900', 20, 5, 5000,  2, 'Отменён заказчиком',                    'КП38.К'),
             ('З-901',  1, 0, 12000, 0, 'Новый заказ — задания ещё не созданы',  'ВТ20.Н')) v(num, ago, due, qty, status, note, art);

-- ============================================================
-- Телеметрия: ppc точек на каждый датчик на каждый цикл, по фазам цикла
-- (prio — какие фазы берутся при ppc < 6; frac — доля длительности цикла, NULL — конец литья)
-- ============================================================
DO $$ BEGIN RAISE NOTICE 'Телеметрия...'; END $$;

CREATE TEMP TABLE _phase (k int, prio int, frac numeric, press numeric, screw numeric, mold_dt numeric, clamp numeric);
INSERT INTO _phase VALUES
  (1, 1, 0.00,    5, 80, 0.0,    0),   -- смыкание
  (2, 4, 0.04,  950, 78, 0.6, 1450),   -- начало литья
  (3, 2, NULL,  620, 12, 1.6, 1500),   -- конец литья
  (4, 5, 0.60,   40, 64, 1.1, 1500),   -- середина охлаждения
  (5, 6, 0.92,    5, 80, 0.4,  350),   -- начало раскрытия
  (6, 3, 1.00,    5, 80, 0.0,    0);   -- раскрытие

INSERT INTO "Telemetry" ("ImmId","Timestamp","ParameterName","ValueNumeric")
SELECT c."ImmId",
       CASE WHEN ph.frac IS NULL THEN c."InjectionStartTime" + c."InjectionDurationMs" * interval '1 millisecond'
            ELSE c."StartTime" + ph.frac * c."DurationSeconds" * interval '1 second' END,
       s.name,
       round((CASE s.name
         WHEN 'MeltTemp'          THEN 222 + (hashtext(c."ImmId"::text) & 7) + random() * 1.5
         WHEN 'InjectionPressure' THEN ph.press * (0.95 + random() * 0.1)
         WHEN 'ScrewPosition'     THEN ph.screw + random() * 2
         WHEN 'MoldTemp'          THEN 45 + ph.mold_dt + random() * 0.5
         ELSE                          ph.clamp * (0.98 + random() * 0.04) END)::numeric, 1)
FROM "ImmCycles" c
CROSS JOIN (SELECT * FROM _phase WHERE prio <= (SELECT ppc FROM _cfg)) ph
CROSS JOIN (VALUES ('MeltTemp'), ('InjectionPressure'), ('ScrewPosition'), ('MoldTemp'), ('ClampForce')) s(name)
WHERE c."EndTime" IS NOT NULL;

-- Пульс приложения = t0: при старте API закроет открытые статусы записью Offline сразу после t0
INSERT INTO "AppHeartbeat" ("Id","LastHeartbeatAt") SELECT 1, t0 FROM _cfg
ON CONFLICT ("Id") DO UPDATE SET "LastHeartbeatAt" = EXCLUDED."LastHeartbeatAt";

ANALYZE;

SELECT i."Name",
       (SELECT count(*) FROM "ShiftTasks" t WHERE t."ImmId" = i."Id")       AS tasks,
       (SELECT count(*) FROM "ImmCycles" c WHERE c."ImmId" = i."Id")        AS cycles,
       (SELECT count(*) FROM "ImmStatusHistory" h WHERE h."ImmId" = i."Id") AS statuses,
       (SELECT count(*) FROM "Events" e WHERE e."ImmId" = i."Id")           AS downtimes
FROM "Imms" i ORDER BY i."Name";
```

- [ ] **Step 2: Прогнать проверки**

Run: `pwsh deploy/demo/test-seed.ps1`
Expected: 12 строк `NOTICE: ТПА-… — готово`, `Телеметрия...`, таблица по ТПА, строка «сид за mm:ss, размер БД ~3 GB»,
затем все проверки `0` и `Сид в порядке.` Если какая-то проверка ≠ 0 — исправить сид (не проверку) и
прогнать снова. Размер БД больше 3,5 ГБ — уменьшить `ct`-нагрузку нельзя без смены дизайна: сообщить
пользователю и предложить `ppc = 4`.

- [ ] **Step 3: Commit**

```bash
git add deploy/demo/seed/seed_demo.sql
git commit -m "feat(demo): new demo seed relative to load time (30 days, 12 IMMs)"
```

---

### Task 5: Пакет для VPS: compose, mosquitto, `.env`, `reset-db.sh`

**Files:**
- Create: `deploy/demo/package/docker-compose.yml`
- Create: `deploy/demo/package/mosquitto.conf`
- Create: `deploy/demo/package/.env.example`
- Create: `deploy/demo/package/reset-db.sh`
- Modify: `.gitattributes`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: `seed_demo.sql` (Task 4), образ `ghcr.io/a-v-kalabuhov/control/api:${API_TAG}`.
- Produces: на VPS в `~/wtctrl-demo/`: `docker compose up -d` поднимает стек; `bash reset-db.sh` сбрасывает БД.
  Переменные `.env`: `DB_PASSWORD` (обязательна), `API_TAG`, необязательные `HTTP_PORT`/`HTTPS_PORT`
  (для локальной проверки). Используются `deploy.ps1` (Task 6).

- [ ] **Step 1: `docker-compose.yml`**

```yaml
name: wtctrl-demo

# Публичное демо Control (control-demo.ru). DemoMode=true НАМЕРЕННО: открытые демо-аккаунты всех
# ролей на экране входа — суть демо. Для клиентских установок — deploy/pzp/ (DemoMode=false).
# Эмулятора нет: живых данных на демо нет, все ТПА «Нет связи» (живая телеметрия — BL-46).

x-logging: &logging
  driver: json-file
  options:
    max-size: "10m"
    max-file: "3"

services:
  postgres:
    image: postgres:16-alpine
    container_name: wtctrl-demo-postgres
    environment:
      POSTGRES_DB: WintimeControlDb
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: ${DB_PASSWORD:?задайте DB_PASSWORD в .env}
    command: postgres -c shared_buffers=128MB -c max_connections=20
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -h 127.0.0.1 -U postgres -d WintimeControlDb"]
      interval: 5s
      timeout: 3s
      retries: 20
    restart: unless-stopped
    mem_limit: 384m
    logging: *logging
    networks: [wtctrl]

  mosquitto:
    image: eclipse-mosquitto:2.0
    container_name: wtctrl-demo-mosquitto
    # Портов наружу нет: брокер анонимный, нужен только API внутри сети compose.
    volumes:
      - ./mosquitto.conf:/mosquitto/config/mosquitto.conf:ro
      - mosquitto_data:/mosquitto/data
    restart: unless-stopped
    mem_limit: 64m
    logging: *logging
    networks: [wtctrl]

  api:
    image: ghcr.io/a-v-kalabuhov/control/api:${API_TAG:-latest}
    container_name: wtctrl-demo-api
    depends_on:
      postgres:
        condition: service_healthy
      mosquitto:
        condition: service_started
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      DemoMode: "true"
      # TLS завершает Kestrel: https на 8443 (снаружи 443), http на 8080 (снаружи 80) редиректит на https
      ASPNETCORE_URLS: "http://+:8080;https://+:8443"
      ASPNETCORE_HTTPS_PORT: "${HTTPS_PORT:-443}"
      Https__Redirect: "true"
      ASPNETCORE_Kestrel__Certificates__Default__Path: /certs/fullchain.pem
      ASPNETCORE_Kestrel__Certificates__Default__KeyPath: /certs/privkey.pem
      DOTNET_gcServer: "0"
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=WintimeControlDb;Username=postgres;Password=${DB_PASSWORD};"
      MqttSettings__BrokerUrl: mosquitto
    ports:
      - "${HTTP_PORT:-80}:8080"
      - "${HTTPS_PORT:-443}:8443"
    volumes:
      - ./certs:/certs:ro
    restart: unless-stopped
    mem_limit: 512m
    logging: *logging
    networks: [wtctrl]

volumes:
  postgres_data:
  mosquitto_data:

networks:
  wtctrl:
    driver: bridge
```

- [ ] **Step 2: `mosquitto.conf` и `.env.example`**

`mosquitto.conf`:

```
listener 1883
allow_anonymous true
```

`.env.example`:

```
# Скопировать в ~/wtctrl-demo/.env на VPS (в git не хранится).
DB_PASSWORD=смените-на-длинный-пароль
# Тег образа API — deploy.ps1 выставляет его сам при каждом деплое
API_TAG=latest
# Только для локальной проверки пакета (на VPS не задавать — там 80/443):
# HTTP_PORT=18080
# HTTPS_PORT=18443
```

- [ ] **Step 3: `reset-db.sh`**

```bash
#!/usr/bin/env bash
# Сброс БД демо: удаляет volume postgres и грузит свежий seed_demo.sql (даты — от момента запуска).
# Запуск из папки пакета на VPS: bash reset-db.sh   (deploy.ps1 -ResetDb / -ResetDbOnly вызывает его сам)
# На 1 ядре сид грузится 25–40 мин; всё это время демо недоступно.
set -euo pipefail
cd "$(dirname "$0")"

NEED_GB=5
DB=WintimeControlDb
PG=wtctrl-demo-postgres

[ -f seed_demo.sql ] || { echo "Нет seed_demo.sql рядом со скриптом" >&2; exit 1; }

echo "==> 1/6 Останавливаю API и удаляю БД..."
docker compose stop api
docker compose rm -f -s postgres
docker volume rm -f wtctrl-demo_postgres_data >/dev/null

avail=$(df -BG --output=avail / | tail -1 | tr -dc '0-9')
if [ "$avail" -lt "$NEED_GB" ]; then
  echo "Свободно ${avail} ГБ, нужно не меньше ${NEED_GB} ГБ. API остановлен." >&2
  echo "Освободите диск или уменьшите ppc/days в начале seed_demo.sql и запустите снова." >&2
  exit 1
fi

echo "==> 2/6 Поднимаю postgres..."
docker compose up -d postgres
until [ "$(docker inspect -f '{{.State.Health.Status}}' "$PG")" = "healthy" ]; do sleep 2; done

echo "==> 3/6 Запускаю API: миграции и демо-аккаунты..."
docker compose up -d api
for i in $(seq 1 90); do
  if docker exec "$PG" psql -U postgres -d "$DB" -Atc "SELECT 1 FROM \"Users\" WHERE \"UserName\" = 'adjuster'" 2>/dev/null | grep -q 1; then
    break
  fi
  if [ "$i" -eq 90 ]; then
    echo "API не создал демо-аккаунты за 3 мин:" >&2
    docker compose logs --tail 50 api >&2
    exit 1
  fi
  sleep 2
done

echo "==> 4/6 Останавливаю API на время загрузки (фоновые воркеры не должны видеть полусид)..."
docker compose stop api

echo "==> 5/6 Загружаю сид (на 1 ядре — 25–40 мин)..."
start=$(date +%s)
docker exec -i "$PG" psql -U postgres -d "$DB" -v ON_ERROR_STOP=1 -q < seed_demo.sql
size=$(docker exec "$PG" psql -U postgres -d "$DB" -Atc "SELECT pg_size_pretty(pg_database_size(current_database()))")
echo "    готово за $(( ($(date +%s) - start) / 60 )) мин, размер БД ${size}"

echo "==> 6/6 Запускаю API..."
docker compose up -d api
echo "Сброс БД завершён."
```

- [ ] **Step 4: `.gitattributes` и `.gitignore`**

В конец `.gitattributes`:

```
# Shell-скрипты деплоя выполняются на Linux-VPS — только LF, иначе bash падает на \r.
*.sh text eol=lf
```

В конец `.gitignore`:

```
# Demo VPS deploy — локальные настройки и секреты (не в git)
deploy/demo/deploy.local.psd1
deploy/demo/package/certs/
deploy/demo/package/.env
```

- [ ] **Step 5: Локальная проверка пакета**

Собрать образ локально (без push) и поднять пакет на нестандартных портах с самоподписанным сертификатом
(Git Bash, из корня репо):

```bash
cd Wintime-Control-Frontend && npm ci && npm run build && cd ..
dotnet publish Wintime.Control.API -c Release -o ./publish/api /p:UseAppHost=false /p:SkipFrontendBuild=true
docker build -f Wintime.Control.API/Dockerfile.prod -t ghcr.io/a-v-kalabuhov/control/api:local .
cp deploy/demo/seed/seed_demo.sql deploy/demo/package/
mkdir -p deploy/demo/package/certs
MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj "/CN=localhost" \
  -keyout deploy/demo/package/certs/privkey.pem -out deploy/demo/package/certs/fullchain.pem
chmod 644 deploy/demo/package/certs/*.pem
printf 'DB_PASSWORD=local-test\nAPI_TAG=local\nHTTP_PORT=18080\nHTTPS_PORT=18443\n' > deploy/demo/package/.env
cd deploy/demo/package && docker compose config -q && bash reset-db.sh
```

Expected: `docker compose config -q` без вывода; `reset-db.sh` проходит шаги 1/6…6/6 и печатает
`Сброс БД завершён.` (на Windows `df --output` в Git Bash работает; если нет — временно `NEED_GB=0` не
коммитить, а проверить на VPS). Затем:

```bash
curl -sk -o /dev/null -w "%{http_code}\n" https://localhost:18443/health      # 200
curl -s  -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:18080/health  # 307 https://localhost:18443/health
curl -sk https://localhost:18443/api/app-info                                  # {"demoMode":true}
docker compose ps --format '{{.Name}} {{.Ports}}'                              # у postgres/mosquitto портов нет
```

Открыть `https://localhost:18443` в браузере (предупреждение о сертификате — ожидаемо): экран входа с
подписью `… · демо-версия` и демо-учётками; вход `admin / Admin123!`; отчёты за последние 30 суток заполнены;
под `adjuster` — 7 выданных заданий.

Убрать за собой: `docker compose down -v && rm -rf certs .env seed_demo.sql && cd ../../..`

- [ ] **Step 6: Commit**

```bash
git add deploy/demo/package .gitattributes .gitignore
git commit -m "feat(demo): compose package with Kestrel HTTPS and reset-db script"
```

---

### Task 6: `deploy.ps1`, локальный конфиг и `ASSEMBLE.md`

**Files:**
- Create: `deploy/demo/deploy.ps1`
- Create: `deploy/demo/deploy.local.example.psd1`
- Create: `deploy/demo/ASSEMBLE.md`

**Interfaces:**
- Consumes: пакет (Task 5), `seed_demo.sql` (Task 4), `VERSION`.
- Produces: `.\deploy\demo\deploy.ps1 [-ResetDb] [-ResetDbOnly] [-Certs] [-Force]` (используется в Task 7, 8).

- [ ] **Step 1: `deploy.local.example.psd1`**

```powershell
# Скопировать в deploy.local.psd1 (в git не хранится) и поправить пути к сертификату.
@{
    Host      = '195.208.2.44'
    User      = 'deploy'
    RemoteDir = 'wtctrl-demo'                       # относительно домашней папки пользователя на VPS
    Domain    = 'control-demo.ru'
    CertFile  = 'C:\certs\control-demo.ru\control-demo.ru.crt'   # сертификат домена
    CaFile    = 'C:\certs\control-demo.ru\ca.crt'                # корневой/промежуточный от УЦ
    KeyFile   = 'C:\certs\control-demo.ru\control-demo.ru.key'   # закрытый ключ (без пароля)
}
```

- [ ] **Step 2: `deploy.ps1`**

```powershell
# Деплой публичного демо Control на VPS (control-demo.ru). Подробно — ASSEMBLE.md.
#   .\deploy.ps1               сборка -> push в GHCR -> пакет на VPS -> pull + up -d -> проверка
#   .\deploy.ps1 -ResetDb      то же + сброс БД свежим сидом (25–40 мин)
#   .\deploy.ps1 -ResetDbOnly  только сброс БД (перед показом)
#   .\deploy.ps1 -Certs        только сертификат (fullchain + ключ) на VPS и перезапуск API
#   -Force                     не проверять чистоту дерева и то, что VERSION поднят
param(
    [switch]$ResetDb,
    [switch]$ResetDbOnly,
    [switch]$Certs,
    [switch]$Force
)
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$ScriptDir    = $PSScriptRoot
$ControlRoot  = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$PackageDir   = Join-Path $ScriptDir "package"
$SeedFile     = Join-Path $ScriptDir "seed\seed_demo.sql"
$Registry     = "ghcr.io/a-v-kalabuhov/control"
$ApiContainer = "wtctrl-demo-api"
$MinFreeMb    = 1536

$cfgPath = Join-Path $ScriptDir "deploy.local.psd1"
if (-not (Test-Path $cfgPath)) {
    throw "Нет $cfgPath — скопируйте deploy.local.example.psd1 и заполните (ASSEMBLE.md, «Первичная настройка»)"
}
$cfg     = Import-PowerShellDataFile $cfgPath
$Target  = "$($cfg.User)@$($cfg.Host)"
$Remote  = $cfg.RemoteDir
$SshOpts = @("-o", "BatchMode=yes", "-o", "ServerAliveInterval=30")

function Invoke-Remote([string]$Command) { ssh @SshOpts $Target $Command }

function Copy-ToRemote([string[]]$Files, [string]$Dest) { scp @SshOpts @Files "${Target}:$Dest" }

function Get-RemoteImage {
    $PSNativeCommandUseErrorActionPreference = $false   # контейнера может ещё не быть
    $img = ssh @SshOpts $Target "docker inspect -f '{{.Config.Image}}' $ApiContainer 2>/dev/null"
    $PSNativeCommandUseErrorActionPreference = $true
    return "$img".Trim()
}

function Assert-FreeSpace {
    $mb = [int]("$(Invoke-Remote "df -BM --output=avail / | tail -1")" -replace '\D', '')
    if ($mb -lt $MinFreeMb) { throw "На VPS свободно $mb МБ, нужно не меньше $MinFreeMb МБ (ASSEMBLE.md, «Диск»)" }
}

function Wait-Site {
    $url = "https://$($cfg.Domain)/health"
    for ($i = 0; $i -lt 24; $i++) {
        try {
            $r = Invoke-WebRequest $url -TimeoutSec 10 -UseBasicParsing
            if ($r.StatusCode -eq 200) { Write-Host "    $url -> 200"; return }
        } catch { }
        Start-Sleep -Seconds 5
    }
    Invoke-Remote "cd $Remote && docker compose logs --tail 100 api"
    throw "$url не отвечает 200 за 2 мин"
}

function Publish-Certs {
    $local = Join-Path $PackageDir "certs"
    New-Item -ItemType Directory -Force $local | Out-Null
    # Kestrel отдаёт цепочку из Path: сертификат домена + сертификат УЦ
    $chain = (Get-Content $cfg.CertFile -Raw).TrimEnd() + "`n" + (Get-Content $cfg.CaFile -Raw).TrimEnd() + "`n"
    [IO.File]::WriteAllText((Join-Path $local "fullchain.pem"), $chain)
    Copy-Item $cfg.KeyFile (Join-Path $local "privkey.pem") -Force
    Invoke-Remote "mkdir -p $Remote/certs"
    Copy-ToRemote @((Join-Path $local "fullchain.pem"), (Join-Path $local "privkey.pem")) "$Remote/certs/"
    # API в контейнере работает от пользователя app (не deploy) — ему нужно чтение файлов
    Invoke-Remote "chmod 755 $Remote/certs && chmod 644 $Remote/certs/fullchain.pem $Remote/certs/privkey.pem"
}

function Build-And-Push([string]$Version) {
    Write-Host "==> Сборка фронта..."
    Push-Location (Join-Path $ControlRoot "Wintime-Control-Frontend")
    try { npm ci; npm run build } finally { Pop-Location }
    Write-Host "==> Publish и образ API $Version..."
    Push-Location $ControlRoot
    try {
        dotnet publish Wintime.Control.API -c Release -o .\publish\api /p:UseAppHost=false /p:SkipFrontendBuild=true
        docker build -f Wintime.Control.API\Dockerfile.prod -t "$Registry/api:$Version" -t "$Registry/api:latest" .
    } finally { Pop-Location }
    Write-Host "==> Push в GHCR (нужен docker login ghcr.io)..."
    docker push "$Registry/api:$Version"
    docker push "$Registry/api:latest"
}

try { Invoke-Remote "true" }
catch { throw "Нет входа по ключу на $Target — см. ASSEMBLE.md, «Первичная настройка», шаг 1" }

if ($Certs) {
    Publish-Certs
    Invoke-Remote "cd $Remote && if [ -n `"`$(docker compose ps -q api 2>/dev/null)`" ]; then docker compose restart api; fi"
    Write-Host "Сертификат обновлён."
    return
}

if ($ResetDbOnly) {
    Copy-ToRemote @((Join-Path $PackageDir "reset-db.sh"), $SeedFile) "$Remote/"
} else {
    $Version = (Get-Content (Join-Path $ControlRoot "VERSION") -Raw).Trim()
    Write-Host "==> Версия $Version"
    if (-not $Force) {
        if (git -C $ControlRoot status --porcelain) { throw "Рабочее дерево не чистое — закоммитьте изменения (или -Force)" }
        if ((Get-RemoteImage) -eq "$Registry/api:$Version") { throw "На VPS уже $Version — поднимите VERSION (или -Force)" }
    }
    Assert-FreeSpace
    Build-And-Push $Version

    Write-Host "==> Пакет на VPS..."
    Invoke-Remote "mkdir -p $Remote"
    Copy-ToRemote @((Join-Path $PackageDir "docker-compose.yml"), (Join-Path $PackageDir "mosquitto.conf"),
                    (Join-Path $PackageDir ".env.example"), (Join-Path $PackageDir "reset-db.sh"), $SeedFile) "$Remote/"
    try { Invoke-Remote "test -f $Remote/.env && test -f $Remote/certs/fullchain.pem && test -f $Remote/certs/privkey.pem" }
    catch { throw "На VPS нет $Remote/.env или сертификата — ASSEMBLE.md, «Первичная настройка», шаги 5–6" }

    Write-Host "==> pull + up -d..."
    Invoke-Remote "cd $Remote && sed -i '/^API_TAG=/d' .env && echo API_TAG=$Version >> .env && docker compose pull api && docker compose up -d"
    Wait-Site
    $img = Get-RemoteImage
    if ($img -ne "$Registry/api:$Version") { throw "На VPS запущен $img, ожидался $Registry/api:$Version" }
    Invoke-Remote "docker image prune -f" | Out-Null
}

if ($ResetDb -or $ResetDbOnly) {
    Write-Host "==> Сброс БД (25–40 мин)..."
    Invoke-Remote "cd $Remote && bash reset-db.sh"
    Wait-Site
}

Write-Host "Готово: https://$($cfg.Domain)"
```

- [ ] **Step 3: `ASSEMBLE.md`**

````markdown
# Демо-VPS: деплой (автор)

Публичное демо Control — `https://control-demo.ru`, VPS `deploy@195.208.2.44` (1 ГБ ОЗУ, 1 ядро, 8,6 ГБ).
`DemoMode=true` намеренно: открытые демо-аккаунты всех ролей. Живых данных нет (все ТПА «Нет связи»),
история — из `seed/seed_demo.sql`, заканчивается моментом последнего сброса БД.

Всё запускается с вашей машины: `.\deploy\demo\deploy.ps1` (PowerShell 7, Docker, OpenSSH, Node, .NET SDK).

## Первичная настройка (один раз)

1. **Вход по ключу для `deploy`** (под root на VPS):
   ```bash
   mkdir -p /home/deploy/.ssh
   cat >> /home/deploy/.ssh/authorized_keys      # вставить содержимое ~/.ssh/id_rsa.pub, Ctrl+D
   chown -R deploy:deploy /home/deploy/.ssh && chmod 700 /home/deploy/.ssh && chmod 600 /home/deploy/.ssh/authorized_keys
   usermod -aG docker deploy                      # docker без sudo
   ```
   Проверка с вашей машины: `ssh deploy@195.208.2.44 docker ps`.
2. **Снести старое демо** (под root). Старый стек с эмулятором забил диск телеметрией:
   ```bash
   docker inspect wtctrl-prod-api --format '{{ index .Config.Labels "com.docker.compose.project.working_dir" }}'
   cd <папка из вывода> && docker compose -f docker-compose.prod.yml down -v
   docker image prune -a -f
   df -h /                                         # нужно ≥ 5 ГБ свободно
   ```
3. **Файрвол:** `ufw status`; если активен — `ufw allow 80/tcp && ufw allow 443/tcp`.
4. **GHCR:** проверить под `deploy`: `docker pull ghcr.io/a-v-kalabuhov/control/api:latest`. Если «denied» —
   `docker login ghcr.io -u a-v-kalabuhov` с PAT (`read:packages`). На своей машине — `docker login ghcr.io`
   с PAT (`write:packages`).
5. **Локальный конфиг:** скопировать `deploy.local.example.psd1` → `deploy.local.psd1`, указать пути к
   `.crt` домена, `.crt` УЦ и `.key`. Затем `.\deploy\demo\deploy.ps1 -Certs`.
6. **Секреты на VPS** (под deploy): `mkdir -p ~/wtctrl-demo && cd ~/wtctrl-demo`, создать `.env` по образцу
   `.env.example` (`DB_PASSWORD` — длинный случайный).
7. **Первый деплой:** `.\deploy\demo\deploy.ps1 -ResetDb`.

## Обновление версии

1. Поднять `VERSION` (SemVer), закоммитить.
2. `.\deploy\demo\deploy.ps1` — сборка, push `api:<VERSION>` и `latest`, копирование пакета, `pull` + `up -d`,
   ожидание `https://control-demo.ru/health`, сверка образа, `docker image prune -f`.

## Сброс БД перед показом

`.\deploy\demo\deploy.ps1 -ResetDbOnly` — 25–40 мин, всё это время демо недоступно. Даты сдвигаются к
«сейчас», у `adjuster` снова 7 выданных заданий.

## Сертификат

Продление: заменить файлы по путям из `deploy.local.psd1` и выполнить `.\deploy\demo\deploy.ps1 -Certs`.
На VPS — `~/wtctrl-demo/certs/fullchain.pem` (домен + УЦ) и `privkey.pem` (права 644: API в контейнере
читает их не от `deploy`). Ключ с паролем не поддерживается — снять пароль: `openssl rsa -in key -out key.nopass`.

## Откат

На VPS: `cd ~/wtctrl-demo && sed -i '/^API_TAG=/d' .env && echo API_TAG=<старая версия> >> .env && docker compose up -d`.
Схема БД при этом не откатывается — если в новой версии были миграции, после отката нужен `-ResetDbOnly`
со старым сидом.

## Диск

Раздел 8,6 ГБ. БД после сида ~3 ГБ и после деплоя почти не растёт. `deploy.ps1` требует ≥ 1,5 ГБ свободно
перед `pull`, `reset-db.sh` — ≥ 5 ГБ перед сидом. Не хватает — уменьшить `ppc` (точек телеметрии на цикл)
или `days` в начале `seed/seed_demo.sql`.

## Неполадки

- `Нет входа по ключу` — шаг 1 первичной настройки.
- `docker compose logs --tail 100 api` на VPS — ошибки старта API (сертификат, БД).
- `Cannot ... certificate` в логах API — проверить, что `fullchain.pem` начинается с сертификата домена и
  права 644.
- Сид упал — `reset-db.sh` печатает ошибку psql; API остаётся на пустой БД с демо-аккаунтами. Исправить сид,
  проверить локально `pwsh deploy\demo\test-seed.ps1`, повторить `-ResetDbOnly`.
````

- [ ] **Step 4: Синтаксическая проверка**

Run:
```powershell
pwsh -NoProfile -Command "$null = [scriptblock]::Create((Get-Content deploy/demo/deploy.ps1 -Raw)); Import-PowerShellDataFile deploy/demo/deploy.local.example.psd1 | Out-Null; 'OK'"
```
Expected: `OK`.

Run: `pwsh deploy/demo/deploy.ps1` (без `deploy.local.psd1`)
Expected: ошибка `Нет ...deploy.local.psd1 — скопируйте deploy.local.example.psd1 ...`.

- [ ] **Step 5: Commit**

```bash
git add deploy/demo/deploy.ps1 deploy/demo/deploy.local.example.psd1 deploy/demo/ASSEMBLE.md
git commit -m "feat(demo): deploy.ps1 (GHCR + ssh) and author guide"
```

---

### Task 7: Уборка старого деплоя, версия 1.18.0, полный прогон тестов

**Files:**
- Delete: `build.ps1`, `docker-compose.prod.yml`, `seed_demo.sql`
- Modify: `CLAUDE.md:21`
- Modify: `VERSION`
- Modify: `docs/superpowers/specs/2026-10-19-demo-vps-deploy-design.md` (проверка выпуска)

- [ ] **Step 1: Удалить старые файлы и поднять версию**

```bash
git rm build.ps1 docker-compose.prod.yml seed_demo.sql
printf '1.18.0\n' > VERSION
```

В `CLAUDE.md` строку

```
.\build.ps1          # публикация образов; ПЕРЕД ней поднять номер в VERSION (SemVer)
```

заменить на

```
.\deploy\demo\deploy.ps1   # деплой демо-VPS (GHCR + ssh); ПЕРЕД ним поднять номер в VERSION (SemVer); -ResetDbOnly — сброс БД
```

В спеке, раздел «Проверка сида», пункт `выпуск задания ≤ плана; …` заменить на
`выпуск задания < плана + гнёзда ПФ (ТПА останавливается по числу смыканий ⌈План/Гнёзда⌉); …`.

- [ ] **Step 2: Нет ссылок на удалённое**

Run: `git grep -n "build.ps1\|docker-compose.prod.yml\|seed_demo.sql" -- ':!docs/superpowers' ':!deploy/pzp' ':!deploy/smoke-test'`
Expected: только `deploy/demo/...` (свои пути) и `CLAUDE.md` с новой строкой; ссылок на корневые файлы нет.

- [ ] **Step 3: Полный прогон**

Run:
```bash
dotnet test Wintime.Control.Tests.Unit
dotnet test Wintime.Control.Tests.Integration
cd Wintime-Control-Frontend && npx vitest run && cd ..
pwsh deploy/demo/test-seed.ps1
```
Expected: все тесты PASS, `Сид в порядке.`

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore: replace root build/compose/seed with deploy/demo; bump 1.18.0"
```

---

### Task 8: Деплой на VPS (вместе с пользователем) и PR

Внешнее действие — выполнять только с явного согласия пользователя; шаги под root делает пользователь.

- [ ] **Step 1:** пользователь выполняет «Первичную настройку» шаги 1–4 из `ASSEMBLE.md`
  (ключ, снос старого стека, файрвол, GHCR); сообщает вывод `df -h /`.
- [ ] **Step 2:** пользователь создаёт `deploy/demo/deploy.local.psd1` и `~/wtctrl-demo/.env` (шаги 5–6);
  `.\deploy\demo\deploy.ps1 -Certs`.
- [ ] **Step 3:** `.\deploy\demo\deploy.ps1 -ResetDb` → `Готово: https://control-demo.ru`.
- [ ] **Step 4: Критерии готовности** (спека):
  - `https://control-demo.ru` — валидный сертификат (`curl -sI https://control-demo.ru/health` без `-k` → 200);
    `curl -sI http://control-demo.ru/` → 307 на https;
  - снаружи закрыты 5000/1883/9001: `Test-NetConnection 195.208.2.44 -Port 1883` → `TcpTestSucceeded : False`;
  - под логотипом `v1.18.0 · <дата> · демо-версия`; вход `admin / Admin123!`; отчёты за 30 суток заполнены;
  - с телефона под `adjuster`: 7 выданных заданий, камера QR-сканера включается, сценарий UC-5 до конца.
- [ ] **Step 5: PR**

```bash
git push -u origin feature/demo-deploy
gh pr create --title "Demo VPS deploy package, demo-version label, v1.18.0" --body "<описание: состав deploy/demo, app-info, сид, проверка на VPS>

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```
