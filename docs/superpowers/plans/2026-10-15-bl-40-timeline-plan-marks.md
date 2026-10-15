# BL-40 Плановые метки на хронологии карточки ТПА — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** На хронологии смены в карточке ТПА (`ImmDetailModal` → `ShiftTimeline`) показать ряд «План» с плановым окном наладки, отклонением старта и фактической длительностью наладки; разложить перекрывающиеся бары по дорожкам.

**Architecture:** Только фронтенд, бэкенд не меняется. Вся логика — чистые функции в `src/utils/shiftTimeline.js` (покрыты Vitest). `ShiftTimeline.vue` рисует ряд «План» и дорожки. `ImmDetailModal.vue` делает второй запрос `GET /api/tasks` по плановой дате и сливает результат с активными заданиями.

**Tech Stack:** Vue 3 (`<script setup>`), Element Plus, Vitest + @vue/test-utils (jsdom).

**Спека:** `docs/superpowers/specs/2026-10-15-bl-40-timeline-plan-marks-design.md`

## Global Constraints

- Плитка ТПА (`ImmCard`) не меняется.
- Плановое окончание наладки = `plannedStartAt + plannedSetupMinutes` (от планового начала, не от факта).
- Отклонение Δ = `setupStartedAt − plannedStartAt`: |Δ| < 1 мин → `вовремя`; Δ > 0 → `опозд. 25м` (красный); Δ < 0 → `раньше на 10м` (серый).
- Факт наладки = `startedAt − setupStartedAt`, показывается только при наличии `startedAt`.
- Наладка не началась → в баре только `норм 45м`.
- В ряд «План» попадает задание, только если плановое окно пересекает смену; просроченные неначатые из прошлых смен — нет.
- Формат длительностей: `1ч 10м`, `45м`, `2ч`.
- Время отображается в поясе браузера (не переводить в пояс завода).
- Тесты фронта: Vitest. Команды запускать из `Wintime-Control-Frontend`.

---

## Файлы

- Modify: `Wintime-Control-Frontend/src/utils/shiftTimeline.js` — чистые функции: план, отклонение, метки, дорожки, слияние, диапазон дат.
- Modify: `Wintime-Control-Frontend/src/utils/__tests__/shiftTimeline.spec.js` — тесты утилит.
- Modify: `Wintime-Control-Frontend/src/components/dashboard/ShiftTimeline.vue` — ряд «План», дорожки, тултипы.
- Create: `Wintime-Control-Frontend/src/components/dashboard/__tests__/ShiftTimeline.spec.js` — тест рендера.
- Modify: `Wintime-Control-Frontend/src/views/dashboard/ImmDetailModal.vue` — второй запрос и слияние.
- Modify: `docs/user-guide/manager.md` (раздел 2.5) — описание ряда «План».

---

### Task 1: Утилиты плана наладки (окно, отклонение, метки, тултип)

**Files:**
- Modify: `Wintime-Control-Frontend/src/utils/shiftTimeline.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/shiftTimeline.spec.js`

**Interfaces:**
- Consumes: `TaskDto` поля `plannedStartAt` (ISO UTC), `plannedSetupMinutes` (int), `setupStartedAt`, `startedAt` (ISO | null).
- Produces:
  - `formatDuration(ms: number): string` — `'1ч 10м' | '45м' | '2ч'`
  - `formatClock(date: Date|string|number): string` — `'HH:MM'` (ru-RU, пояс браузера)
  - `planBarLayout(task, shiftStart: Date, shiftEnd: Date): { leftPct, widthPct, clippedLeft, clippedRight, isMarker } | null`
  - `setupDeviation(task): { minutes: number (со знаком), kind: 'late'|'early'|'onTime', label: string } | null`
  - `setupActualMs(task): number | null`
  - `planMarks(task): Array<{ text: string, tone: 'late'|'muted'|'normal' }>`
  - `planTooltipLines(task): string[]`

- [ ] **Step 1: Написать падающие тесты**

Дописать в конец `src/utils/__tests__/shiftTimeline.spec.js` и расширить импорт в начале файла:

```js
import {
  taskBarLayout, planBarLayout, setupDeviation, setupActualMs, planMarks, planTooltipLines, formatDuration
} from '../shiftTimeline'
```

