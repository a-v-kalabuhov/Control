export const SHIFT_START_HOUR = 8
export const SHIFT_DURATION_HOURS = 8

export function getCurrentShift() {
  const now = new Date()
  const todayStart = new Date(now)
  todayStart.setHours(SHIFT_START_HOUR, 0, 0, 0)

  if (now >= todayStart) {
    // Текущая или завершённая сегодняшняя смена
    return {
      start: todayStart,
      end: new Date(todayStart.getTime() + SHIFT_DURATION_HOURS * 3_600_000),
    }
  } else {
    // До начала сегодняшней смены — показываем вчерашнюю
    const start = new Date(todayStart.getTime() - 24 * 3_600_000)
    return {
      start,
      end: new Date(start.getTime() + SHIFT_DURATION_HOURS * 3_600_000),
    }
  }
}
