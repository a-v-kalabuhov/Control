import { describe, it, expect } from 'vitest'
import { formatRunStatus } from '../unplannedRunFormat'

describe('formatRunStatus', () => {
  it('показывает «Назначено» при наличии задания', () => {
    expect(formatRunStatus({ assignedTaskId: 'x', assignedTaskLabel: 'ПФ · план 50' }))
      .toBe('Назначено: ПФ · план 50')
  })
  it('показывает «Не назначено» без задания', () => {
    expect(formatRunStatus({ assignedTaskId: null })).toBe('Не назначено')
  })
})
