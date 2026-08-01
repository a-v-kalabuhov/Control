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
    wrapper.vm.form.plannedFullCycleSeconds = 60
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
      planQuantity: 10, plannedDate: null, note: '', orderId: null,
      plannedFullCycleSeconds: 60
    }
    const wrapper = mountModal({ task })
    await wrapper.vm.loadMolds()
    await flushPromises()

    wrapper.vm.form.orderId = 'order-1'
    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(ordersApi.setTaskOrder).toHaveBeenCalledWith('task-1', 'order-1')
  })

  it('в режиме редактирования сама открывает форму и без ручного loadMolds подгружает заказы по типу изделия задания', async () => {
    const task = {
      id: 'task-2', immId: 'imm-1', moldId: 'mold-1', personnelId: '',
      planQuantity: 10, plannedDate: null, note: '', orderId: null
    }

    // Никакого wrapper.vm.loadMolds() здесь — только открытие формы через prop `task`,
    // как это делает TasksView в реальном режиме редактирования.
    const wrapper = mountModal({ task })
    await flushPromises()

    expect(ordersApi.getList).toHaveBeenCalledWith(
      expect.objectContaining({ status: 'Active', productTypeId: 'pt-X' })
    )
    expect(wrapper.vm.molds.length).toBeGreaterThan(0)
    expect(wrapper.vm.selectedProductTypeId).toBe('pt-X')
  })
})

describe('TaskFormModal — заблокированный заказ (lockedOrder)', () => {
  const lockedOrder = {
    id: 'order-9',
    number: 'ORD-9',
    productTypeId: 'pt-X',
    productTypeArticle: 'ART-X'
  }

  beforeEach(() => vi.clearAllMocks())

  it('подставляет заказ в форму и не запрашивает список заказов', async () => {
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    expect(wrapper.vm.form.orderId).toBe('order-9')
    expect(ordersApi.getList).not.toHaveBeenCalled()
  })

  it('блокирует поле «Заказ» и показывает в нём номер заказа', async () => {
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    // Element Plus (текущая версия) не пишет value/placeholder заказа в атрибуты
    // нативного <input> — отображаемый текст рендерится в соседнем
    // .el-select__placeholder <span>, а сам <input> остаётся пустым. Поэтому вместо
    // поиска input по placeholder="Выберите заказ" (как было в плане) проверяем
    // блокировку и отображаемый текст через обёртку .el-select__wrapper.is-disabled —
    // единственный disabled-селект в этой форме.
    const orderSelect = wrapper.find('.el-select__wrapper.is-disabled')
    expect(orderSelect.exists()).toBe(true)
    expect(orderSelect.find('input').element.disabled).toBe(true)
    expect(orderSelect.text()).toContain('ORD-9')
  })

  it('оставляет в списке ПФ только пресс-формы с типом изделия заказа', async () => {
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    // мок moldsApi отдаёт mold-1 (pt-X) и mold-2 (pt-Y)
    expect(wrapper.vm.availableMolds.map(m => m.id)).toEqual(['mold-1'])
  })

  it('при выборе ПФ не сбрасывает orderId и не грузит заказы', async () => {
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    wrapper.vm.form.moldId = 'mold-1'
    await flushPromises()

    expect(wrapper.vm.form.orderId).toBe('order-9')
    expect(ordersApi.getList).not.toHaveBeenCalled()

    // selectedProductTypeId здесь truthy (moldId выбран), поэтому эта проверка
    // изолированно подтверждает именно ветку !!lockedOrder в :disabled, а не
    // побочный эффект !selectedProductTypeId (см. следующий тест).
    const orderSelect = wrapper.find('.el-select__wrapper.is-disabled')
    expect(orderSelect.exists()).toBe(true)
  })

  it('без пропа список ПФ остаётся полным', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    await flushPromises()

    expect(wrapper.vm.availableMolds.map(m => m.id)).toEqual(['mold-1', 'mold-2'])
  })

  it('без пропа lockedOrder: поле «Заказ» активно после выбора ПФ', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.moldId = 'mold-1'
    await flushPromises()

    expect(wrapper.vm.selectedProductTypeId).toBe('pt-X')
    expect(wrapper.find('.el-select__wrapper.is-disabled').exists()).toBe(false)
  })

  it('создаёт задание с orderId заказа', async () => {
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    wrapper.vm.form.immId = 'imm-1'
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.planQuantity = 10
    wrapper.vm.form.plannedFullCycleSeconds = 60
    await flushPromises()

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ orderId: 'order-9', moldId: 'mold-1' })
    )
  })
})

