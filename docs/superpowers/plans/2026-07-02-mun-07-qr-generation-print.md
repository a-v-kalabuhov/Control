# MUN-07 — Генерация и печать QR-кодов (Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** В справочниках ПФ и ТПА добавить кнопку «QR» → модальное окно с QR-картинкой и печатью; на бэкенде — endpoint выдачи QR-данных для ТПА по образцу существующего для ПФ.

**Architecture:** Бэкенд отдаёт только JSON-строку `QrData` (endpoint на ТПА добавляется, на ПФ — исправляется). Картинка QR генерируется на фронте пакетом `qrcode` в виде **SVG-вектора** с уровнем коррекции **H**; печать — через отдельное окно `window.open` с минимальным HTML (строится чистой функцией-утилитой, покрытой тестом). Диалог `QrCodeDialog.vue` переиспользуется обоими справочниками (DRY).

**Tech Stack:** ASP.NET Core 9 (контроллеры + `System.Text.Json`), xUnit + FluentAssertions (integration), Vue 3 + Element Plus, npm `qrcode`, Vitest.

## Global Constraints

- **Формат QR-данных (решение принято 2026-07-02):** в QR зашивается **только** `entity` + `id`, где `id` — это **Guid `Id`** сущности (не `FormId`, не код, не наименование). Payload: `{"entity":"mold","id":"<Mold.Id>"}` и `{"entity":"machine","id":"<Imm.Id>"}`. Причины: (1) QR печатается один раз и клеится на оснастку навсегда — в него нельзя класть изменяемые поля (`FormId`/артикул и `Name` редактируются через справочник, распечатанная бирка «протухнет»); `Id` (первичный ключ) неизменен. (2) Мобильный сканер сверяет `parsed.id === setupTask.moldId`, где `moldId` — Guid (`TaskDto.MoldId`). Существующий endpoint ПФ сейчас кладёт `mold.FormId` → скан не совпадает; это исправляется в Task 2. Человекочитаемая подпись (артикул/имя) в QR **не** зашивается — она берётся «живьём» по `id` в момент показа/печати (`label` строится на фронте из текущих данных строки).
- **Сериализация payload — только `System.Text.Json`** (анонимный объект с полями `entity`/`id` в нижнем регистре), не ручная конкатенация строк.
- **Роли доступа к QR-endpoint:** `[Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]` — как у существующего `GetMoldQr`.
- **Картинку QR в бэкенд НЕ тащить** — генерация только на фронте (`qrcode`). `QrCodeDto.QrImageBase64` остаётся `null`.
- **Параметры генерации QR (решение 2026-07-02):** символика — стандартный QR Code Model 2; вывод — **SVG-вектор** (`QRCode.toString(data, { type: 'svg' })`), не растр; уровень коррекции ошибок — **`errorCorrectionLevel: 'H'`** (~30%, живучесть к повреждениям бирки в цеху); `margin: 2`. Режим кодирования выбирается библиотекой автоматически (Byte/UTF-8 из-за JSON со строчными буквами).
- **DateTime→Postgres:** в этой задаче новых date-запросов нет; правило CLAUDE.md не затрагивается.
- **Запуск тестов:** .NET — `dotnet test Wintime.Control.Tests.Integration`; фронт — `cd Wintime-Control-Frontend && npm run test`.

---

### Task 1: Backend — endpoint `GET /api/imm/{id}/qr` (ТПА)

Additive. Отдаёт `QrCodeDto` с `EntityType="machine"`, `EntityId=Imm.Id`, `QrData` по формату из Global Constraints.

**Files:**
- Modify: `Wintime.Control.API/Controllers/ImmController.cs` (добавить метод в конец класса, перед закрывающей `}`; уже есть `using System.Text.Json`? — нет, добавить `using System.Text.Json;` в шапку)
- Reuse: `Wintime.Control.Core/DTOs/Mold/QrCodeDto.cs` (существующий DTO, менять не нужно)
- Test: `Wintime.Control.Tests.Integration/Imm/ImmQrTests.cs` (создать)

