# Импорт/экспорт шаблонов ТПА — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Кнопки «Экспорт» (строка таблицы) и «Импорт» (шапка) на странице «Шаблоны оборудования»: экспорт шаблона в JSON-файл, импорт файла в панель «Новый шаблон» со стандартным сохранением.

**Architecture:** Только фронтенд. Вся логика формата — чистые функции в `src/utils/templateFile.js` (покрыты Vitest, включая круговой тест экспорт→импорт→сохранение→экспорт). `TemplatesView.vue` только вызывает их и работает с Blob/файлами. Отдельный xUnit-интеграционный тест фиксирует, что сервер сохраняет `jsonConfig` без семантических потерь (Create и Update).

**Tech Stack:** Vue 3 + Element Plus, Vitest; ASP.NET Core 9, xUnit + FluentAssertions + Testcontainers (PostgreSQL).

**Spec:** `docs/superpowers/specs/2026-09-25-template-import-export-design.md`

## Global Constraints

- Идентификатор формата: `"format": "wintime-control-template"`, `"formatVersion": 1`.
- В `template` ровно поля панели: `name, manufacturer, model, version, author, connectorType, jsonConfig`. Не экспортировать `id`, `createdAt`, `updatedAt`, `isActive`.
- `jsonConfig` в файле — вложенный объект, не строка.
- Нормализация экспорта: `manufacturer/model/author`: `null` → `""`; `connectorType`: пусто → `null`; `version`: пусто → `"1.0"`.
- Файл: `JSON.stringify(obj, null, 2)`, UTF-8, имя `template-<name>-<version>.json` (`\ / : * ? " < > |`, управляющие и пробельные символы → `_`).
- Тексты ошибок (дословно):
  - «Шаблон содержит некорректную JSON-конфигурацию»
  - «Файл не является корректным JSON»
  - «Файл не является шаблоном Wintime Control»
  - «Версия формата N не поддерживается» (N — значение из файла)
  - «В файле нет наименования шаблона»
  - «JSON-конфигурация в файле должна быть объектом»
  - «Неверный формат JSON-конфигурации»
- Импорт всегда создаёт новый шаблон (`editingTemplate = null`), без проверки дубликатов имени.
- Бэкенд не меняется. Прямой push в `master` запрещён — работа в ветке `feature/template-import-export` (уже создана, спека закоммичена).

## File Structure

| Файл | Действие | Ответственность |
|---|---|---|
| `Wintime-Control-Frontend/src/utils/templateFile.js` | Create | Формат файла: сборка экспорта, имя файла, разбор импорта, форма → тело запроса |
| `Wintime-Control-Frontend/src/utils/__tests__/templateFile.spec.js` | Create | Vitest: юнит + круговой тест |
| `Wintime-Control-Frontend/src/views/admin/TemplatesView.vue` | Modify | Кнопки, скачивание Blob, выбор файла, `saveTemplate` через `formToRequest` |
| `Wintime.Control.Tests.Integration/Templates/TemplateJsonConfigRoundTripTests.cs` | Create | xUnit: `jsonConfig` переживает Create/Update + GET |

---

### Task 1: Модуль формата файла `templateFile.js` + Vitest

**Files:**
- Create: `Wintime-Control-Frontend/src/utils/templateFile.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/templateFile.spec.js`

**Interfaces:**
- Consumes: DTO шаблона из `templatesApi.getList()` — `{ id, name, manufacturer, model, version, author, connectorType, jsonConfig: string, createdAt, updatedAt }`.
- Produces (ES-экспорты):
  - `TEMPLATE_FILE_FORMAT: string`, `TEMPLATE_FILE_VERSION: number`
  - `buildTemplateExport(template: TemplateDto, now?: Date): { format, formatVersion, exportedAt: string, template: { name, manufacturer, model, version, author, connectorType: string|null, jsonConfig: object } }` — бросает `Error`
  - `templateExportFileName(template: TemplateDto): string`
  - `parseTemplateImport(text: string): { name, manufacturer, model, version, author, connectorType, jsonConfigString }` (все — строки) — бросает `Error`
  - `formToRequest(form): { name, manufacturer, model, version, author, connectorType: string|null, jsonConfig: any }` — бросает `Error`

