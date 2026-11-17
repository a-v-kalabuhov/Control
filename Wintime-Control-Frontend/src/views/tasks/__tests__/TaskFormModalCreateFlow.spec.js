// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import TaskFormModal from '../TaskFormModal.vue'
import { moldsApi } from '@/api/molds'
import { ordersApi } from '@/api/orders'
import { productTypesApi } from '@/api/productTypes'
import { tasksApi } from '@/api/tasks'

const productTypes = [
  { id: 'pt-X', article: 'ART-X', name: 'Тип X', isActive: true, packagingUnit: 24, products: [
    { id: 'p-X0', article: '0', fullArticle: 'ART-X.0', name: 'Основное', isActive: true, cavities: 2, partWeightGrams: 10, plannedInjectionCycleSeconds: 20 }
  ] },
  { id: 'pt-Y', article: 'ART-Y', name: 'Тип Y', isActive: true, packagingUnit: 12, products: [
    { id: 'p-Y0', article: '0', fullArticle: 'ART-Y.0', name: 'Основное', isActive: true, partWeightGrams: 11, plannedInjectionCycleSeconds: 21 },
    { id: 'p-YB', article: 'BLK', fullArticle: 'ART-Y.BLK', name: 'Чёрное', isActive: true, partWeightGrams: 12.5, plannedInjectionCycleSeconds: 25 }
  ] }
]

const molds = [
  { id: 'mold-X-archived', name: 'Архивная X', productTypeId: 'pt-X', cavities: 8, isActive: false },
  { id: 'mold-X-first', name: 'Первая X', productTypeId: 'pt-X', cavities: 6, isActive: true },
  { id: 'mold-X-second', name: 'Вторая X', productTypeId: 'pt-X', cavities: 7, isActive: true },
  { id: 'mold-Y', name: 'Форма Y', productTypeId: 'pt-Y', cavities: 4, isActive: true }
]

const orders = [
  { id: 'order-X', number: 'ORD-X', productId: 'p-X0', productTypeId: 'pt-X', productFullArticle: 'ART-X.0', quantity: 200 },
  { id: 'order-Y', number: 'ORD-Y', productId: 'p-YB', productTypeId: 'pt-Y', productFullArticle: 'ART-Y.BLK', quantity: 80 },
  { id: 'order-Z', number: 'ORD-Z', productId: 'p-X0', productTypeId: 'pt-X', productFullArticle: 'ART-X.0', quantity: 40 }
]

const orderDetails = {
  'order-X': { quantity: 200, tasks: [
    { status: 'Completed', planQuantity: 50, actualQuantity: 30, defectQuantity: 4 },
    { status: 'Closed', planQuantity: 25, actualQuantity: 20, defectQuantity: 0 },
    { status: 'InProgress', planQuantity: 40, actualQuantity: 10, defectQuantity: 1 },
    { status: 'Setup', planQuantity: 10, actualQuantity: 0, defectQuantity: 0 },
    { status: 'Planned', planQuantity: 5, actualQuantity: 0, defectQuantity: 0 },
    { status: 'Issued', planQuantity: 5, actualQuantity: 0, defectQuantity: 0 }
  ] },
  'order-Y': { quantity: 80, tasks: [] },
  'order-Z': { quantity: 40, tasks: [
    { status: 'Completed', planQuantity: 50, actualQuantity: 50, defectQuantity: 0 },
    { status: 'Planned', planQuantity: 10, actualQuantity: 0, defectQuantity: 0 }
  ] }
}

vi.mock('@/api/imm', () => ({ immApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) } }))
vi.mock('@/api/molds', () => ({ moldsApi: { getList: vi.fn(() => Promise.resolve({ data: molds })) } }))
vi.mock('@/api/personnel', () => ({ personnelApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) } }))
vi.mock('@/api/tasks', () => ({
  tasksApi: { create: vi.fn(), update: vi.fn(), setPersonnel: vi.fn() }
}))
vi.mock('@/api/orders', () => ({
  ordersApi: {
    getList: vi.fn(() => Promise.resolve({ data: orders })),
    getById: vi.fn((id) => Promise.resolve({ data: orderDetails[id] })),
    setTaskOrder: vi.fn()
  }
}))
vi.mock('@/api/productTypes', () => ({
  productTypesApi: { getList: vi.fn(() => Promise.resolve({ data: productTypes })), getById: vi.fn() }
}))
vi.mock('@/api/planning', () => ({
  planningApi: { getConflicts: vi.fn(() => Promise.resolve({ data: { items: [] } })) }
}))
vi.mock('@/api/settings', () => ({
  settingsApi: { getPlanningDefaults: vi.fn(() => Promise.resolve({ data: {
    setupMinutes: 30, releaseBufferFixedMinutes: 10, releaseBufferPercent: 10
  } })) }
}))

function mountModal(props = {}) {
  setActivePinia(createPinia())
  return mount(TaskFormModal, {
    props: { modelValue: true, task: null, ...props },
    global: { plugins: [ElementPlus] }
  })
}

function select(wrapper, placeholder) {
  return wrapper.findAllComponents({ name: 'ElSelect' })
    .find(component => component.props('placeholder') === placeholder)
}

