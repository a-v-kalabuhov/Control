import { describe, it, expect } from 'vitest'
import { shiftWindow } from '../telemetryWindow'

const MIN = 60_000
const T = Date.UTC(2026, 9, 1, 10, 0, 0) // 10:00
const NOW = T + 24 * 60 * MIN             // «сейчас» далеко справа

describe('shiftWindow', () => {
  it('◀ сдвигает на треть окна, длина сохраняется', () => {
    expect(shiftWindow(T, T + 60 * MIN, -1, NOW)).toEqual({ fromMs: T - 20 * MIN, toMs: T + 40 * MIN })
  })

  it('▶ сдвигает на треть окна, длина сохраняется', () => {
    expect(shiftWindow(T, T + 60 * MIN, 1, NOW)).toEqual({ fromMs: T + 20 * MIN, toMs: T + 80 * MIN })
  })

  it('шаг округляется вниз до миллисекунды', () => {
    expect(shiftWindow(0, 100, -1, NOW)).toEqual({ fromMs: -33, toMs: 67 })
  })

  it('▶ прижимает конец к «сейчас», если шаг уводит в будущее', () => {
    const now = T + 70 * MIN
    expect(shiftWindow(T, T + 60 * MIN, 1, now)).toEqual({ fromMs: T + 10 * MIN, toMs: now })
  })

  it('▶ невозможен, если конец уже в «сейчас» или в будущем', () => {
    expect(shiftWindow(T, T + 60 * MIN, 1, T + 60 * MIN)).toBeNull()
    expect(shiftWindow(T, T + 60 * MIN, 1, T + 30 * MIN)).toBeNull()
  })

  it('◀ не ограничен «сейчас»', () => {
    expect(shiftWindow(T, T + 60 * MIN, -1, T)).toEqual({ fromMs: T - 20 * MIN, toMs: T + 40 * MIN })
  })

  it('некорректное окно → null', () => {
    expect(shiftWindow(T, T, -1, NOW)).toBeNull()
    expect(shiftWindow(T + MIN, T, 1, NOW)).toBeNull()
    expect(shiftWindow(NaN, T, -1, NOW)).toBeNull()
  })
})