- [ ] **Step 1: Написать падающие тесты**

Создать `Wintime-Control-Frontend/src/utils/__tests__/templateFile.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import {
  TEMPLATE_FILE_FORMAT,
  TEMPLATE_FILE_VERSION,
  buildTemplateExport,
  templateExportFileName,
  parseTemplateImport,
  formToRequest
} from '../templateFile'

const NOW = new Date('2026-09-25T10:00:00.000Z')

const fullTemplate = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Haitian MA1200 (цех 2)',
  manufacturer: 'Haitian',
  model: 'MA1200',
  version: '2.1',
  author: 'Wintime',
  connectorType: 'usr-modbus',
  jsonConfig: '{"sensors":[{"name":"Температура зоны 1","field":"temp_zone_1","type":"float","threshold":0.5}]}',
  createdAt: '2026-09-01T08:00:00Z',
  updatedAt: '2026-09-02T08:00:00Z'
}

const sparseTemplate = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'Минимальный',
  manufacturer: null,
  model: '',
  version: '',
  author: null,
  connectorType: '',
  jsonConfig: '{}'
}

const numericTemplate = {
  id: '33333333-3333-3333-3333-333333333333',
  name: 'Числа и кириллица',
  manufacturer: 'Siger',
  model: '160v5',
  version: '1.0',
  author: 'Технолог',
  connectorType: null,
  jsonConfig: '{"a":0.5,"b":1.5,"nested":{"arr":[1,2.25,-3]},"label":"Давление, бар"}'
}

// Имитация TemplatesController.Create: строки ?? "", version ?? "1.0",
// jsonConfig сериализуется обратно в строку.
function simulateServerCreate(request) {
  return {
    id: '99999999-9999-9999-9999-999999999999',
    name: request.name,
    manufacturer: request.manufacturer ?? '',
    model: request.model ?? '',
    version: request.version ?? '1.0',
    author: request.author ?? '',
    connectorType: request.connectorType,
    jsonConfig: JSON.stringify(request.jsonConfig)
  }
}

function withoutExportedAt(file) {
  const { exportedAt, ...rest } = file
  return rest
}

describe('buildTemplateExport', () => {
  it('собирает файл формата v1 с полями панели', () => {
    const file = buildTemplateExport(fullTemplate, NOW)
    expect(file).toEqual({
      format: TEMPLATE_FILE_FORMAT,
      formatVersion: TEMPLATE_FILE_VERSION,
      exportedAt: '2026-09-25T10:00:00.000Z',
      template: {
        name: 'Haitian MA1200 (цех 2)',
        manufacturer: 'Haitian',
        model: 'MA1200',
        version: '2.1',
        author: 'Wintime',
        connectorType: 'usr-modbus',
        jsonConfig: {
          sensors: [{ name: 'Температура зоны 1', field: 'temp_zone_1', type: 'float', threshold: 0.5 }]
        }
      }
    })
  })

  it('не экспортирует служебные поля', () => {
    const { template } = buildTemplateExport(fullTemplate, NOW)
    expect(template).not.toHaveProperty('id')
    expect(template).not.toHaveProperty('createdAt')
    expect(template).not.toHaveProperty('updatedAt')
    expect(template).not.toHaveProperty('isActive')
  })

  it('нормализует пустые поля', () => {
    const { template } = buildTemplateExport(sparseTemplate, NOW)
    expect(template.manufacturer).toBe('')
    expect(template.model).toBe('')
    expect(template.author).toBe('')
    expect(template.version).toBe('1.0')
    expect(template.connectorType).toBeNull()
    expect(template.jsonConfig).toEqual({})
  })

  it('бросает ошибку на некорректном jsonConfig', () => {
    expect(() => buildTemplateExport({ ...fullTemplate, jsonConfig: '{oops' }, NOW))
      .toThrow('Шаблон содержит некорректную JSON-конфигурацию')
  })
})

describe('templateExportFileName', () => {
  it('строит имя из наименования и версии', () => {
    expect(templateExportFileName({ name: 'Haitian', version: '2.1' })).toBe('template-Haitian-2.1.json')
  })

  it('заменяет недопустимые и пробельные символы на _', () => {
    expect(templateExportFileName({ name: 'A/B\\C:D*E?F"G<H>I|J K', version: '1.0' }))
      .toBe('template-A_B_C_D_E_F_G_H_I_J_K-1.0.json')
  })

  it('подставляет версию 1.0, если она пустая', () => {
    expect(templateExportFileName({ name: 'X', version: '' })).toBe('template-X-1.0.json')
  })
})

describe('parseTemplateImport', () => {
  const validFile = (templatePatch = {}, filePatch = {}) => JSON.stringify({
    format: TEMPLATE_FILE_FORMAT,
    formatVersion: 1,
    exportedAt: '2026-09-25T10:00:00.000Z',
    template: {
      name: 'Импорт',
      manufacturer: 'Haitian',
      model: 'MA1200',
      version: '2.1',
      author: 'Wintime',
      connectorType: 'usr-modbus',
      jsonConfig: { sensors: [] },
      ...templatePatch
    },
    ...filePatch
  })

  it('возвращает поля формы', () => {
    expect(parseTemplateImport(validFile())).toEqual({
      name: 'Импорт',
      manufacturer: 'Haitian',
      model: 'MA1200',
      version: '2.1',
      author: 'Wintime',
      connectorType: 'usr-modbus',
      jsonConfigString: JSON.stringify({ sensors: [] }, null, 2)
    })
  })

  it('подставляет умолчания для отсутствующих необязательных полей', () => {
    const text = JSON.stringify({ format: TEMPLATE_FILE_FORMAT, formatVersion: 1, template: { name: 'Только имя' } })
    expect(parseTemplateImport(text)).toEqual({
      name: 'Только имя',
      manufacturer: '',
      model: '',
      version: '1.0',
      author: '',
      connectorType: '',
      jsonConfigString: '{}'
    })
  })

  it('connectorType null → пустая строка формы', () => {
    expect(parseTemplateImport(validFile({ connectorType: null })).connectorType).toBe('')
  })

  it('ошибка: не JSON', () => {
    expect(() => parseTemplateImport('not json')).toThrow('Файл не является корректным JSON')
  })

  it('ошибка: чужой формат', () => {
    expect(() => parseTemplateImport(validFile({}, { format: 'something-else' })))
      .toThrow('Файл не является шаблоном Wintime Control')
    expect(() => parseTemplateImport('[1,2,3]')).toThrow('Файл не является шаблоном Wintime Control')
  })

  it('ошибка: неподдерживаемая версия формата', () => {
    expect(() => parseTemplateImport(validFile({}, { formatVersion: 2 })))
      .toThrow('Версия формата 2 не поддерживается')
    expect(() => parseTemplateImport(validFile({}, { formatVersion: 0 })))
      .toThrow('Версия формата 0 не поддерживается')
  })

  it('ошибка: нет наименования', () => {
    expect(() => parseTemplateImport(validFile({ name: '   ' }))).toThrow('В файле нет наименования шаблона')
    expect(() => parseTemplateImport(validFile({}, { template: null }))).toThrow('В файле нет наименования шаблона')
  })

  it('ошибка: jsonConfig не объект', () => {
    expect(() => parseTemplateImport(validFile({ jsonConfig: '{"a":1}' })))
      .toThrow('JSON-конфигурация в файле должна быть объектом')
    expect(() => parseTemplateImport(validFile({ jsonConfig: [1, 2] })))
      .toThrow('JSON-конфигурация в файле должна быть объектом')
  })
})

describe('formToRequest', () => {
  const form = {
    name: 'Ф', manufacturer: 'M', model: 'X', version: '1.0', author: 'A',
    connectorType: '', jsonConfigString: '{\n  "a": 1\n}'
  }

  it('пустой connectorType → null, jsonConfig разобран', () => {
    expect(formToRequest(form)).toEqual({
      name: 'Ф', manufacturer: 'M', model: 'X', version: '1.0', author: 'A',
      connectorType: null, jsonConfig: { a: 1 }
    })
  })

  it('пустая JSON-конфигурация → {}', () => {
    expect(formToRequest({ ...form, jsonConfigString: '' }).jsonConfig).toEqual({})
  })

  it('ошибка на битом JSON', () => {
    expect(() => formToRequest({ ...form, jsonConfigString: '{oops' }))
      .toThrow('Неверный формат JSON-конфигурации')
  })
})

describe('круговой тест: экспорт → импорт → сохранение → экспорт', () => {
  it.each([
    ['полный шаблон', fullTemplate],
    ['пустые необязательные поля', sparseTemplate],
    ['кириллица и дробные числа', numericTemplate]
  ])('%s: повторный экспорт совпадает с первым (кроме exportedAt)', (_, template) => {
    const first = buildTemplateExport(template, NOW)
    const form = parseTemplateImport(JSON.stringify(first, null, 2))
    const saved = simulateServerCreate(formToRequest(form))
    const second = buildTemplateExport(saved, new Date('2026-09-26T00:00:00.000Z'))

    expect(withoutExportedAt(second)).toEqual(withoutExportedAt(first))
  })
})
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `cd Wintime-Control-Frontend && npx vitest run src/utils/__tests__/templateFile.spec.js`
Expected: FAIL — `Failed to resolve import "../templateFile"`.

- [ ] **Step 3: Реализовать модуль**

Создать `Wintime-Control-Frontend/src/utils/templateFile.js`:

```js
// Формат файла шаблона оборудования (импорт/экспорт между инсталляциями Control).
// Спека: docs/superpowers/specs/2026-09-25-template-import-export-design.md