async function selectOrder(wrapper, orderId = 'order-X') {
  wrapper.vm.orderOptions = orders
  wrapper.vm.form.orderId = orderId
  await flushPromises()
}

function deferred() {
  let resolve
  const promise = new Promise((res) => { resolve = res })
  return { promise, resolve }
}

describe('TaskFormModal — новая последовательность создания', () => {
  beforeEach(() => vi.clearAllMocks())

  it('заказ и изделие доступны сразу, заказы загружаются без фильтра по изделию', async () => {
    const wrapper = mountModal()
    await flushPromises()
    const orderSelect = select(wrapper, 'Выберите заказ')
    const productSelect = select(wrapper, 'Выберите изделие')

    expect(orderSelect.props('disabled')).toBe(false)
    expect(orderSelect.props('clearable')).toBe(true)
    expect(productSelect.props('disabled')).toBe(false)

    await orderSelect.find('input').trigger('focus')
    await flushPromises()
    expect(ordersApi.getList).toHaveBeenCalledWith({ status: 'Active' })
    expect(wrapper.vm.orderOptions).toEqual(orders)
  })

  it('разрешает выбор ПФ только после изделия, а ТПА — только после ПФ', async () => {
    const wrapper = mountModal()
    await flushPromises()
    const moldSelect = select(wrapper, 'Выберите пресс-форму')
    const immSelect = select(wrapper, 'Выберите ТПА')
    expect(moldSelect.props('disabled')).toBe(true)
    expect(immSelect.props('disabled')).toBe(true)

    wrapper.vm.form.productId = 'p-X0'
    await flushPromises()
    expect(moldSelect.props('disabled')).toBe(false)
    expect(immSelect.props('disabled')).toBe(false)

    wrapper.vm.form.moldId = ''
    await flushPromises()
    expect(moldSelect.props('disabled')).toBe(false)
    expect(immSelect.props('disabled')).toBe(true)

    wrapper.vm.form.moldId = 'mold-X-first'
    await flushPromises()
    wrapper.vm.form.productId = null
    await flushPromises()
    expect(moldSelect.props('disabled')).toBe(true)
    expect(immSelect.props('disabled')).toBe(true)
  })

  it('выбор заказа подставляет его изделие и первую активную подходящую ПФ', async () => {
    const wrapper = mountModal()
    await selectOrder(wrapper)

    expect(wrapper.vm.form.productId).toBe('p-X0')
    expect(wrapper.vm.form.moldId).toBe('mold-X-first')
    expect(wrapper.vm.isProductLocked).toBe(true)
  })

  it('при открытии из карточки заказа применяет все автозаполнения и ограничения', async () => {
    const lockedOrder = {
      id: 'order-X', number: 'ORD-X', productId: 'p-X0', productTypeId: 'pt-X', productFullArticle: 'ART-X.0'
    }
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    expect(wrapper.vm.form.orderId).toBe('order-X')
    expect(wrapper.vm.form.productId).toBe('p-X0')
    expect(wrapper.vm.form.moldId).toBe('mold-X-first')
    expect(wrapper.vm.form.cavities).toBe(2)
    expect(wrapper.vm.form.partWeightGrams).toBe(10)
    expect(wrapper.vm.form.packagingUnit).toBe(24)
    expect(wrapper.vm.form.plannedInjectionCycleSeconds).toBe(20)
    expect(wrapper.vm.form.plannedFullCycleSeconds).toBeNull()
    expect(wrapper.vm.form.planQuantity).toBe(90)
    expect(wrapper.find('[data-test="order-plan-hint"]').text()).toBe('Нужно выпустить 90 из 200 шт.')
    expect(select(wrapper, 'Выберите заказ').props('disabled')).toBe(true)
    expect(select(wrapper, 'Выберите изделие').props('disabled')).toBe(true)
    expect(select(wrapper, 'Выберите пресс-форму').props('disabled')).toBe(false)
    expect(select(wrapper, 'Выберите ТПА').props('disabled')).toBe(false)
    expect(ordersApi.getById).toHaveBeenCalledWith('order-X')
    expect(ordersApi.getList).not.toHaveBeenCalled()
  })

  it('выбор заказа подставляет невыполненную и незапланированную часть в план', async () => {
    const wrapper = mountModal()
    await selectOrder(wrapper)

    // 200 − (30 факт Completed + 20 факт Closed) − (40 + 10 + 5 + 5 план остальных) = 90.
    expect(wrapper.vm.form.planQuantity).toBe(90)
    expect(ordersApi.getById).toHaveBeenCalledWith('order-X')
  })

  it('под заказом показывает остаток выпуска и очищает подсказку при снятии заказа', async () => {
    const wrapper = mountModal()
    await flushPromises()
    const hint = wrapper.find('[data-test="order-plan-hint"]')
    expect(hint.exists()).toBe(true)
    expect(hint.text()).toBe('')

    await selectOrder(wrapper)
    expect(hint.text()).toBe('Нужно выпустить 90 из 200 шт.')
    wrapper.vm.form.orderId = null
    await flushPromises()
    expect(hint.text()).toBe('')
  })

  it('подсказка под заказом показывает нулевой остаток', async () => {
    const wrapper = mountModal()
    await selectOrder(wrapper, 'order-Z')

    const hint = wrapper.find('[data-test="order-plan-hint"]')
    expect(hint.exists()).toBe(true)
    expect(hint.text()).toBe('Нужно выпустить 0 из 40 шт.')
  })

  it('при быстрой смене заказа поздний расчёт не заменяет план нового заказа', async () => {
    const x = deferred()
    const y = deferred()
    ordersApi.getById
      .mockImplementationOnce(() => x.promise)
      .mockImplementationOnce(() => y.promise)
    const wrapper = mountModal()
    wrapper.vm.orderOptions = orders
    wrapper.vm.form.orderId = 'order-X'
    await flushPromises()
    wrapper.vm.form.orderId = 'order-Y'
    await flushPromises()

    y.resolve({ data: orderDetails['order-Y'] })
    await flushPromises()
    expect(wrapper.vm.form.planQuantity).toBe(80)
    x.resolve({ data: orderDetails['order-X'] })
    await flushPromises()
    expect(wrapper.vm.form.planQuantity).toBe(80)
  })

  it('отрицательный остаток заказа ограничен нулём', async () => {
    const wrapper = mountModal()
    await selectOrder(wrapper, 'order-Z')

    expect(wrapper.vm.form.planQuantity).toBe(0)
  })

  it('без заказа выбор изделия подставляет ПФ и параметры, кроме полного цикла', async () => {
    const wrapper = mountModal()
    wrapper.vm.form.productId = 'p-X0'
    await flushPromises()

    expect(wrapper.vm.form.moldId).toBe('mold-X-first')
    expect(wrapper.vm.form.cavities).toBe(2)
    expect(wrapper.findComponent('[data-test="task-cavities"]').props('max')).toBe(6)
    expect(wrapper.vm.form.partWeightGrams).toBe(10)
    expect(wrapper.vm.form.packagingUnit).toBe(24)
    expect(wrapper.vm.form.plannedInjectionCycleSeconds).toBe(20)
    expect(wrapper.vm.form.plannedFullCycleSeconds).toBeNull()
    expect(moldsApi.getList).toHaveBeenCalledWith({ isActive: true })
    expect(productTypesApi.getList).toHaveBeenCalledWith({ isActive: true })
  })

  it('очистка заказа сохраняет изделие и снова разрешает его выбор', async () => {
    const wrapper = mountModal()
    await selectOrder(wrapper)
    wrapper.vm.form.orderId = null
    await flushPromises()

    expect(wrapper.vm.form.productId).toBe('p-X0')
    expect(wrapper.vm.isProductLocked).toBe(false)
    expect(select(wrapper, 'Выберите изделие').props('disabled')).toBe(false)
  })

  it('ручная смена ПФ повторно заполняет параметры и сохраняет ручной полный цикл', async () => {
    const wrapper = mountModal()
    wrapper.vm.form.productId = 'p-X0'
    await flushPromises()
    Object.assign(wrapper.vm.form, {
      cavities: 1,
      partWeightGrams: 99,
      packagingUnit: 99,
      plannedInjectionCycleSeconds: 99,
      plannedFullCycleSeconds: 45
    })

    wrapper.vm.form.moldId = 'mold-X-second'
    await flushPromises()

    expect(wrapper.vm.form.cavities).toBe(2)
    expect(wrapper.findComponent('[data-test="task-cavities"]').props('max')).toBe(7)
    expect(wrapper.vm.form.partWeightGrams).toBe(10)
    expect(wrapper.vm.form.packagingUnit).toBe(24)
    expect(wrapper.vm.form.plannedInjectionCycleSeconds).toBe(20)
    expect(wrapper.vm.form.plannedFullCycleSeconds).toBe(45)
  })

  it('разрешает сохранить гнёзда выше умолчания изделия, но не выше максимума ПФ', async () => {
    tasksApi.create.mockResolvedValueOnce({ data: { id: 'new-task' } })
    const wrapper = mountModal()
    wrapper.vm.form.productId = 'p-X0'
    await flushPromises()
    Object.assign(wrapper.vm.form, {
      immId: 'imm-1', plannedFullCycleSeconds: 60, cavities: 4
    })

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.create).toHaveBeenCalledWith(expect.objectContaining({ cavities: 4 }))
  })

  it('проверка перед сохранением не пропускает гнёзда выше максимума выбранной ПФ', async () => {
    const wrapper = mountModal()
    wrapper.vm.form.productId = 'p-X0'
    await flushPromises()
    let validationError = null

    wrapper.vm.rules.cavities[0].validator(null, 7, error => { validationError = error })

    expect(validationError?.message).toContain('Не больше 6')
    expect(tasksApi.create).not.toHaveBeenCalled()
  })

  it('плановая дата по умолчанию — завтра по локальному календарю', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 10, 18, 12))
    try {
      const wrapper = mountModal()
      await flushPromises()
      expect(wrapper.vm.form.plannedDate).toBe('2026-11-19')
    } finally {
      vi.useRealTimers()
    }
  })
})
