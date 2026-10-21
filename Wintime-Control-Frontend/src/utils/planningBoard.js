// План-график цеха (PZP-07a): геометрия Гантта. Время — в поясе браузера.
import { formatClock, formatDuration, assignLanes } from './shiftTimeline'
import { TASK_STATUS_LABELS } from '@/constants/taskStatus'
import { conflictLine } from './planConflicts'

export const SCALES = ['day', 'week', 'month']
export const SCALE_LABELS = { day: 'Сутки', week: 'Неделя', month: 'Месяц' }

const ms = (v) => (v == null ? null : new Date(v).getTime())
const fmtDate = (date) => date.toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' })

// Окно доски [from, to): сутки — от начала первой смены, неделя — с понедельника 00:00,
// месяц — с 1-го числа 00:00.
export function periodFor(scale, anchor, firstShiftStartMinutes = 0) {
  const a = new Date(anchor)
  if (scale === 'day') {
    const from = new Date(a.getFullYear(), a.getMonth(), a.getDate(), 0, firstShiftStartMinutes)
    if (from > a) from.setDate(from.getDate() - 1)
    const to = new Date(from)
    to.setDate(to.getDate() + 1)
    return { from, to }
  }
  if (scale === 'week') {
    const from = new Date(a.getFullYear(), a.getMonth(), a.getDate())
    from.setDate(from.getDate() - ((from.getDay() + 6) % 7))
    const to = new Date(from)
    to.setDate(to.getDate() + 7)
    return { from, to }
  }
  return { from: new Date(a.getFullYear(), a.getMonth(), 1), to: new Date(a.getFullYear(), a.getMonth() + 1, 1) }
}

export function stepAnchor(scale, anchor, dir) {
  const d = new Date(anchor)
  if (scale === 'day') d.setDate(d.getDate() + dir)
  else if (scale === 'week') d.setDate(d.getDate() + 7 * dir)
  else {
    d.setDate(1)
    d.setMonth(d.getMonth() + dir)
  }
  return d
}

export function periodLabel(scale, from, to) {
  if (scale === 'day') return `${fmtDate(from)} ${formatClock(from)} — ${fmtDate(to)} ${formatClock(to)}`
  if (scale === 'week') return `${fmtDate(from)} — ${fmtDate(new Date(to.getTime() - 1))}`
  return from.toLocaleDateString('ru-RU', { month: 'long', year: 'numeric' })
}

const pctOf = (t, f, total) => (t - f) / total * 100

// Вертикальная сетка: на сутках — начала смен, на неделе и месяце — границы суток.
export function gridTicks(scale, from, to, shifts = []) {
  const f = from.getTime()
  const t = to.getTime()
  const total = t - f
  const ticks = []
  if (scale === 'day') {
    const starts = [...new Set(shifts.map(s => s.startMinutes))]
    for (let day = 0; day <= 1; day++) {
      for (const minutes of starts) {
        const at = new Date(from.getFullYear(), from.getMonth(), from.getDate() + day, 0, minutes)
        if (at.getTime() >= f && at.getTime() < t) ticks.push({ at, pct: pctOf(at.getTime(), f, total), label: formatClock(at) })
      }
    }
    return ticks.sort((a, b) => a.at - b.at)
  }
  for (let at = new Date(from); at.getTime() < t; at = new Date(at.getFullYear(), at.getMonth(), at.getDate() + 1)) {
    const label = scale === 'week'
      ? at.toLocaleDateString('ru-RU', { weekday: 'short', day: '2-digit', month: '2-digit' })
      : String(at.getDate())
    ticks.push({ at, pct: pctOf(at.getTime(), f, total), label })
  }
  return ticks
}

// Интервал → позиция в процентах ширины окна; обрезается краями; пусто — null.
export function spanPct(start, end, from, to) {
  const f = from.getTime()
  const t = to.getTime()
  const s = Math.max(ms(start), f)
  const e = Math.min(ms(end), t)
  if (!(e > s)) return null
  return { leftPct: pctOf(s, f, t - f), widthPct: (e - s) / (t - f) * 100 }
}

// Цвет пары баров — по статусу задания (оттенки Element Plus).
export const GANTT_STATUS_COLORS = {
  Planned: '#909399',
  Issued: '#409eff',
  Setup: '#e6a23c',
  InProgress: '#67c23a',
  Completed: '#529b2e',
  Closed: '#a8abb2'
}

function seg(kind, start, end) {
  const s = ms(start)
  const e = ms(end)
  return s != null && e != null && e > s ? { kind, start: s, end: e } : null
}