export const TEMPLATE_FILE_FORMAT = 'wintime-control-template'
export const TEMPLATE_FILE_VERSION = 1

const DEFAULT_VERSION = '1.0'
const FORBIDDEN_FILENAME_CHARS = '\\/:*?"<>|'

const isPlainObject = (value) =>
  value !== null && typeof value === 'object' && !Array.isArray(value)

const asString = (value) => (typeof value === 'string' ? value : '')

/** Файл экспорта из DTO шаблона (jsonConfig в DTO — строка). */
export function buildTemplateExport(template, now = new Date()) {
  let jsonConfig
  try {
    jsonConfig = template.jsonConfig ? JSON.parse(template.jsonConfig) : {}
  } catch {
    throw new Error('Шаблон содержит некорректную JSON-конфигурацию')
  }

  return {
    format: TEMPLATE_FILE_FORMAT,
    formatVersion: TEMPLATE_FILE_VERSION,
    exportedAt: now.toISOString(),
    template: {
      name: template.name ?? '',
      manufacturer: template.manufacturer ?? '',
      model: template.model ?? '',
      version: template.version || DEFAULT_VERSION,
      author: template.author ?? '',
      connectorType: template.connectorType || null,
      jsonConfig
    }
  }
}

function sanitizeFileNamePart(value) {
  return [...String(value)]
    .map((ch) => (ch < ' ' || /\s/.test(ch) || FORBIDDEN_FILENAME_CHARS.includes(ch) ? '_' : ch))
    .join('')
}

