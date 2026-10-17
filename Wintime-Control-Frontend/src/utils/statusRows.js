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
  // Целые секунды от начала периода (как на бэкенде): длительность сходится с показанными временами и суммами.
  return rows.map(r => ({ ...r, durationSeconds: Math.floor((r.end - from) / 1000) - Math.floor((r.start - from) / 1000) }))
}

// Эпизод (строка таблицы) для сегмента диаграммы: первая строка, пересекающая [start, end).
// Сегмент из полей ±30 мин, задевающий период, даёт крайнюю строку; целиком вне периода — -1.
export function findEpisodeIndex(rows, start, end) {
  return rows.findIndex(r => r.start < end && start < r.end)
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