```js
const MIN = 60_000
const plan = (h, minutes, extra = {}) => ({ plannedStartAt: at(h), plannedSetupMinutes: minutes, ...extra })

describe('formatDuration', () => {
  it('часы и минуты', () => {
    expect(formatDuration(70 * MIN)).toBe('1ч 10м')
    expect(formatDuration(120 * MIN)).toBe('2ч')
    expect(formatDuration(45 * MIN)).toBe('45м')
    expect(formatDuration(0)).toBe('0м')
  })
})

describe('planBarLayout', () => {
  it('окно внутри смены', () => {
    const l = planBarLayout(plan(2, 60), S, E)
    expect(l.leftPct).toBeCloseTo(2 / 12 * 100)
    expect(l.widthPct).toBeCloseTo(1 / 12 * 100)
    expect(l.clippedLeft).toBe(false)
    expect(l.clippedRight).toBe(false)
    expect(l.isMarker).toBe(false)
  })

  it('начинается до смены — обрезка слева', () => {
    const l = planBarLayout(plan(-1, 120), S, E)
    expect(l.leftPct).toBeCloseTo(0)
    expect(l.widthPct).toBeCloseTo(1 / 12 * 100)
    expect(l.clippedLeft).toBe(true)
    expect(l.clippedRight).toBe(false)
  })

  it('заканчивается после смены — обрезка справа', () => {
    const l = planBarLayout(plan(11, 120), S, E)
    expect(l.widthPct).toBeCloseTo(1 / 12 * 100)
    expect(l.clippedRight).toBe(true)
  })

  it('окно вне смены — null', () => {
    expect(planBarLayout(plan(-3, 60), S, E)).toBeNull()
    expect(planBarLayout(plan(-1, 60), S, E)).toBeNull() // кончается ровно в начале смены
    expect(planBarLayout(plan(13, 60), S, E)).toBeNull()
  })

  it('норматив 0 — маркер', () => {
    const l = planBarLayout(plan(3, 0), S, E)
    expect(l.isMarker).toBe(true)
    expect(l.leftPct).toBeCloseTo(25)
    expect(l.widthPct).toBeCloseTo(0)
  })

  it('маркер на границе конца смены — null', () => {
    expect(planBarLayout(plan(12, 0), S, E)).toBeNull()
  })

  it('нет plannedStartAt — null', () => {
    expect(planBarLayout({ plannedSetupMinutes: 30 }, S, E)).toBeNull()
  })
})

describe('setupDeviation', () => {
  it('наладка не началась — null', () => {
    expect(setupDeviation(plan(2, 30))).toBeNull()
  })

  it('опоздание', () => {
    const d = setupDeviation(plan(2, 30, { setupStartedAt: new Date(S.getTime() + 2 * 3_600_000 + 25 * MIN).toISOString() }))
    expect(d).toEqual({ minutes: 25, kind: 'late', label: 'опозд. 25м' })
  })

  it('ранний старт', () => {
    const d = setupDeviation(plan(2, 30, { setupStartedAt: new Date(S.getTime() + 2 * 3_600_000 - 70 * MIN).toISOString() }))
    expect(d).toEqual({ minutes: -70, kind: 'early', label: 'раньше на 1ч 10м' })
  })

  it('|Δ| < 1 мин — вовремя', () => {
    const d = setupDeviation(plan(2, 30, { setupStartedAt: new Date(S.getTime() + 2 * 3_600_000 + 50_000).toISOString() }))
    expect(d).toEqual({ minutes: 0, kind: 'onTime', label: 'вовремя' })
  })
})

describe('setupActualMs', () => {
  it('есть startedAt — разница', () => {
    expect(setupActualMs({ setupStartedAt: at(1), startedAt: at(2) })).toBe(3_600_000)
  })
  it('нет startedAt — null', () => {
    expect(setupActualMs({ setupStartedAt: at(1) })).toBeNull()
  })
})

describe('planMarks', () => {
  it('не началась — только норматив', () => {
    expect(planMarks(plan(2, 45))).toEqual([{ text: 'норм 45м', tone: 'normal' }])
  })

  it('идёт наладка с опозданием — только отклонение', () => {
    const t = plan(2, 45, { setupStartedAt: at(2.5) })
    expect(planMarks(t)).toEqual([{ text: 'опозд. 30м', tone: 'late' }])
  })

  it('ранний старт — серый', () => {
    const t = plan(2, 45, { setupStartedAt: at(1.5) })
    expect(planMarks(t)).toEqual([{ text: 'раньше на 30м', tone: 'muted' }])
  })

  it('наладка завершена — отклонение и факт против норматива', () => {
    const t = plan(2, 45, { setupStartedAt: at(2), startedAt: new Date(S.getTime() + 2 * 3_600_000 + 70 * MIN).toISOString() })
    expect(planMarks(t)).toEqual([
      { text: 'вовремя', tone: 'normal' },
      { text: 'наладка 1ч 10м / норм 45м', tone: 'normal' }
    ])
  })
})

describe('planTooltipLines', () => {
  it('не началась — только плановое окно', () => {
    const lines = planTooltipLines(plan(2, 45))
    expect(lines).toHaveLength(1)
    expect(lines[0]).toMatch(/^План наладки: \d\d:\d\d — \d\d:\d\d \(норм 45м\)$/)
  })

  it('завершена — окно, отклонение, факт', () => {
    const t = plan(2, 45, { setupStartedAt: at(2.5), startedAt: at(3.5) })
    const lines = planTooltipLines(t)
    expect(lines).toHaveLength(3)
    expect(lines[1]).toBe('Начало наладки: опозд. 30м')
    expect(lines[2]).toBe('Факт наладки: 1ч')
  })

  it('нет plannedStartAt — пусто', () => {
    expect(planTooltipLines({})).toEqual([])
  })
})
```

- [ ] **Step 2: Запустить — тесты падают**

Run: `npx vitest run src/utils/__tests__/shiftTimeline.spec.js`
Expected: FAIL — `planBarLayout is not a function` (и т.п.)

- [ ] **Step 3: Реализовать**

Дописать в `src/utils/shiftTimeline.js` (после `taskBarLayout`):

