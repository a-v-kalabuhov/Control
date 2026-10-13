import { describe, it, expect } from 'vitest'
import { taskBarLayout } from '../shiftTimeline'

const S = new Date('2026-10-13T08:00:00Z')
const E = new Date('2026-10-13T20:00:00Z') // смена 12 ч
const at = (h) => new Date(S.getTime() + h * 3_600_000).toISOString()
const NOW = new Date(S.getTime() + 6 * 3_600_000)

describe('taskBarLayout', () => {
  it('нет начала наладки — бара нет', () => {
    expect(taskBarLayout({ setupStartedAt: null }, S, E, NOW)).toBeNull()
  })

  it('начато до смены и идёт — от начала смены до «сейчас», наладки в смене нет', () => {
    const l = taskBarLayout({ setupStartedAt: at(-30), startedAt: at(-29) }, S, E, NOW)
    expect(l.leftPct).toBeCloseTo(0)
    expect(l.widthPct).toBeCloseTo(50)
    expect(l.setupPct).toBeCloseTo(0)
  })

  it('наладка и выпуск внутри смены — доля наладки', () => {
    const l = taskBarLayout({ setupStartedAt: at(1), startedAt: at(2), completedAt: at(5) }, S, E, NOW)
    expect(l.leftPct).toBeCloseTo(100 / 12)
    expect(l.widthPct).toBeCloseTo(4 / 12 * 100)
    expect(l.setupPct).toBeCloseTo(25)
  })

  it('идёт наладка — весь бар наладка', () => {
    const l = taskBarLayout({ setupStartedAt: at(4) }, S, E, NOW)
    expect(l.widthPct).toBeCloseTo(2 / 12 * 100)
    expect(l.setupPct).toBeCloseTo(100)
  })

  it('закрыто без завершения — до closedAt', () => {
    const l = taskBarLayout({ setupStartedAt: at(1), closedAt: at(3) }, S, E, NOW)
    expect(l.widthPct).toBeCloseTo(2 / 12 * 100)
  })

  it('завершилось до смены — бара нет', () => {
    expect(taskBarLayout({ setupStartedAt: at(-10), startedAt: at(-9), completedAt: at(-1) }, S, E, NOW)).toBeNull()
  })
})
