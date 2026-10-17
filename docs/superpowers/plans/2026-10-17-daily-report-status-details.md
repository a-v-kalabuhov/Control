# «Картина рабочего дня»: масштаб и таблица статусов — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** В отчёте «Картина рабочего дня» диаграмму статусов можно масштабировать (ползунок + колесо мыши), а на соседней вкладке есть таблица статусов по каждому ТПА с началом, концом и длительностью (ч:мм:сс).

**Architecture:** Бэкенд добавляет в `DailyReportDto` границы периода `PeriodStart/PeriodEnd` (UTC, смена или сутки завода без полей ±30 мин). Фронт строит строки таблицы из уже приходящего `immData[].timeline` чистой функцией (обрезка по периоду, склейка соседних одинаковых). Масштаб — штатный `dataZoom` ECharts в `GanttChart.vue`. Диаграмма и таблица — две вкладки `el-tabs`, по умолчанию «Диаграмма».

**Tech Stack:** ASP.NET Core 9, xUnit + FluentAssertions (integration, Testcontainers/Postgres), Vue 3 `<script setup>`, Element Plus, ECharts 6, dayjs, Vitest + @vue/test-utils.

**Spec:** `docs/superpowers/specs/2026-10-17-daily-report-status-details-design.md`

## Global Constraints

- Ветка `feature/daily-report-status-details` (от `origin/master`). Прямой push в master запрещён.
- Версия: фича → `VERSION` = `0.16.0` (сейчас `0.15.1`).
- Палитра и подписи статусов — только из `src/constants/effectiveStatus.js` (`REPORT_STATUS`, `REPORT_STATUS_KEYS`); неизвестный тип отображается как `REPORT_STATUS.NoData`.
- Время на фронте — в поясе браузера (dayjs без tz), не переводить в пояс смен.
- Длительность в таблице — формат `ч:мм:сс` (часы без ведущего нуля и без ограничения, напр. `0:04:07`, `27:00:00`).
- Таблица — ровно период (смена/сутки), не окно диаграммы ±30 мин.
- Комментарии в коде — на русском, в стиле окружающего кода. Тесты фронта — Vitest, бэка — xUnit.
- Коммиты заканчиваются строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Фронт-тесты запускать из `Wintime-Control-Frontend`: `npx vitest run <path>`.

---

## File Structure

| Файл | Ответственность |
|---|---|
| `Wintime.Control.Core/DTOs/Report/DailyReportDto.cs` (modify) | + `PeriodStart`, `PeriodEnd` |
| `Wintime.Control.Infrastructure/Reports/ReportService.cs` (modify) | заполнение полей из `DailyReportPeriod` |
| `Wintime.Control.Tests.Integration/Reports/DailyReportEffectiveStatusTests.cs` (modify) | проверка границ для суток и смены |
| `Wintime-Control-Frontend/src/utils/statusRows.js` (create) | чистые функции строк таблицы и форматирования |
| `Wintime-Control-Frontend/src/utils/__tests__/statusRows.spec.js` (create) | тесты функций |
| `Wintime-Control-Frontend/src/components/reports/StatusTimelineTable.vue` (create) | таблица статусов, группы по ТПА |
| `Wintime-Control-Frontend/src/components/reports/__tests__/StatusTimelineTable.spec.js` (create) | тесты компонента |
| `Wintime-Control-Frontend/src/components/reports/GanttChart.vue` (modify) | dataZoom, подписи оси, `resetZoom`/`resize`, фикс утечки resize |
| `Wintime-Control-Frontend/src/components/reports/__tests__/GanttChart.spec.js` (modify) | тесты масштаба |
| `Wintime-Control-Frontend/src/views/reports/DailyReportView.vue` (modify) | вкладки, кнопка «Сбросить масштаб» |
| `Wintime-Control-Frontend/src/views/reports/__tests__/DailyReportView.spec.js` (modify) | тесты вкладок |
| `docs/user-guide/manager.md` (modify), `VERSION` (modify) | документация, версия |

---

### Task 1: Бэкенд — границы периода в `DailyReportDto`

**Files:**
- Modify: `Wintime.Control.Core/DTOs/Report/DailyReportDto.cs`
- Modify: `Wintime.Control.Infrastructure/Reports/ReportService.cs:52-59`
- Test: `Wintime.Control.Tests.Integration/Reports/DailyReportEffectiveStatusTests.cs`

**Interfaces:**
- Produces: JSON-поля `periodStart`, `periodEnd` (ISO-строки UTC с `Z`) в ответе `GET /api/reports/daily`.