**Interfaces:**
- Consumes: `ControlDbContext.Imms` (уже внедрён как `_context`); `Roles.Admin`, `Roles.Manager` (`Wintime.Control.Shared.Constants`, уже в usings); `QrCodeDto` (`Wintime.Control.Core.DTOs.Mold`, уже в usings через `DTOs.Imm`? — нет: добавить `using Wintime.Control.Core.DTOs.Mold;`).
- Produces: HTTP `GET /api/imm/{id:guid}/qr` → `200 QrCodeDto` | `404`. `QrData` = строка вида `{"entity":"machine","id":"<guid>"}` (только `entity` + `id` — инварианты; изменяемые поля вроде `Name` в QR не зашиваются).

- [ ] **Step 1: Написать падающий integration-тест**

Создать `Wintime.Control.Tests.Integration/Imm/ImmQrTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Imm;

[Collection("Integration")]
public class ImmQrTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ImmQrTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetImmQr_ExistingImm_ReturnsMachinePayloadWithGuidId()
    {
        Guid immId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = new Core.Entities.Imm
            {
                Name = "ТПА-QR-тест",
                TemplateId = _factory.TestTemplateId,
                IsActive = true
            };
            db.Imms.Add(imm);
            await db.SaveChangesAsync();
            immId = imm.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var response = await client.GetAsync($"/api/imm/{immId}/qr");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<QrCodeDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        dto!.EntityType.Should().Be("machine");
        dto.EntityId.Should().Be(immId.ToString());

        var payload = JsonSerializer.Deserialize<JsonElement>(dto.QrData);
        payload.GetProperty("entity").GetString().Should().Be("machine");
        payload.GetProperty("id").GetString().Should().Be(immId.ToString());
        payload.TryGetProperty("name", out _).Should().BeFalse("в QR зашивается только id");
    }

    [Fact]
    public async Task GetImmQr_UnknownId_Returns404()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var response = await client.GetAsync($"/api/imm/{Guid.NewGuid()}/qr");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: Запустить — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ImmQrTests`
Expected: FAIL — эндпоинт `/api/imm/{id}/qr` ещё не существует (404 в первом тесте на реально созданном ТПА, либо ошибка компиляции если DTO не заимпортился).

- [ ] **Step 3: Добавить endpoint**

В `Wintime.Control.API/Controllers/ImmController.cs` в шапку добавить usings (если отсутствуют):

```csharp
using System.Text.Json;
using Wintime.Control.Core.DTOs.Mold;
```

Перед закрывающей `}` класса добавить метод:

```csharp
/// <summary>
/// Получить QR-код для ТПА
/// </summary>
[HttpGet("{id:guid}/qr")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
public async Task<ActionResult<QrCodeDto>> GetImmQr(Guid id)
{
    var imm = await _context.Imms.FindAsync(id);
    if (imm == null)
        return NotFound();

    var qrData = JsonSerializer.Serialize(new
    {
        entity = "machine",
        id = imm.Id
    });

    var dto = new QrCodeDto
    {
        EntityType = "machine",
        EntityId = imm.Id.ToString(),
        QrData = qrData
    };

    return Ok(dto);
}
```

- [ ] **Step 4: Запустить — убедиться, что проходит**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~ImmQrTests`
Expected: PASS (2 теста).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.API/Controllers/ImmController.cs Wintime.Control.Tests.Integration/Imm/ImmQrTests.cs
git commit -m "feat(MUN-07): add GET /api/imm/{id}/qr endpoint"
```

---

### Task 2: Backend — исправить payload `GET /api/molds/{id}/qr` (ПФ)

Regression-fix: заменить `mold.FormId` → `mold.Id`, перейти на `System.Text.Json`. Приводит payload ПФ к формату (только `entity`+`id`), который совпадает при скане (`parsed.id === setupTask.moldId`).

