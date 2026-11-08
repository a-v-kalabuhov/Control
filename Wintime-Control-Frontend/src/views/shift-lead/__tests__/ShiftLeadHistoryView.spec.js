// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import ShiftLeadHistoryView from '../ShiftLeadHistoryView.vue'
import { shiftStaffingApi } from '@/api/shiftStaffing'
import { useShiftStaffingStore } from '@/stores/shiftStaffing'

const { push } = vi.hoisted(() => ({ push: vi.fn() }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('@/api/shiftStaffing', () => ({
  shiftStaffingApi: {
    nearest: vi.fn(), history: vi.fn(), item: vi.fn(), task: vi.fn(),
    operators: vi.fn(), assign: vi.fn(), unassign: vi.fn()
  }
}))

function summary(itemId, overrides = {}) {
  return {
    itemId, shiftDate: '2026-11-08', position: 1,
    startAtUtc: '2026-11-08T08:00:00Z', endAtUtc: '2026-11-08T20:00:00Z',
    timeZoneId: 'Europe/Moscow', state: 'Historical', canEditOperators: false,
    ...overrides
  }
}

function deferred() {
  let resolve
  const promise = new Promise(res => { resolve = res })
  return { promise, resolve }
}

describe('ShiftLeadHistoryView', () => {
  let wrapper

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
  })

  it('загружает только history, показывает серверный список без future-подмешивания и редактирования', async () => {
    const request = deferred()
    shiftStaffingApi.history.mockReturnValue(request.promise)
    wrapper = mount(ShiftLeadHistoryView, { global: { plugins: [ElementPlus] } })
    await nextTick()

    expect(shiftStaffingApi.history).toHaveBeenCalledTimes(1)
    expect(shiftStaffingApi.nearest).not.toHaveBeenCalled()
    expect(wrapper.find('.el-loading-mask').exists()).toBe(true)

    request.resolve({
      data: [
        summary('history-2', { position: 2 }),
        summary('history-1', { shiftDate: '2026-11-07' }),
        summary('future-1', { shiftDate: '2026-11-09', state: 'Future' })
      ]
    })
    await flushPromises()

    const rows = wrapper.findAll('[data-test^="history-item-"]')
    expect(rows.map(row => row.attributes('data-test'))).toEqual([
      'history-item-history-2', 'history-item-history-1', 'history-item-future-1'
    ])
    expect(useShiftStaffingStore().historyLoading).toBe(false)
    expect(wrapper.text()).not.toContain('Назначить')
    expect(wrapper.text()).not.toContain('Снять')
    expect(shiftStaffingApi.item).not.toHaveBeenCalled()
    expect(shiftStaffingApi.operators).not.toHaveBeenCalled()

    await rows[0].trigger('click')
    expect(push).toHaveBeenCalledWith('/shift-lead/items/history-2')
    expect(shiftStaffingApi.item).not.toHaveBeenCalled()
  })
})