export function templateExportFileName(template) {
  const name = sanitizeFileNamePart(template.name ?? '')
  const version = sanitizeFileNamePart(template.version || DEFAULT_VERSION)
  return `template-${name}-${version}.json`
}

/** Текст файла → поля формы шаблона. Бросает Error с сообщением для пользователя. */
export function parseTemplateImport(text) {
  let data
  try {
    data = JSON.parse(text)
  } catch {
    throw new Error('Файл не является корректным JSON')
  }

  if (!isPlainObject(data) || data.format !== TEMPLATE_FILE_FORMAT) {
    throw new Error('Файл не является шаблоном Wintime Control')
  }

  const formatVersion = data.formatVersion
  if (!Number.isInteger(formatVersion) || formatVersion < 1 || formatVersion > TEMPLATE_FILE_VERSION) {
    throw new Error(`Версия формата ${formatVersion} не поддерживается`)
  }

  const t = data.template
  if (!isPlainObject(t) || typeof t.name !== 'string' || !t.name.trim()) {
    throw new Error('В файле нет наименования шаблона')
  }

  if (t.jsonConfig !== undefined && t.jsonConfig !== null && !isPlainObject(t.jsonConfig)) {
    throw new Error('JSON-конфигурация в файле должна быть объектом')
  }

  return {
    name: t.name,
    manufacturer: asString(t.manufacturer),
    model: asString(t.model),
    version: asString(t.version) || DEFAULT_VERSION,
    author: asString(t.author),
    connectorType: asString(t.connectorType),
    jsonConfigString: JSON.stringify(t.jsonConfig ?? {}, null, 2)
  }
}

