import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/client', () => ({
  default: { get: vi.fn(), put: vi.fn() }
}))

const { default: apiClient } = await import('@/api/client')
const { shiftPlansApi } = await import('@/api/shiftPlans')

describe('shiftPlansApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('запрашивает диапазон дат', () => {
    shiftPlansApi.getRange('2026-11-08', '2026-11-10')
    expect(apiClient.get).toHaveBeenCalledWith('/shift-plan', {
      params: { from: '2026-11-08', to: '2026-11-10' }
    })
  })

  it('запрашивает кандидатов без клиентской фильтрации', () => {
    shiftPlansApi.getLeadCandidates()
    expect(apiClient.get).toHaveBeenCalledWith('/shift-plan/lead-candidates')
  })

  it.each(['lead-1', null])('сохраняет старшего %s по дате и позиции', shiftLeadId => {
    shiftPlansApi.setLead('2026-11-10', 2, shiftLeadId)
    expect(apiClient.put).toHaveBeenCalledWith('/shift-plan/days/2026-11-10/items/2/lead', {
      shiftLeadId
    })
  })
})
