// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import TaskFormModal from '../TaskFormModal.vue'

vi.mock('@/api/imm', () => ({
  immApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) }
}))
vi.mock('@/api/molds', () => ({
  moldsApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      { id: 'mold-1', name: 'Форма 1', cavities: 4, isActive: true, productTypeId: 'pt-X', productTypeArticle: 'ART-X' },
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
    update: vi.fn(() => Promise.resolve({ data: {} })),
    setPersonnel: vi.fn(() => Promise.resolve({ data: {} }))
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
vi.mock('@/api/settings', () => ({
  settingsApi: {
    getPlanningDefaults: vi.fn(() => Promise.resolve({ data: {
      setupMinutes: 30, releaseBufferFixedMinutes: 10, releaseBufferPercent: 10, updatedAt: null
    } }))
  }
}))

import { moldsApi } from '@/api/molds'
import { tasksApi } from '@/api/tasks'
import { ordersApi } from '@/api/orders'
import { settingsApi } from '@/api/settings'

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

  it('при создании: пустой список заказов называет изделие выбранной ПФ', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.moldId = 'mold-1'
    await flushPromises()

    expect(wrapper.vm.orderNoDataText).toBe('Нет активных заказов на изделие ART-X')
  })

  it('при редактировании: пустой список заказов называет изделие ПФ задания', async () => {
    const task = { id: 'task-1', immId: 'imm-1', moldId: 'mold-1', planQuantity: 10, status: 'Planned' }
    const wrapper = mountModal({ task })
    await flushPromises()

    expect(wrapper.vm.orderNoDataText).toBe('Нет активных заказов на изделие ART-X')
  })

  it('пустой список заказов без артикула у ПФ — без пустого места в тексте', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.moldId = 'mold-2'
    await flushPromises()

    expect(wrapper.vm.orderNoDataText).toBe('Нет активных заказов на изделие выбранной пресс-формы')
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
      { id: 'mold-1', name: 'Форма 1', cavities: 4, isActive: true, productTypeId: 'pt-X', productTypeArticle: 'ART-X' }
    ] })

    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.immId = 'imm-1'
    wrapper.vm.form.planQuantity = 10
    wrapper.vm.form.plannedFullCycleSeconds = 60
    wrapper.vm.form.plannedDate = '2099-01-01'
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
      planQuantity: 10, plannedDate: '2026-10-20', plannedSetupStartTime: null, note: '', orderId: null,
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
      planQuantity: 10, plannedDate: '2026-10-20', plannedSetupStartTime: null, note: '', orderId: null
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

  it('при пустом списке ПФ объясняет, что нет форм с типом изделия заказа', async () => {
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    expect(wrapper.vm.moldNoDataText).toBe('Нет активных пресс-форм для типа изделия ART-X')
  })

  it('при пустом списке ПФ без артикула заказа не выводит пустое место', async () => {
    const wrapper = mountModal({ lockedOrder: { ...lockedOrder, productTypeArticle: null } })
    await flushPromises()

    expect(wrapper.vm.moldNoDataText).toBe('Нет активных пресс-форм для типа изделия заказа')
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
    // без заказа — стандартный текст Element Plus
    expect(wrapper.vm.moldNoDataText).toBeUndefined()
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
    wrapper.vm.form.plannedDate = '2099-01-01'
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
    wrapper.vm.form.plannedDate = '2099-01-01'
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
      planQuantity: 10, plannedDate: '2026-10-20', plannedSetupStartTime: null, note: '', orderId: null,
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
      planQuantity: 10, plannedDate: '2026-10-20', plannedSetupStartTime: null, note: '', orderId: null,
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

describe('TaskFormModal — плановое начало и наладчик (PZP-13)', () => {
  beforeEach(() => vi.clearAllMocks())

  const draft = {
    id: 't1', status: 'Planned', immId: 'imm-1', moldId: 'mold-1', personnelId: null,
    planQuantity: 10, plannedDate: '2026-10-20', plannedSetupStartTime: '14:00:00',
    note: '', orderId: null, workMode: 'Auto', plannedFullCycleSeconds: 30, plannedInjectionCycleSeconds: null
  }

  it('без плановой даты форма не отправляется', async () => {
    const wrapper = mountModal()
    await flushPromises()
    Object.assign(wrapper.vm.form, { immId: 'imm-1', moldId: 'mold-1', planQuantity: 5, plannedFullCycleSeconds: 30, plannedDate: null })
    await wrapper.vm.handleSubmit()
    await flushPromises()
    expect(tasksApi.create).not.toHaveBeenCalled()
  })

  it('создание без времени и без наладчика проходит', async () => {
    const wrapper = mountModal()
    await flushPromises()
    Object.assign(wrapper.vm.form, { immId: 'imm-1', moldId: 'mold-1', planQuantity: 5, plannedFullCycleSeconds: 30, plannedDate: '2099-01-01' })
    await wrapper.vm.handleSubmit()
    await flushPromises()
    expect(tasksApi.create).toHaveBeenCalledWith(expect.objectContaining({
      plannedDate: '2099-01-01', plannedSetupStartTime: null, personnelId: null
    }))
  })

  it('очистка времени при правке шлёт clearPlannedSetupStartTime', async () => {
    const wrapper = mountModal({ task: draft })
    await flushPromises()
    wrapper.vm.form.plannedSetupStartTime = null
    await wrapper.vm.handleSubmit()
    await flushPromises()
    expect(tasksApi.update).toHaveBeenCalledWith('t1', expect.objectContaining({ clearPlannedSetupStartTime: true }))
  })

  it('установка времени при правке шлёт время и clearPlannedSetupStartTime: false', async () => {
    const wrapper = mountModal({ task: { ...draft, plannedSetupStartTime: null } })
    await flushPromises()
    wrapper.vm.form.plannedSetupStartTime = '14:00:00'
    await wrapper.vm.handleSubmit()
    await flushPromises()
    expect(tasksApi.update).toHaveBeenCalledWith('t1', expect.objectContaining({
      plannedSetupStartTime: '14:00:00', clearPlannedSetupStartTime: false
    }))
  })

  it('смена наладчика при правке идёт через setPersonnel', async () => {
    const wrapper = mountModal({ task: draft })
    await flushPromises()
    wrapper.vm.form.personnelId = 'adj-9'
    await wrapper.vm.handleSubmit()
    await flushPromises()
    expect(tasksApi.setPersonnel).toHaveBeenCalledWith('t1', 'adj-9')
    expect(tasksApi.update.mock.calls[0][1]).not.toHaveProperty('personnelId')
  })

  it('setPersonnel вызывается раньше update; при отказе update не вызывается', async () => {
    tasksApi.setPersonnel.mockRejectedValueOnce({ response: { data: 'Укажите активного наладчика' } })
    const wrapper = mountModal({ task: draft })
    await flushPromises()
    wrapper.vm.form.personnelId = 'adj-9'
    await wrapper.vm.handleSubmit()
    await flushPromises()
    expect(tasksApi.setPersonnel).toHaveBeenCalled()
    expect(tasksApi.update).not.toHaveBeenCalled()

    const w2 = mountModal({ task: draft })
    await flushPromises()
    w2.vm.form.personnelId = 'adj-8'
    await w2.vm.handleSubmit()
    await flushPromises()
    expect(tasksApi.setPersonnel.mock.invocationCallOrder.at(-1))
      .toBeLessThan(tasksApi.update.mock.invocationCallOrder.at(-1))
  })

  it('в «Выдано» выбор наладчика нельзя очистить', async () => {
    const issued = mountModal({ task: { ...draft, status: 'Issued', personnelId: 'adj-1' } })
    await flushPromises()
    const sel = (w) => w.findAllComponents({ name: 'ElSelect' }).find(s => s.props('placeholder') === 'Не назначен')
    expect(sel(issued).props('clearable')).toBe(false)
    const planned = mountModal({ task: draft })
    await flushPromises()
    expect(sel(planned).props('clearable')).toBe(true)
  })

  it('в «Выдано» заблокированы все поля, кроме даты, времени и наладчика', async () => {
    const wrapper = mountModal({ task: { ...draft, status: 'Issued', personnelId: 'adj-1' } })
    await flushPromises()
    expect(wrapper.vm.isLimitedEdit).toBe(true)
    const planInput = wrapper.findAllComponents({ name: 'ElInputNumber' })[0]
    expect(planInput.props('disabled')).toBe(true)
  })
})

describe('TaskFormModal — сообщение после сохранения', () => {
  beforeEach(() => vi.clearAllMocks())

  it('после создания сообщает, что задание планируется, а не выдано', async () => {
    const success = vi.spyOn(ElMessage, 'success')
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.immId = 'imm-1'
    wrapper.vm.form.planQuantity = 10
    wrapper.vm.form.plannedFullCycleSeconds = 60
    wrapper.vm.form.plannedDate = '2099-01-01'
    await flushPromises()

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.create).toHaveBeenCalled()
    expect(success).toHaveBeenCalledWith('Задание создано в статусе «Планируется». Чтобы наладчик его увидел, нажмите «Выдать»')
  })
})

describe('TaskFormModal — норматив наладки и буфер выпуска (PZP-14)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('при создании предзаполняет поля умолчаниями', async () => {
    const wrapper = mountModal()
    await flushPromises()

    expect(settingsApi.getPlanningDefaults).toHaveBeenCalled()
    expect(wrapper.vm.form.plannedSetupMinutes).toBe(30)
    expect(wrapper.vm.form.releaseBufferFixedMinutes).toBe(10)
    expect(wrapper.vm.form.releaseBufferPercent).toBe(10)
  })

  it('если умолчания не загрузились, поля нормативов остаются null (сервер применит свои)', async () => {
    settingsApi.getPlanningDefaults.mockRejectedValueOnce(new Error('network'))
    const wrapper = mountModal()
    await flushPromises()

    expect(wrapper.vm.form.plannedSetupMinutes).toBeNull()
    expect(wrapper.vm.form.releaseBufferFixedMinutes).toBeNull()
    expect(wrapper.vm.form.releaseBufferPercent).toBeNull()
  })

  it('при редактировании умолчания не запрашивает и берёт значения задания', async () => {
    const wrapper = mountModal({ task: {
      id: 't1', status: 'Planned', immId: 'imm-1', moldId: 'mold-1', planQuantity: 100,
      plannedDate: '2026-10-20', plannedFullCycleSeconds: 60,
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 5, releaseBufferPercent: 20
    } })
    await flushPromises()

    expect(settingsApi.getPlanningDefaults).not.toHaveBeenCalled()
    expect(wrapper.vm.form.plannedSetupMinutes).toBe(45)
    expect(wrapper.vm.form.releaseBufferFixedMinutes).toBe(5)
    expect(wrapper.vm.form.releaseBufferPercent).toBe(20)
  })

  it('показывает предпросмотр выпуска, буфера и итога', async () => {
    const wrapper = mountModal()
    await flushPromises()
    await wrapper.vm.loadMolds()
    wrapper.vm.form.moldId = 'mold-1'          // 4 гнезда
    wrapper.vm.form.planQuantity = 100
    wrapper.vm.form.plannedFullCycleSeconds = 60
    await flushPromises()

    const preview = wrapper.find('[data-test="plan-preview"]').text()
    expect(preview).toContain('Выпуск ≈ 25 мин')
    expect(preview).toContain('буфер ≈ 13 мин')
    expect(preview).toContain('всего с наладкой ≈ 1 ч 8 мин')
  })

  it('в «В работе» доступен только буфер', async () => {
    const wrapper = mountModal({ task: {
      id: 't1', status: 'InProgress', immId: 'imm-1', moldId: 'mold-1', planQuantity: 100,
      plannedDate: '2026-10-20', plannedFullCycleSeconds: 60,
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 5, releaseBufferPercent: 20
    } })
    await flushPromises()

    expect(wrapper.find('[data-test="plan-quantity"]').classes()).toContain('is-disabled')
    expect(wrapper.find('[data-test="setup-minutes"]').classes()).toContain('is-disabled')
    expect(wrapper.find('[data-test="buffer-fixed-minutes"]').classes()).not.toContain('is-disabled')
    expect(wrapper.find('[data-test="buffer-percent"]').classes()).not.toContain('is-disabled')
  })

  it('в «Выдано» доступны и норматив, и буфер', async () => {
    const wrapper = mountModal({ task: {
      id: 't1', status: 'Issued', immId: 'imm-1', moldId: 'mold-1', planQuantity: 100,
      plannedDate: '2026-10-20', plannedFullCycleSeconds: 60, personnelId: 'p1',
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 5, releaseBufferPercent: 20
    } })
    await flushPromises()

    expect(wrapper.find('[data-test="plan-quantity"]').classes()).toContain('is-disabled')
    expect(wrapper.find('[data-test="setup-minutes"]').classes()).not.toContain('is-disabled')
    expect(wrapper.find('[data-test="buffer-fixed-minutes"]').classes()).not.toContain('is-disabled')
  })

  it('при сохранении шлёт норматив и буфер', async () => {
    const wrapper = mountModal({ task: {
      id: 't1', status: 'InProgress', immId: 'imm-1', moldId: 'mold-1', planQuantity: 100,
      plannedDate: '2026-10-20', plannedFullCycleSeconds: 60,
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 5, releaseBufferPercent: 20
    } })
    await flushPromises()
    wrapper.vm.form.releaseBufferFixedMinutes = 40

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.update).toHaveBeenCalledWith('t1', expect.objectContaining({
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 40, releaseBufferPercent: 20
    }))
  })
})