/** Поля формы → тело запроса create/update. */
export function formToRequest(form) {
  let jsonConfig
  try {
    jsonConfig = form.jsonConfigString ? JSON.parse(form.jsonConfigString) : {}
  } catch {
    throw new Error('Неверный формат JSON-конфигурации')
  }

  return {
    name: form.name,
    manufacturer: form.manufacturer,
    model: form.model,
    version: form.version,
    author: form.author,
    connectorType: form.connectorType || null,
    jsonConfig
  }
}
```

- [ ] **Step 4: Убедиться, что тесты проходят**

Run: `cd Wintime-Control-Frontend && npx vitest run src/utils/__tests__/templateFile.spec.js`
Expected: PASS, все тесты зелёные (включая 3 круговых).

- [ ] **Step 5: Commit**

```bash
git add Wintime-Control-Frontend/src/utils/templateFile.js Wintime-Control-Frontend/src/utils/__tests__/templateFile.spec.js
git commit -m "feat(templates): JSON file format for template import/export"
```

---

### Task 2: Кнопки «Экспорт»/«Импорт» в `TemplatesView.vue`

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/admin/TemplatesView.vue`

**Interfaces:**
- Consumes: `buildTemplateExport`, `templateExportFileName`, `parseTemplateImport`, `formToRequest` из `@/utils/templateFile` (Task 1).
- Produces: ничего для других задач.

- [ ] **Step 1: Кнопка «Импорт» и скрытый input в шапке**

Заменить блок кнопки «Новый шаблон» (строки 8–11):

```vue
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Новый шаблон
      </el-button>
```

на:

```vue
      <div class="flex gap-2">
        <el-button @click="openImportDialog">Импорт</el-button>
        <el-button type="primary" @click="showCreateModal">
          <el-icon class="mr-1"><Plus /></el-icon>
          Новый шаблон
        </el-button>
        <input
          ref="importInput"
          type="file"
          accept=".json,application/json"
          class="hidden"
          @change="onImportFileSelected"
        />
      </div>
```

- [ ] **Step 2: Кнопка «Экспорт» в строке таблицы**

Заменить блок действий (строки 28–31):

```vue
          <div class="flex flex-col gap-1">
            <el-button size="small" style="width: 130px; margin: 0" @click="editTemplate(row)">Редактировать</el-button>
            <el-button size="small" type="danger" style="width: 130px; margin: 0" @click="deleteTemplate(row)">Удалить</el-button>
          </div>
```

на:

```vue
          <div class="flex flex-col gap-1">
            <el-button size="small" style="width: 130px; margin: 0" @click="editTemplate(row)">Редактировать</el-button>
            <el-button size="small" style="width: 130px; margin: 0" @click="exportTemplate(row)">Экспорт</el-button>
            <el-button size="small" type="danger" style="width: 130px; margin: 0" @click="deleteTemplate(row)">Удалить</el-button>
          </div>
```