describe('TaskFormModal — рабочий режим и эталоны цикла', () => {
  beforeEach(() => vi.clearAllMocks())

  it('по умолчанию режим «автомат»', async () => {
    const wrapper = mountModal()
    await flushPromises()

    expect(wrapper.vm.form.workMode).toBe('Auto')
  })

  it('передаёт режим и эталоны в tasksApi.create', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.immId = 'imm-1'
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.planQuantity = 10
    wrapper.vm.form.workMode = 'SemiAuto'
    wrapper.vm.form.plannedFullCycleSeconds = 60
    wrapper.vm.form.plannedInjectionCycleSeconds = 25
    await flushPromises()

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.create).toHaveBeenCalledWith(
      expect.objectContaining({
        workMode: 'SemiAuto',
        plannedFullCycleSeconds: 60,
        plannedInjectionCycleSeconds: 25
      })
    )
  })

  it('не отправляет форму без эталона полного цикла', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.immId = 'imm-1'
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.planQuantity = 10
    wrapper.vm.form.plannedFullCycleSeconds = null
    await flushPromises()

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.create).not.toHaveBeenCalled()
  })

  it('не отправляет форму, если цикл литья больше полного цикла', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.immId = 'imm-1'
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.planQuantity = 10
    wrapper.vm.form.plannedFullCycleSeconds = 60
    wrapper.vm.form.plannedInjectionCycleSeconds = 61
    await flushPromises()

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.create).not.toHaveBeenCalled()
  })

  // Находка 3: бэкенд не требует эталон при редактировании legacy-заданий
  // (UpdateTask_LegacyTaskWithoutFullCycle_SavesWithoutRequiringCycle). Форма не
  // должна блокировать сохранение сильнее бэкенда — иначе менеджер не может
  // поправить старое задание, не выдумав цифру эталона.
  it('при редактировании legacy-задания (plannedFullCycleSeconds: null) сохраняет без заполнения эталона', async () => {
    const task = {
      id: 'task-legacy', immId: 'imm-1', moldId: 'mold-1', personnelId: '',
      planQuantity: 10, plannedDate: null, note: '', orderId: null,
      plannedFullCycleSeconds: null
    }
    const wrapper = mountModal({ task })
    await flushPromises()

    expect(wrapper.vm.form.plannedFullCycleSeconds).toBeNull()

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.update).toHaveBeenCalledWith(
      'task-legacy',
      expect.objectContaining({ plannedFullCycleSeconds: null })
    )
  })

  it('при редактировании заполняет поля из задания', async () => {
    const task = {
      id: 'task-3', immId: 'imm-1', moldId: 'mold-1', personnelId: '',
      planQuantity: 10, plannedDate: null, note: '', orderId: null,
      workMode: 'SemiAuto', plannedFullCycleSeconds: 90,
      plannedInjectionCycleSeconds: 30
    }
    const wrapper = mountModal({ task })
    await flushPromises()

    expect(wrapper.vm.form.workMode).toBe('SemiAuto')
    expect(wrapper.vm.form.plannedFullCycleSeconds).toBe(90)
    expect(wrapper.vm.form.plannedInjectionCycleSeconds).toBe(30)
  })
})
