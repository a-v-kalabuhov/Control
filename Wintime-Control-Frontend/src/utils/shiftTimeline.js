// Геометрия бара задания на хронологии смены (карточка ТПА, ARCH-03).
// Бар = окно активности задания [setupStartedAt, completedAt ?? closedAt ?? now),
// обрезанное границами смены; setupPct — доля бара, занятая наладкой (до startedAt).

export function taskBarLayout(task, shiftStart, shiftEnd, now) {
  if (!task?.setupStartedAt) return null
  const s0 = shiftStart.getTime()
  const e0 = shiftEnd.getTime()
  const dur = e0 - s0
  if (dur <= 0) return null

  const clamp = (t) => Math.min(Math.max(t, s0), e0)
  const start = new Date(task.setupStartedAt).getTime()
  const endRaw = task.completedAt ?? task.closedAt
  const end = endRaw ? new Date(endRaw).getTime() : now.getTime()
  const releaseStart = task.startedAt ? new Date(task.startedAt).getTime() : end

  const s = clamp(start)
  const e = clamp(end)
  if (e <= s) return null
  const setupEnd = clamp(Math.min(releaseStart, end))

  return {
    leftPct: (s - s0) / dur * 100,
    widthPct: (e - s) / dur * 100,
    setupPct: Math.max(0, setupEnd - s) / (e - s) * 100
  }
}

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
