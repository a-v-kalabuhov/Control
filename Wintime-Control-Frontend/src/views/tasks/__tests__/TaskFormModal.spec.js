// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import TaskFormModal from '../TaskFormModal.vue'

vi.mock('@/api/imm', () => ({
  immApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) }
}))
vi.mock('@/api/molds', () => ({
  moldsApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      { id: 'mold-1', name: 'Форма 1', cavities: 4, isActive: true, productTypeId: 'pt-X' },
      { id: 'mold-2', name: 'Форма 2', cavities: 2, isActive: true, productTypeId: 'pt-Y' }
    ] }))
  }
}))
vi.mock('@/api/personnel', () => ({
  personnelApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) }
}))
vi.mock('@/api/tasks', () => ({
  tasksApi: {
    create: vi.fn(() => Promise.resolve({ data: {} })),
    update: vi.fn(() => Promise.resolve({ data: {} }))
  }
}))
vi.mock('@/api/orders', () => ({
  ordersApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      { id: 'order-1', number: 'ORD-1' }
    ] })),
    setTaskOrder: vi.fn(() => Promise.resolve({ data: {} }))
  }
}))

import { moldsApi } from '@/api/molds'
import { tasksApi } from '@/api/tasks'
import { ordersApi } from '@/api/orders'

function mountModal(props = {}) {
  return mount(TaskFormModal, {
    props: { modelValue: true, task: null, ...props },
    global: {
      plugins: [ElementPlus]
    }
  })
}

describe('TaskFormModal — заказ', () => {
  beforeEach(() => vi.clearAllMocks())

  it('при выборе ПФ запрашивает активные заказы по её типу изделия', async () => {
    const wrapper = mountModal()
    await flushPromises()

    // имитируем выбор пресс-формы
    await wrapper.vm.loadMolds()
    wrapper.vm.form.moldId = 'mold-1'
    await flushPromises()

    expect(ordersApi.getList).toHaveBeenCalledWith(
      expect.objectContaining({ status: 'Active', productTypeId: 'pt-X' })
    )
  })

  it('при смене ПФ сбрасывает orderId, если заказ больше не входит в список', async () => {
    ordersApi.getList
      .mockResolvedValueOnce({ data: [{ id: 'order-1', number: 'ORD-1' }] })
      .mockResolvedValueOnce({ data: [{ id: 'order-2', number: 'ORD-2' }] })

    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.moldId = 'mold-1'
    await flushPromises()
    wrapper.vm.form.orderId = 'order-1'

    wrapper.vm.form.moldId = 'mold-2'
    await flushPromises()

    expect(wrapper.vm.form.orderId).toBeFalsy()
  })

  it('при создании задания передаёт orderId в tasksApi.create', async () => {
    moldsApi.getList.mockResolvedValueOnce({ data: [
      { id: 'mold-1', name: 'Форма 1', cavities: 4, isActive: true, productTypeId: 'pt-X' }
    ] })

    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.immId = 'imm-1'
    wrapper.vm.form.planQuantity = 10
    await flushPromises()
    wrapper.vm.form.orderId = 'order-1'

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ orderId: 'order-1' })
    )
  })

  it('при редактировании — если orderId изменился, вызывает ordersApi.setTaskOrder', async () => {
    const task = {
      id: 'task-1', immId: 'imm-1', moldId: 'mold-1', personnelId: '',
      planQuantity: 10, plannedDate: null, note: '', orderId: null
    }
    const wrapper = mountModal({ task })
    await wrapper.vm.loadMolds()
    await flushPromises()

    wrapper.vm.form.orderId = 'order-1'
    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(ordersApi.setTaskOrder).toHaveBeenCalledWith('task-1', 'order-1')
  })
})