- [ ] **Step 1: Write the failing test.** В `DailyReportEffectiveStatusTests.PastDay_HoursAndEfficiencyAreEffectiveStatus_AndMatchEquipmentReport` сразу после строки `var item = daily.ImmData.Should().ContainSingle().Subject;` добавить:

```csharp
        // Границы периода отчёта — локальные сутки завода, без полей графика.
        daily.PeriodStart.Should().Be(d0);
        daily.PeriodEnd.Should().Be(d0.AddDays(1));
```

В `DailyReportShiftEffectiveStatusTests.ShiftPeriod_AggregatesCoverShift_AndTimelineHasThirtyMinuteMargin` после `var item = daily.ImmData.Should().ContainSingle().Subject;` добавить:

```csharp
        // Период — ровно смена (поля ±30 мин — только у графика).
        daily.PeriodStart.Should().Be(shiftStart);
        daily.PeriodEnd.Should().Be(shiftStart.AddMinutes(durationMinutes));
```

- [ ] **Step 2: Run test to verify it fails (compile error).**
Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~DailyReport"`
Expected: build error `'DailyReportDto' does not contain a definition for 'PeriodStart'`.

- [ ] **Step 3: Implement.** В `DailyReportDto` после `ShiftEndTime`:

```csharp
    /// <summary>Начало периода отчёта (смена или сутки завода), UTC. Без полей графика ±30 мин.</summary>
    public DateTime PeriodStart { get; set; }
    /// <summary>Конец периода отчёта (смена или сутки завода), UTC, не включая.</summary>
    public DateTime PeriodEnd { get; set; }
```

В `ReportService.GetDailyReportAsync`, инициализатор `new DailyReportDto`:

```csharp
        var report = new DailyReportDto
        {
            Date = date.Date,
            ShiftNumber = shiftNumber,
            ShiftStartTime = shiftStartTime,
            ShiftEndTime = shiftEndTime,
            PeriodStart = period.PeriodStart,
            PeriodEnd = period.PeriodEnd,
            ImmData = new List<DailyReportImmItemDto>()
        };
```

- [ ] **Step 4: Run tests.** Тот же фильтр. Expected: все `DailyReport*` PASS. (Нужен запущенный Docker для Testcontainers.)

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/DTOs/Report/DailyReportDto.cs Wintime.Control.Infrastructure/Reports/ReportService.cs Wintime.Control.Tests.Integration/Reports/DailyReportEffectiveStatusTests.cs
git commit -m "feat(reports): daily report exposes period bounds (PeriodStart/PeriodEnd)"
```

---

### Task 2: Фронт — чистые функции строк таблицы `statusRows.js`

**Files:**
- Create: `Wintime-Control-Frontend/src/utils/statusRows.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/statusRows.spec.js`

**Interfaces:**
- Produces (все экспорты именованные):
  - `buildStatusRows(timeline: Array<{type, start, end}>, periodStart: string|number, periodEnd: string|number) → Array<{ type: string, start: number(ms), end: number(ms), durationSeconds: number }>`
  - `summarizeStatusRows(rows) → Array<{ type: string, seconds: number }>` — ненулевые, в порядке `REPORT_STATUS_KEYS`, неизвестный тип → `'NoData'`
  - `isCutAtNow(rows, periodEnd, now = Date.now()) → boolean` — последний сегмент обрезан по «сейчас»
  - `formatDuration(seconds: number) → string` — `ч:мм:сс`
  - `formatPoint(ms: number, periodStart) → string` — `HH:mm:ss`, при другой дате, чем у начала периода, — `DD.MM HH:mm:ss`

- [ ] **Step 1: Write the failing test** — `src/utils/__tests__/statusRows.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import dayjs from 'dayjs'
import { buildStatusRows, summarizeStatusRows, isCutAtNow, formatDuration, formatPoint } from '../statusRows'

// Локальное время браузера — как в отчёте.
const t = (s) => dayjs(`2026-10-01 ${s}`).valueOf()
const iso = (s) => dayjs(`2026-10-01 ${s}`).toISOString()
const seg = (type, from, to) => ({ type, start: iso(from), end: iso(to) })

