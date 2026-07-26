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
    update: vi.fn(() => Promise.resolve({ data: {} })),
    complete: vi.fn(() => Promise.resolve({ data: {} })),
    cancel: vi.fn(() => Promise.resolve({ data: {} })),
    reopen: vi.fn(() => Promise.resolve({ data: {} })),
  }
}))
vi.mock('@/api/productTypes', () => ({
  productTypesApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) }
}))

import { ordersApi } from '@/api/orders'

function mountView(orders) {
  ordersApi.getList.mockResolvedValueOnce({ data: orders })
  return mount(OrdersView, {
    global: {
      plugins: [ElementPlus],
      stubs: { 'el-select': true, 'el-option': true }
    }
  })
}

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

  it('ограничивает прогресс-бар 100%, но показывает реальные числа в подписи', async () => {
    const wrapper = mountView([
      { id: '2', number: 'ORD-2', productTypeArticle: 'B', quantity: 100,
        goodQuantity: 130, progressPercent: 130, status: 'Active',
        dueDate: '2026-08-01T00:00:00Z' }
    ])
    await flushPromises()

    const progress = wrapper.findComponent({ name: 'ElProgress' })
    expect(progress.props('percentage')).toBe(100)
    expect(wrapper.text()).toContain('130 / 100')
  })

  it('блокирует «Завершить», если годных меньше плана, и разрешает, если план выполнен', async () => {
    const wrapper = mountView([
      { id: '3', number: 'ORD-3', productTypeArticle: 'C', quantity: 100,
        goodQuantity: 40, progressPercent: 40, status: 'Active', dueDate: null },
      { id: '4', number: 'ORD-4', productTypeArticle: 'D', quantity: 100,
        goodQuantity: 100, progressPercent: 100, status: 'Active', dueDate: null }
    ])
    await flushPromises()

    const buttons = wrapper.findAll('button').filter(b => b.text() === 'Завершить')
    expect(buttons).toHaveLength(2)
    expect(buttons[0].attributes('disabled')).not.toBeUndefined()
    expect(buttons[1].attributes('disabled')).toBeUndefined()
  })

  it('редактирование существующего заказа вызывает ordersApi.update', async () => {
    const wrapper = mountView([
      { id: '5', number: 'ORD-5', productTypeArticle: 'E', quantity: 50,
        goodQuantity: 10, progressPercent: 20, status: 'Active', dueDate: null,
        orderDate: '2026-07-01', productTypeId: 'pt-1', note: 'старое примечание' }
    ])
    await flushPromises()

    const editButton = wrapper.findAll('button').find(b => b.text() === 'Редактировать')
    expect(editButton).toBeTruthy()
    await editButton.trigger('click')
    await flushPromises()

    const saveButton = wrapper.findAll('button').find(b => b.text() === 'Сохранить')
    expect(saveButton).toBeTruthy()
    await saveButton.trigger('click')
    await flushPromises()

    expect(ordersApi.update).toHaveBeenCalledWith('5', expect.objectContaining({ number: 'ORD-5' }))
    expect(ordersApi.create).not.toHaveBeenCalled()
  })

  it('создание заказа отправляет orderDate (а не date) в ordersApi.create', async () => {
    const wrapper = mountView([])
    await flushPromises()

    const createButton = wrapper.findAll('button').find(b => b.text().includes('Создать заказ'))
    expect(createButton).toBeTruthy()
    await createButton.trigger('click')
    await flushPromises()

    await wrapper.find('input[placeholder="ORD-001"]').setValue('ORD-NEW')
    // Дата/срок/тип изделия не заполняем через UI (el-date-picker/el-select застублены) —
    // подставляем значения напрямую через vm, чтобы обойти валидацию формы и проверить payload.
    wrapper.vm.form.orderDate = '2026-07-27'
    wrapper.vm.form.dueDate = '2026-08-01'
    wrapper.vm.form.productTypeId = 'pt-1'
    await flushPromises()

    const saveButton = wrapper.findAll('button').find(b => b.text() === 'Сохранить')
    expect(saveButton).toBeTruthy()
    await saveButton.trigger('click')
    await flushPromises()

    expect(ordersApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ orderDate: '2026-07-27' })
    )
    const callArg = ordersApi.create.mock.calls[0][0]
    expect(callArg.date).toBeUndefined()
  })
})
