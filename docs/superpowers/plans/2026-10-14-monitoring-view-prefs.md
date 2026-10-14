# Сохранение вида «Мониторинга» — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Страница «Мониторинг» запоминает выбранные датчики и порядок диаграмм для пары (пользователь, шаблон ТПА) и восстанавливает их при следующем визите.

**Architecture:** Чистые функции в `src/utils/jwt.js` и `src/utils/monitoringViewPrefs.js` (под Vitest) + интеграция в `TelemetryDashboardView.vue`: применение в `switchImm`, явная запись в `onSelectionChange` и `move`. Хранилище — `localStorage`.

**Tech Stack:** Vue 3 (`<script setup>`), Pinia, Element Plus, Vitest.

**Спека:** `docs/superpowers/specs/2026-10-14-monitoring-view-prefs-design.md`

## Global Constraints

- Только frontend (`Wintime-Control-Frontend`), backend не меняется.
- Ключ: `wtctrl.monitoring.view.v1.<userId>.<templateId>`; значение — JSON-массив строк (`parameterName` в порядке диаграмм сверху вниз).
- `userId` = `authStore.user?.id`, иначе клейм `sub` access-токена (`authStore.accessToken`).
- Нет `userId` или `templateId` — вид не читается и не пишется.
- Ошибки хранилища и мусор в значении никогда не выбрасывают исключение наружу; страница работает как раньше (первый датчик).
- Сохранять только явно — в `onSelectionChange` и `move`; **не** через `watch(order)`.
- Режим, период, частота опроса не сохраняются.
- Все команды — из каталога `Wintime-Control-Frontend`.
- Commit message заканчивается пустой строкой и `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Чистые функции `jwtSubject` и `monitoringViewPrefs`

**Files:**
- Create: `Wintime-Control-Frontend/src/utils/jwt.js`
- Create: `Wintime-Control-Frontend/src/utils/monitoringViewPrefs.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/jwt.spec.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/monitoringViewPrefs.spec.js`

**Interfaces:**
- Produces:
  - `jwtSubject(token: string|null|undefined): string|null` — клейм `sub` из payload JWT; `null` на любом мусоре.
  - `viewPrefsKey(userId, templateId): string|null` — `'wtctrl.monitoring.view.v1.' + userId + '.' + templateId`; `null`, если любой из аргументов пустой.
  - `browserStorage(): Storage|null` — `window.localStorage` или `null`, если доступ бросает.
  - `readViewPrefs(storage: Storage|null, key: string|null): string[]|null`.
  - `writeViewPrefs(storage: Storage|null, key: string|null, order: string[]): void`.
  - `resolveSignalOrder(available: string[], saved: string[]|null): string[]`.

- [ ] **Step 1: Падающие тесты `jwtSubject`**

`src/utils/__tests__/jwt.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import { jwtSubject } from '@/utils/jwt'

const b64url = (obj) => Buffer.from(JSON.stringify(obj)).toString('base64url')
const token = (payload) => `${b64url({ alg: 'HS256', typ: 'JWT' })}.${b64url(payload)}.sig`

describe('jwtSubject', () => {
  it('возвращает sub из payload', () => {
    expect(jwtSubject(token({ sub: 'a1b2c3d4-0000-0000-0000-000000000001', role: 'Manager' })))
      .toBe('a1b2c3d4-0000-0000-0000-000000000001')
  })

  it('декодирует base64url-символы (- и _) в payload', () => {
    // '>>>' и '???' дают в base64 символы '+' и '/', в base64url — '-' и '_'
    const t = token({ sub: 'u1', note: '>>>???' })
    expect(t.split('.')[1]).toMatch(/[-_]/)
    expect(jwtSubject(t)).toBe('u1')
  })

  it('null на пустом и мусорном токене', () => {
    expect(jwtSubject(null)).toBeNull()
    expect(jwtSubject(undefined)).toBeNull()
    expect(jwtSubject('')).toBeNull()
    expect(jwtSubject('not-a-jwt')).toBeNull()
    expect(jwtSubject('a.%%%.c')).toBeNull()
    expect(jwtSubject(`${b64url({})}.${Buffer.from('not json').toString('base64url')}.s`)).toBeNull()
  })

  it('null, если sub нет или он не строка', () => {
    expect(jwtSubject(token({ role: 'Manager' }))).toBeNull()
    expect(jwtSubject(token({ sub: 42 }))).toBeNull()
  })
})
```

- [ ] **Step 2: Падающие тесты `monitoringViewPrefs`**

`src/utils/__tests__/monitoringViewPrefs.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import {
  viewPrefsKey, readViewPrefs, writeViewPrefs, resolveSignalOrder,
} from '@/utils/monitoringViewPrefs'