describe('buildStatusRows', () => {
  it('обрезает сегменты по границам периода (поля графика ±30 мин отбрасываются)', () => {
    const rows = buildStatusRows(
      [seg('NoTask', '07:30', '08:10'), seg('Production', '08:10', '16:00'), seg('NoTask', '16:00', '16:30')],
      iso('08:00'), iso('16:00'))
    expect(rows).toEqual([
      { type: 'NoTask', start: t('08:00'), end: t('08:10'), durationSeconds: 600 },
      { type: 'Production', start: t('08:10'), end: t('16:00'), durationSeconds: 28200 }
    ])
  })

  it('склеивает соседние сегменты одного типа без разрыва', () => {
    const rows = buildStatusRows(
      [seg('Production', '09:00', '10:00'), seg('Production', '08:00', '09:00'), seg('Setup', '10:00', '10:05')],
      iso('08:00'), iso('16:00'))
    expect(rows.map(r => [r.type, r.durationSeconds])).toEqual([['Production', 7200], ['Setup', 300]])
  })

  it('не склеивает одинаковые сегменты с разрывом', () => {
    const rows = buildStatusRows(
      [seg('Production', '08:00', '09:00'), seg('Production', '09:01', '10:00')],
      iso('08:00'), iso('16:00'))
    expect(rows).toHaveLength(2)
  })

  it('ночная смена через полночь', () => {
    const next = (s) => dayjs(`2026-10-02 ${s}`).toISOString()
    const rows = buildStatusRows(
      [{ type: 'Production', start: iso('23:00'), end: next('01:00') }],
      iso('20:00'), next('08:00'))
    expect(rows[0].durationSeconds).toBe(7200)
  })

  it('отбрасывает сегменты нулевой длины и вне периода; пустой timeline → []', () => {
    expect(buildStatusRows([seg('Setup', '08:00', '08:00'), seg('Setup', '06:00', '07:00')], iso('08:00'), iso('16:00'))).toEqual([])
    expect(buildStatusRows(undefined, iso('08:00'), iso('16:00'))).toEqual([])
  })
})

describe('summarizeStatusRows', () => {
  it('суммы по типам в порядке палитры, только ненулевые; неизвестный тип → NoData', () => {
    const rows = [
      { type: 'Setup', durationSeconds: 300 },
      { type: 'Production', durationSeconds: 3600 },
      { type: 'Production', durationSeconds: 60 },
      { type: 'Weird', durationSeconds: 5 }
    ]
    expect(summarizeStatusRows(rows)).toEqual([
      { type: 'Production', seconds: 3660 },
      { type: 'Setup', seconds: 300 },
      { type: 'NoData', seconds: 5 }
    ])
  })
})

describe('isCutAtNow', () => {
  it('последний сегмент кончается раньше конца периода, а период ещё идёт → true', () => {
    const rows = [{ end: t('10:00') }]
    expect(isCutAtNow(rows, iso('16:00'), t('10:00'))).toBe(true)
  })
  it('прошедший период или сегмент до конца периода → false', () => {
    expect(isCutAtNow([{ end: t('16:00') }], iso('16:00'), t('10:00'))).toBe(false)
    expect(isCutAtNow([{ end: t('10:00') }], iso('16:00'), t('17:00'))).toBe(false)
    expect(isCutAtNow([], iso('16:00'), t('10:00'))).toBe(false)
  })
})

describe('formatDuration', () => {
  it.each([[0, '0:00:00'], [59, '0:00:59'], [247, '0:04:07'], [3600, '1:00:00'], [97200, '27:00:00']])(
    '%i с → %s', (s, expected) => expect(formatDuration(s)).toBe(expected))
})

describe('formatPoint', () => {
  it('та же дата, что у начала периода → только время', () => {
    expect(formatPoint(t('08:05:09'), iso('08:00'))).toBe('08:05:09')
  })
  it('другая дата → с датой', () => {
    expect(formatPoint(dayjs('2026-10-02 01:00:00').valueOf(), iso('20:00'))).toBe('02.10 01:00:00')
  })
})
```

- [ ] **Step 2: Run test to verify it fails.**
Run (из `Wintime-Control-Frontend`): `npx vitest run src/utils/__tests__/statusRows.spec.js`
Expected: FAIL — `Failed to resolve import "../statusRows"`.

- [ ] **Step 3: Implement** — `src/utils/statusRows.js`:

```js
import dayjs from 'dayjs'
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'

// Строки таблицы статусов «Картины рабочего дня» из сегментов диаграммы (timeline ТПА).

const toMs = (v) => (typeof v === 'number' ? v : dayjs(v).valueOf())

