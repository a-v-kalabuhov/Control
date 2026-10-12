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
      active: [task],
      queue: [],
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
    // Факт (80) меньше плана (100) — без причины форма не отправится
    await wrapper.find('.el-dialog textarea').setValue('Нет сырья')
    await flushPromises()

    const confirmButton = wrapper.findAll('button').find(b => b.text().trim() === 'Завершить' && b.element.closest('.el-dialog'))
    await confirmButton.trigger('click')
    await flushPromises()

    expect(mobileApi.completeTask).toHaveBeenCalledWith(
      't1',
      expect.objectContaining({ defectQuantity: 5 })
    )
  })

  it('«Брак» max следует за живым значением «Фактическое количество», не за снапшотом задания', async () => {
    const wrapper = mount(MobileTasksView, {
      global: {
        plugins: [createPinia(), ElementPlus],
        stubs: { QrScanner: true }
      }
    })
    await flushPromises()

    const completeButton = wrapper.findAll('button').find(b => b.text().includes('Завершить') && !b.text().includes('наладку'))
    await completeButton.trigger('click')
    await flushPromises()

    const inputNumbers = wrapper.findAllComponents({ name: 'ElInputNumber' })
    expect(inputNumbers.length).toBe(2)
    const [actualQtyField, defectField] = inputNumbers

    // Изначально форма заполнена посчитанным выпуском (80); max «Брака» должен
    // смотреть на живое значение формы, а не на снапшот задания.
    expect(defectField.props('max')).toBe(80)

    await actualQtyField.find('input').setValue(50)
    await actualQtyField.find('input').trigger('change')
    await flushPromises()

    expect(defectField.props('max')).toBe(50)
  })
})

describe('MobileTasksView — предзаполнение факта при завершении', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  async function openCompleteDialog() {
    const wrapper = mount(MobileTasksView, {
      global: {
        plugins: [createPinia(), ElementPlus],
        stubs: { QrScanner: true }
      }
    })
    await flushPromises()
    const completeButton = wrapper.findAll('button').find(b => b.text().includes('Завершить') && !b.text().includes('наладку'))
    await completeButton.trigger('click')
    await flushPromises()
    return wrapper
  }

  it('подставляет посчитанный системой выпуск, свежий с сервера, а не план', async () => {
    mobileApi.getTaskById.mockResolvedValueOnce({ data: { ...task, actualQuantity: 90 } })

    const wrapper = await openCompleteDialog()

    expect(mobileApi.getTaskById).toHaveBeenCalledWith('t1')
    const [actualQtyField] = wrapper.findAllComponents({ name: 'ElInputNumber' })
    expect(actualQtyField.props('modelValue')).toBe(90)
  })

  it('если свежие данные не получены — подставляет выпуск из списка заданий', async () => {
    mobileApi.getTaskById.mockRejectedValueOnce(new Error('network'))

    const wrapper = await openCompleteDialog()

    const [actualQtyField] = wrapper.findAllComponents({ name: 'ElInputNumber' })
    expect(actualQtyField.props('modelValue')).toBe(80)
  })
})

describe('MobileTasksView — очередь по плановому началу (PZP-13)', () => {
  beforeEach(() => { vi.clearAllMocks(); setActivePinia(createPinia()) })

  it('показывает разделы «В работе», «Очередь» и метки', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2026, 9, 15, 12, 0))
    mobileApi.getMyTasks.mockResolvedValueOnce({ data: {
      active: [task],
      queue: [
        { ...task, id: 'q1', status: 'Issued', immName: 'ТПА-2', plannedDate: '2026-10-15', plannedSetupStartTime: '08:00:00',
          plannedStartAt: new Date(2026, 9, 15, 8, 0).toISOString() },
        { ...task, id: 'q2', status: 'Issued', immName: 'ТПА-3', plannedDate: '2026-10-15', plannedSetupStartTime: '18:00:00',
          plannedStartAt: new Date(2026, 9, 15, 18, 0).toISOString() }
      ],
      archive: { items: [], total: 0, page: 1, pageSize: 20 }
    } })

    const wrapper = mount(MobileTasksView, {
      global: { plugins: [createPinia(), ElementPlus], stubs: { QrScanner: true } }
    })
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('В работе')
    expect(text).toContain('Очередь')
    expect(text).toContain('15.10 08:00')
    expect(text).toContain('опаздывает')
    expect(text).toContain('сегодня')
    expect(text).not.toContain('Текущая смена')
    expect(mobileApi.getMyTasks).toHaveBeenCalledWith(expect.not.objectContaining({ boundary: expect.anything() }))
    vi.useRealTimers()
  })
})
