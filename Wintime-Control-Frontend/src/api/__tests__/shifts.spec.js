import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/client', () => ({
  default: { get: vi.fn(), put: vi.fn() }
}))

const { default: apiClient } = await import('@/api/client')
const { shiftsApi } = await import('@/api/shifts')

describe('shiftsApi default leads', () => {
  beforeEach(() => vi.clearAllMocks())

  it('запрашивает настройки старших по умолчанию', () => {
    shiftsApi.getDefaultLeads()

    expect(apiClient.get).toHaveBeenCalledWith('/shifts/default-leads')
  })

  it('отправляет полный набор позиций, включая null', () => {
    const items = [
      { position: 1, shiftLeadId: 'lead-1' },
      { position: 2, shiftLeadId: 'lead-1' },
      { position: 3, shiftLeadId: null }
    ]

    shiftsApi.saveDefaultLeads(items)

    expect(apiClient.put).toHaveBeenCalledWith('/shifts/default-leads', { items })
  })

  it('сохраняет прежний контракт полного редактирования графика', () => {
    const shifts = [{ startMinutes: 480, durationMinutes: 720 }]

    shiftsApi.saveShifts(shifts)

    expect(apiClient.put).toHaveBeenCalledWith('/shifts', { shifts })
  })
})
