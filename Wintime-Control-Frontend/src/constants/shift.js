function todayAt(minutes) {
  const d = new Date()
  d.setHours(Math.floor(minutes / 60), minutes % 60, 0, 0)
  return d
}

function yesterdayAt(minutes) {
  const d = todayAt(minutes)
  d.setDate(d.getDate() - 1)
  return d
}

function tomorrowAt(minutes) {
  const d = todayAt(minutes)
  d.setDate(d.getDate() + 1)
  return d
}

/**
 * Определяет текущую (или последнюю завершённую) смену по расписанию из API.
 * @param {Array} shifts  — массив ShiftDto (поля: startMinutes, durationMinutes)
 */
export function computeCurrentShift(shifts) {
  if (!shifts || shifts.length === 0) {
    const start = todayAt(8 * 60)
    return { start, end: new Date(start.getTime() + 8 * 3_600_000) }
  }

  const now    = new Date()
  const nowMin = now.getHours() * 60 + now.getMinutes()

  let fallback    = null
  let fallbackEnd = null

  for (const s of shifts) {
    const startMin = s.startMinutes
    const totalEnd = startMin + s.durationMinutes
    const crosses  = totalEnd > 24 * 60
    const endMin   = crosses ? totalEnd - 24 * 60 : totalEnd

    if (crosses) {
      if (nowMin >= startMin) {
        // Смена началась сегодня, заканчивается завтра
        return { start: todayAt(startMin), end: tomorrowAt(endMin) }
      } else if (nowMin < endMin) {
        // Мы в «хвосте» (после полуночи) — смена началась вчера
        return { start: yesterdayAt(startMin), end: todayAt(endMin) }
      }
      // Смена не активна, запоминаем как кандидат на fallback
      const prevEnd = todayAt(endMin) <= now ? todayAt(endMin) : yesterdayAt(endMin)
      if (!fallbackEnd || prevEnd > fallbackEnd) {
        fallbackEnd = prevEnd
        fallback = { start: new Date(prevEnd.getTime() - s.durationMinutes * 60_000), end: prevEnd }
      }
    } else {
      if (nowMin >= startMin && nowMin < endMin) {
        return { start: todayAt(startMin), end: todayAt(endMin) }
      }
      const prevEnd = todayAt(endMin) <= now ? todayAt(endMin) : yesterdayAt(endMin)
      if (!fallbackEnd || prevEnd > fallbackEnd) {
        fallbackEnd = prevEnd
        fallback = { start: new Date(prevEnd.getTime() - s.durationMinutes * 60_000), end: prevEnd }
      }
    }
  }

  // Ни одна смена не активна — возвращаем последнюю завершённую
  if (fallback) return fallback

  const start = todayAt(8 * 60)
  return { start, end: new Date(start.getTime() + 8 * 3_600_000) }
}

/**
 * Граница текущей смены для разбивки заданий наладчика по разделам.
 * Задание относится к текущей смене, если оно выдано (IssuedAt) на этой границе или позже;
 * выданное раньше — к прошедшим сменам.
 *
 * - Если сейчас идёт смена → граница = её начало.
 * - Если между сменами → граница = конец последней завершённой смены
 *   (так задания, выданные заранее на ближайшую смену, попадают в «текущую»).
 *
 * @param {Array} shifts — массив ShiftDto
 * @returns {Date}
 */
export function computeShiftBoundary(shifts) {
  const { start, end } = computeCurrentShift(shifts)
  // computeCurrentShift возвращает либо активную смену (start ≤ now ≤ end),
  // либо последнюю завершённую (end ≤ now). В первом случае граница — начало смены,
  // во втором — её конец (начало ожидания следующей смены).
  return new Date() <= end ? start : end
}