```js
// BL-40: плановое окно наладки и отклонения от него.
// План наладки = [plannedStartAt, plannedStartAt + plannedSetupMinutes) — от планового начала, не от факта.

const MIN = 60_000

export function formatDuration(ms) {
  const totalMin = Math.round(ms / MIN)
  const h = Math.floor(totalMin / 60)
  const m = totalMin % 60
  if (h > 0 && m > 0) return `${h}ч ${m}м`
  if (h > 0) return `${h}ч`
  return `${m}м`
}

export function formatClock(date) {
  return new Date(date).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
}

function plannedWindow(task) {
  if (!task?.plannedStartAt) return null
  const start = new Date(task.plannedStartAt).getTime()
  return { start, end: start + (task.plannedSetupMinutes ?? 0) * MIN }
}

// Геометрия планового бара; null — окно не пересекает смену. Норматив 0 — маркер (ширина 0).
export function planBarLayout(task, shiftStart, shiftEnd) {
  const w = plannedWindow(task)
  if (!w) return null
  const s0 = shiftStart.getTime()
  const e0 = shiftEnd.getTime()
  const dur = e0 - s0
  if (dur <= 0) return null

  const isMarker = w.end === w.start
  const visible = isMarker ? (w.start >= s0 && w.start < e0) : (w.end > s0 && w.start < e0)
  if (!visible) return null

  const s = Math.max(w.start, s0)
  const e = Math.min(w.end, e0)
  return {
    leftPct: (s - s0) / dur * 100,
    widthPct: (e - s) / dur * 100,
    clippedLeft: w.start < s0,
    clippedRight: w.end > e0,
    isMarker
  }
}

// Отклонение начала наладки от плана; null — наладка не началась.
export function setupDeviation(task) {
  if (!task?.setupStartedAt || !task?.plannedStartAt) return null
  const deltaMs = new Date(task.setupStartedAt).getTime() - new Date(task.plannedStartAt).getTime()
  if (Math.abs(deltaMs) < MIN) return { minutes: 0, kind: 'onTime', label: 'вовремя' }
  const minutes = Math.round(deltaMs / MIN)
  return deltaMs > 0
    ? { minutes, kind: 'late', label: `опозд. ${formatDuration(deltaMs)}` }
    : { minutes, kind: 'early', label: `раньше на ${formatDuration(-deltaMs)}` }
}

// Фактическая длительность наладки; null — наладка не завершена.
export function setupActualMs(task) {
  if (!task?.setupStartedAt || !task?.startedAt) return null
  return new Date(task.startedAt).getTime() - new Date(task.setupStartedAt).getTime()
}

const DEVIATION_TONE = { late: 'late', early: 'muted', onTime: 'normal' }

// Текстовые метки внутри планового бара.
export function planMarks(task) {
  const norm = formatDuration((task?.plannedSetupMinutes ?? 0) * MIN)
  const dev = setupDeviation(task)
  if (!dev) return [{ text: `норм ${norm}`, tone: 'normal' }]
  const marks = [{ text: dev.label, tone: DEVIATION_TONE[dev.kind] }]
  const actual = setupActualMs(task)
  if (actual != null) marks.push({ text: `наладка ${formatDuration(actual)} / норм ${norm}`, tone: 'normal' })
  return marks
}

// Строки тултипа о плане наладки — полные времена, даже если бар обрезан сменой.
export function planTooltipLines(task) {
  const w = plannedWindow(task)
  if (!w) return []
  const lines = [`План наладки: ${formatClock(w.start)} — ${formatClock(w.end)} (норм ${formatDuration(w.end - w.start)})`]
  const dev = setupDeviation(task)
  if (dev) lines.push(`Начало наладки: ${dev.label}`)
  const actual = setupActualMs(task)
  if (actual != null) lines.push(`Факт наладки: ${formatDuration(actual)}`)
  return lines
}
```

- [ ] **Step 4: Запустить — тесты проходят**

Run: `npx vitest run src/utils/__tests__/shiftTimeline.spec.js`
Expected: PASS (все, включая прежние `taskBarLayout`)

- [ ] **Step 5: Commit**

```bash
git add src/utils/shiftTimeline.js src/utils/__tests__/shiftTimeline.spec.js
git commit -m "feat(timeline): planned setup window and deviation utils (BL-40)"
```

---

### Task 2: Утилиты дорожек, слияния заданий и диапазона плановых дат

**Files:**
- Modify: `Wintime-Control-Frontend/src/utils/shiftTimeline.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/shiftTimeline.spec.js`

**Interfaces:**
- Produces:
  - `assignLanes(items: Array<{ start: number, end: number }>): number[]` — номер дорожки для каждого элемента, в порядке входа.
  - `mergeTimelineTasks(active: TaskDto[], planned: TaskDto[]): TaskDto[]` — объединение по `id` (версия из `active` приоритетна), сортировка по `plannedStartAt`, затем по `id`.
  - `plannedRangeForShift(shiftStart: Date, shiftEnd: Date): { plannedFrom: 'YYYY-MM-DD', plannedTo: 'YYYY-MM-DD' }` — локальные даты смены с запасом ±1 день.

- [ ] **Step 1: Написать падающие тесты**

Расширить импорт в `shiftTimeline.spec.js` на `assignLanes, mergeTimelineTasks, plannedRangeForShift` и дописать:

