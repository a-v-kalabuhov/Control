// Сдвиг окна истории телеметрии кнопками ◀ / ▶ (спека 2026-10-16-telemetry-history-shift-design).
// Шаг — треть окна, длина сохраняется; вправо окно не уходит дальше «сейчас».
// null — сдвигать некуда (▶ при конце ≥ «сейчас») или окно некорректно.
export function shiftWindow(fromMs, toMs, direction, nowMs) {
  if (!Number.isFinite(fromMs) || !Number.isFinite(toMs) || toMs <= fromMs) return null
  const length = toMs - fromMs
  const step = Math.floor(length / 3)

  if (direction < 0) return { fromMs: fromMs - step, toMs: toMs - step }

  if (toMs >= nowMs) return null
  const newTo = Math.min(toMs + step, nowMs)
  return { fromMs: newTo - length, toMs: newTo }
}
