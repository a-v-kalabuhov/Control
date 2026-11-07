import dayjs from 'dayjs'
import utc from 'dayjs/plugin/utc'
import timezone from 'dayjs/plugin/timezone'

dayjs.extend(utc)
dayjs.extend(timezone)

function timestamp(value) {
  const result = new Date(value).getTime()
  return Number.isFinite(result) ? result : null
}

export function resolveShiftWindow(date, shift) {
  const timeZone = shift.timeZoneId ?? dayjs.tz.guess()
  const start = dayjs.tz(`${date} 00:00`, 'YYYY-MM-DD HH:mm', timeZone).add(shift.startMinutes, 'minute')
  const end = start.add(shift.durationMinutes, 'minute')
  return { start: start.toDate(), end: end.toDate() }
}

export function resolveInitialShiftSelection(shifts, now = new Date()) {
  for (const shift of shifts) {
    const timeZone = shift.timeZoneId ?? dayjs.tz.guess()
    const today = dayjs(now).tz(timeZone).format('YYYY-MM-DD')
    const yesterday = dayjs(today).subtract(1, 'day').format('YYYY-MM-DD')
    for (const date of [today, yesterday]) {
      const window = resolveShiftWindow(date, shift)
      if (now >= window.start && now < window.end) return { date, shiftNumber: shift.number }
    }
  }

  const first = shifts[0]
  const timeZone = first?.timeZoneId ?? dayjs.tz.guess()
  return {
    date: dayjs(now).tz(timeZone).format('YYYY-MM-DD'),
    shiftNumber: first?.number ?? null
  }
}

function overlapsActivity(task, window, now) {
  const start = timestamp(task.setupStartedAt)
  if (start === null) return false
  const end = timestamp(task.completedAt ?? task.closedAt) ?? now.getTime()
  return start < window.end.getTime() && end > window.start.getTime()
}

function startsInside(task, window) {
  const start = timestamp(task.plannedStartAt)
  return start !== null && start >= window.start.getTime() && start < window.end.getTime()
}

export function selectShiftWorkPlan(tasks, window, now = new Date()) {
  const unique = new Map()
  for (const task of tasks) {
    const belongs = task.status === 'Planned' || task.status === 'Issued'
      ? startsInside(task, window)
      : (task.status === 'Setup' || task.status === 'InProgress') && overlapsActivity(task, window, now)
    if (belongs) unique.set(task.id, task)
  }

  return [...unique.values()].sort((a, b) =>
    timestamp(a.plannedStartAt) - timestamp(b.plannedStartAt) ||
    (a.immName ?? '').localeCompare(b.immName ?? '', 'ru'))
}

export function isReadyToIssue(task, activeAdjusterIds) {
  return task.status === 'Planned' && !!task.personnelId && activeAdjusterIds.has(task.personnelId)
}