```js
describe('assignLanes', () => {
  it('без перекрытий — одна дорожка', () => {
    expect(assignLanes([{ start: 0, end: 10 }, { start: 20, end: 30 }])).toEqual([0, 0])
  })

  it('перекрытие — две дорожки', () => {
    expect(assignLanes([{ start: 0, end: 10 }, { start: 5, end: 15 }])).toEqual([0, 1])
  })

  it('стык end == start — одна дорожка', () => {
    expect(assignLanes([{ start: 0, end: 10 }, { start: 10, end: 20 }])).toEqual([0, 0])
  })

  it('освободившаяся дорожка переиспользуется, порядок входа сохраняется', () => {
    // вход не отсортирован: c, a, b
    const lanes = assignLanes([{ start: 12, end: 20 }, { start: 0, end: 10 }, { start: 5, end: 15 }])
    expect(lanes).toEqual([0, 0, 1])
  })

  it('пустой вход', () => {
    expect(assignLanes([])).toEqual([])
  })
})

describe('mergeTimelineTasks', () => {
  it('дубликаты по id — версия из active, сортировка по plannedStartAt', () => {
    const active = [{ id: 'b', plannedStartAt: at(3), v: 'active' }]
    const planned = [
      { id: 'b', plannedStartAt: at(3), v: 'planned' },
      { id: 'a', plannedStartAt: at(1) }
    ]
    const merged = mergeTimelineTasks(active, planned)
    expect(merged.map(t => t.id)).toEqual(['a', 'b'])
    expect(merged[1].v).toBe('active')
  })

  it('равный plannedStartAt — по id', () => {
    const merged = mergeTimelineTasks([{ id: 'z', plannedStartAt: at(1) }], [{ id: 'y', plannedStartAt: at(1) }])
    expect(merged.map(t => t.id)).toEqual(['y', 'z'])
  })

  it('null-входы', () => {
    expect(mergeTimelineTasks(null, undefined)).toEqual([])
  })
})

describe('plannedRangeForShift', () => {
  it('ночная смена через полночь — ±1 день к локальным датам', () => {
    const r = plannedRangeForShift(new Date(2026, 9, 15, 20, 0), new Date(2026, 9, 16, 8, 0))
    expect(r).toEqual({ plannedFrom: '2026-10-14', plannedTo: '2026-10-17' })
  })

  it('переход месяца', () => {
    const r = plannedRangeForShift(new Date(2026, 9, 1, 8, 0), new Date(2026, 9, 31, 20, 0))
    expect(r).toEqual({ plannedFrom: '2026-09-30', plannedTo: '2026-11-01' })
  })
})
```

- [ ] **Step 2: Запустить — тесты падают**

Run: `npx vitest run src/utils/__tests__/shiftTimeline.spec.js`
Expected: FAIL — `assignLanes is not a function`

- [ ] **Step 3: Реализовать**

Дописать в `src/utils/shiftTimeline.js`:

```js
// Жадная раскладка пересекающихся интервалов по дорожкам: по возрастанию start элемент
// идёт в первую дорожку, где предыдущий кончился не позже его начала. Результат — в порядке входа.
export function assignLanes(items) {
  const order = items.map((_, i) => i).sort((a, b) => (items[a].start - items[b].start) || (a - b))
  const laneEnds = []
  const lanes = new Array(items.length)
  for (const i of order) {
    let lane = laneEnds.findIndex(end => end <= items[i].start)
    if (lane === -1) {
      lane = laneEnds.length
      laneEnds.push(0)
    }
    laneEnds[lane] = items[i].end
    lanes[i] = lane
  }
  return lanes
}

// Задания хронологии = активные в смене ∪ запланированные около смены; версия из active приоритетна.
// Порядок (по плановому началу) задаёт цвет задания в рядах «План» и «Задания».
export function mergeTimelineTasks(active, planned) {
  const byId = new Map()
  for (const t of planned ?? []) byId.set(t.id, t)
  for (const t of active ?? []) byId.set(t.id, t)
  return [...byId.values()].sort((a, b) =>
    (new Date(a.plannedStartAt) - new Date(b.plannedStartAt)) || String(a.id).localeCompare(String(b.id)))
}

function toYmd(d) {
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${mm}-${dd}`
}

// PlannedDate — дата завода, смена — в поясе браузера: берём запас ±1 день,
// точный отбор — по plannedStartAt в planBarLayout.
export function plannedRangeForShift(shiftStart, shiftEnd) {
  const from = new Date(shiftStart)
  from.setDate(from.getDate() - 1)
  const to = new Date(shiftEnd)
  to.setDate(to.getDate() + 1)
  return { plannedFrom: toYmd(from), plannedTo: toYmd(to) }
}
```

- [ ] **Step 4: Запустить — тесты проходят**

Run: `npx vitest run src/utils/__tests__/shiftTimeline.spec.js`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/utils/shiftTimeline.js src/utils/__tests__/shiftTimeline.spec.js
git commit -m "feat(timeline): lane layout, task merge and planned date range utils (BL-40)"
```

---

### Task 3: Ряд «План» и дорожки в `ShiftTimeline.vue`

**Files:**
- Modify: `Wintime-Control-Frontend/src/components/dashboard/ShiftTimeline.vue` (полная замена файла)
- Create: `Wintime-Control-Frontend/src/components/dashboard/__tests__/ShiftTimeline.spec.js`

**Interfaces:**
- Consumes (Task 1–2): `taskBarLayout`, `planBarLayout`, `planMarks`, `planTooltipLines`, `assignLanes`, `formatDuration`, `formatClock` из `@/utils/shiftTimeline`.
- Produces: компонент с прежними пропсами (`statusSegments`, `tasks`, `shiftStart`, `shiftEnd`). `tasks` теперь может содержать задания без `setupStartedAt` — они попадают только в ряд «План». Индекс задания в `tasks` = индекс цвета в обоих рядах. CSS-классы для тестов: `.plan-block`, `.task-block`, `.mark--late`, `.mark--muted`, `.mark--normal`.

- [ ] **Step 1: Написать падающий тест компонента**