- [ ] **Step 3: Скрипт — импорт модуля и ref input**

После строки `import dayjs from 'dayjs'` добавить:

```js
import {
  buildTemplateExport,
  templateExportFileName,
  parseTemplateImport,
  formToRequest
} from '@/utils/templateFile'
```

После `const templates = ref([])` добавить:

```js
const importInput = ref(null)
```

- [ ] **Step 4: `saveTemplate` через `formToRequest`**

Заменить в `saveTemplate` фрагмент от `let jsonConfig` до конца объекта `data` включительно:

```js
  let jsonConfig
  try {
    jsonConfig = form.jsonConfigString ? JSON.parse(form.jsonConfigString) : {}
  } catch (error) {
    ElMessage.error('Неверный формат JSON-конфигурации')
    return
  }

  saving.value = true
  try {
    const data = {
      name: form.name,
      manufacturer: form.manufacturer,
      model: form.model,
      version: form.version,
      author: form.author,
      connectorType: form.connectorType || null,
      jsonConfig
    }
```

на:

```js
  let data
  try {
    data = formToRequest(form)
  } catch (error) {
    ElMessage.error(error.message)
    return
  }

  saving.value = true
  try {
```

(Остаток `saveTemplate` — `if (editingTemplate.value) { … update … } else { … create … }` — не меняется.)

- [ ] **Step 5: Функции экспорта и импорта**

Перед `const deleteTemplate = async (template) => {` вставить:

```js
const exportTemplate = (template) => {
  let file
  try {
    file = buildTemplateExport(template)
  } catch (error) {
    ElMessage.error(error.message)
    return
  }

  const blob = new Blob([JSON.stringify(file, null, 2)], { type: 'application/json;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = templateExportFileName(template)
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

const openImportDialog = () => {
  importInput.value?.click()
}

const onImportFileSelected = async (event) => {
  const input = event.target
  const file = input.files?.[0]
  if (!file) return

  try {
    const fields = parseTemplateImport(await file.text())
    editingTemplate.value = null
    Object.assign(form, fields)
    dialogVisible.value = true
  } catch (error) {
    ElMessage.error(error.message)
  } finally {
    input.value = '' // чтобы повторный выбор того же файла снова вызвал change
  }
}
```

- [ ] **Step 6: Прогнать тесты и сборку фронта**

Run: `cd Wintime-Control-Frontend && npm test && npm run build`
Expected: все Vitest-тесты PASS; `vite build` завершается без ошибок.

- [ ] **Step 7: Ручная проверка в браузере**

Запустить API (`dotnet run --project Wintime.Control.API`) и фронт (`cd Wintime-Control-Frontend && npm run dev`), войти админом, открыть «Шаблоны оборудования»:
1. «Экспорт» у любого шаблона → скачан `template-<name>-<version>.json`; внутри `format`, `formatVersion: 1`, `jsonConfig` — объект, кириллица читаема.
2. «Импорт» → выбрать этот файл → открыта панель «Новый шаблон» с теми же полями; «Сохранить» → в таблице новый шаблон.
3. «Экспорт» нового шаблона → содержимое совпадает с первым файлом, кроме `exportedAt`.
4. «Импорт» произвольного JSON (например, `package.json`) → «Файл не является шаблоном Wintime Control», панель не открылась.
5. Повторный «Импорт» того же файла подряд снова открывает панель.

- [ ] **Step 8: Commit**

```bash
git add Wintime-Control-Frontend/src/views/admin/TemplatesView.vue
git commit -m "feat(templates): export/import buttons on templates page"
```

---

### Task 3: Интеграционный тест сохранения `jsonConfig` (Create/Update)

Это характеризующий тест: он ожидаемо проходит сразу. Если падает — это реальный дефект сервера; **не** правь контроллер молча, а сообщи (в спеке выравнивание Create/Update вынесено в отдельный баг-фикс).