// Сегменты пары: план (наладка → выпуск → буфер) и факт (наладка → выпуск до «сейчас» →
// прогноз → остаток буфера или перерасход). Нулевые сегменты отбрасываются.
export function boardSegments(item, now) {
  const n = now.getTime()
  const { plan: p, fact: f, task } = item
  const plan = [
    seg('setup', p.setupStart, p.setupEnd),
    seg('release', p.setupEnd, p.releaseEnd),
    seg('buffer', p.releaseEnd, p.bufferEnd)
  ].filter(Boolean)

  const fact = []
  if (f.setupStart) fact.push(seg('setup', f.setupStart, f.setupEnd ?? n))
  if (task.startedAt) fact.push(seg('release', task.startedAt, f.releaseEnd ?? n))
  if (f.forecastEnd) fact.push(seg('forecast', f.forecastStart ?? n, f.forecastEnd))
  const r = item.bufferRemainingSeconds
  if (r != null && p.bufferEnd) {
    const releaseFinish = task.status === 'InProgress' ? (f.forecastEnd ?? n) : f.releaseEnd
    fact.push(r > 0 ? seg('remaining', releaseFinish, p.bufferEnd) : seg('overrun', p.bufferEnd, releaseFinish))
  }
  return { plan, fact: fact.filter(Boolean) }
}

function pairInterval(item, now) {
  const { plan, fact } = boardSegments(item, now)
  const all = [...plan, ...fact]
  const start0 = ms(item.plan.setupStart)
  if (all.length === 0) return { start: start0, end: start0 }
  return { start: Math.min(...all.map(s => s.start)), end: Math.max(...all.map(s => s.end)) }
}

// Рамка пары в % окна и сегменты в % рамки; пара вне окна — null.
export function pairLayout(item, from, to, now) {
  const { plan, fact } = boardSegments(item, now)
  const { start, end } = pairInterval(item, now)
  const box = spanPct(start, end, from, to)
    ?? (start >= from.getTime() && start < to.getTime()
      ? { leftPct: pctOf(start, from.getTime(), to.getTime() - from.getTime()), widthPct: 0 }
      : null)
  if (!box) return null
  const inBox = (s) => {
    const sp = spanPct(s.start, s.end, from, to)
    if (!sp || box.widthPct === 0) return null
    return { kind: s.kind, leftPct: (sp.leftPct - box.leftPct) / box.widthPct * 100, widthPct: sp.widthPct / box.widthPct * 100 }
  }
  return { box, plan: plan.map(inBox).filter(Boolean), fact: fact.map(inBox).filter(Boolean) }
}


// Дорожки пар в строке ТПА: пересекающиеся пары не накладываются друг на друга.
export function rowLanes(items, now) {
  return assignLanes(items.map(i => pairInterval(i, now)))
}

// Текстовые метки отклонений; меньше минуты не показываем.
export function boardMarks(item) {
  const marks = []
  if (item.overdueNotStartedSeconds >= 60)
    marks.push({ text: `не начато, опоздание ${formatDuration(item.overdueNotStartedSeconds * 1000)}`, row: 'plan' })
  if (item.setupLagSeconds >= 60)
    marks.push({ text: `наладка отстала на ${formatDuration(item.setupLagSeconds * 1000)}`, row: 'fact' })
  if (item.bufferRemainingSeconds <= -60)
    marks.push({ text: `перерасход ${formatDuration(-item.bufferRemainingSeconds * 1000)}`, row: 'fact' })
  return marks
}

export function planLabel(item) {
  return [item.productArticle, item.task.moldName, `${item.task.planQuantity} шт`].filter(Boolean).join(' · ')
}

const fmtStamp = (v) => {
  const date = new Date(v)
  return `${date.toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit' })} ${formatClock(date)}`
}

// Строки нативного тултипа (title) пары.
export function tooltipLines(item) {
  const { task, plan, fact } = item
  const lines = [
    `${task.immName ?? ''} · ${planLabel(item)}`,
    `Статус: ${TASK_STATUS_LABELS[task.status] ?? task.status}`,
    `Наладчик: ${task.personnelName ?? '—'}`,
    `План: ${fmtStamp(plan.setupStart)} — ${fmtStamp(plan.bufferEnd ?? plan.setupEnd)}`,
    `Норматив наладки: ${formatDuration((task.plannedSetupMinutes ?? 0) * 60_000)}`
  ]
  if (task.plannedReleaseBufferSeconds != null)
    lines.push(`Буфер выпуска: ${formatDuration(task.plannedReleaseBufferSeconds * 1000)}`)
  if (fact.setupStart) lines.push(`Начало наладки: ${fmtStamp(fact.setupStart)}`)
  if (task.startedAt) lines.push(`Начало выпуска: ${fmtStamp(task.startedAt)}`)
  if (fact.releaseEnd) lines.push(`Окончание выпуска: ${fmtStamp(fact.releaseEnd)}`)
  if (fact.forecastEnd) lines.push(`Прогноз окончания выпуска: ${fmtStamp(fact.forecastEnd)}`)
  const r = item.bufferRemainingSeconds
  if (r != null) lines.push(r >= 0 ? `Остаток буфера: ${formatDuration(r * 1000)}` : `Перерасход: ${formatDuration(-r * 1000)}`)
  for (const m of boardMarks(item)) if (!m.text.startsWith('перерасход')) lines.push(m.text)
  for (const c of item.conflicts ?? []) lines.push(conflictLine(c, task.id))
  return lines
}