Создать `src/components/dashboard/__tests__/ShiftTimeline.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ShiftTimeline from '../ShiftTimeline.vue'

const H = 3_600_000
const now = Date.now()
const S = new Date(now - 6 * H)
const E = new Date(now + 6 * H)
const iso = (h) => new Date(now + h * H).toISOString()

const mountTimeline = (tasks) => mount(ShiftTimeline, {
  props: { statusSegments: [], tasks, shiftStart: S, shiftEnd: E }
})

// В наладке опоздал на 30 мин, наладка 30 мин против норматива 1ч, сейчас в выпуске.
const inRelease = {
  id: 'a', moldName: 'ПФ-12', planQuantity: 500, actualQuantity: 120,
  plannedStartAt: iso(-4), plannedSetupMinutes: 60,
  setupStartedAt: iso(-3.5), startedAt: iso(-3)
}
// Запланировано на эту смену, наладка не началась.
const notStarted = {
  id: 'b', moldName: 'ПФ-7', planQuantity: 300, actualQuantity: 0,
  plannedStartAt: iso(2), plannedSetupMinutes: 45
}

describe('ShiftTimeline — ряд «План» (BL-40)', () => {
  it('плановые бары с метками; неначатое задание — только в ряду «План»', () => {
    const w = mountTimeline([inRelease, notStarted])
    const plans = w.findAll('.plan-block')
    expect(plans).toHaveLength(2)
    expect(plans[0].text()).toContain('ПФ-12')
    expect(plans[0].find('.mark--late').text()).toContain('опозд. 30м')
    expect(plans[0].text()).toContain('наладка 30м / норм 1ч')
    expect(plans[1].text()).toContain('норм 45м')
    expect(w.findAll('.task-block')).toHaveLength(1)
  })

  it('тултип планового бара — плановое окно и отклонение', () => {
    const w = mountTimeline([inRelease])
    const title = w.find('.plan-block').attributes('title')
    expect(title).toMatch(/План наладки: \d\d:\d\d — \d\d:\d\d \(норм 1ч\)/)
    expect(title).toContain('Начало наладки: опозд. 30м')
  })

  it('тултип бара факта дополнен планом наладки', () => {
    const w = mountTimeline([inRelease])
    expect(w.find('.task-block').attributes('title')).toContain('План наладки:')
  })

  it('перекрывающиеся задания — разные дорожки', () => {
    const overlapping = {
      id: 'c', moldName: 'ПФ-3', planQuantity: 100, actualQuantity: 0,
      plannedStartAt: iso(-2), plannedSetupMinutes: 30,
      setupStartedAt: iso(-2), startedAt: iso(-1.5)
    }
    const w = mountTimeline([inRelease, overlapping])
    const tops = w.findAll('.task-block').map(b => b.attributes('style').match(/top:\s*([\d.]+px)/)[1])
    expect(new Set(tops).size).toBe(2)
  })

  it('план вне смены — нет бара', () => {
    const w = mountTimeline([{ ...notStarted, plannedStartAt: iso(-10) }])
    expect(w.findAll('.plan-block')).toHaveLength(0)
  })
})
```

- [ ] **Step 2: Запустить — тест падает**

Run: `npx vitest run src/components/dashboard/__tests__/ShiftTimeline.spec.js`
Expected: FAIL — `.plan-block` не найден (длина 0 вместо 2)

- [ ] **Step 3: Заменить `ShiftTimeline.vue` целиком**