// Обрезка по [periodStart, periodEnd) — поля графика ±30 мин в таблицу не попадают,
// склейка соседних сегментов одного типа без разрыва. Время — в мс.
export function buildStatusRows(timeline, periodStart, periodEnd) {
  const from = toMs(periodStart)
  const to = toMs(periodEnd)
  const clipped = (timeline ?? [])
    .map(s => ({ type: s.type, start: Math.max(toMs(s.start), from), end: Math.min(toMs(s.end), to) }))
    .filter(s => s.end > s.start)
    .sort((a, b) => a.start - b.start)

  const rows = []
  for (const s of clipped) {
    const prev = rows[rows.length - 1]
    if (prev && prev.type === s.type && prev.end === s.start) prev.end = s.end
    else rows.push({ ...s })
  }
  return rows.map(r => ({ ...r, durationSeconds: Math.round((r.end - r.start) / 1000) }))
}

// Сумма по состояниям — ненулевые, в порядке палитры отчёта. Неизвестный тип — «Нет данных».
export function summarizeStatusRows(rows) {
  const sums = {}
  for (const r of rows) {
    const key = REPORT_STATUS[r.type] ? r.type : 'NoData'
    sums[key] = (sums[key] ?? 0) + r.durationSeconds
  }
  return REPORT_STATUS_KEYS.filter(k => sums[k] > 0).map(k => ({ type: k, seconds: sums[k] }))
}

// Отчёт за идущий период: бэкенд обрезает текущий статус по «сейчас» —
// последний сегмент кончается раньше конца периода.
export function isCutAtNow(rows, periodEnd, now = Date.now()) {
  if (!rows.length) return false
  const end = toMs(periodEnd)
  return end > now && rows[rows.length - 1].end < end
}

const pad = (n) => String(n).padStart(2, '0')

export function formatDuration(seconds) {
  const s = Math.max(0, Math.round(seconds))
  return `${Math.floor(s / 3600)}:${pad(Math.floor((s % 3600) / 60))}:${pad(s % 60)}`
}

// Время точки; если сутки сменились относительно начала периода (ночная смена) — с датой.
export function formatPoint(ms, periodStart) {
  const d = dayjs(ms)
  return d.isSame(dayjs(toMs(periodStart)), 'day') ? d.format('HH:mm:ss') : d.format('DD.MM HH:mm:ss')
}
```

- [ ] **Step 4: Run test.** Same command. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime-Control-Frontend/src/utils/statusRows.js Wintime-Control-Frontend/src/utils/__tests__/statusRows.spec.js
git commit -m "feat(reports-ui): status rows builder for daily report status table"
```

---

### Task 3: Фронт — компонент `StatusTimelineTable.vue`

**Files:**
- Create: `Wintime-Control-Frontend/src/components/reports/StatusTimelineTable.vue`
- Test: `Wintime-Control-Frontend/src/components/reports/__tests__/StatusTimelineTable.spec.js`

**Interfaces:**
- Consumes: из `@/utils/statusRows` — `buildStatusRows`, `summarizeStatusRows`, `isCutAtNow`, `formatDuration`, `formatPoint` (см. Task 2).
- Produces: компонент `StatusTimelineTable` с props `data: Array` (`immData`: `{ immId, immName, timeline: [{type,start,end}] }`), `periodStart: String`, `periodEnd: String`. Атрибуты для тестов: `data-test="imm-group"` (панель ТПА), `data-test="summary-item"` (элемент сводки), `data-test="no-data"` (пустой ТПА), `data-test="row-end"` (ячейка «Конец»).