**Files:**
- Modify: `Wintime.Control.API/Controllers/MoldsController.cs:222-230` (тело `GetMoldQr`; добавить `using System.Text.Json;` в шапку)
- Test: `Wintime.Control.Tests.Integration/Mold/MoldQrTests.cs` (создать)

**Interfaces:**
- Consumes: `ControlDbContext.Molds` (`_context`), `QrCodeDto` (уже в usings `DTOs.Mold`).
- Produces: `GET /api/molds/{id:guid}/qr` → `QrData` = `{"entity":"mold","id":"<Mold.Id guid>"}` (было `id=<FormId>`).

- [ ] **Step 1: Написать падающий integration-тест**

Создать `Wintime.Control.Tests.Integration/Mold/MoldQrTests.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Mold;

[Collection("Integration")]
public class MoldQrTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MoldQrTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMoldQr_PayloadIdEqualsMoldGuid_NotFormId()
    {
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Core.Entities.Mold
            {
                FormId = "ART-QR-001",
                Name = "ПФ-QR-тест",
                IsActive = true
            };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var dto = await client.GetFromJsonAsync<QrCodeDto>(
            $"/api/molds/{moldId}/qr",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var payload = JsonSerializer.Deserialize<JsonElement>(dto!.QrData);
        payload.GetProperty("entity").GetString().Should().Be("mold");
        payload.GetProperty("id").GetString().Should().Be(moldId.ToString());
        payload.GetProperty("id").GetString().Should().NotBe("ART-QR-001");
        payload.TryGetProperty("name", out _).Should().BeFalse("в QR зашивается только id");
    }
}
```

> Примечание: если у сущности `Mold` обязательны иные поля (проверить `Core/Entities/Mold.cs`), дополнить инициализатор минимально необходимыми значениями, чтобы `SaveChangesAsync` прошёл.

- [ ] **Step 2: Запустить — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~MoldQrTests`
Expected: FAIL — сейчас `id` в payload == `FormId` (`"ART-QR-001"`), а тест ждёт Guid.

- [ ] **Step 3: Исправить endpoint**

В `Wintime.Control.API/Controllers/MoldsController.cs` добавить в шапку `using System.Text.Json;`. Заменить блок `MoldsController.cs:222-230`:

```csharp
        var qrData = $"{{\"entity\":\"mold\",\"id\":\"{mold.FormId}\"}}";

        var dto = new QrCodeDto
        {
            EntityType = "mold",
            EntityId = mold.Id.ToString(),
            QrData = qrData
            // QrImageBase64 можно сгенерировать через библиотеку QRCode
        };
```

на:

```csharp
        var qrData = JsonSerializer.Serialize(new
        {
            entity = "mold",
            id = mold.Id
        });

        var dto = new QrCodeDto
        {
            EntityType = "mold",
            EntityId = mold.Id.ToString(),
            QrData = qrData
        };
```

- [ ] **Step 4: Запустить — убедиться, что проходит**

Run: `dotnet test Wintime.Control.Tests.Integration --filter FullyQualifiedName~MoldQrTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.API/Controllers/MoldsController.cs Wintime.Control.Tests.Integration/Mold/MoldQrTests.cs
git commit -m "fix(MUN-07): mold QR payload uses Guid Id (matches setup scan)"
```

---

### Task 3: Frontend — зависимость `qrcode`, api-метод `getQr`, утилита печати

**Files:**
- Modify: `Wintime-Control-Frontend/package.json` (добавляется автоматически через `npm install`)
- Modify: `Wintime-Control-Frontend/src/api/imm.js` (добавить `getQr`)
- Create: `Wintime-Control-Frontend/src/utils/qrPrint.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/qrPrint.spec.js`

**Interfaces:**
- Consumes: `apiClient` (`@/api/client`).
- Produces:
  - `immApi.getQr(id)` → `Promise<{ data: { entityType, entityId, qrData } }>`.
  - `buildQrPrintHtml(qrSvg: string, label: string): string` — самодостаточный HTML-документ для окна печати (авто-`window.print()` при загрузке): инлайнит SVG-разметку QR и экранированный `label`.

- [ ] **Step 1: Установить пакет `qrcode`**

Run: `cd Wintime-Control-Frontend && npm install qrcode`
Expected: в `package.json` → `dependencies` появляется `"qrcode": "^1.x"`.

- [ ] **Step 2: Написать падающий unit-тест утилиты печати**

Создать `Wintime-Control-Frontend/src/utils/__tests__/qrPrint.spec.js`:

```javascript
import { describe, it, expect } from 'vitest'
import { buildQrPrintHtml } from '@/utils/qrPrint'

