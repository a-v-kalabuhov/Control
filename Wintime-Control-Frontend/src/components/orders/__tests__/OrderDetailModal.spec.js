// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import OrderDetailModal from '../OrderDetailModal.vue'

vi.mock('@/api/orders', () => ({
  ordersApi: {
    getById: vi.fn(() => Promise.resolve({ data: {
      id: '1', number: 'ORD-1', quantity: 100, producedQuantity: 110,
      defectQuantity: 10, goodQuantity: 100, progressPercent: 100, status: 'Active',
      productTypeId: 'pt-1', productTypeArticle: 'A', dueDate: '2026-08-01T00:00:00Z',
      tasks: [
        { taskId: 't1', immName: 'ТПА-1', moldName: 'ПФ-1', planQuantity: 60, actualQuantity: 60, defectQuantity: 0, status: 'Completed' }
      ]
    } })),
    detachTask: vi.fn(() => Promise.resolve({ data: {} })),
    attachTask: vi.fn(() => Promise.resolve({ data: {} })),
  }
}))

vi.mock('@/api/tasks', () => ({
  tasksApi: {
    getList: vi.fn(() => Promise.resolve({ data: [] }))
  }
}))

vi.mock('@/api/molds', () => ({
  moldsApi: {
    getList: vi.fn(() => Promise.resolve({ data: [] }))
  }
}))

import { ordersApi } from '@/api/orders'

describe('OrderDetailModal', () => {
  beforeEach(() => vi.clearAllMocks())

  it('показывает разбивку прогресса и список заданий', async () => {
    const wrapper = mount(OrderDetailModal, {
      props: { modelValue: true, orderId: '1' },
      global: {
        plugins: [ElementPlus],
        stubs: { 'el-progress': true }
      }
    })
    await flushPromises()
    expect(wrapper.text()).toContain('ТПА-1')
    expect(wrapper.text()).toContain('100') // годных
  })

  it('отвязывает задание по кнопке «Отвязать»', async () => {
    const wrapper = mount(OrderDetailModal, {
      props: { modelValue: true, orderId: '1' },
      global: {
        plugins: [ElementPlus],
        stubs: { 'el-progress': true }
      }
    })
    await flushPromises()

    const detachButton = wrapper.findAll('button').find(b => b.text() === 'Отвязать')
    expect(detachButton).toBeTruthy()
    await detachButton.trigger('click')
    await flushPromises()

    expect(ordersApi.detachTask).toHaveBeenCalledWith('1', 't1')
    expect(wrapper.emitted('updated')).toBeTruthy()
  })
})
