# Мониторинг «История»: сдвиг окна ◀ ▶ — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** В режиме «История» страницы «Мониторинг» кнопки ◀ / ▶ сдвигают окно на треть его длины и сразу перезагружают графики; ▶ не уходит дальше «сейчас».

**Architecture:** Чистая функция `shiftWindow` (utils) считает новое окно; страница `TelemetryDashboardView.vue` пишет его в поля дат и вызывает существующую `loadHistory()`. В `fetchWindow` добавляется счётчик запросов — применяется только ответ на последний.

**Tech Stack:** Vue 3 (`<script setup>`), Element Plus, dayjs, Vitest + @vue/test-utils (jsdom).

**Spec:** `docs/superpowers/specs/2026-10-16-telemetry-history-shift-design.md`

## Global Constraints

- Шаг = ⌊(конец − начало) / 3⌋ мс; длина окна сохраняется.
- ▶: если новый конец > «сейчас» — окно `[сейчас − длина, сейчас]`; если конец уже ≥ «сейчас» — ▶ неактивна. ◀ без ограничения.
- Обе кнопки неактивны без любой из границ, при конце ≤ начала или без выбранного ТПА.
- Сдвигаются текущие значения полей; новые границы пишутся в поля в формате `YYYY-MM-DDTHH:mm:ss` (локальное время) и вызывается `loadHistory()`.
- `fetchWindow` применяет только ответ на последний запрос.
- Только фронт; тесты — Vitest. Версия перед PR: `0.14.0` (master `0.13.0`).
- Коммиты заканчивать строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: `shiftWindow` — чистая функция сдвига окна

**Files:**
- Create: `Wintime-Control-Frontend/src/utils/telemetryWindow.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/telemetryWindow.spec.js`

**Interfaces:**
- Produces: `shiftWindow(fromMs: number, toMs: number, direction: -1 | 1, nowMs: number) → { fromMs: number, toMs: number } | null`.

- [ ] **Step 1: Write the failing test**

