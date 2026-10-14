import { describe, it, expect } from 'vitest'
import { formatForecastMoment } from '../releaseForecast'

describe('releaseForecast', () => {
  const now = new Date(2026, 9, 14, 10, 0)

  it('сегодня → «сегодня в ЧЧ:ММ»', () => {
    expect(formatForecastMoment(new Date(2026, 9, 14, 17, 5), now)).toBe('сегодня в 17:05')
  })

  it('завтра → «завтра в ЧЧ:ММ»', () => {
    expect(formatForecastMoment(new Date(2026, 9, 15, 8, 30), now)).toBe('завтра в 08:30')
  })

  it('завтра через границу года', () => {
    expect(formatForecastMoment(new Date(2027, 0, 1, 0, 15), new Date(2026, 11, 31, 23, 0)))
      .toBe('завтра в 00:15')
  })

  it('послезавтра и дальше → дата и время', () => {
    expect(formatForecastMoment(new Date(2026, 9, 16, 8, 30), now)).toBe('16.10 в 08:30')
  })

  it('другой год → дата с годом', () => {
    expect(formatForecastMoment(new Date(2027, 0, 2, 8, 30), now)).toBe('02.01.2027 в 08:30')
  })
})