function memoryStorage(initial = {}) {
  const data = { ...initial }
  return {
    data,
    getItem: (k) => (k in data ? data[k] : null),
    setItem: (k, v) => { data[k] = String(v) },
  }
}
const throwingStorage = {
  getItem: () => { throw new Error('SecurityError') },
  setItem: () => { throw new Error('QuotaExceededError') },
}
const KEY = 'wtctrl.monitoring.view.v1.u1.t1'

describe('viewPrefsKey', () => {
  it('собирает ключ из пользователя и шаблона', () => {
    expect(viewPrefsKey('u1', 't1')).toBe(KEY)
  })

  it('null без пользователя или шаблона', () => {
    expect(viewPrefsKey(null, 't1')).toBeNull()
    expect(viewPrefsKey('u1', undefined)).toBeNull()
    expect(viewPrefsKey('', 't1')).toBeNull()
  })
})

describe('readViewPrefs', () => {
  it('читает сохранённый массив строк', () => {
    const s = memoryStorage({ [KEY]: '["B","A"]' })
    expect(readViewPrefs(s, KEY)).toEqual(['B', 'A'])
  })

  it('null, если ключа нет', () => {
    expect(readViewPrefs(memoryStorage(), KEY)).toBeNull()
  })

  it('null на мусоре: не JSON, не массив, не строки', () => {
    expect(readViewPrefs(memoryStorage({ [KEY]: '{oops' }), KEY)).toBeNull()
    expect(readViewPrefs(memoryStorage({ [KEY]: '{"a":1}' }), KEY)).toBeNull()
    expect(readViewPrefs(memoryStorage({ [KEY]: '["A",1]' }), KEY)).toBeNull()
  })

  it('null без хранилища или ключа и когда getItem бросает', () => {
    expect(readViewPrefs(null, KEY)).toBeNull()
    expect(readViewPrefs(memoryStorage({ [KEY]: '["A"]' }), null)).toBeNull()
    expect(readViewPrefs(throwingStorage, KEY)).toBeNull()
  })
})

describe('writeViewPrefs', () => {
  it('пишет JSON-массив', () => {
    const s = memoryStorage()
    writeViewPrefs(s, KEY, ['C', 'A'])
    expect(s.data[KEY]).toBe('["C","A"]')
  })

  it('ничего не делает без хранилища или ключа и не бросает, когда setItem бросает', () => {
    const s = memoryStorage()
    writeViewPrefs(s, null, ['A'])
    expect(s.data).toEqual({})
    expect(() => writeViewPrefs(null, KEY, ['A'])).not.toThrow()
    expect(() => writeViewPrefs(throwingStorage, KEY, ['A'])).not.toThrow()
  })
})