- [ ] **Step 1: Write the failing test** — `src/components/reports/__tests__/StatusTimelineTable.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import dayjs from 'dayjs'
import StatusTimelineTable from '../StatusTimelineTable.vue'

const iso = (s) => dayjs(`2026-10-01 ${s}`).toISOString()
const seg = (type, from, to) => ({ type, start: iso(from), end: iso(to) })

async function mountTable(data, periodStart = iso('08:00'), periodEnd = iso('16:00')) {
  const w = mount(StatusTimelineTable, {
    props: { data, periodStart, periodEnd },
    global: { plugins: [ElementPlus] }
  })
  await flushPromises()
  return w
}

describe('StatusTimelineTable', () => {
  afterEach(() => vi.useRealTimers())

  it('группа на каждый ТПА со сводкой ненулевых состояний', async () => {
    const w = await mountTable([
      { immId: 'a', immName: 'ТПА-1', timeline: [seg('Setup', '07:30', '08:30'), seg('Production', '08:30', '16:30')] },
      { immId: 'b', immName: 'ТПА-2', timeline: [seg('NoTask', '08:00', '16:00')] }
    ])

    const groups = w.findAll('[data-test="imm-group"]')
    expect(groups).toHaveLength(2)
    expect(groups[0].text()).toContain('ТПА-1')
    const summary = groups[0].findAll('[data-test="summary-item"]').map(s => s.text())
    expect(summary).toEqual(['Работа 7:30:00', 'Наладка 0:30:00'])
  })

  it('строки: состояние, начало, конец, длительность — в пределах периода', async () => {
    const w = await mountTable([
      { immId: 'a', immName: 'ТПА-1', timeline: [seg('Setup', '07:30', '08:30'), seg('Production', '08:30', '16:30')] }
    ])

    const text = w.find('[data-test="imm-group"] .el-table').text()
    expect(text).toContain('Наладка')
    expect(text).toContain('08:00:00')
    expect(text).toContain('08:30:00')
    expect(text).toContain('0:30:00')
    expect(text).toContain('16:00:00')
    expect(text).not.toContain('07:30:00')
  })

  it('идущий период: конец последнего сегмента — «сейчас»', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(dayjs('2026-10-01 10:00').toDate())
    const w = await mountTable([
      { immId: 'a', immName: 'ТПА-1', timeline: [seg('Production', '08:00', '10:00')] }
    ])

    expect(w.findAll('[data-test="row-end"]').at(-1).text()).toBe('сейчас')
  })

  it('ТПА без сегментов в периоде — «Нет данных за период»', async () => {
    const w = await mountTable([{ immId: 'a', immName: 'ТПА-1', timeline: [] }])

    expect(w.find('[data-test="no-data"]').text()).toBe('Нет данных за период')
  })
})
```

- [ ] **Step 2: Run test to verify it fails.**
Run: `npx vitest run src/components/reports/__tests__/StatusTimelineTable.spec.js`
Expected: FAIL — cannot resolve `../StatusTimelineTable.vue`.

- [ ] **Step 3: Implement** — `src/components/reports/StatusTimelineTable.vue`:

```vue
<template>
  <el-collapse v-model="expanded">
    <el-collapse-item v-for="g in groups" :key="g.immId" :name="g.immId" data-test="imm-group">
      <template #title>
        <div class="flex flex-wrap items-center gap-x-4 gap-y-1 leading-5">
          <span class="font-semibold">{{ g.immName }}</span>
          <span
            v-for="s in g.summary"
            :key="s.type"
            data-test="summary-item"
            class="flex items-center gap-1 text-xs text-gray-600"
          >
            <span class="inline-block w-2.5 h-2.5 rounded-full" :style="{ background: meta(s.type).hex }"></span>
            {{ meta(s.type).label }} {{ formatDuration(s.seconds) }}
          </span>
        </div>
      </template>

      <el-table v-if="g.rows.length" :data="g.rows" stripe size="small" style="width: 100%">
        <el-table-column label="Состояние" min-width="200">
          <template #default="{ row }">
            <span class="flex items-center gap-2">
              <span class="inline-block w-3 h-3 rounded" :style="{ background: meta(row.type).hex }"></span>
              {{ meta(row.type).label }}
            </span>
          </template>
        </el-table-column>
        <el-table-column label="Начало" width="150">
          <template #default="{ row }">{{ formatPoint(row.start, periodStart) }}</template>
        </el-table-column>
        <el-table-column label="Конец" width="150">
          <template #default="{ row, $index }">
            <span data-test="row-end">
              {{ g.cutAtNow && $index === g.rows.length - 1 ? 'сейчас' : formatPoint(row.end, periodStart) }}
            </span>
          </template>
        </el-table-column>
        <el-table-column label="Длительность" width="130" align="right">
          <template #default="{ row }">{{ formatDuration(row.durationSeconds) }}</template>
        </el-table-column>
      </el-table>
      <div v-else data-test="no-data" class="text-gray-400 text-sm py-2">Нет данных за период</div>
    </el-collapse-item>
  </el-collapse>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { REPORT_STATUS } from '@/constants/effectiveStatus'
import { buildStatusRows, summarizeStatusRows, isCutAtNow, formatDuration, formatPoint } from '@/utils/statusRows'

// Таблица статусов «Картины рабочего дня»: по группе на ТПА, строки — за период отчёта
// (смена/сутки, без полей ±30 мин диаграммы), сводка в заголовке совпадает с часами сводной таблицы.
const props = defineProps({
  data: { type: Array, default: () => [] },
  periodStart: { type: String, required: true },
  periodEnd: { type: String, required: true }
})

const meta = (type) => REPORT_STATUS[type] ?? REPORT_STATUS.NoData

const groups = computed(() => props.data.map(imm => {
  const rows = buildStatusRows(imm.timeline, props.periodStart, props.periodEnd)
  return {
    immId: imm.immId,
    immName: imm.immName,
    rows,
    summary: summarizeStatusRows(rows),
    cutAtNow: isCutAtNow(rows, props.periodEnd)
  }
}))

// По умолчанию все группы раскрыты; новый отчёт снова раскрывает все.
const expanded = ref([])
watch(() => props.data, (data) => { expanded.value = data.map(i => i.immId) }, { immediate: true })
</script>
```

