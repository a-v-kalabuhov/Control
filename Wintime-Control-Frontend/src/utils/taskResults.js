// Форматтеры вкладки «Результаты выполнения» задания. API отдаёт секунды —
// проценты и отклонения от эталона считаются здесь.

export function formatSeconds(v) {
  if (v === null || v === undefined) return '—'
  return `${v.toFixed(1)} с`
}

export function sharePercent(part, whole) {
  if (!whole) return null
  return Math.round((part / whole) * 100)
}

export function deviationPercent(fact, plan) {
  if (fact === null || fact === undefined || !plan) return null
  return Math.round(((fact - plan) / plan) * 100)
}

export function formatDeviation(pct) {
  if (pct === null || pct === undefined) return '—'
  if (pct > 0) return `+${pct} %`
  if (pct < 0) return `−${Math.abs(pct)} %`
  return '0 %'
}
