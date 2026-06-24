import { describe, it, expect } from 'vitest'
import { EFFECTIVE_STATUS, EFFECTIVE_STATUS_KEYS, getEffectiveStatusMeta } from '@/constants/effectiveStatus'

describe('EFFECTIVE_STATUS', () => {
  it('содержит ровно 6 состояний', () => {
    expect(EFFECTIVE_STATUS_KEYS).toEqual(
      ['Production', 'Setup', 'Downtime', 'Unplanned', 'NoTask', 'Offline']
    )
  })

  it('у каждого состояния заполнены все поля палитры', () => {
    for (const key of EFFECTIVE_STATUS_KEYS) {
      const m = EFFECTIVE_STATUS[key]
      expect(m.label).toBeTruthy()
      expect(m.bg).toMatch(/^bg-/)
      expect(m.text).toMatch(/^text-/)
      expect(m.dot).toMatch(/^bg-/)
      expect(m.border).toMatch(/^border-/)
      expect(m.hex).toMatch(/^#[0-9a-f]{6}$/i)
    }
  })

  it('getEffectiveStatusMeta откатывается на Offline для неизвестного ключа', () => {
    expect(getEffectiveStatusMeta('???')).toBe(EFFECTIVE_STATUS.Offline)
    expect(getEffectiveStatusMeta(null)).toBe(EFFECTIVE_STATUS.Offline)
  })
})
