import { describe, it, expect } from 'vitest'
import { buildStepSeries, resolveAxisKind, mergeLivePoints, enforceMinOneSignal } from '@/utils/telemetryChart'

const t = (iso) => new Date(iso).getTime()

describe('buildStepSeries', () => {
  it('преобразует числовые точки в пары [ms, value]', () => {
    const points = [
      { t: '2026-07-27T08:00:00Z', num: 218.4 },
      { t: '2026-07-27T08:01:00Z', num: 219.1 },
    ]
    const end = t('2026-07-27T08:02:00Z')
    const series = buildStepSeries(points, end)
    expect(series[0]).toEqual([t('2026-07-27T08:00:00Z'), 218.4])
    expect(series[1]).toEqual([t('2026-07-27T08:01:00Z'), 219.1])
  })

  it('добавляет замыкающую точку до конца окна', () => {
    const points = [{ t: '2026-07-27T08:00:00Z', num: 5 }]
    const end = t('2026-07-27T08:05:00Z')
    const series = buildStepSeries(points, end)
    expect(series).toHaveLength(2)
    expect(series[1]).toEqual([end, 5])
  })

  it('булев текст маппится в 1/0', () => {
    const points = [{ t: '2026-07-27T08:00:00Z', txt: 'true' }]
    const series = buildStepSeries(points, t('2026-07-27T08:00:00Z'))
    expect(series[0][1]).toBe(1)
  })

  it('пустой вход даёт пустую серию', () => {
    expect(buildStepSeries([], t('2026-07-27T08:00:00Z'))).toEqual([])
  })

  // Метки времени телеметрии хранятся с точностью до секунды, а ТПА шлёт чаще —
  // в одну секунду попадает несколько сэмплов. Для ступенчатой серии значим последний:
  // иначе точки накладываются друг на друга и подсказка echarts печатает строку на каждую.
  it('точки с одинаковой меткой схлопываются в последнюю', () => {
    const points = [
      { t: '2026-07-27T08:00:00Z', num: 1 },
      { t: '2026-07-27T08:00:00Z', num: 2 },
      { t: '2026-07-27T08:00:00Z', num: 3 },
      { t: '2026-07-27T08:00:01Z', num: 4 },
    ]
    const end = t('2026-07-27T08:00:01Z')
    const series = buildStepSeries(points, end)
    expect(series).toEqual([
      [t('2026-07-27T08:00:00Z'), 3],
      [t('2026-07-27T08:00:01Z'), 4],
    ])
  })
})

describe('buildStepSeries + offline', () => {
  it('правый край останавливается на начале Offline (не тянется до windowEndMs)', () => {
    const t0 = t('2026-07-27T08:00:00Z')
    const points = [{ t: '2026-07-27T08:00:00Z', num: 5 }]
    const windowEndMs = t0 + 600_000
    const offlineStarts = [t0 + 300_000]
    const series = buildStepSeries(points, windowEndMs, offlineStarts)
    expect(series).toContainEqual([t0 + 300_000, 5])
    const idx = series.findIndex(p => p[0] === t0 + 300_000 && p[1] === 5)
    expect(series[idx + 1]).toEqual([t0 + 300_000, null])
    expect(series.some(p => p[0] === windowEndMs)).toBe(false)
  })

  it('разрыв между двумя точками через Offline-провал', () => {
    const t0 = t('2026-07-27T08:00:00Z')
    const points = [
      { t: '2026-07-27T08:00:00Z', num: 1 },
      { t: '2026-07-27T08:10:00Z', num: 2 },
    ]
    const windowEndMs = t0 + 600_000
    const offlineStarts = [t0 + 200_000]
    const series = buildStepSeries(points, windowEndMs, offlineStarts)
    expect(series).toEqual([
      [t0, 1],
      [t0 + 200_000, 1],
      [t0 + 200_000, null],
      [t0 + 600_000, 2],
    ])
  })

  it('без Offline поведение не меняется (замыкающая точка на правом крае)', () => {
    const points = [{ t: '2026-07-27T08:00:00Z', num: 5 }]
    const end = t('2026-07-27T08:05:00Z')
    const series = buildStepSeries(points, end, [])
    expect(series).toHaveLength(2)
    expect(series[1]).toEqual([end, 5])
  })
})

describe('resolveAxisKind', () => {
  it('числовые типы → numeric', () => {
    expect(resolveAxisKind('float')).toBe('numeric')
    expect(resolveAxisKind('int')).toBe('numeric')
    expect(resolveAxisKind('cycleCounter')).toBe('numeric')
  })
  // Находка 2: injectionDuration/cyclePause — защёлкнутые числовые длительности
  // цикла литья и паузы; должны разбираться как numeric, а не проваливаться в state.
  it('injectionDuration/cyclePause → numeric', () => {
    expect(resolveAxisKind('injectionDuration')).toBe('numeric')
    expect(resolveAxisKind('cyclePause')).toBe('numeric')
  })
  it('boolean/string → state', () => {
    expect(resolveAxisKind('boolean')).toBe('state')
    expect(resolveAxisKind('string')).toBe('state')
    expect(resolveAxisKind('unknown')).toBe('state')
  })
})

describe('mergeLivePoints', () => {
  it('дописывает только точки новее последней и держит carry-in на левом крае', () => {
    const existing = [
      { t: '2026-07-27T08:00:00Z', num: 1 },
      { t: '2026-07-27T08:01:00Z', num: 2 },
    ]
    const incoming = [
      { t: '2026-07-27T08:01:00Z', num: 2 }, // дубль границы — не добавляем
      { t: '2026-07-27T08:02:00Z', num: 3 },
    ]
    const windowStart = t('2026-07-27T08:01:00Z')
    const merged = mergeLivePoints(existing, incoming, windowStart)
    expect(merged.map(p => p.num)).toEqual([1, 2, 3]) // 08:00 остаётся как carry-in слева
  })

  it('держит ровно одну ближайшую carry-in точку, даже если слева от окна их несколько', () => {
    const existing = [
      { t: '2026-07-27T08:00:00Z', num: 1 },
      { t: '2026-07-27T08:00:30Z', num: 2 },
    ]
    const incoming = [
      { t: '2026-07-27T08:02:00Z', num: 3 },
    ]
    const windowStart = t('2026-07-27T08:01:00Z')
    const merged = mergeLivePoints(existing, incoming, windowStart)
    expect(merged.map(p => p.num)).toEqual([2, 3]) // только 08:00:30 (ближайшая слева), 08:00 отброшена
  })
})

describe('enforceMinOneSignal', () => {
  it('непустой новый выбор проходит как есть', () => {
    expect(enforceMinOneSignal(['a', 'b'], ['a'])).toEqual(['a', 'b'])
  })

  it('пустой новый выбор откатывается на прежний', () => {
    expect(enforceMinOneSignal([], ['a'])).toEqual(['a'])
  })

  it('не-массив/undefined откатывается на прежний', () => {
    expect(enforceMinOneSignal(undefined, ['a'])).toEqual(['a'])
    expect(enforceMinOneSignal(null, ['a'])).toEqual(['a'])
  })
})
