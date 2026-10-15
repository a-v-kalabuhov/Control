import { describe, it, expect } from 'vitest'
import {
  taskBarLayout, planBarLayout, setupDeviation, setupActualMs, planMarks, planTooltipLines, formatDuration
} from '../shiftTimeline'

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

const MIN = 60_000
const plan = (h, minutes, extra = {}) => ({ plannedStartAt: at(h), plannedSetupMinutes: minutes, ...extra })

describe('formatDuration', () => {
  it('часы и минуты', () => {
    expect(formatDuration(70 * MIN)).toBe('1ч 10м')
    expect(formatDuration(120 * MIN)).toBe('2ч')
    expect(formatDuration(45 * MIN)).toBe('45м')
    expect(formatDuration(0)).toBe('0м')
  })
})

describe('planBarLayout', () => {
  it('окно внутри смены', () => {
    const l = planBarLayout(plan(2, 60), S, E)
    expect(l.leftPct).toBeCloseTo(2 / 12 * 100)
    expect(l.widthPct).toBeCloseTo(1 / 12 * 100)
    expect(l.clippedLeft).toBe(false)
    expect(l.clippedRight).toBe(false)
    expect(l.isMarker).toBe(false)
  })

  it('начинается до смены — обрезка слева', () => {
    const l = planBarLayout(plan(-1, 120), S, E)
    expect(l.leftPct).toBeCloseTo(0)
    expect(l.widthPct).toBeCloseTo(1 / 12 * 100)
    expect(l.clippedLeft).toBe(true)
    expect(l.clippedRight).toBe(false)
  })

  it('заканчивается после смены — обрезка справа', () => {
    const l = planBarLayout(plan(11, 120), S, E)
    expect(l.widthPct).toBeCloseTo(1 / 12 * 100)
    expect(l.clippedRight).toBe(true)
  })

  it('окно вне смены — null', () => {
    expect(planBarLayout(plan(-3, 60), S, E)).toBeNull()
    expect(planBarLayout(plan(-1, 60), S, E)).toBeNull() // кончается ровно в начале смены
    expect(planBarLayout(plan(13, 60), S, E)).toBeNull()
  })

  it('норматив 0 — маркер', () => {
    const l = planBarLayout(plan(3, 0), S, E)
    expect(l.isMarker).toBe(true)
    expect(l.leftPct).toBeCloseTo(25)
    expect(l.widthPct).toBeCloseTo(0)
  })

  it('маркер на границе конца смены — null', () => {
    expect(planBarLayout(plan(12, 0), S, E)).toBeNull()
  })

  it('нет plannedStartAt — null', () => {
    expect(planBarLayout({ plannedSetupMinutes: 30 }, S, E)).toBeNull()
  })
})

describe('setupDeviation', () => {
  it('наладка не началась — null', () => {
    expect(setupDeviation(plan(2, 30))).toBeNull()
  })

  it('опоздание', () => {
    const d = setupDeviation(plan(2, 30, { setupStartedAt: new Date(S.getTime() + 2 * 3_600_000 + 25 * MIN).toISOString() }))
    expect(d).toEqual({ minutes: 25, kind: 'late', label: 'опозд. 25м' })
  })

  it('ранний старт', () => {
    const d = setupDeviation(plan(2, 30, { setupStartedAt: new Date(S.getTime() + 2 * 3_600_000 - 70 * MIN).toISOString() }))
    expect(d).toEqual({ minutes: -70, kind: 'early', label: 'раньше на 1ч 10м' })
  })

  it('|Δ| < 1 мин — вовремя', () => {
    const d = setupDeviation(plan(2, 30, { setupStartedAt: new Date(S.getTime() + 2 * 3_600_000 + 50_000).toISOString() }))
    expect(d).toEqual({ minutes: 0, kind: 'onTime', label: 'вовремя' })
  })
})

describe('setupActualMs', () => {
  it('есть startedAt — разница', () => {
    expect(setupActualMs({ setupStartedAt: at(1), startedAt: at(2) })).toBe(3_600_000)
  })
  it('нет startedAt — null', () => {
    expect(setupActualMs({ setupStartedAt: at(1) })).toBeNull()
  })
})

describe('planMarks', () => {
  it('не началась — только норматив', () => {
    expect(planMarks(plan(2, 45))).toEqual([{ text: 'норм 45м', tone: 'normal' }])
  })

  it('идёт наладка с опозданием — только отклонение', () => {
    const t = plan(2, 45, { setupStartedAt: at(2.5) })
    expect(planMarks(t)).toEqual([{ text: 'опозд. 30м', tone: 'late' }])
  })

  it('ранний старт — серый', () => {
    const t = plan(2, 45, { setupStartedAt: at(1.5) })
    expect(planMarks(t)).toEqual([{ text: 'раньше на 30м', tone: 'muted' }])
  })

  it('наладка завершена — отклонение и факт против норматива', () => {
    const t = plan(2, 45, { setupStartedAt: at(2), startedAt: new Date(S.getTime() + 2 * 3_600_000 + 70 * MIN).toISOString() })
    expect(planMarks(t)).toEqual([
      { text: 'вовремя', tone: 'normal' },
      { text: 'наладка 1ч 10м / норм 45м', tone: 'normal' }
    ])
  })
})

describe('planTooltipLines', () => {
  it('не началась — только плановое окно', () => {
    const lines = planTooltipLines(plan(2, 45))
    expect(lines).toHaveLength(1)
    expect(lines[0]).toMatch(/^План наладки: \d\d:\d\d — \d\d:\d\d \(норм 45м\)$/)
  })

  it('завершена — окно, отклонение, факт', () => {
    const t = plan(2, 45, { setupStartedAt: at(2.5), startedAt: at(3.5) })
    const lines = planTooltipLines(t)
    expect(lines).toHaveLength(3)
    expect(lines[1]).toBe('Начало наладки: опозд. 30м')
    expect(lines[2]).toBe('Факт наладки: 1ч')
  })

  it('нет plannedStartAt — пусто', () => {
    expect(planTooltipLines({})).toEqual([])
  })
})
