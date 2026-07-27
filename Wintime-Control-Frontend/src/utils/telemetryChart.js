// Чистые хелперы для дашборда телеметрии BL-23 (без зависимостей от echarts/Vue).

const NUMERIC_TYPES = new Set(['float', 'int', 'cycleCounter'])

function numericValue(p) {
  if (p.num !== null && p.num !== undefined) return Number(p.num)
  if (p.txt === 'true') return 1
  if (p.txt === 'false') return 0
  const n = Number(p.txt)
  return Number.isNaN(n) ? null : n
}

// Ступенчатая серия для echarts (series.step='end'): пары [timestampMs, value].
// Держим последнее значение до конца окна замыкающей парой.
export function buildStepSeries(points, windowEndMs) {
  const data = points.map(p => [new Date(p.t).getTime(), numericValue(p)])
  if (data.length > 0) {
    const last = data[data.length - 1]
    if (last[0] < windowEndMs) data.push([windowEndMs, last[1]])
  }
  return data
}

// Тип оси: числовая шкала vs дорожка состояний.
export function resolveAxisKind(type) {
  return NUMERIC_TYPES.has(type) ? 'numeric' : 'state'
}

// Слияние live-точек: добавляем только новее последней, режем левый край окна.
export function mergeLivePoints(existing, incoming, windowStartMs) {
  const lastT = existing.length ? new Date(existing[existing.length - 1].t).getTime() : -Infinity
  const fresh = incoming.filter(p => new Date(p.t).getTime() > lastT)
  return existing.concat(fresh).filter(p => new Date(p.t).getTime() >= windowStartMs)
}

// Не даём снять последний сигнал: если новый выбор пуст — сохраняем прежний.
export function enforceMinOneSignal(next, prev) {
  return (Array.isArray(next) && next.length > 0) ? next : prev
}