describe('resolveSignalOrder', () => {
  const available = ['A', 'B', 'C', 'D']

  it('сохранённый порядок', () => {
    expect(resolveSignalOrder(available, ['C', 'A'])).toEqual(['C', 'A'])
  })

  it('отбрасывает отсутствующие у ТПА датчики и дубли', () => {
    expect(resolveSignalOrder(available, ['X', 'B', 'B', 'D'])).toEqual(['B', 'D'])
  })

  it('первый датчик, если сохранения нет или пересечение пустое', () => {
    expect(resolveSignalOrder(available, null)).toEqual(['A'])
    expect(resolveSignalOrder(available, [])).toEqual(['A'])
    expect(resolveSignalOrder(available, ['X'])).toEqual(['A'])
  })

  it('пусто, если у ТПА нет датчиков', () => {
    expect(resolveSignalOrder([], ['A'])).toEqual([])
  })
})
```

- [ ] **Step 3: Запустить — должны упасть**

Run: `npx vitest run src/utils/__tests__/jwt.spec.js src/utils/__tests__/monitoringViewPrefs.spec.js`
Expected: FAIL — `Failed to resolve import "@/utils/jwt"` / `"@/utils/monitoringViewPrefs"`.

- [ ] **Step 4: Реализация `jwt.js`**

`src/utils/jwt.js`:

```js
// Клейм sub (= User.Id, см. JwtTokenService) из JWT без проверки подписи — только для
// клиентских нужд (ключ локальных настроек), не для авторизации.
export function jwtSubject(token) {
  if (typeof token !== 'string') return null
  const parts = token.split('.')
  if (parts.length !== 3) return null
  try {
    const b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/')
    const payload = JSON.parse(atob(b64.padEnd(b64.length + ((4 - (b64.length % 4)) % 4), '=')))
    return typeof payload?.sub === 'string' && payload.sub ? payload.sub : null
  } catch {
    return null
  }
}
```

- [ ] **Step 5: Реализация `monitoringViewPrefs.js`**

`src/utils/monitoringViewPrefs.js`:

```js
// Вид страницы «Мониторинг» (выбранные датчики и порядок диаграмм) в localStorage —
// на пару (пользователь, шаблон ТПА). Спека 2026-10-14-monitoring-view-prefs-design.
// Любая ошибка хранилища или мусор в значении → «настроек нет», исключения наружу не выходят.

const PREFIX = 'wtctrl.monitoring.view.v1'

export function viewPrefsKey(userId, templateId) {
  if (!userId || !templateId) return null
  return `${PREFIX}.${userId}.${templateId}`
}

export function browserStorage() {
  try {
    return window.localStorage
  } catch {
    return null
  }
}

export function readViewPrefs(storage, key) {
  if (!storage || !key) return null
  try {
    const raw = storage.getItem(key)
    if (raw == null) return null
    const value = JSON.parse(raw)
    return Array.isArray(value) && value.every(v => typeof v === 'string') ? value : null
  } catch {
    return null
  }
}

export function writeViewPrefs(storage, key, order) {
  if (!storage || !key) return
  try {
    storage.setItem(key, JSON.stringify(order))
  } catch {
    // приватный режим / квота — вид просто не запомнится
  }
}

// Сохранённый порядок, ограниченный датчиками ТПА; иначе — первый датчик (как без настроек).
export function resolveSignalOrder(available, saved) {
  const avail = new Set(available)
  const order = [...new Set((saved ?? []).filter(n => avail.has(n)))]
  if (order.length) return order
  return available.length ? [available[0]] : []
}
```

- [ ] **Step 6: Запустить — должны пройти**

Run: `npx vitest run src/utils/__tests__/jwt.spec.js src/utils/__tests__/monitoringViewPrefs.spec.js`
Expected: PASS, 4 + 12 тестов.

Run: `npm test`
Expected: все тесты PASS.

- [ ] **Step 7: Commit**

```bash
git add src/utils/jwt.js src/utils/monitoringViewPrefs.js src/utils/__tests__/jwt.spec.js src/utils/__tests__/monitoringViewPrefs.spec.js
git commit -m "feat(monitoring): view prefs storage helpers and jwtSubject"
```

---

### Task 2: Применение и сохранение вида на странице мониторинга

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/telemetry/TelemetryDashboardView.vue` (только `<script setup>`)

**Interfaces:**
- Consumes (Task 1): `jwtSubject(token)` из `@/utils/jwt`; `viewPrefsKey`, `browserStorage`, `readViewPrefs`, `writeViewPrefs`, `resolveSignalOrder` из `@/utils/monitoringViewPrefs`. `useAuthStore` из `@/stores/auth` (state `user` с полем `id`, state `accessToken`).
- Produces: —

Текущий код (для сопоставления): `immOptions` хранит `{ id, name }`; `loadImmOptions` мапит `data.map(i => ({ id: i.id, name: i.name }))`; `ensureImmOption` пушит `{ id: data.id, name: data.name }`; `switchImm` после `getSignals` выбирает `[data[0].parameterName]`; `onSelectionChange` и `move` ничего не сохраняют.