const SVG = '<svg data-qr="1"><path d="M0 0h1v1H0z"/></svg>'

describe('buildQrPrintHtml', () => {
  it('инлайнит переданную SVG-разметку QR', () => {
    const html = buildQrPrintHtml(SVG, 'ТПА-05')
    expect(html).toContain(SVG)
  })

  it('печатает подпись и вызывает печать при загрузке', () => {
    const html = buildQrPrintHtml(SVG, 'ТПА-05')
    expect(html).toContain('ТПА-05')
    expect(html).toContain('window.print()')
  })

  it('экранирует угловые скобки в подписи (label — не доверенный текст)', () => {
    const html = buildQrPrintHtml(SVG, '<script>x</script>')
    expect(html).not.toContain('<script>x')
    expect(html).toContain('&lt;script&gt;')
  })
})
```

- [ ] **Step 3: Запустить — убедиться, что падает**

Run: `cd Wintime-Control-Frontend && npm run test -- qrPrint`
Expected: FAIL — модуль `@/utils/qrPrint` не найден.

- [ ] **Step 4: Реализовать утилиту**

Создать `Wintime-Control-Frontend/src/utils/qrPrint.js`:

```javascript
function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/**
 * Собирает самодостаточный HTML-документ для окна печати QR-кода.
 * Документ сам вызывает window.print() при загрузке.
 * qrSvg — доверенная SVG-разметка, сгенерированная пакетом qrcode из нашего
 * payload (entity+id), инлайнится как есть; label — пользовательский текст, экранируется.
 */
export function buildQrPrintHtml(qrSvg, label) {
  const safeLabel = escapeHtml(label)
  return `<!doctype html><html lang="ru"><head><meta charset="utf-8"><title>QR</title>
<style>
  html, body { margin: 0; padding: 0; }
  .wrap { display: flex; flex-direction: column; align-items: center;
          justify-content: center; min-height: 100vh; font-family: sans-serif; }
  .wrap svg { width: 60mm; height: 60mm; }
  .label { margin-top: 4mm; font-size: 14pt; font-weight: 600; text-align: center; }
  @media print { .wrap { min-height: auto; padding-top: 10mm; } }
</style></head>
<body onload="window.focus(); window.print();">
  <div class="wrap">
    ${qrSvg}
    <div class="label">${safeLabel}</div>
  </div>
</body></html>`
}
```

- [ ] **Step 5: Добавить `getQr` в imm-api**

В `Wintime-Control-Frontend/src/api/imm.js` внутрь объекта `immApi`, после `getStatistics(...)`, добавить (не забыть запятую после предыдущего метода):

```javascript
  ,
  getQr(id) {
    return apiClient.get(`/imm/${id}/qr`)
  }
