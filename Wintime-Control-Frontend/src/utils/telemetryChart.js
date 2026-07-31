// Чистые хелперы для дашборда телеметрии BL-23 (без зависимостей от echarts/Vue).

const NUMERIC_TYPES = new Set(['float', 'int', 'cycleCounter'])

function numericValue(p) {
  if (p.num !== null && p.num !== undefined) return Number(p.num)
  if (p.txt === 'true') return 1
  if (p.txt === 'false') return 0
  const n = Number(p.txt)
  return Number.isNaN(n) ? null : n
}

// Метки времени телеметрии хранятся с точностью до секунды (unix seconds), а ТПА публикует
// чаще — на одну секунду приходится несколько сэмплов. Для ступенчатой серии значим последний:
// совпадающие по x точки иначе рисуются «столбиком» друг на друге, а подсказка echarts выводит
// строку на каждую из них (все точки, равноудалённые от курсора) и растёт на весь экран.
function dedupeByTimestamp(pairs) {
  const out = []
  for (const pair of pairs) {
    if (out.length && out[out.length - 1][0] === pair[0]) out[out.length - 1] = pair
    else out.push(pair)
  }
  return out
}

// Ступенчатая серия для echarts (series.step='end'): пары [timestampMs, value].
// Держим последнее значение до конца окна замыкающей парой — если только не наступил
// Offline: тогда удержание обрывается на его начале (Вариант A, ADR/спека BL-23 §7).
export function buildStepSeries(points, windowEndMs, offlineStarts = []) {
  const raw = dedupeByTimestamp(points.map(p => [new Date(p.t).getTime(), numericValue(p)]))
  if (raw.length === 0) return []
  const firstOfflineStartIn = (t, tEnd) => {
    for (const o of offlineStarts) if (o > t && o < tEnd) return o
    return null
  }
  const out = []
  for (let i = 0; i < raw.length; i++) {
    const [t, v] = raw[i]
    out.push([t, v])
    const tEnd = (i + 1 < raw.length) ? raw[i + 1][0] : windowEndMs
    if (tEnd > t) {
      const o = firstOfflineStartIn(t, tEnd)
      if (o != null) {
        out.push([o, v])      // держим значение до ухода в Offline
        out.push([o, null])   // разрыв — линии нет до следующего сэмпла
      } else if (i === raw.length - 1) {
        out.push([windowEndMs, v]) // правый край держим только если не оборвались в Offline
      }
    }
  }
  return out
}

// Тип оси: числовая шкала vs дорожка состояний.
export function resolveAxisKind(type) {
  return NUMERIC_TYPES.has(type) ? 'numeric' : 'state'
}

// Слияние live-точек: добавляем только новее последней; из левого края окна держим
// carry-in — одну ближайшую точку строго до windowStartMs, — чтобы левая граница
// оставалась определена, пока окно скользит.
export function mergeLivePoints(existing, incoming, windowStartMs) {
  const lastT = existing.length ? new Date(existing[existing.length - 1].t).getTime() : -Infinity
  const fresh = incoming.filter(p => new Date(p.t).getTime() > lastT)
  const all = existing.concat(fresh)
  const inWindow = all.filter(p => new Date(p.t).getTime() >= windowStartMs)
  const before = all.filter(p => new Date(p.t).getTime() < windowStartMs)
  const carry = before.length ? before[before.length - 1] : null // одна ближайшая точка слева
  return carry ? [carry, ...inWindow] : inWindow
}

// Не даём снять последний сигнал: если новый выбор пуст — сохраняем прежний.
export function enforceMinOneSignal(next, prev) {
  return (Array.isArray(next) && next.length > 0) ? next : prev
}
