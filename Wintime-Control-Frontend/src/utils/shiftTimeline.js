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