```vue
<template>
  <div class="shift-timeline">
    <!-- Временна́я шкала -->
    <div class="flex">
      <div class="row-label"></div>
      <div class="flex-1 relative time-axis">
        <div
          v-for="tick in timeTicks"
          :key="tick.label"
          class="tick"
          :style="{ left: tick.pct }"
        >{{ tick.label }}</div>
      </div>
    </div>

    <!-- Строка: Статус -->
    <div class="flex items-center mb-2">
      <div class="row-label">Статус</div>
      <div class="flex-1 relative">
        <div class="status-bar">
          <div
            v-for="(seg, i) in statusSegments"
            :key="i"
            class="seg-block"
            :style="segStyle(seg)"
            :title="segTitle(seg)"
          ></div>
          <!-- Ещё не прошедшее время смены -->
          <div
            v-if="futureStyle"
            class="seg-future"
            :style="futureStyle"
            :title="`Ещё не наступило\n${formatClock(nowClamped)} — ${formatClock(shiftEnd)}`"
          ></div>
        </div>
        <div class="now-line now-line--labeled" :style="{ left: nowPct }" :data-time="nowLabel"></div>
      </div>
    </div>

    <!-- Строка: План наладки (BL-40) -->
    <div class="flex items-center mb-2">
      <div class="row-label">План</div>
      <div class="flex-1 relative">
        <div class="plan-bar" :style="{ height: planRowHeight + 'px' }">
          <div
            v-for="bar in planBars"
            :key="'plan-' + bar.task.id"
            class="plan-block"
            :style="planStyle(bar)"
            :title="planTitle(bar.task)"
          >
            <template v-if="!bar.layout.isMarker">
              <div class="plan-name">{{ bar.task.moldName || 'Задание' }}</div>
              <div class="plan-marks">
                <span
                  v-for="(m, i) in planMarks(bar.task)"
                  :key="i"
                  :class="'mark--' + m.tone"
                >{{ i ? ' · ' : '' }}{{ m.text }}</span>
              </div>
            </template>
          </div>
        </div>
        <div class="now-line" :style="{ left: nowPct }"></div>
      </div>
    </div>

    <!-- Строка: Задания -->
    <div class="flex items-center">
      <div class="row-label">Задания</div>
      <div class="flex-1 relative">
        <div class="task-bar" :style="{ height: taskRowHeight + 'px' }">
          <div
            v-for="bar in taskBars"
            :key="bar.task.id"
            class="task-block"
            :style="taskStyle(bar)"
            :title="taskTitle(bar.task)"
          >
            <div class="task-name">{{ bar.task.moldName || 'Задание' }}</div>
            <div class="task-qty">{{ bar.task.actualQuantity }}&thinsp;/&thinsp;{{ bar.task.planQuantity }} шт</div>
          </div>
        </div>
        <div class="now-line" :style="{ left: nowPct }"></div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, onMounted, onUnmounted } from 'vue'
import { getEffectiveStatusMeta } from '@/constants/effectiveStatus'
import {
  taskBarLayout, planBarLayout, planMarks, planTooltipLines, assignLanes, formatDuration, formatClock
} from '@/utils/shiftTimeline'

const props = defineProps({
  statusSegments: { type: Array, default: () => [] }, // EffectiveStatusSegmentDto[]
  tasks:          { type: Array, default: () => [] }, // TaskDto[]; индекс = цвет задания в обоих рядах
  shiftStart:     { type: Date, required: true },
  shiftEnd:       { type: Date, required: true },
})

const TASK_COLORS = ['#3b82f6', '#7c3aed', '#0891b2', '#059669', '#d97706']

// Дорожки (BL-40): высота дорожки фиксирована, ряд растёт с числом дорожек.
const TASK_LANE_H = 42
const PLAN_LANE_H = 32
const ROW_BORDER = 2 // рамка ряда сверху и снизу
const LANE_GAP = 4
const TASK_PAD = 7
const PLAN_PAD = 4
// Узкие бары/маркеры при раскладке считаем не уже этой доли смены — чтобы маркеры не слипались.
const MIN_LANE_PCT = 0.5

const shiftDur = computed(() => props.shiftEnd - props.shiftStart)

const now = ref(new Date())
let _nowTimer = null
onMounted(() => { _nowTimer = setInterval(() => { now.value = new Date() }, 30_000) })
onUnmounted(() => clearInterval(_nowTimer))

const nowClamped = computed(() => {
  if (now.value < props.shiftStart) return props.shiftStart
  if (now.value > props.shiftEnd)   return props.shiftEnd
  return now.value
})

function taskColor(idx) {
  return TASK_COLORS[idx % TASK_COLORS.length]
}

function withLanes(bars) {
  const lanes = assignLanes(bars.map(b => ({
    start: b.layout.leftPct,
    end:   b.layout.leftPct + Math.max(b.layout.widthPct, MIN_LANE_PCT),
  })))
  return bars.map((b, i) => ({ ...b, lane: lanes[i] }))
}

function rowHeight(bars, laneH, pad) {
  const n = bars.reduce((max, b) => Math.max(max, b.lane + 1), 1)
  return pad * 2 + n * laneH + (n - 1) * LANE_GAP
}

// Бары заданий: только задания с окном активности, пересекающим смену.
const taskBars = computed(() => withLanes(props.tasks
  .map((task, idx) => ({ task, idx, layout: taskBarLayout(task, props.shiftStart, props.shiftEnd, nowClamped.value) }))
  .filter(b => b.layout)))
// Плановые бары: только задания, чьё плановое окно наладки пересекает смену.
const planBars = computed(() => withLanes(props.tasks
  .map((task, idx) => ({ task, idx, layout: planBarLayout(task, props.shiftStart, props.shiftEnd) }))
  .filter(b => b.layout)))

const taskRowHeight = computed(() => rowHeight(taskBars.value, TASK_LANE_H, TASK_PAD))
const planRowHeight = computed(() => rowHeight(planBars.value, PLAN_LANE_H, PLAN_PAD))

const nowPct     = computed(() => toPct(nowClamped.value))
const nowLabel   = computed(() => formatClock(nowClamped.value))
// Заштрихованный «хвост» смены, который ещё не наступил (от now до конца смены).
// Позиционируется абсолютно, как и сегменты статусов.
const futureStyle = computed(() => {
  if (nowClamped.value >= props.shiftEnd) return null
  const left  = ((nowClamped.value - props.shiftStart) / shiftDur.value * 100).toFixed(3)
  const width = ((props.shiftEnd - nowClamped.value)   / shiftDur.value * 100).toFixed(3)
  return { left: left + '%', width: width + '%' }
})

const timeTicks = computed(() => {
  const ticks = []
  const start = props.shiftStart.getTime()
  const end   = props.shiftEnd.getTime()
  const hourMs = 3_600_000
  let t = new Date(start)
  t.setMinutes(0, 0, 0)
  if (t.getTime() < start) t = new Date(t.getTime() + hourMs)
  while (t.getTime() <= end) {
    ticks.push({ label: formatClock(t), pct: toPct(t) })
    t = new Date(t.getTime() + hourMs)
  }
  // Всегда добавляем начало смены если оно ровное
  const startTick = { label: formatClock(props.shiftStart), pct: '0%' }
  if (!ticks.find(tk => tk.pct === '0%')) ticks.unshift(startTick)
  return ticks
})

function toPct(date) {
  const ms = (new Date(date) - props.shiftStart)
  return (Math.max(0, Math.min(1, ms / shiftDur.value)) * 100).toFixed(3) + '%'
}

// Сегмент статуса позиционируется абсолютно по реальному времени (changedAt),
// а не упаковкой flex'ом — иначе блоки «уезжают» к левому краю, если история
// статусов начинается не с начала смены или в ней есть разрывы.
function segStyle(seg) {
  const start = new Date(seg.changedAt)
  const end   = seg.endedAt ? new Date(seg.endedAt) : nowClamped.value
  const clampedStart = start < props.shiftStart ? props.shiftStart : start
  const clampedEnd   = end   > props.shiftEnd   ? props.shiftEnd   : end
  const left  = ((clampedStart - props.shiftStart) / shiftDur.value * 100).toFixed(3)
  const width = ((clampedEnd   - clampedStart)     / shiftDur.value * 100).toFixed(3)
  return {
    left:       Math.max(0, left) + '%',
    width:      Math.max(0, width) + '%',
    background: statusColor(seg.effectiveStatus),
  }
}

// Наладка — светлым оттенком цвета задания, выпуск — основным цветом.
function taskStyle(bar) {
  const color = taskColor(bar.idx)
  const sp = bar.layout.setupPct.toFixed(3)
  return {
    left:       bar.layout.leftPct.toFixed(3) + '%',
    width:      bar.layout.widthPct.toFixed(3) + '%',
    top:        (TASK_PAD + bar.lane * (TASK_LANE_H + LANE_GAP)) + 'px',
    height:     TASK_LANE_H + 'px',
    background: `linear-gradient(to right, ${color}80 0 ${sp}%, ${color} ${sp}% 100%)`,
  }
}

// План — контур цвета задания; обрезанный сменой край без скругления.
function planStyle(bar) {
  const color = taskColor(bar.idx)
  const l = bar.layout
  const style = {
    left:        l.leftPct.toFixed(3) + '%',
    width:       l.isMarker ? '4px' : l.widthPct.toFixed(3) + '%',
    top:         (PLAN_PAD + bar.lane * (PLAN_LANE_H + LANE_GAP)) + 'px',
    height:      PLAN_LANE_H + 'px',
    borderColor: color,
    background:  color + '1f',
    color,
  }
  if (l.clippedLeft)  { style.borderTopLeftRadius = '0';  style.borderBottomLeftRadius = '0' }
  if (l.clippedRight) { style.borderTopRightRadius = '0'; style.borderBottomRightRadius = '0' }
  return style
}

function statusColor(seg) {
  return getEffectiveStatusMeta(seg).hex
}

function segTitle(seg) {
  const start = new Date(seg.changedAt)
  const end   = seg.endedAt ? new Date(seg.endedAt) : now.value
  return `${getEffectiveStatusMeta(seg.effectiveStatus).label}\n${formatClock(start)} — ${formatClock(end)}\n${formatDuration(end - start)}`
}

function planTitle(task) {
  return [task.moldName || 'Задание', ...planTooltipLines(task)].join('\n')
}

function taskTitle(task) {
  const start = new Date(task.setupStartedAt)
  const end   = new Date(task.completedAt ?? task.closedAt ?? now.value)
  const progress = task.planQuantity ? Math.round(task.actualQuantity / task.planQuantity * 100) : 0
  return [
    task.moldName || 'Задание',
    `${formatClock(start)} — ${formatClock(end)} (${formatDuration(end - start)})`,
    task.startedAt ? `Наладка: ${formatClock(task.setupStartedAt)} — ${formatClock(task.startedAt)}` : (task.closedAt || task.completedAt) ? 'Закрыто во время наладки' : 'Идёт наладка',
    ...planTooltipLines(task),
    `Факт: ${task.actualQuantity} / План: ${task.planQuantity} шт (${progress}%)`,
    task.personnelName ? `Наладчик: ${task.personnelName}` : null,
  ].filter(Boolean).join('\n')
}
</script>

<style scoped>
.shift-timeline {
  font-family: inherit;
  user-select: none;
}

.row-label {
  width: 5rem;
  flex-shrink: 0;
  text-align: right;
  padding-right: 0.75rem;
  font-size: 0.7rem;
  color: #9ca3af;
  display: flex;
  align-items: center;
  justify-content: flex-end;
}

/* Временная шкала */
.time-axis {
  height: 22px;
  margin-bottom: 4px;
}

.tick {
  position: absolute;
  bottom: 0;
  transform: translateX(-50%);
  font-size: 10px;
  color: #9ca3af;
  white-space: nowrap;
}
.tick::before {
  content: '';
  position: absolute;
  bottom: 100%;
  left: 50%;
  width: 1px;
  height: 4px;
  background: #d1d5db;
  margin-bottom: 1px;
}

/* Полоса статусов */
.status-bar {
  position: relative;
  height: 32px;
  background: #e5e7eb;
  border-radius: 6px;
  overflow: hidden;
}

.seg-block {
  position: absolute;
  top: 0;
  height: 100%;
  transition: filter 0.12s;
  cursor: default;
}
.seg-block:hover { filter: brightness(1.12); }

.seg-future {
  position: absolute;
  top: 0;
  height: 100%;
  background: repeating-linear-gradient(
    45deg,
    #e5e7eb, #e5e7eb 4px,
    #f3f4f6 4px, #f3f4f6 8px
  );
  cursor: default;
}

/* Полоса плана наладки */
.plan-bar {
  background: #fafafa;
  border-radius: 6px;
  border: 1px dashed #e5e7eb;
  position: relative;
}

.plan-block {
  position: absolute;
  box-sizing: border-box;
  border: 1.5px dashed;
  border-radius: 5px;
  padding: 2px 6px;
  overflow: hidden;
  cursor: default;
  display: flex;
  flex-direction: column;
  justify-content: center;
}

.plan-name {
  font-size: 11px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.plan-marks {
  font-size: 10px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: #374151;
}
.mark--late   { color: #dc2626; font-weight: 600; }
.mark--muted  { color: #6b7280; }
.mark--normal { color: #374151; }

/* Полоса заданий */
.task-bar {
  background: #f3f4f6;
  border-radius: 6px;
  border: 1px solid #e5e7eb;
  position: relative;
}

.task-block {
  position: absolute;
  box-sizing: border-box;
  border-radius: 5px;
  padding: 3px 8px;
  overflow: hidden;
  cursor: default;
  display: flex;
  flex-direction: column;
  justify-content: center;
  color: #fff;
  border: 1px solid rgba(0, 0, 0, 0.08);
  transition: filter 0.12s;
}
.task-block:hover { filter: brightness(1.08); }

.task-name {
  font-size: 11px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.task-qty {
  font-size: 10px;
  opacity: 0.82;
  white-space: nowrap;
}

/* Линия текущего времени */
.now-line {
  position: absolute;
  top: -5px;
  bottom: -5px;
  width: 2px;
  background: #ef4444;
  z-index: 10;
  pointer-events: none;
}
.now-line::before {
  content: '';
  position: absolute;
  top: 3px;
  left: -4px;
  width: 10px;
  height: 10px;
  background: #ef4444;
  border-radius: 50%;
}
.now-line--labeled::after {
  content: attr(data-time);
  position: absolute;
  top: -18px;
  left: 50%;
  transform: translateX(-50%);
  background: #ef4444;
  color: #fff;
  font-size: 10px;
  font-weight: 600;
  padding: 1px 5px;
  border-radius: 3px;
  white-space: nowrap;
}
</style>
```