```

> `moldsApi.getQr` уже существует (`src/api/molds.js`) — трогать не нужно.

- [ ] **Step 6: Запустить — убедиться, что проходит**

Run: `cd Wintime-Control-Frontend && npm run test -- qrPrint`
Expected: PASS (3 теста).

- [ ] **Step 7: Commit**

```bash
git add Wintime-Control-Frontend/package.json Wintime-Control-Frontend/package-lock.json Wintime-Control-Frontend/src/api/imm.js Wintime-Control-Frontend/src/utils/qrPrint.js Wintime-Control-Frontend/src/utils/__tests__/qrPrint.spec.js
git commit -m "feat(MUN-07): add qrcode dep, imm getQr api, qr print util"
```

---

### Task 4: Frontend — переиспользуемый компонент `QrCodeDialog.vue`

Модальное окно: получает `qrData` (строку), рендерит QR-картинку через `qrcode`, показывает подпись и кнопку «Печать».

**Files:**
- Create: `Wintime-Control-Frontend/src/components/common/QrCodeDialog.vue`

**Interfaces:**
- Consumes: `qrcode` (`QRCode.toDataURL`), `buildQrPrintHtml` (`@/utils/qrPrint`), Element Plus (`el-dialog`, `el-button`), `ElMessage`.
- Produces: компонент `<QrCodeDialog v-model="visible" :qr-data="qrData" :label="label" />`.
  - Props: `modelValue: Boolean`, `qrData: String`, `label: String`.
  - Emits: `update:modelValue`.

- [ ] **Step 1: Создать компонент**

Создать `Wintime-Control-Frontend/src/components/common/QrCodeDialog.vue`:

```vue
<template>
  <el-dialog
    :model-value="modelValue"
    title="QR-код"
    width="360px"
    align-center
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <div class="flex flex-col items-center gap-3">
      <div v-if="qrSvg" class="qr-box" v-html="qrSvg" />
      <div v-else class="qr-box flex items-center justify-center text-gray-400">
        Генерация…
      </div>
      <div class="text-base font-semibold text-center">{{ label }}</div>
    </div>

    <template #footer>
      <el-button @click="$emit('update:modelValue', false)">Закрыть</el-button>
      <el-button type="primary" :disabled="!qrSvg" @click="print">
        Печать
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, watch } from 'vue'
import QRCode from 'qrcode'
import { ElMessage } from 'element-plus'
import { buildQrPrintHtml } from '@/utils/qrPrint'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  qrData: { type: String, default: '' },
  label: { type: String, default: '' }
})
defineEmits(['update:modelValue'])

// SVG-разметка, сгенерированная qrcode из props.qrData (доверенный источник — не user input).
const qrSvg = ref('')

const render = async () => {
  qrSvg.value = ''
  if (!props.qrData) return
  try {
    qrSvg.value = await QRCode.toString(props.qrData, {
      type: 'svg',
      errorCorrectionLevel: 'H',
      margin: 2
    })
  } catch {
    ElMessage.error('Не удалось сгенерировать QR-код')
  }
}

watch(
  () => [props.modelValue, props.qrData],
  ([visible]) => { if (visible) render() }
)

const print = () => {
  const win = window.open('', '_blank', 'width=480,height=600')
  if (!win) {
    ElMessage.warning('Разрешите всплывающие окна для печати')
    return
  }
  win.document.write(buildQrPrintHtml(qrSvg.value, props.label))
  win.document.close()
}
</script>

<style scoped>
.qr-box {
  width: 16rem;
  height: 16rem;
}
.qr-box :deep(svg) {
  width: 100%;
  height: 100%;
}
</style>
```

- [ ] **Step 2: Проверить сборку фронта (нет теста монтирования — проверяем компиляцию)**

Run: `cd Wintime-Control-Frontend && npm run build`
Expected: сборка проходит без ошибок (компонент валиден, импорт `qrcode` резолвится).

- [ ] **Step 3: Commit**

```bash
git add Wintime-Control-Frontend/src/components/common/QrCodeDialog.vue
git commit -m "feat(MUN-07): reusable QrCodeDialog component (render + print)"
```

---

### Task 5: Frontend — кнопка «QR» и диалог в справочниках ПФ и ТПА

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/dictionary/ImmDictionary.vue`
- Modify: `Wintime-Control-Frontend/src/views/dictionary/MoldDictionary.vue`

