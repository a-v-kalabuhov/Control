import { describe, expect, it } from 'vitest'
import { isReadyToIssue, resolveShiftWindow, selectShiftWorkPlan } from '../shiftWorkPlan'

const shift = { number: 1, startMinutes: 8 * 60, durationMinutes: 12 * 60 }
const window = resolveShiftWindow('2026-11-06', shift)

function task(overrides) {
  return {
    id: 'task-1',
    status: 'Planned',
    plannedStartAt: '2026-11-06T08:00:00Z',
    immName: 'ТПА-1',
    ...overrides
  }
}

describe('resolveShiftWindow', () => {
  it('строит границы обычной смены по выбранной дате', () => {
    expect(window.start).toEqual(new Date(2026, 10, 6, 8, 0))
    expect(window.end).toEqual(new Date(2026, 10, 6, 20, 0))
  })

  it('использует часовой пояс завода, а не браузера', () => {
    const utc = resolveShiftWindow('2026-11-06', { ...shift, timeZoneId: 'UTC' })
    const moscow = resolveShiftWindow('2026-11-06', { ...shift, timeZoneId: 'Europe/Moscow' })

    expect(utc.start.toISOString()).toBe('2026-11-06T08:00:00.000Z')
    expect(utc.end.toISOString()).toBe('2026-11-06T20:00:00.000Z')
    expect(moscow.start.toISOString()).toBe('2026-11-06T05:00:00.000Z')
    expect(moscow.end.toISOString()).toBe('2026-11-06T17:00:00.000Z')
  })

  it('переносит конец ночной смены на следующие сутки', () => {
    const night = resolveShiftWindow('2026-11-06', {
      number: 2,
      startMinutes: 20 * 60,
      durationMinutes: 12 * 60
    })

    expect(night.start).toEqual(new Date(2026, 10, 6, 20, 0))
    expect(night.end).toEqual(new Date(2026, 10, 7, 8, 0))
  })
})

describe('selectShiftWorkPlan', () => {
  it('включает плановые и выданные задания с началом внутри смены', () => {
    const planned = task({ id: 'planned', status: 'Planned' })
    const issued = task({ id: 'issued', status: 'Issued', plannedStartAt: new Date(window.end.getTime() - 1).toISOString() })
    const otherShift = task({ id: 'other', plannedStartAt: window.end.toISOString() })

    expect(selectShiftWorkPlan([planned, issued, otherShift], window).map(t => t.id))
      .toEqual(['planned', 'issued'])
  })

  it('включает начатую раньше наладку и выпуск, пересекающие смену', () => {
    const setup = task({
      id: 'setup', status: 'Setup',
      setupStartedAt: '2026-11-06T06:00:00Z',
      plannedStartAt: '2026-11-06T06:00:00Z'
    })
    const production = task({
      id: 'production', status: 'InProgress',
      setupStartedAt: '2026-11-05T20:00:00Z',
      startedAt: '2026-11-05T21:00:00Z',
      plannedStartAt: '2026-11-05T20:00:00Z'
    })

    expect(selectShiftWorkPlan([setup, production], window).map(t => t.id))
      .toEqual(['production', 'setup'])
  })

  it('не включает завершённые, закрытые и непересекающиеся активные задания', () => {
    const completed = task({ id: 'completed', status: 'Completed' })
    const closed = task({ id: 'closed', status: 'Closed' })
    const later = task({
      id: 'later', status: 'Setup',
      setupStartedAt: '2026-11-06T20:00:00Z'
    })

    expect(selectShiftWorkPlan([completed, closed, later], window)).toEqual([])
  })

  it('убирает дубликаты из плановой и активной выборок', () => {
    const item = task({
      id: 'same', status: 'InProgress',
      setupStartedAt: '2026-11-06T07:00:00Z'
    })

    expect(selectShiftWorkPlan([item, { ...item }], window)).toHaveLength(1)
  })
})

describe('isReadyToIssue', () => {
  it('готово только плановое задание с активным назначенным наладчиком', () => {
    const active = new Set(['adjuster-1'])

    expect(isReadyToIssue(task({ personnelId: 'adjuster-1' }), active)).toBe(true)
    expect(isReadyToIssue(task({ status: 'Issued', personnelId: 'adjuster-1' }), active)).toBe(false)
    expect(isReadyToIssue(task({ personnelId: null }), active)).toBe(false)
    expect(isReadyToIssue(task({ personnelId: 'archived' }), active)).toBe(false)
  })
})
