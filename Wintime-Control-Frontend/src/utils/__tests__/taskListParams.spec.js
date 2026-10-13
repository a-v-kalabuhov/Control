import { describe, it, expect } from 'vitest'
import { buildTaskListParams } from '../taskListParams'

const localIso = (y, m, d, h = 0, min = 0, s = 0, ms = 0) => new Date(y, m - 1, d, h, min, s, ms).toISOString()

describe('buildTaskListParams', () => {
  it('убирает пустые значения', () => {
    expect(buildTaskListParams({ status: null, immId: '', personnelId: undefined, search: '' })).toEqual({})
  })

  it('«Выдано» — включительно до конца дня (пояс браузера)', () => {
    const p = buildTaskListParams({ issuedFrom: '2026-10-13', issuedTo: '2026-10-14' })
    expect(p.issuedFrom).toBe(localIso(2026, 10, 13))
    expect(p.issuedTo).toBe(localIso(2026, 10, 14, 23, 59, 59, 999))
  })

  it('«В работе» — полуинтервал до начала следующего дня', () => {
    const p = buildTaskListParams({ activeFrom: '2026-10-13', activeTo: '2026-10-14' })
    expect(p.activeFrom).toBe(localIso(2026, 10, 13))
    expect(p.activeTo).toBe(localIso(2026, 10, 15))
  })

  it('«Плановая дата» — передаётся датой как есть', () => {
    const p = buildTaskListParams({ plannedFrom: '2026-10-13', plannedTo: '2026-10-20' })
    expect(p).toEqual({ plannedFrom: '2026-10-13', plannedTo: '2026-10-20' })
  })

  it('прочие фильтры передаются без изменений', () => {
    expect(buildTaskListParams({ status: 'Issued', immId: 'imm-1', personnelId: 'u-1' }))
      .toEqual({ status: 'Issued', immId: 'imm-1', personnelId: 'u-1' })
  })
})
