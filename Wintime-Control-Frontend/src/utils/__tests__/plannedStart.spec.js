import { describe, it, expect } from 'vitest'
import { formatPlannedStart, isPlannedStartOverdue, isPlannedStartToday } from '../plannedStart'

describe('plannedStart', () => {
  it('formatPlannedStart: только дата', () => {
    expect(formatPlannedStart({ plannedDate: '2026-10-15', plannedSetupStartTime: null })).toBe('15.10')
  })

  it('formatPlannedStart: дата и время без секунд', () => {
    expect(formatPlannedStart({ plannedDate: '2026-10-15', plannedSetupStartTime: '14:00:00' })).toBe('15.10 14:00')
  })

  it('formatPlannedStart: нет даты → прочерк', () => {
    expect(formatPlannedStart({})).toBe('—')
  })

  it('isPlannedStartOverdue: выдано и плановое начало прошло', () => {
    const now = new Date('2026-10-15T12:00:00Z')
    expect(isPlannedStartOverdue({ status: 'Issued', plannedStartAt: '2026-10-15T11:00:00Z' }, now)).toBe(true)
    expect(isPlannedStartOverdue({ status: 'Draft', plannedStartAt: '2026-10-15T11:00:00Z' }, now)).toBe(true)
  })

  it('isPlannedStartOverdue: в работе — не просрочено', () => {
    const now = new Date('2026-10-15T12:00:00Z')
    expect(isPlannedStartOverdue({ status: 'InProgress', plannedStartAt: '2026-10-15T11:00:00Z' }, now)).toBe(false)
  })

  it('isPlannedStartOverdue: начало впереди — не просрочено', () => {
    const now = new Date('2026-10-15T12:00:00Z')
    expect(isPlannedStartOverdue({ status: 'Issued', plannedStartAt: '2026-10-15T13:00:00Z' }, now)).toBe(false)
  })

  it('isPlannedStartToday: та же локальная дата', () => {
    const now = new Date(2026, 9, 15, 9, 0)
    expect(isPlannedStartToday({ plannedStartAt: new Date(2026, 9, 15, 18, 0).toISOString() }, now)).toBe(true)
    expect(isPlannedStartToday({ plannedStartAt: new Date(2026, 9, 16, 8, 0).toISOString() }, now)).toBe(false)
  })
})