**Interfaces:**
- Consumes: `QrCodeDialog` (`@/components/common/QrCodeDialog.vue`), `immApi.getQr` / `moldsApi.getQr`.

- [ ] **Step 1: ImmDictionary — кнопка QR в колонке «Действия»**

В `ImmDictionary.vue` в колонке действий (`ImmDictionary.vue:52-59`) после кнопки «Редактировать» добавить кнопку QR:

```html
            <el-button size="small" style="width: 130px; margin: 0" @click="editImm(row)">Редактировать</el-button>
            <el-button size="small" style="width: 130px; margin: 0" @click="showQr(row)">QR</el-button>
            <el-button size="small" style="width: 130px; margin: 0" type="danger" @click="deleteImm(row)">Удалить</el-button>
```

При необходимости увеличить ширину колонки: `<el-table-column label="Действия" width="200" fixed="right">` → оставить `200` (три кнопки по вертикали помещаются).

- [ ] **Step 2: ImmDictionary — диалог в шаблоне**

Перед закрывающим `</div>` корневого шаблона (после `</el-dialog>` формы) добавить:

```html
    <QrCodeDialog v-model="qrDialogVisible" :qr-data="qrData" :label="qrLabel" />
```

- [ ] **Step 3: ImmDictionary — логика в `<script setup>`**

После строки `import { immApi } from '@/api/imm'` добавить импорт:

```javascript
import QrCodeDialog from '@/components/common/QrCodeDialog.vue'
```

Рядом с остальными `ref` (например после `const editingImm = ref(null)`) добавить состояние и метод:

```javascript
const qrDialogVisible = ref(false)
const qrData = ref('')
const qrLabel = ref('')

const showQr = async (imm) => {
  try {
    const response = await immApi.getQr(imm.id)
    qrData.value = response.data.qrData
    qrLabel.value = imm.name
    qrDialogVisible.value = true
  } catch (error) {
    ElMessage.error('Ошибка получения QR-кода')
  }
}
```

- [ ] **Step 4: MoldDictionary — кнопка QR в колонке «Действия»**

В `MoldDictionary.vue` расширить колонку действий и добавить кнопку. Заменить `MoldDictionary.vue:60-67`:

```html
      <el-table-column label="Действия" width="130" fixed="right">
        <template #default="{ row }">
          <div class="flex flex-col gap-1 items-start">
            <el-button size="small" style="width: 110px" @click="editMold(row)">Редактировать</el-button>
            <el-button size="small" style="width: 110px; margin-left: 0" type="danger" @click="deleteMold(row)">Удалить</el-button>
          </div>
        </template>
      </el-table-column>
```

на:

```html
      <el-table-column label="Действия" width="150" fixed="right">
        <template #default="{ row }">
          <div class="flex flex-col gap-1 items-start">
            <el-button size="small" style="width: 130px; margin-left: 0" @click="editMold(row)">Редактировать</el-button>
            <el-button size="small" style="width: 130px; margin-left: 0" @click="showQr(row)">QR</el-button>
            <el-button size="small" style="width: 130px; margin-left: 0" type="danger" @click="deleteMold(row)">Удалить</el-button>
          </div>
        </template>
      </el-table-column>
```

- [ ] **Step 5: MoldDictionary — диалог + логика**

Перед закрывающим `</div>` корневого шаблона добавить:

```html
    <QrCodeDialog v-model="qrDialogVisible" :qr-data="qrData" :label="qrLabel" />
```

В `<script setup>` после `import { moldsApi } from '@/api/molds'` (проверить точное имя импорта в файле) добавить:

```javascript
import QrCodeDialog from '@/components/common/QrCodeDialog.vue'
```

И состояние + метод (подпись для ПФ — артикул + наименование):

```javascript
const qrDialogVisible = ref(false)
const qrData = ref('')
const qrLabel = ref('')

const showQr = async (mold) => {
  try {
    const response = await moldsApi.getQr(mold.id)
    qrData.value = response.data.qrData
    qrLabel.value = `${mold.formId} · ${mold.name}`
    qrDialogVisible.value = true
  } catch (error) {
    ElMessage.error('Ошибка получения QR-кода')
  }
}
```

