import { describe, expect, it } from 'vitest'
import { formatBytes, formatDateRange, formatMonth } from '@/utils/telemetryAdmin'

describe('telemetry admin formatting', () => {
  it.each([
    [0, '0 Б'], [512, '512 Б'], [1024, '1 КБ'], [1536, '1,5 КБ'],
    [1024 ** 2, '1 МБ'], [1024 ** 3, '1 ГБ'], [1024 ** 4, '1 ТБ']
  ])('formatBytes(%s) = %s', (bytes, expected) => {
    expect(formatBytes(bytes)).toBe(expected)
  })

  it('форматирует локальный диапазон дат включительно', () => {
    expect(formatDateRange({ startDate: '2026-11-23', endDate: '2026-11-29' }))
      .toBe('23.11.2026 — 29.11.2026')
  })

  it('форматирует русский месяц и год', () => {
    expect(formatMonth({ startDate: '2026-11-01' })).toBe('ноябрь 2026')
  })
})
