import { describe, it, expect } from 'vitest'
import { calcTaskPlan, formatDuration } from '../taskPlan'

const base = { planQuantity: 100, cavities: 4, fullCycleSeconds: 60, setupMinutes: 30, bufferFixedMinutes: 10, bufferPercent: 10 }

describe('calcTaskPlan', () => {
  it('считает выпуск, буфер и итог как бэкенд', () => {
    expect(calcTaskPlan(base)).toEqual({ setupSeconds: 1800, releaseSeconds: 1500, bufferSeconds: 750, totalSeconds: 4050 })
  })

  it('неполное последнее смыкание округляется вверх', () => {
    expect(calcTaskPlan({ ...base, planQuantity: 101 }).releaseSeconds).toBe(26 * 60)
  })

  it('процентная часть буфера округляется вверх до секунды', () => {
    const plan = calcTaskPlan({ planQuantity: 1, cavities: 1, fullCycleSeconds: 33, setupMinutes: 0, bufferFixedMinutes: 0, bufferPercent: 10 })
    expect(plan.bufferSeconds).toBe(4)
  })

  it('процент берётся только от выпуска, без наладки', () => {
    const plan = calcTaskPlan({ planQuantity: 1, cavities: 1, fullCycleSeconds: 60, setupMinutes: 600, bufferFixedMinutes: 0, bufferPercent: 100 })
    expect(plan.bufferSeconds).toBe(60)
  })

  it.each([
    { fullCycleSeconds: null },
    { cavities: 0 },
    { cavities: undefined }
  ])('без эталона или гнёзд известна только наладка: %o', (patch) => {
    expect(calcTaskPlan({ ...base, ...patch })).toEqual({ setupSeconds: 1800, releaseSeconds: null, bufferSeconds: null, totalSeconds: null })
  })
})

describe('formatDuration', () => {
  it.each([
    [null, '—'],
    [0, '0 мин'],
    [1500, '25 мин'],
    [750, '13 мин'],
    [7200, '2 ч'],
    [4050, '1 ч 8 мин']
  ])('%s с → %s', (seconds, text) => {
    expect(formatDuration(seconds)).toBe(text)
  })
})
