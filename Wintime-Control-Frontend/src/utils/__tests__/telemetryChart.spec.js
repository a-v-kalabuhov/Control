import { describe, it, expect } from 'vitest'
import { buildStepSeries, resolveAxisKind, mergeLivePoints } from '@/utils/telemetryChart'

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
})

describe('resolveAxisKind', () => {
  it('числовые типы → numeric', () => {
    expect(resolveAxisKind('float')).toBe('numeric')
    expect(resolveAxisKind('int')).toBe('numeric')
    expect(resolveAxisKind('cycleCounter')).toBe('numeric')
  })
  it('boolean/string → state', () => {
    expect(resolveAxisKind('boolean')).toBe('state')
    expect(resolveAxisKind('string')).toBe('state')
    expect(resolveAxisKind('unknown')).toBe('state')
  })
})

describe('mergeLivePoints', () => {
  it('дописывает только точки новее последней и режет левый край', () => {
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
    expect(merged.map(p => p.num)).toEqual([2, 3]) // 08:00 ушёл за левый край
  })
})