```js
import { describe, it, expect } from 'vitest'
import { shiftWindow } from '../telemetryWindow'

const MIN = 60_000
const T = Date.UTC(2026, 9, 1, 10, 0, 0) // 10:00
const NOW = T + 24 * 60 * MIN             // «сейчас» далеко справа

describe('shiftWindow', () => {
  it('◀ сдвигает на треть окна, длина сохраняется', () => {
    expect(shiftWindow(T, T + 60 * MIN, -1, NOW)).toEqual({ fromMs: T - 20 * MIN, toMs: T + 40 * MIN })
  })

  it('▶ сдвигает на треть окна, длина сохраняется', () => {
    expect(shiftWindow(T, T + 60 * MIN, 1, NOW)).toEqual({ fromMs: T + 20 * MIN, toMs: T + 80 * MIN })
  })

  it('шаг округляется вниз до миллисекунды', () => {
    expect(shiftWindow(0, 100, -1, NOW)).toEqual({ fromMs: -33, toMs: 67 })
  })

  it('▶ прижимает конец к «сейчас», если шаг уводит в будущее', () => {
    const now = T + 70 * MIN
    expect(shiftWindow(T, T + 60 * MIN, 1, now)).toEqual({ fromMs: T + 10 * MIN, toMs: now })
  })

  it('▶ невозможен, если конец уже в «сейчас» или в будущем', () => {
    expect(shiftWindow(T, T + 60 * MIN, 1, T + 60 * MIN)).toBeNull()
    expect(shiftWindow(T, T + 60 * MIN, 1, T + 30 * MIN)).toBeNull()
  })

  it('◀ не ограничен «сейчас»', () => {
    expect(shiftWindow(T, T + 60 * MIN, -1, T)).toEqual({ fromMs: T - 20 * MIN, toMs: T + 40 * MIN })
  })

  it('некорректное окно → null', () => {
    expect(shiftWindow(T, T, -1, NOW)).toBeNull()
    expect(shiftWindow(T + MIN, T, 1, NOW)).toBeNull()
    expect(shiftWindow(NaN, T, -1, NOW)).toBeNull()
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run (из `Wintime-Control-Frontend`): `npx vitest run src/utils/__tests__/telemetryWindow.spec.js`
Expected: FAIL — `Failed to resolve import "../telemetryWindow"`.

- [ ] **Step 3: Implement**

```js
// Сдвиг окна истории телеметрии кнопками ◀ / ▶ (спека 2026-10-16-telemetry-history-shift-design).
// Шаг — треть окна, длина сохраняется; вправо окно не уходит дальше «сейчас».
// null — сдвигать некуда (▶ при конце ≥ «сейчас») или окно некорректно.
export function shiftWindow(fromMs, toMs, direction, nowMs) {
  if (!Number.isFinite(fromMs) || !Number.isFinite(toMs) || toMs <= fromMs) return null
  const length = toMs - fromMs
  const step = Math.floor(length / 3)

  if (direction < 0) return { fromMs: fromMs - step, toMs: toMs - step }

  if (toMs >= nowMs) return null
  const newTo = Math.min(toMs + step, nowMs)
  return { fromMs: newTo - length, toMs: newTo }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `npx vitest run src/utils/__tests__/telemetryWindow.spec.js`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add Wintime-Control-Frontend/src/utils/telemetryWindow.js Wintime-Control-Frontend/src/utils/__tests__/telemetryWindow.spec.js
git commit -m "feat(monitoring): shiftWindow — shift telemetry history window by a third"
```

---

### Task 2: Кнопки ◀ ▶ на странице, защита от устаревших ответов, руководство, версия

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/telemetry/TelemetryDashboardView.vue` (шаблон строки 32–42; `fetchWindow` ~строка 183; новые функции рядом с `loadHistory` ~строка 223)
- Create: `Wintime-Control-Frontend/src/views/telemetry/__tests__/TelemetryDashboardView.spec.js`
- Modify: `docs/user-guide/manager.md` (§3.3 «Режим „История"», ~строка 228)
- Modify: `VERSION`

**Interfaces:**
- Consumes: `shiftWindow` из Task 1.
- Produces: кнопки `data-test="shift-back"` (◀) и `data-test="shift-forward"` (▶).

- [ ] **Step 1: Write the failing tests**

Create `Wintime-Control-Frontend/src/views/telemetry/__tests__/TelemetryDashboardView.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import dayjs from 'dayjs'

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: { imm: '11111111-1111-1111-1111-111111111111' } }),
  useRouter: () => ({ replace: vi.fn(), push: vi.fn() })
}))
vi.mock('@/api/imm', () => ({ immApi: { getList: vi.fn(), getById: vi.fn() } }))
vi.mock('@/api/telemetry', () => ({ telemetryApi: { getSignals: vi.fn(), getDashboard: vi.fn() } }))

const { immApi } = await import('@/api/imm')
const { telemetryApi } = await import('@/api/telemetry')
const { default: TelemetryDashboardView } = await import('../TelemetryDashboardView.vue')

const IMM = '11111111-1111-1111-1111-111111111111'
const F = 'YYYY-MM-DDTHH:mm:ss'
const dashboard = (truncated = false) => ({ data: { signals: [], cycles: [], statusSegments: [], truncated } })
const iso = (local) => new Date(local).toISOString()

// Страница в режиме «История» с заданными полями периода; вызовы API до этого момента сброшены.
async function mountHistory(from, to) {
  const w = mount(TelemetryDashboardView, {
    global: { plugins: [ElementPlus], stubs: { SignalChart: true, 'el-date-picker': true } }
  })
  await flushPromises()
  w.vm.mode = 'history'
  w.vm.onModeChange()
  w.vm.historyFrom = from
  w.vm.historyTo = to
  await flushPromises()
  telemetryApi.getDashboard.mockClear()
  return w
}

describe('TelemetryDashboardView — сдвиг окна истории', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    immApi.getList.mockResolvedValue({ data: [{ id: IMM, name: 'ТПА-4', templateId: 't1' }] })
    telemetryApi.getSignals.mockResolvedValue({ data: [{ parameterName: 'p1', name: 'p1', type: 'number' }] })
    telemetryApi.getDashboard.mockResolvedValue(dashboard())
  })

  it('◀ сдвигает обе границы на треть окна и загружает новое окно', async () => {
    const w = await mountHistory('2026-10-01T10:00:00', '2026-10-01T11:00:00')

    await w.find('[data-test="shift-back"]').trigger('click')
    await flushPromises()

    expect(w.vm.historyFrom).toBe('2026-10-01T09:40:00')
    expect(w.vm.historyTo).toBe('2026-10-01T10:40:00')
    expect(telemetryApi.getDashboard).toHaveBeenCalledTimes(1)
    const params = telemetryApi.getDashboard.mock.calls[0][1]
    expect(params.from).toBe(iso('2026-10-01T09:40:00'))
    expect(params.to).toBe(iso('2026-10-01T10:40:00'))
  })

  it('▶ сдвигает прошлое окно вправо на треть', async () => {
    const w = await mountHistory('2026-10-01T10:00:00', '2026-10-01T11:00:00')

    await w.find('[data-test="shift-forward"]').trigger('click')
    await flushPromises()

    expect(w.vm.historyFrom).toBe('2026-10-01T10:20:00')
    expect(w.vm.historyTo).toBe('2026-10-01T11:20:00')
  })

  it('▶ неактивна, когда конец окна в «сейчас» или позже; ◀ при этом активна', async () => {
    const to = dayjs().add(10, 'minute')
    const w = await mountHistory(to.subtract(1, 'hour').format(F), to.format(F))

    expect(w.find('[data-test="shift-forward"]').attributes('disabled')).toBeDefined()
    expect(w.find('[data-test="shift-back"]').attributes('disabled')).toBeUndefined()
  })

  it('без границ периода обе кнопки неактивны', async () => {
    const w = await mountHistory(null, null)

    expect(w.find('[data-test="shift-back"]').attributes('disabled')).toBeDefined()
    expect(w.find('[data-test="shift-forward"]').attributes('disabled')).toBeDefined()
  })

  it('при двух быстрых сдвигах показывается ответ на последний, даже если первый пришёл позже', async () => {
    const w = await mountHistory('2026-10-01T10:00:00', '2026-10-01T11:00:00')
    let resolveFirst
    let resolveSecond
    telemetryApi.getDashboard
      .mockImplementationOnce(() => new Promise(r => { resolveFirst = r }))
      .mockImplementationOnce(() => new Promise(r => { resolveSecond = r }))

    await w.find('[data-test="shift-back"]').trigger('click')
    await w.find('[data-test="shift-back"]').trigger('click')
    resolveSecond(dashboard(false))
    await flushPromises()
    resolveFirst(dashboard(true))   // устаревший ответ: «Показаны не все точки»
    await flushPromises()

    expect(w.text()).not.toContain('Показаны не все точки')
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/views/telemetry/__tests__/TelemetryDashboardView.spec.js`
Expected: FAIL — first four tests fail with `Cannot call trigger on an empty DOMWrapper` / missing `[data-test="shift-…"]`; the stale-response test may fail later with `expected '…Показаны не все точки…' not to contain …` once the buttons exist.

If mounting itself throws (e.g. on the auth store / view prefs), find the cause and adjust only the test setup (mocks/stubs), not the component; record it in the report.

- [ ] **Step 3: Implement — template**

In `TelemetryDashboardView.vue` replace the history block:

```vue
        <template v-if="mode === 'history'">
          <el-button
            size="small" data-test="shift-back" :disabled="!canShiftBack"
            title="Сдвинуть период назад на треть" @click="shift(-1)"
          >◀</el-button>
          <el-date-picker
            v-model="historyFrom" type="datetime" size="small"
            placeholder="Начало" format="YYYY-MM-DD HH:mm:ss" value-format="YYYY-MM-DDTHH:mm:ss"
          />
          <el-date-picker
            v-model="historyTo" type="datetime" size="small"
            placeholder="Конец" format="YYYY-MM-DD HH:mm:ss" value-format="YYYY-MM-DDTHH:mm:ss"
          />
          <el-button
            size="small" data-test="shift-forward" :disabled="!canShiftForward"
            title="Сдвинуть период вперёд на треть" @click="shift(1)"
          >▶</el-button>
          <el-button size="small" type="primary" @click="loadHistory">Показать</el-button>
        </template>
```

- [ ] **Step 4: Implement — script**

Add imports:

```js
import dayjs from 'dayjs'
import { shiftWindow } from '@/utils/telemetryWindow'
```

Request counter in `fetchWindow` — declare next to `let pollTimer = null`:

```js
let requestSeq = 0 // номер последнего запроса окна: применяется только ответ на последний
```

and change `fetchWindow`:

```js
async function fetchWindow(fromMs, toMs, { append = false, pointsFromMs = null } = {}) {
  const reqImmId = immId.value
  const seq = ++requestSeq
  if (!reqImmId || selected.value.length === 0) {
    clearData()
    return
  }
  try {
    const { data } = await telemetryApi.getDashboard(reqImmId, {
      from: toIsoUtc(fromMs),
      to: toIsoUtc(toMs),
      parameters: selected.value,
      pointsFrom: pointsFromMs ? toIsoUtc(pointsFromMs) : undefined,
    })
    // ответ устарел: пришёл позже более нового запроса или для прежнего ТПА
    if (seq !== requestSeq || reqImmId !== immId.value) return
    // ... (остальное тело без изменений)
  } catch (e) {
    if (seq !== requestSeq || reqImmId !== immId.value) return
    // ... (без изменений)
  }
}
```

After `loadHistory` add:

```js
// ── Сдвиг окна истории ◀ / ▶ (треть окна, вправо — не дальше «сейчас») ──
const PICKER_FORMAT = 'YYYY-MM-DDTHH:mm:ss' // = value-format полей периода

function shiftedWindow(direction) {
  if (!immId.value || !historyFrom.value || !historyTo.value) return null
  return shiftWindow(
    new Date(historyFrom.value).getTime(), new Date(historyTo.value).getTime(), direction, Date.now())
}

const canShiftBack = computed(() => shiftedWindow(-1) !== null)
const canShiftForward = computed(() => shiftedWindow(1) !== null)

function shift(direction) {
  const next = shiftedWindow(direction)
  if (!next) return
  historyFrom.value = dayjs(next.fromMs).format(PICKER_FORMAT)
  historyTo.value = dayjs(next.toMs).format(PICKER_FORMAT)
  loadHistory()
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `npx vitest run src/views/telemetry/__tests__/TelemetryDashboardView.spec.js src/utils/__tests__/telemetryWindow.spec.js`
Expected: PASS, 12 tests.

- [ ] **Step 6: User guide**

In `docs/user-guide/manager.md`, section `### 3.3. Режим «История»`, after item 3 («Нажмите **«Показать»**.») add a blank line and:

```markdown
Чтобы посмотреть чуть раньше или чуть позже, нажмите **◀** или **▶** рядом с полями периода: окно
сдвинется на треть своей длины, графики обновятся сразу. Вправо окно не уходит дальше текущего
момента — когда конец периода дошёл до «сейчас», кнопка **▶** неактивна.
```

- [ ] **Step 7: Version**

Write `0.14.0` with a trailing newline (LF) into `VERSION`.

- [ ] **Step 8: Run all frontend tests and build**

Run (из `Wintime-Control-Frontend`): `npx vitest run` and `npm run build`.
Expected: all PASS; build OK.

- [ ] **Step 9: Commit**

```bash
git add Wintime-Control-Frontend/src/views/telemetry/TelemetryDashboardView.vue Wintime-Control-Frontend/src/views/telemetry/__tests__/TelemetryDashboardView.spec.js docs/user-guide/manager.md
git commit -m "feat(monitoring): shift history window with ◀ ▶, drop stale window responses"
git add VERSION
git commit -m "build: bump version to 0.14.0"
```