- [ ] **Step 1: Импорты и вычисление ключа**

После строки `import { monitoringQueryWithImm, showBackToCard, isImmId } from '@/utils/monitoringNav'` добавить:

```js
import { useAuthStore } from '@/stores/auth'
import { jwtSubject } from '@/utils/jwt'
import {
  viewPrefsKey, browserStorage, readViewPrefs, writeViewPrefs, resolveSignalOrder,
} from '@/utils/monitoringViewPrefs'
```

Комментарий у `immOptions` заменить на `// [{ id, name, templateId }] — действующие ТПА (+ архивный из query, если пришёл по ссылке)`.

После строки `const showBack = computed(() => showBackToCard(route.query))` добавить:

```js
const authStore = useAuthStore()

// Вид (датчики и их порядок) запоминается на пару (пользователь, шаблон ТПА).
// После F5 профиль ещё не загружен (App.onMounted позже страницы) — берём sub из токена.
function viewKey(id) {
  const userId = authStore.user?.id ?? jwtSubject(authStore.accessToken)
  const templateId = immOptions.value.find(i => i.id === id)?.templateId
  return viewPrefsKey(userId, templateId)
}

function saveView() {
  writeViewPrefs(browserStorage(), viewKey(immId.value), order.value)
}
```

- [ ] **Step 2: `templateId` в опциях ТПА**

В `loadImmOptions` заменить `.map(i => ({ id: i.id, name: i.name }))` на
`.map(i => ({ id: i.id, name: i.name, templateId: i.templateId }))`.

В `ensureImmOption` заменить `immOptions.value.push({ id: data.id, name: data.name })` на
`immOptions.value.push({ id: data.id, name: data.name, templateId: data.templateId })`.

- [ ] **Step 3: Применение в `switchImm`**

Заменить блок

```js
    availableSignals.value = data
    if (data.length) {
      selected.value = [data[0].parameterName] // по умолчанию первый сигнал
      lastNonEmptySelection = [...selected.value]
      syncOrder()
    }
```

на

```js
    availableSignals.value = data
    // сохранённый вид для шаблона этого ТПА; без него — первый сигнал
    const initial = resolveSignalOrder(
      data.map(s => s.parameterName),
      readViewPrefs(browserStorage(), viewKey(id)),
    )
    selected.value = [...initial]
    order.value = [...initial]
    lastNonEmptySelection = [...initial]
```

- [ ] **Step 4: Сохранение в `onSelectionChange` и `move`**

В `onSelectionChange` после строки `syncOrder()` добавить строку `saveView()`.

В `move` после строки `order.value = arr` добавить строку `saveView()`.

(Ранний `return` в `onSelectionChange` при попытке снять последний датчик остаётся до `saveView()` — откат не сохраняется, вид не меняется.)

- [ ] **Step 5: Сборка и тесты**

Run: `npm run build`
Expected: сборка без ошибок (известный шум — предупреждение о размере чанка).

Run: `npm test`
Expected: все тесты PASS.

- [ ] **Step 6: Commit**

```bash
git add src/views/telemetry/TelemetryDashboardView.vue
git commit -m "feat(monitoring): remember selected signals and chart order per user and IMM template"
```

---

### Task 3: Ручная проверка сценариев

**Files:** —

- [ ] **Step 1: Стенд** — dev Postgres/Mosquitto (`docker compose -f docker-compose.dev.yml start postgres mosquitto`), API, `Wintime.Control.Emulator`, `npm run dev`; в БД уже есть два ТПА шаблона «Borche Emu».
- [ ] **Step 2: Сценарии спеки** — 1) отметить 3 датчика, переставить, уйти на дашборд, вернуться и выбрать тот же ТПА → тот же вид; F5 → тот же вид; 2) другой ТПА того же шаблона → тот же вид; 3) нет сохранения → первый датчик; 4) войти другим пользователем → свой (дефолтный) вид.
- [ ] **Step 3:** Остановить всё, что запускалось.
