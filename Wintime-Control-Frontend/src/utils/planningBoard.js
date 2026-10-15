// План-график цеха (PZP-07a): геометрия Гантта. Время — в поясе браузера.
import { formatClock } from './shiftTimeline'

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