- [ ] **Step 4: Run test.** Same command. Expected: PASS. Если `el-table` в jsdom не рендерит ячейки до `flushPromises` — добавить ещё один `await flushPromises()` в `mountTable` (не менять логику компонента).

- [ ] **Step 5: Commit**

```bash
git add Wintime-Control-Frontend/src/components/reports/StatusTimelineTable.vue Wintime-Control-Frontend/src/components/reports/__tests__/StatusTimelineTable.spec.js
git commit -m "feat(reports-ui): daily report status table grouped by IMM"
```

---

### Task 4: Фронт — масштаб диаграммы в `GanttChart.vue`

**Files:**
- Modify: `Wintime-Control-Frontend/src/components/reports/GanttChart.vue`
- Test: `Wintime-Control-Frontend/src/components/reports/__tests__/GanttChart.spec.js`

**Interfaces:**
- Produces: `defineExpose({ resetZoom, resize })` — `resetZoom()` возвращает полный период, `resize()` пересчитывает размер (нужно после показа скрытой вкладки).

- [ ] **Step 1: Write the failing test.** В `GanttChart.spec.js` заменить мок echarts на hoisted-объект и добавить блок тестов (существующий тест легенды оставить):

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'

const chartMock = vi.hoisted(() => ({
  setOption: vi.fn(), resize: vi.fn(), dispose: vi.fn(), dispatchAction: vi.fn()
}))

vi.mock('echarts', () => ({
  init: () => chartMock,
  graphic: { clipRectByRect: vi.fn(() => ({})) }
}))

const { default: GanttChart } = await import('../GanttChart.vue')

const data = [{
  immName: 'ТПА-1',
  timeline: [{ type: 'Production', start: '2026-10-01T05:00:00Z', end: '2026-10-01T06:00:00Z' }]
}]
const lastOption = () => chartMock.setOption.mock.calls.at(-1)[0]
```

(существующий `describe('GanttChart — палитра эффективных состояний', …)` остаётся как есть), затем:

```js
describe('GanttChart — масштаб', () => {
  beforeEach(() => vi.clearAllMocks())

  it('ось времени масштабируется ползунком и колесом, минимум — минута', () => {
    mount(GanttChart, { props: { data, shifts: [], date: '2026-10-01' } })

    const { dataZoom, xAxis } = lastOption()
    expect(dataZoom.map(z => z.type)).toEqual(['slider', 'inside'])
    dataZoom.forEach(z => {
      expect(z.xAxisIndex).toBe(0)
      expect(z.filterMode).toBe('none')
      expect(z.minValueSpan).toBe(60 * 1000)
    })
    // Иначе при приближении не появятся мелкие деления
    expect(xAxis.minInterval).toBeUndefined()
    expect(xAxis.axisLabel.formatter.second).toBe('{HH}:{mm}:{ss}')
    expect(xAxis.axisLabel.formatter.minute).toBe('{HH}:{mm}')
    expect(xAxis.axisLabel.formatter.day).toBe('{HH}:{mm}')
  })

  it('resetZoom возвращает весь период', () => {
    const w = mount(GanttChart, { props: { data, shifts: [], date: '2026-10-01' } })

    w.vm.resetZoom()

    expect(chartMock.dispatchAction).toHaveBeenCalledWith({ type: 'dataZoom', start: 0, end: 100 })
  })

  it('снимает тот же обработчик resize, что повесил', () => {
    const add = vi.spyOn(window, 'addEventListener')
    const remove = vi.spyOn(window, 'removeEventListener')
    const w = mount(GanttChart, { props: { data, shifts: [], date: '2026-10-01' } })
    const handler = add.mock.calls.find(c => c[0] === 'resize')[1]

    w.unmount()

    expect(remove).toHaveBeenCalledWith('resize', handler)
    add.mockRestore(); remove.mockRestore()
  })
})
```

- [ ] **Step 2: Run test to verify it fails.**
Run: `npx vitest run src/components/reports/__tests__/GanttChart.spec.js`
Expected: тест легенды PASS; три новых FAIL (`dataZoom` undefined, `resetZoom is not a function`, обработчик не совпадает).

- [ ] **Step 3: Implement** в `GanttChart.vue`:

3a. В `option` заменить `grid` и `xAxis` и добавить `dataZoom` (между `grid` и `xAxis`):

```js
    grid: {
      left: '3%',
      right: '4%',
      bottom: 48, // место под ползунок масштаба
      top: '5%',
      containLabel: true
    },
    // Масштаб по времени: ползунок под осью + колесо мыши/перетаскивание по диаграмме.
    // filterMode 'none' — сегмент, частично попавший в окно, не пропадает (его обрезает renderItem).
    dataZoom: [
      {
        type: 'slider', xAxisIndex: 0, filterMode: 'none', minValueSpan: 60 * 1000,
        bottom: 8, height: 20, labelFormatter: (value) => dayjs(value).format('HH:mm:ss')
      },
      { type: 'inside', xAxisIndex: 0, filterMode: 'none', minValueSpan: 60 * 1000 }
    ],
    xAxis: {
      type: 'time',
      min: axisMin ?? undefined,
      max: axisMax ?? undefined,
      axisLabel: {
        // Подпись по уровню деления: при сильном приближении — до секунд, полночь — время, не дата.
        formatter: {
          year: '{HH}:{mm}', month: '{HH}:{mm}', day: '{HH}:{mm}',
          hour: '{HH}:{mm}', minute: '{HH}:{mm}', second: '{HH}:{mm}:{ss}', millisecond: '{HH}:{mm}:{ss}'
        }
      },
      splitLine: { show: false }
    },
