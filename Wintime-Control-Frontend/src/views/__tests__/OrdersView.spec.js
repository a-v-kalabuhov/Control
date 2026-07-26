// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import OrdersView from '../OrdersView.vue'

vi.mock('@/api/orders', () => ({
  ordersApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      { id: '1', number: 'ORD-1', productTypeArticle: 'A', quantity: 100,
        goodQuantity: 40, progressPercent: 40, status: 'Active',
        dueDate: '2026-08-01T00:00:00Z' }
    ] })),
    create: vi.fn(() => Promise.resolve({ data: {} })),
    complete: vi.fn(() => Promise.resolve({ data: {} })),
    cancel: vi.fn(() => Promise.resolve({ data: {} })),
  }
}))
vi.mock('@/api/productTypes', () => ({
  productTypesApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) }
}))

describe('OrdersView', () => {
  beforeEach(() => vi.clearAllMocks())

  it('загружает и показывает список заказов с прогрессом', async () => {
    const wrapper = mount(OrdersView, {
      global: {
        plugins: [ElementPlus],
        stubs: { 'el-progress': true, 'el-select': true, 'el-option': true }
      }
    })
    await flushPromises()
    expect(wrapper.text()).toContain('ORD-1')
    expect(wrapper.text()).toContain('40')
  })
})