> Проверить, что `ElMessage` уже импортирован в обоих файлах (в `ImmDictionary.vue` — да; в `MoldDictionary.vue` — подтвердить, при отсутствии добавить в существующий импорт `element-plus`).

- [ ] **Step 6: Проверить сборку**

Run: `cd Wintime-Control-Frontend && npm run build`
Expected: сборка без ошибок.

- [ ] **Step 7: Commit**

```bash
git add Wintime-Control-Frontend/src/views/dictionary/ImmDictionary.vue Wintime-Control-Frontend/src/views/dictionary/MoldDictionary.vue
git commit -m "feat(MUN-07): QR button + dialog in Imm and Mold dictionaries"
```

---

### Task 6: Ручная проверка и обновление бэклога

**Files:**
- Modify: (память) `feature_backlog.md` — отметить MUN-07 как ✅ (делает пользователь/агент памяти, не в репозитории)

- [ ] **Step 1: Запустить всё окружение**

Run (в отдельных терминалах):
```powershell
dotnet run --project Wintime.Control.API
```
```powershell
cd Wintime-Control-Frontend; npm run dev
```

- [ ] **Step 2: Ручной smoke-тест (роль Admin/Manager)**

- Справочник ТПА → у строки нажать «QR» → в диалоге видна картинка QR и подпись (имя ТПА) → «Печать» открывает окно печати с QR и подписью.
- Справочник пресс-форм → аналогично; подпись = «артикул · наименование».
- (Опционально, если доступен планшет/эмулятор сканера) — распечатанный QR ПФ при наладке в мобильном сверяется с заданием: `parsed.id === setupTask.moldId` теперь совпадает.

- [ ] **Step 3: Прогнать все тесты**

Run: `dotnet test Wintime.Control.Tests.Integration`
Run: `cd Wintime-Control-Frontend && npm run test`
Expected: всё зелёное.

- [ ] **Step 4: Финальный коммит (если были правки по итогам проверки)**

```bash
git add -A
git commit -m "chore(MUN-07): manual verification fixes"
```

---

## Self-Review

**Spec coverage (бэклог MUN-07):**
- «Бэкенд: `GET /api/imm/{id}/qr` по образцу `/api/molds/{id}/qr`» → Task 1. ✅
- «Фронт: в справочнике ПФ и ТПА кнопка QR → модалка с картинкой + печать» → Tasks 4, 5. ✅
- «Генерация картинки на фронте через `qrcode`» → Task 3 (dep) + Task 4 (`QRCode.toString`, SVG, EC=H). ✅
- «Формат `{"entity":"mold","id":...}` / `{"entity":"machine","id":...}`» → Tasks 1, 2 (только `entity`+`id`, id=Guid). ✅
- Доп.: исправление payload ПФ (FormId→Id) для реальной работы скана → Task 2 (решение пользователя 2026-07-02). ✅

**Placeholder scan:** код приведён полностью во всех шагах; «проверить X» — только там, где нужно свериться с фактическим файлом (обязательные поля `Mold`, наличие импорта `ElMessage`, точное имя импорта `moldsApi`) — это осознанные точки сверки, не заглушки.

**Type consistency:** `getQr` возвращает `{ data: { qrData } }`; `QrCodeDialog` принимает `qr-data`/`label`; payload-поля `entity`/`id` согласованы между Task 1/2 (бэкенд) и потребителями. `MobileTasksView` читает `parsed.name || parsed.id` — при отсутствии `name` в информационной ветке скана покажется Guid вместо имени (косметика, вне скоупа MUN-07; при желании — отдельная доработка «подтягивать имя по id»). `buildQrPrintHtml(imageDataUrl, label)` — одна сигнатура в Task 3 и Task 4.
