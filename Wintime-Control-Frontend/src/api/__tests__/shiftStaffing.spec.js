import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/client', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn() }
}))

const { default: apiClient } = await import('@/api/client')
const { shiftStaffingApi } = await import('@/api/shiftStaffing')

describe('shiftStaffing actual interval API', () => {
  beforeEach(() => vi.clearAllMocks())

  it('assignOperator отправляет ТПА, литейщика и nullable фактические границы', () => {
    const payload = {
      immId: 'imm-1', operatorId: 'op-1',
      assignedAt: '2026-11-18T06:00:00.000Z', unassignedAt: null
    }
    shiftStaffingApi.assignOperator('item-1', payload)

    expect(apiClient.post).toHaveBeenCalledWith('/shift-staffing/items/item-1/operators', payload)
  })

  it('unassignOperator по умолчанию отправляет обязательное пустое тело', () => {
    shiftStaffingApi.unassignOperator('assignment-1')

    expect(apiClient.post).toHaveBeenCalledWith(
      '/shift-staffing/assignments/assignment-1/unassign', {})
  })

  it('unassignOperator отправляет явный unassignedAt', () => {
    shiftStaffingApi.unassignOperator('assignment-1', {
      unassignedAt: '2026-11-18T10:00:00.000Z'
    })

    expect(apiClient.post).toHaveBeenCalledWith(
      '/shift-staffing/assignments/assignment-1/unassign',
      { unassignedAt: '2026-11-18T10:00:00.000Z' })
  })

  it('updateOperatorInterval использует PUT и передаёт обе фактические границы', () => {
    const payload = {
      assignedAt: '2026-11-18T06:30:00.000Z',
      unassignedAt: '2026-11-18T09:30:00.000Z'
    }
    shiftStaffingApi.updateOperatorInterval('assignment-1', payload)

    expect(apiClient.put).toHaveBeenCalledWith(
      '/shift-staffing/assignments/assignment-1/interval', payload)
  })
})