```

(строка `minInterval: 3600 * 1000,` удаляется.)

3b. Заменить блок `onMounted`/`onUnmounted` и добавить экспорт:

```js
// Одна и та же функция для add/removeEventListener — иначе обработчик не снимается.
const onResize = () => chart?.resize()

// Сброс масштаба к полному периоду (кнопка «Сбросить масштаб» в отчёте).
const resetZoom = () => chart?.dispatchAction({ type: 'dataZoom', start: 0, end: 100 })

defineExpose({ resetZoom, resize: onResize })

onMounted(() => {
  initChart()
  window.addEventListener('resize', onResize)
})

onUnmounted(() => {
  chart?.dispose()
  window.removeEventListener('resize', onResize)
})
```

(`watch(() => props.data, …)` не трогать: `initChart` пересоздаёт график — масштаб при новом отчёте сбрасывается сам.)

- [ ] **Step 4: Run test.** Same command. Expected: 4 PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime-Control-Frontend/src/components/reports/GanttChart.vue Wintime-Control-Frontend/src/components/reports/__tests__/GanttChart.spec.js
git commit -m "feat(reports-ui): zoomable daily report Gantt (slider + wheel), fix resize listener leak"
```

---

### Task 5: Фронт — вкладки в `DailyReportView.vue`, документация, версия

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/reports/DailyReportView.vue:56-62` (карточка графика) и `<script setup>`
- Test: `Wintime-Control-Frontend/src/views/reports/__tests__/DailyReportView.spec.js`
- Modify: `docs/user-guide/manager.md` (§8.1), `VERSION`

**Interfaces:**
- Consumes: `GanttChart` с `defineExpose({ resetZoom, resize })` (Task 4); `StatusTimelineTable` с props `data`, `periodStart`, `periodEnd` (Task 3); `reportData.periodStart/periodEnd` из API (Task 1).

- [ ] **Step 1: Write the failing test.** В `DailyReportView.spec.js`:
  - в `mountWithReport` ответ мока заменить на
    `{ data: { date: '2026-10-01', periodStart: '2026-10-01T05:00:00Z', periodEnd: '2026-10-02T05:00:00Z', immData: rows } }`;
  - в конец файла добавить:

```js
describe('DailyReportView — вкладки диаграммы и таблицы статусов', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('две вкладки, по умолчанию активна «Диаграмма»', async () => {
    const w = await mountWithReport([item()])

    const tabs = w.findComponent({ name: 'ElTabs' })
    expect(tabs.props('modelValue')).toBe('chart')
    const panes = w.findAllComponents({ name: 'ElTabPane' }).map(p => p.props('label'))
    expect(panes).toEqual(['Диаграмма', 'Таблица статусов'])
    expect(w.find('[data-test="reset-zoom"]').exists()).toBe(true)
  })

  it('вкладка «Таблица статусов» показывает группы по ТПА за период отчёта', async () => {
    const w = await mountWithReport([item()])

    await w.findComponent({ name: 'ElTabs' }).vm.$emit('update:modelValue', 'table')
    await flushPromises()

    const table = w.findComponent({ name: 'StatusTimelineTable' })
    expect(table.exists()).toBe(true)
    expect(table.props('periodStart')).toBe('2026-10-01T05:00:00Z')
    expect(table.props('periodEnd')).toBe('2026-10-02T05:00:00Z')
    expect(w.find('[data-test="reset-zoom"]').exists()).toBe(false)
  })
})
```

- [ ] **Step 2: Run test to verify it fails.**
Run: `npx vitest run src/views/reports/__tests__/DailyReportView.spec.js`
Expected: 3 старых PASS, 2 новых FAIL (нет `ElTabs`).

- [ ] **Step 3: Implement.** Карточку `<!-- График Ганта -->` заменить на:

```vue
    <!-- Диаграмма и таблица статусов -->
    <el-card class="mb-4" v-if="reportData?.immData?.length">
      <template #header>
        <div class="flex items-center justify-between">
          <span class="font-semibold">{{ ganttTitle }}</span>
          <el-button v-if="activeTab === 'chart'" size="small" data-test="reset-zoom" @click="ganttRef?.resetZoom?.()">
            Сбросить масштаб
          </el-button>
        </div>
      </template>
      <el-tabs v-model="activeTab" @tab-change="onTabChange">
        <el-tab-pane label="Диаграмма" name="chart">
          <GanttChart ref="ganttRef" :data="reportData.immData" :shifts="shifts" :date="filters.date" :shiftId="filters.shiftId" />
        </el-tab-pane>
        <el-tab-pane label="Таблица статусов" name="table" lazy>
          <StatusTimelineTable
            :data="reportData.immData"
            :period-start="reportData.periodStart"
            :period-end="reportData.periodEnd"
          />
        </el-tab-pane>
      </el-tabs>
    </el-card>
