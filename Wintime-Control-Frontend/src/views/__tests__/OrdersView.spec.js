// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import OrdersView from '../OrdersView.vue'

vi.mock('@/api/orders', () => ({
  ordersApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      { id: '1', number: 'ORD-1', productFullArticle: 'A123.BLK', quantity: 100,
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
  productTypesApi: { getList: vi.fn(() => Promise.resolve({ data: [
    { id: 'pt-1', article: 'A123', name: 'Крышка', products: [
      { id: 'p-1', article: 'BLK', fullArticle: 'A123.BLK', name: 'Чёрная', isActive: true }
    ] },
    { id: 'pt-2', article: 'B7', name: 'Ведро', products: [
      { id: 'p-2', article: 'RED', fullArticle: 'B7.RED', name: 'Красное', isActive: true },
      { id: 'p-3', article: 'BLU', fullArticle: 'B7.BLU', name: 'Синее', isActive: true },
      { id: 'p-4', article: 'OLD', fullArticle: 'B7.OLD', name: 'Старое', isActive: false }
    ] }
  ] })) }
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
      { id: '2', number: 'ORD-2', productFullArticle: 'B7.RED', productId: 'p-2', productName: 'Красное',
        productTypeId: 'pt-2', quantity: 100,
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
      { id: '3', number: 'ORD-3', productFullArticle: 'A123.BLK', productId: 'p-1', productName: 'Чёрная',
        productTypeId: 'pt-1', quantity: 100,
        goodQuantity: 40, progressPercent: 40, status: 'Active', dueDate: null },
      { id: '4', number: 'ORD-4', productFullArticle: 'B7.BLU', productId: 'p-3', productName: 'Синее',
        productTypeId: 'pt-2', quantity: 100,
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
      { id: '5', number: 'ORD-5', productFullArticle: 'A123.BLK', productName: 'Чёрная', quantity: 50,
        goodQuantity: 10, progressPercent: 20, status: 'Active', dueDate: '2026-08-01T00:00:00Z',
        orderDate: '2026-07-01', productTypeId: 'pt-1', productId: 'p-1', note: 'старое примечание' }
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

  it('создание заказа без даты проходит и отправляет orderDate: null', async () => {
    const wrapper = mountView([])
    await flushPromises()

    await wrapper.findAll('button').find(b => b.text().includes('Создать заказ')).trigger('click')
    await flushPromises()

    await wrapper.find('input[placeholder="ORD-001"]').setValue('ORD-NODATE')
    wrapper.vm.form.dueDate = '2026-08-01'
    wrapper.vm.form.productTypeId = 'pt-1'
    await flushPromises()
    wrapper.vm.form.productId = 'p-1'
    await flushPromises()

    await wrapper.findAll('button').find(b => b.text() === 'Сохранить').trigger('click')
    await flushPromises()

    expect(ordersApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ number: 'ORD-NODATE', orderDate: null })
    )
  })

  it('очистка даты при редактировании шлёт clearOrderDate: true', async () => {
    const wrapper = mountView([
      { id: '6', number: 'ORD-6', quantity: 5, goodQuantity: 0, progressPercent: 0, status: 'Active',
        dueDate: '2026-08-01T00:00:00Z', orderDate: '2026-07-01', productTypeId: 'pt-1', productId: 'p-1' }
    ])
    await flushPromises()

    await wrapper.findAll('button').find(b => b.text() === 'Редактировать').trigger('click')
    await flushPromises()
    wrapper.vm.form.orderDate = null
    await wrapper.findAll('button').find(b => b.text() === 'Сохранить').trigger('click')
    await flushPromises()

    expect(ordersApi.update).toHaveBeenCalledWith('6',
      expect.objectContaining({ orderDate: null, clearOrderDate: true }))
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
    wrapper.vm.form.productId = 'p-1'
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

  describe('выбор изделия', () => {
    async function openCreate() {
      const wrapper = mountView([])
      await flushPromises()
      await wrapper.findAll('button').find(b => b.text().includes('Создать заказ')).trigger('click')
      await flushPromises()
      return wrapper
    }

    it('поле «Изделие» заблокировано, пока не выбран тип', async () => {
      const wrapper = await openCreate()
      expect(wrapper.findAll('el-select-stub').at(-1).attributes('disabled')).toBe('true')
      wrapper.vm.form.productTypeId = 'pt-1'
      await flushPromises()
      expect(wrapper.findAll('el-select-stub').at(-1).attributes('disabled')).toBe('false')
    })

    it('тип с одним активным изделием подставляет его', async () => {
      const wrapper = await openCreate()
      wrapper.vm.form.productTypeId = 'pt-1'
      await flushPromises()
      expect(wrapper.vm.form.productId).toBe('p-1')
    })

    it('тип с двумя активными изделиями не подставляет, архивное не в опциях', async () => {
      const wrapper = await openCreate()
      wrapper.vm.form.productTypeId = 'pt-2'
      await flushPromises()
      expect(wrapper.vm.form.productId).toBeNull()
      expect(wrapper.vm.productOptions.map(p => p.id)).toEqual(['p-2', 'p-3'])
    })

    it('смена типа сбрасывает изделие', async () => {
      const wrapper = await openCreate()
      wrapper.vm.form.productTypeId = 'pt-1'
      await flushPromises()
      expect(wrapper.vm.form.productId).toBe('p-1')
      wrapper.vm.form.productTypeId = 'pt-2'
      await flushPromises()
      expect(wrapper.vm.form.productId).toBeNull()
    })

    it('создание шлёт productId, а не productTypeId', async () => {
      const wrapper = await openCreate()
      await wrapper.find('input[placeholder="ORD-001"]').setValue('ORD-P')
      wrapper.vm.form.dueDate = '2026-08-01'
      wrapper.vm.form.productTypeId = 'pt-1'
      await flushPromises()
      await wrapper.findAll('button').find(b => b.text() === 'Сохранить').trigger('click')
      await flushPromises()
      const arg = ordersApi.create.mock.calls[0][0]
      expect(arg).toEqual(expect.objectContaining({ productId: 'p-1' }))
      expect(arg).not.toHaveProperty('productTypeId')
    })

    it('колонка «Изделие» показывает полный артикул', async () => {
      const wrapper = mount(OrdersView, {
        global: { plugins: [ElementPlus], stubs: { 'el-progress': true, 'el-select': true, 'el-option': true } }
      })
      await flushPromises()
      expect(wrapper.text()).toContain('A123.BLK')
    })

    it('редактирование заполняет тип и изделие из строки', async () => {
      const wrapper = mountView([
        { id: '7', number: 'ORD-7', quantity: 5, goodQuantity: 0, progressPercent: 0, status: 'Active',
          dueDate: '2026-08-01T00:00:00Z', productTypeId: 'pt-2', productId: 'p-3' }
      ])
      await flushPromises()
      await wrapper.findAll('button').find(b => b.text() === 'Редактировать').trigger('click')
      await flushPromises()
      expect(wrapper.vm.form.productTypeId).toBe('pt-2')
      expect(wrapper.vm.form.productId).toBe('p-3')
    })

    it('редактирование заказа с архивным изделием сохраняет productId и добавляет его в опции', async () => {
      const wrapper = mountView([
        { id: '8', number: 'ORD-8', productFullArticle: 'B7.OLD', productId: 'p-4', productName: 'Старое',
          productTypeId: 'pt-2', quantity: 5, goodQuantity: 0, progressPercent: 0, status: 'Active',
          dueDate: '2026-08-01T00:00:00Z' }
      ])
      await flushPromises()
      await wrapper.findAll('button').find(b => b.text() === 'Редактировать').trigger('click')
      await flushPromises()

      expect(wrapper.vm.form.productId).toBe('p-4')
      // el-select застуб не рендерит слоты, поэтому проверяем список опций, из которого строится шаблон
      expect(wrapper.vm.productOptions).toContainEqual(
        expect.objectContaining({ id: 'p-4', fullArticle: 'B7.OLD', name: 'Старое', isActive: false })
      )
    })
  })
})