**Files:**
- Create: `Wintime.Control.Tests.Integration/Templates/TemplateJsonConfigRoundTripTests.cs`

**Interfaces:**
- Consumes: `IntegrationTestFactory`, `AuthHelper` (`Wintime.Control.Tests.Integration.Infrastructure`); `TemplateDto` (`Wintime.Control.Core.DTOs.Template`, `JsonConfig` — `string`); сидированный пользователь `admin` / `Admin123!` (используется в `Auth/AuthControllerTests.cs`).
- Produces: ничего.

- [ ] **Step 1: Написать тест**

Создать `Wintime.Control.Tests.Integration/Templates/TemplateJsonConfigRoundTripTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Wintime.Control.Core.DTOs.Template;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Templates;

/// <summary>
/// jsonConfig шаблона должен переживать сохранение без семантических потерь —
/// на этом держится круговой импорт/экспорт шаблонов (фронт сравнивает данные, не байты).
/// Create и Update сохраняют jsonConfig разными путями, поэтому проверяем оба.
/// </summary>
[Collection("Integration")]
public class TemplateJsonConfigRoundTripTests : IClassFixture<IntegrationTestFactory>
{
    private const string ConfigJson =
        """{"device_timeout_seconds": 30, "sensors": [{"name": "Температура зоны 1", "field": "temp_zone_1", "type": "float", "threshold": 0.5}, {"name": "Давление, бар", "field": "pressure", "type": "float", "threshold": 1.5}]}""";

    private readonly IntegrationTestFactory _factory;
    public TemplateJsonConfigRoundTripTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "admin", "Admin123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static object NewTemplate(string name, string configJson) => new
    {
        name,
        manufacturer = "Haitian",
        model = "MA1200",
        version = "1.0",
        author = "Wintime",
        connectorType = "usr-modbus",
        jsonConfig = JsonNode.Parse(configJson)
    };

    private static void AssertSameJson(string actual, string expected) =>
        JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected))
            .Should().BeTrue($"сохранённый jsonConfig {actual} должен совпадать с {expected}");

    private static async Task<Guid> CreateAsync(HttpClient client, string name, string configJson)
    {
        var resp = await client.PostAsJsonAsync("/api/templates", NewTemplate(name, configJson));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<TemplateDto>();
        return dto!.Id;
    }

    [Fact]
    public async Task Create_PreservesJsonConfigSemantics()
    {
        var client = await AdminClientAsync();
        var id = await CreateAsync(client, $"RT-create-{Guid.NewGuid():N}", ConfigJson);

        var saved = await client.GetFromJsonAsync<TemplateDto>($"/api/templates/{id}");

        AssertSameJson(saved!.JsonConfig, ConfigJson);
    }

    [Fact]
    public async Task Update_PreservesJsonConfigSemantics()
    {
        var client = await AdminClientAsync();
        var name = $"RT-update-{Guid.NewGuid():N}";
        var id = await CreateAsync(client, name, """{"sensors": []}""");

        var put = await client.PutAsJsonAsync($"/api/templates/{id}", NewTemplate(name, ConfigJson));
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var saved = await client.GetFromJsonAsync<TemplateDto>($"/api/templates/{id}");

        AssertSameJson(saved!.JsonConfig, ConfigJson);
    }
}
```

- [ ] **Step 2: Прогнать тест** (нужен запущенный Docker — Testcontainers)

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~TemplateJsonConfigRoundTripTests"`
Expected: PASS, 2 теста. Если FAIL — остановиться и сообщить с выводом (см. примечание к задаче).

- [ ] **Step 3: Прогнать весь интеграционный набор** (регрессии)

Run: `dotnet test Wintime.Control.Tests.Integration`
Expected: все тесты PASS.

- [ ] **Step 4: Commit**

```bash
git add Wintime.Control.Tests.Integration/Templates/TemplateJsonConfigRoundTripTests.cs
git commit -m "test(templates): jsonConfig survives create/update round-trip"
```