```

В `<script setup>`: импорт `nextTick` в строку импорта vue (`import { ref, reactive, computed, onMounted, nextTick } from 'vue'`), импорт компонента и состояние:

```js
import StatusTimelineTable from '@/components/reports/StatusTimelineTable.vue'
```

```js
// Вкладка сохраняется между «Сформировать»; по умолчанию — диаграмма.
const activeTab = ref('chart')
const ganttRef = ref(null)

// Пока вкладка скрыта, ширина диаграммы могла измениться (resize окна) — пересчитать при показе.
const onTabChange = (name) => {
  if (name === 'chart') nextTick(() => ganttRef.value?.resize?.())
}
```

(поставить после `const loadedFilters = ref(...)`.)

- [ ] **Step 4: Run test.** Same command. Expected: 5 PASS. Затем весь фронт: `npx vitest run` — Expected: всё PASS.

- [ ] **Step 5: Docs + version.**
  - `VERSION`: `0.15.1` → `0.16.0`.
  - `docs/user-guide/manager.md`, §8.1, в списке «**Что показывает:**» заменить пункт про диаграмму на:

```markdown
- **диаграмма** и **таблица статусов** — на двух вкладках над сводной таблицей (по умолчанию открыта «Диаграмма»):
  - **Диаграмма** — состояние каждого ТПА во времени. Масштаб меняется колесом мыши над диаграммой
    или ползунком под осью времени; при приближении подписи оси показывают минуты и секунды,
    перетаскивание мышью сдвигает видимый участок. Кнопка **«Сбросить масштаб»** возвращает весь период;
  - **Таблица статусов** — по каждому ТПА (группы сворачиваются) список состояний с **началом**, **концом**
    и **длительностью** (ч:мм:сс), в заголовке группы — сумма по каждому состоянию. Таблица — ровно за
    смену или сутки (диаграмма смены показывает ещё по 30 мин до и после). Если отчёт за текущую смену,
    у последнего состояния вместо конца стоит «сейчас»;
```

  - Подпись скриншота 8.2: заменить «диаграмма Ганта по нескольким ТПА» на «вкладки «Диаграмма» (диаграмма Ганта по нескольким ТПА с ползунком масштаба) и «Таблица статусов»».

- [ ] **Step 6: Commit**

```bash
git add Wintime-Control-Frontend/src/views/reports/DailyReportView.vue Wintime-Control-Frontend/src/views/reports/__tests__/DailyReportView.spec.js docs/user-guide/manager.md VERSION
git commit -m "feat(reports-ui): daily report tabs — zoomable chart and status table (0.16.0)"
```
