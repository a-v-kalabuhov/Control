import { describe, it, expect } from 'vitest'
import { formatSeconds, sharePercent, deviationPercent, formatDeviation } from '../taskResults'

describe('taskResults formatters', () => {
  it('formatSeconds: одна цифра после точки, null → «—»', () => {
    expect(formatSeconds(12.84)).toBe('12.8 с')
    expect(formatSeconds(60)).toBe('60.0 с')
    expect(formatSeconds(null)).toBe('—')
    expect(formatSeconds(undefined)).toBe('—')
  })

  it('sharePercent: округлённый процент, пустое окно → null', () => {
    expect(sharePercent(1200, 3600)).toBe(33)
    expect(sharePercent(0, 3600)).toBe(0)
    expect(sharePercent(10, 0)).toBeNull()
  })

  it('deviationPercent: (факт − эталон) / эталон, без факта или эталона → null', () => {
    expect(deviationPercent(12.8, 55)).toBe(-77)
    expect(deviationPercent(66, 60)).toBe(10)
    expect(deviationPercent(null, 60)).toBeNull()
    expect(deviationPercent(12, null)).toBeNull()
    expect(deviationPercent(12, 0)).toBeNull()
  })

  it('formatDeviation: знак и пробел перед %, null → «—»', () => {
    expect(formatDeviation(-77)).toBe('−77 %')
    expect(formatDeviation(10)).toBe('+10 %')
    expect(formatDeviation(0)).toBe('0 %')
    expect(formatDeviation(null)).toBe('—')
  })
})