Примечание: с одной дорожкой ряд «Задания» имеет высоту 2 + 7·2 + 42 = 58 px и бар 42 px — как раньше (было `height: 58px; top/bottom: 7px`).

- [ ] **Step 4: Запустить тесты компонента и утилит**

Run: `npx vitest run src/components/dashboard/__tests__/ShiftTimeline.spec.js src/utils/__tests__/shiftTimeline.spec.js`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/components/dashboard/ShiftTimeline.vue src/components/dashboard/__tests__/ShiftTimeline.spec.js
git commit -m "feat(timeline): plan row and overlap lanes on IMM card timeline (BL-40)"
```

---

### Task 4: Второй запрос в `ImmDetailModal`, руководство пользователя, полный прогон

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/dashboard/ImmDetailModal.vue:84-92` (импорты), `:181-187` (запросы)
- Modify: `docs/user-guide/manager.md:180-181`

**Interfaces:**
- Consumes (Task 2): `mergeTimelineTasks(active, planned)`, `plannedRangeForShift(shiftStart, shiftEnd)`.
- Consumes (API, без изменений): `tasksApi.getList({ immId, plannedFrom, plannedTo })` — `GET /api/tasks` без `status` скрывает `Completed`/`Closed`; `plannedFrom/To` — `DateOnly` `YYYY-MM-DD`.

