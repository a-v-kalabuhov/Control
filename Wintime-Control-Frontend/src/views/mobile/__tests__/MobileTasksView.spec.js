// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import MobileTasksView from '../MobileTasksView.vue'

const task = {
  id: 't1',
  immId: 'imm-1',
  immName: 'ТПА-1',
  moldName: 'ПФ-1',
  planQuantity: 100,
  actualQuantity: 80,
  status: 'InProgress',
  personnelName: 'Иванов',
  issuedAt: '2026-07-01T00:00:00Z'
}

vi.mock('@/api/mobile', () => ({
  mobileApi: {
    getMyTasks: vi.fn(() => Promise.resolve({ data: {
      currentShift: [task],
      unfinished: [],
      archive: { items: [], total: 0, page: 1, pageSize: 20 }
    } })),
    getDowntimeReasons: vi.fn(() => Promise.resolve({ data: [] })),
    completeTask: vi.fn(() => Promise.resolve({ data: {} })),
    startSetup: vi.fn(() => Promise.resolve({ data: {} })),
    completeSetup: vi.fn(() => Promise.resolve({ data: {} })),
    cancelSetup: vi.fn(() => Promise.resolve({ data: {} })),
    addQuantity: vi.fn(() => Promise.resolve({ data: {} })),
    closeTask: vi.fn(() => Promise.resolve({ data: {} })),
    getTaskById: vi.fn(() => Promise.resolve({ data: task }))
  }
}))

vi.mock('@/api/shifts', () => ({
  shiftsApi: {
    getShifts: vi.fn(() => Promise.resolve({ data: [] }))
  }
}))

import { mobileApi } from '@/api/mobile'

describe('MobileTasksView — завершение задания с браком', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  it('диалог завершения содержит поле «Брак» и отправляет defectQuantity', async () => {
    const wrapper = mount(MobileTasksView, {
      global: {
        plugins: [createPinia(), ElementPlus],
        stubs: { QrScanner: true }
      }
    })
    await flushPromises()

    const completeButton = wrapper.findAll('button').find(b => b.text().includes('Завершить') && !b.text().includes('наладку'))
    expect(completeButton).toBeTruthy()
    await completeButton.trigger('click')
    await flushPromises()

    const dialogText = wrapper.text()
    expect(dialogText).toContain('Брак')

    const defectInput = wrapper.find('input[aria-label="Брак"], .complete-defect-input input')
    expect(defectInput.exists()).toBe(true)
    await defectInput.setValue(5)
    await flushPromises()

    const confirmButton = wrapper.findAll('button').find(b => b.text().trim() === 'Завершить' && b.element.closest('.el-dialog'))
    await confirmButton.trigger('click')
    await flushPromises()

    expect(mobileApi.completeTask).toHaveBeenCalledWith(
      't1',
      expect.objectContaining({ defectQuantity: 5 })
    )
  })
})
