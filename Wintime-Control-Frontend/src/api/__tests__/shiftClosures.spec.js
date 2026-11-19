import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/client', () => ({
  default: { get: vi.fn(), put: vi.fn(), post: vi.fn(), patch: vi.fn() }
}))

const { default: apiClient } = await import('@/api/client')
const api = await import('@/api/shiftClosures')

describe('shiftClosures API', () => {
  beforeEach(() => vi.clearAllMocks())

  it('загружает рапорт только по materialized ShiftPlanItem', () => {
    api.getByItem('item-1')
    expect(apiClient.get).toHaveBeenCalledWith('/shift-closures/items/item-1')
  })

  it('загружает ожидающие закрытия смены', () => {
    api.getPending()
    expect(apiClient.get).toHaveBeenCalledWith('/shift-closures/pending')
  })

  it('сохраняет только переданные ручные записи черновика', () => {
    const entries = [{ shiftTaskId: 'task-1', actualPartWeightGrams: 4.25 }]
    api.saveDraft('item-1', entries)
    expect(apiClient.put).toHaveBeenCalledWith('/shift-closures/items/item-1/draft', { entries })
  })

  it('закрывает смену с явным признаком подтверждения', () => {
    api.close('item-1', true)
    expect(apiClient.post).toHaveBeenCalledWith('/shift-closures/items/item-1/close', {
      confirmEarlyClose: true
    })
  })

  it('исправляет закрытый рапорт с причиной и записями', () => {
    const entries = [{ shiftTaskId: 'task-1', manualGoodQuantity: 8 }]
    api.correct('closure-1', 'Уточнение старшего', entries)
    expect(apiClient.patch).toHaveBeenCalledWith('/shift-closures/closure-1/corrections', {
      reason: 'Уточнение старшего', entries
    })
  })

  it('загружает историю исправлений', () => {
    api.getCorrections('closure-1')
    expect(apiClient.get).toHaveBeenCalledWith('/shift-closures/closure-1/corrections')
  })

  it('загружает оба PDF как blob', () => {
    api.getBlankPdf('item-1')
    api.getClosedPdf('closure-1')
    expect(apiClient.get).toHaveBeenNthCalledWith(1, '/shift-closures/items/item-1/blank-pdf', {
      responseType: 'blob'
    })
    expect(apiClient.get).toHaveBeenNthCalledWith(2, '/shift-closures/closure-1/pdf', {
      responseType: 'blob'
    })
  })
})