- [ ] **Step 1: Добавить импорт**

В `ImmDetailModal.vue` после строки `import { EFFECTIVE_STATUS, EFFECTIVE_STATUS_KEYS } from '@/constants/effectiveStatus'`:

```js
import { mergeTimelineTasks, plannedRangeForShift } from '@/utils/shiftTimeline'
```

- [ ] **Step 2: Заменить блок запросов в `loadData`**

Было:

```js
  try {
    const [historyRes, tasksRes] = await Promise.all([
      dashboardApi.getImmEffectiveStatusHistory(props.immId, { from: fromIso, to: toIso }),
      tasksApi.getList({ immId: props.immId, activeFrom: fromIso, activeTo: toIso }),
    ])
    statusSegments.value = historyRes.data
    tasks.value          = tasksRes.data?.items ?? tasksRes.data ?? []
```

Стало:

```js
  // BL-40: кроме активных в смене — запланированные около смены (для ряда «План»),
  // включая ещё не начатые. Точный отбор по plannedStartAt — в ShiftTimeline.
  const { plannedFrom, plannedTo } = plannedRangeForShift(shift.value.start, shift.value.end)
  const items = (res) => res.data?.items ?? res.data ?? []

  try {
    const [historyRes, activeRes, plannedRes] = await Promise.all([
      dashboardApi.getImmEffectiveStatusHistory(props.immId, { from: fromIso, to: toIso }),
      tasksApi.getList({ immId: props.immId, activeFrom: fromIso, activeTo: toIso }),
      tasksApi.getList({ immId: props.immId, plannedFrom, plannedTo }),
    ])
    statusSegments.value = historyRes.data
    tasks.value          = mergeTimelineTasks(items(activeRes), items(plannedRes))
```

- [ ] **Step 3: Обновить руководство менеджера**

В `docs/user-guide/manager.md` заменить пункт:

```markdown
- **Хронология смены** — лента состояний машины и заданий во времени. При наведении на
  участок ленты появляется подсказка с подробностями.
```

на:

```markdown
- **Хронология смены** — лента во времени из трёх рядов:
  - **Статус** — состояния машины;
  - **План** — плановое окно наладки каждого задания: от планового начала до планового
    окончания (плановое начало + норматив наладки). Внутри — пресс-форма и метки:
    «норм 45м», пока наладка не началась; «опозд. 25м» (красным), «раньше на 10м» или
    «вовремя», когда наладка началась; «наладка 1ч 10м / норм 45м», когда она завершена.
    Если плановое окно выходит за смену, у обрезанного края нет скругления;
  - **Задания** — фактическая работа: наладка светлым оттенком, выпуск — основным цветом.
    Цвет задания в рядах «План» и «Задания» одинаковый.

  Если задания пересекаются во времени (например, наладка следующего началась до закрытия
  предыдущего), они раскладываются по нескольким дорожкам. При наведении на участок ленты
  появляется подсказка с подробностями.
```

- [ ] **Step 4: Полный прогон тестов фронта и сборка**

Run: `npm test`
Expected: все тесты PASS

Run: `npm run build`
Expected: сборка без ошибок

- [ ] **Step 5: Ручная проверка (если поднят стенд)**

`dotnet run --project Wintime.Control.API` + `npm run dev`; открыть дашборд, кликнуть плитку ТПА с заданием: ряд «План» с плановым баром, метки отклонения, тултипы; задание `Issued` с плановым началом в текущей смене видно только в ряду «План».

- [ ] **Step 6: Commit**

```bash
git add src/views/dashboard/ImmDetailModal.vue ../docs/user-guide/manager.md
git commit -m "feat(imm-card): load planned tasks for timeline plan row, user guide (BL-40)"
```
