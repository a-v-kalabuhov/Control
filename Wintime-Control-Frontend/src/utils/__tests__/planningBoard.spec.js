import { describe, it, expect } from 'vitest'
import { periodFor, stepAnchor, gridTicks, spanPct, periodLabel } from '../planningBoard'

const d = (y, m, day, h = 0, min = 0) => new Date(y, m - 1, day, h, min)

describe('periodFor', () => {
  it('сутки — от начала первой смены текущих суток', () => {
    const p = periodFor('day', d(2026, 10, 15, 10), 480)
    expect(p.from).toEqual(d(2026, 10, 15, 8))
    expect(p.to).toEqual(d(2026, 10, 16, 8))
  })

  it('сутки до начала первой смены — это ещё предыдущие сутки', () => {
    const p = periodFor('day', d(2026, 10, 15, 6), 480)
    expect(p.from).toEqual(d(2026, 10, 14, 8))
  })

  it('неделя — с понедельника 00:00', () => {
    expect(periodFor('week', d(2026, 10, 15, 10))).toEqual({ from: d(2026, 10, 12), to: d(2026, 10, 19) })
  })

  it('воскресенье относится к неделе с прошедшего понедельника', () => {
    expect(periodFor('week', d(2026, 10, 18, 23)).from).toEqual(d(2026, 10, 12))
  })

  it('месяц — календарный', () => {
    expect(periodFor('month', d(2026, 10, 15))).toEqual({ from: d(2026, 10, 1), to: d(2026, 11, 1) })
  })
})

describe('stepAnchor', () => {
  it('сутки ±1 день, неделя ±7 дней', () => {
    expect(stepAnchor('day', d(2026, 10, 15, 10), 1)).toEqual(d(2026, 10, 16, 10))
    expect(stepAnchor('week', d(2026, 10, 15, 10), -1)).toEqual(d(2026, 10, 8, 10))
  })

  it('месяц не перескакивает через короткий месяц', () => {
    expect(stepAnchor('month', d(2026, 1, 31), 1)).toEqual(d(2026, 2, 1))
  })
})

describe('gridTicks', () => {
  it('сутки — начала смен внутри окна', () => {
    const shifts = [{ startMinutes: 480 }, { startMinutes: 1200 }]
    const ticks = gridTicks('day', d(2026, 10, 15, 8), d(2026, 10, 16, 8), shifts)
    expect(ticks.map(t => t.pct)).toEqual([0, 50])
    expect(ticks[1].at).toEqual(d(2026, 10, 15, 20))
  })

  it('неделя — 7 границ суток', () => {
    const ticks = gridTicks('week', d(2026, 10, 12), d(2026, 10, 19))
    expect(ticks).toHaveLength(7)
    expect(ticks[1].pct).toBeCloseTo(100 / 7)
  })

  it('месяц — по числу дней, подпись — число', () => {
    const ticks = gridTicks('month', d(2026, 10, 1), d(2026, 11, 1))
    expect(ticks).toHaveLength(31)
    expect(ticks[14].label).toBe('15')
  })
})

describe('spanPct', () => {
  const from = d(2026, 10, 15, 0)
  const to = d(2026, 10, 15, 10)

  it('интервал внутри окна', () => {
    const s = spanPct(d(2026, 10, 15, 1), d(2026, 10, 15, 3), from, to)
    expect(s.leftPct).toBeCloseTo(10)
    expect(s.widthPct).toBeCloseTo(20)
  })

  it('обрезается краями окна', () => {
    const s = spanPct(d(2026, 10, 14, 20), d(2026, 10, 15, 2), from, to)
    expect(s.leftPct).toBeCloseTo(0)
    expect(s.widthPct).toBeCloseTo(20)
  })

  it('вне окна или нулевой длины — null', () => {
    expect(spanPct(d(2026, 10, 15, 11), d(2026, 10, 15, 12), from, to)).toBeNull()
    expect(spanPct(d(2026, 10, 15, 2), d(2026, 10, 15, 2), from, to)).toBeNull()
  })
})

describe('periodLabel', () => {
  it('неделя — даты первого и последнего дня', () => {
    expect(periodLabel('week', d(2026, 10, 12), d(2026, 10, 19))).toBe('12.10.2026 — 18.10.2026')
  })
})
