// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { useShiftStaffingStore } from '@/stores/shiftStaffing'
import dayjs from 'dayjs'
import utc from 'dayjs/plugin/utc'
import timezone from 'dayjs/plugin/timezone'
import ShiftStaffingItemView from '../ShiftStaffingItemView.vue'
import { shiftStaffingApi } from '@/api/shiftStaffing'
import { personnelApi } from '@/api/personnel'
import * as shiftClosuresApi from '@/api/shiftClosures'

const { route, push } = vi.hoisted(() => ({
  route: { params: { itemId: 'item-1' } },
  push: vi.fn()
}))
vi.mock('vue-router', async () => {
  const { reactive } = await import('vue')
  const state = reactive(route)
  return {
    useRoute: () => state,
    useRouter: () => ({ push })
  }
})
vi.mock('@/api/shiftStaffing', () => ({
  shiftStaffingApi: {
    nearest: vi.fn(), history: vi.fn(), item: vi.fn(), task: vi.fn(),
    operators: vi.fn(), assignOperator: vi.fn(), unassignOperator: vi.fn(), updateOperatorInterval: vi.fn()
  }
}))
vi.mock('@/api/personnel', () => ({ personnelApi: { getAll: vi.fn(), getPersonnel: vi.fn() } }))
vi.mock('@/api/shiftClosures', () => ({
  getByItem: vi.fn(), saveDraft: vi.fn(), close: vi.fn()
}))

dayjs.extend(utc)
dayjs.extend(timezone)

function summary(overrides = {}) {
  return {
    itemId: 'item-1', shiftDate: '2026-11-08', position: 1,
    startAtUtc: '2026-11-08T08:00:00Z', endAtUtc: '2026-11-08T20:00:00Z',
    timeZoneId: 'Asia/Kamchatka', state: 'Current', canEditOperators: true,
    ...overrides
  }
}

function assignment(id, operatorId, name, overrides = {}) {
  return {
    id, immId: 'imm-1', operatorId, operatorName: name,
    operatorEmployeeId: `EMP-${operatorId}`, slot: 1,
    assignedAt: '2026-11-08T09:00:00Z', unassignedAt: null,
    workStart: '2026-11-08T09:00:00Z', workEnd: '2026-11-08T20:00:00Z',
    isWorkIntervalEmpty: false, ...overrides
  }
}

function task(overrides = {}) {
  return {
    id: 'task-1', immId: 'imm-1', productFullArticle: 'ТИП.001',
    productName: 'Корпус', moldName: 'ПФ-1', planQuantity: 100,
    actualQuantity: 25, status: 'InProgress', ...overrides
  }
}

function detail({
  assignments = [assignment('a1', 'op1', 'Иванов')],
  tasks = [task()],
  shift = summary(),
  secondImm = false
} = {}) {
  const imms = [{
    immId: 'imm-1', name: 'ТПА-1', inventoryNumber: 'ИНВ-1', isActive: true,
    effectiveStatus: tasks.length ? 'Production' : 'NoTask', tasks,
    activeOperatorCount: shift.state === 'Historical'
      ? 0
      : assignments.filter(item => !item.unassignedAt).length,
    assignments
  }]
  if (secondImm) {
    imms.push({
      immId: 'imm-2', name: 'ТПА-2', inventoryNumber: 'ИНВ-2', isActive: true,
      effectiveStatus: 'NoTask', tasks: [], activeOperatorCount: 1,
      assignments: [assignment('a-other', 'op2', 'Петров', { immId: 'imm-2' })]
    })
  }
  return { shift, shiftLeadId: 'lead-1', shiftLeadName: 'Старший Смены', imms }
}

function closureReport(overrides = {}) {
  return {
    id: 'closure-1', shiftPlanItemId: 'item-1', state: 'Draft',
    shiftDate: '2026-11-08', startAtUtc: '2026-11-08T08:00:00Z', endAtUtc: '2026-11-08T20:00:00Z',
    entries: [], timelines: [], ...overrides
  }
}

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

function mountView() {
  return mount(ShiftStaffingItemView, { global: { plugins: [ElementPlus] } })
}

async function chooseOperator(wrapper, operatorId) {
  await wrapper.find('[data-test="assign-imm-1"]').trigger('click')
  await flushPromises()
  const select = wrapper.findComponent('[data-test="operator-select"]')
  await select.vm.$emit('update:modelValue', operatorId)
  await wrapper.find('[data-test="submit-interval"]').trigger('click')
  await flushPromises()
}

async function confirmUnassign(wrapper, assignmentId) {
  await wrapper.find(`[data-test="unassign-${assignmentId}"]`).trigger('click')
  await wrapper.find('[data-test="submit-interval"]').trigger('click')
  await flushPromises()
}

describe('ShiftStaffingItemView', () => {
  let wrapper
  let errorMessage

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-11-08T12:00:00Z'))
    setActivePinia(createPinia())
    vi.clearAllMocks()
    useRoute().params.itemId = 'item-1'
    shiftStaffingApi.item.mockResolvedValue({ data: detail() })
    shiftClosuresApi.getByItem.mockResolvedValue({ data: closureReport() })
    shiftStaffingApi.operators.mockResolvedValue({
      data: [
        { id: 'op1', fullName: 'Иванов', employeeId: '1' },
        { id: 'op2', fullName: 'Петров', employeeId: '2' },
        { id: 'op3', fullName: 'Без Логина', employeeId: null },
        { id: 'op4', fullName: 'Свободный', employeeId: '4' }
      ]
    })
    errorMessage = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('показывает активные и снятые интервалы, browser timezone, active count и future-пояснение', async () => {
    vi.setSystemTime(new Date('2026-11-08T07:00:00Z'))
    const removed = assignment('a2', 'op2', 'Петров', {
      slot: 2, assignedAt: '2026-11-08T08:30:00Z', unassignedAt: '2026-11-08T10:15:00Z'
    })
    shiftStaffingApi.item.mockResolvedValue({ data: detail({ assignments: [assignment('a1', 'op1', 'Иванов'), removed] }) })
    wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Иванов')
    expect(wrapper.text()).toContain('Петров')
    expect(wrapper.text()).toContain(dayjs('2026-11-08T09:00:00Z').format('DD.MM.YYYY HH:mm'))
    expect(wrapper.text()).toContain(dayjs('2026-11-08T10:15:00Z').format('DD.MM.YYYY HH:mm'))
    expect(wrapper.text()).toContain('1/2')
    expect(wrapper.text()).toContain('Работа учитывается с начала смены')
    expect(wrapper.text()).not.toContain(dayjs('2026-11-08T09:00:00Z').tz('Asia/Kamchatka').format('DD.MM.YYYY HH:mm'))
  })

  it('два активных не блокируют произвольный интервал; снятие и повторное назначение создают новые mutations', async () => {
    const first = assignment('a1', 'op1', 'Иванов')
    const second = assignment('a2', 'op2', 'Петров', { slot: 2 })
    shiftStaffingApi.item.mockResolvedValue({ data: detail({ assignments: [first, second] }) })
    wrapper = mountView()
    await flushPromises()
    expect(wrapper.find('[data-test="assign-imm-1"]').exists()).toBe(true)

    shiftStaffingApi.unassignOperator.mockResolvedValue({ data: {} })
    shiftStaffingApi.item.mockResolvedValueOnce({ data: detail({ assignments: [first, { ...second, unassignedAt: '2026-11-08T12:10:00Z' }] }) })
    await confirmUnassign(wrapper, 'a2')
    expect(shiftStaffingApi.unassignOperator).toHaveBeenCalledWith('a2', {})
    expect(wrapper.find('[data-test="assign-imm-1"]').exists()).toBe(true)

    const assigned = assignment('a3', 'op3', 'Без Логина', { slot: 2 })
    shiftStaffingApi.assignOperator.mockResolvedValue({ data: assigned })
    shiftStaffingApi.item.mockResolvedValueOnce({ data: detail({ assignments: [first, assigned] }) })
    await chooseOperator(wrapper, 'op3')
    expect(shiftStaffingApi.assignOperator).toHaveBeenCalledWith('item-1', {
      immId: 'imm-1', operatorId: 'op3', assignedAt: null, unassignedAt: null
    })

    shiftStaffingApi.item.mockResolvedValueOnce({ data: detail({ assignments: [first, { ...assigned, unassignedAt: '2026-11-08T12:20:00Z' }] }) })
    await confirmUnassign(wrapper, 'a3')
    const reassigned = assignment('a4', 'op3', 'Без Логина', { slot: 2 })
    shiftStaffingApi.item.mockResolvedValueOnce({ data: detail({ assignments: [first, reassigned] }) })
    await chooseOperator(wrapper, 'op3')
    expect(shiftStaffingApi.assignOperator).toHaveBeenCalledTimes(2)
  })

  it('ТПА без задания разрешает назначение через shiftStaffingApi', async () => {
    shiftStaffingApi.item.mockResolvedValue({ data: detail({ tasks: [], assignments: [] }) })
    shiftStaffingApi.assignOperator.mockResolvedValue({ data: {} })
    wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Задание отсутствует')
    await chooseOperator(wrapper, 'op3')
    expect(shiftStaffingApi.assignOperator).toHaveBeenCalledWith('item-1', {
      immId: 'imm-1', operatorId: 'op3', assignedAt: null, unassignedAt: null
    })
    expect(personnelApi.getAll).not.toHaveBeenCalled()
    expect(personnelApi.getPersonnel).not.toHaveBeenCalled()
  })

  it('кандидаты включают всех активных литейщиков: непересекающиеся интервалы проверяет сервер', async () => {
    shiftStaffingApi.item.mockResolvedValue({ data: detail({ secondImm: true }) })
    wrapper = mountView()
    await flushPromises()

    await wrapper.find('[data-test="assign-imm-1"]').trigger('click')
    await flushPromises()
    const values = wrapper.findAllComponents({ name: 'ElOption' }).map(option => option.props('value'))

    expect(values).toContain('op1')
    expect(values).toContain('op2')
    expect(values).toContain('op3')
    expect(values).toContain('op4')
  })

  it('canEditOperators=false убирает все controls у активных и исторических интервалов', async () => {
    const removed = assignment('a2', 'op2', 'Петров', { unassignedAt: '2026-11-08T10:00:00Z' })
    shiftStaffingApi.item.mockResolvedValue({
      data: detail({
        assignments: [assignment('a1', 'op1', 'Иванов'), removed],
        shift: summary({ state: 'Historical', canEditOperators: false })
      })
    })
    wrapper = mountView()
    await flushPromises()

    expect(wrapper.find('[data-test^="unassign-"]').exists()).toBe(false)
    expect(wrapper.find('[data-test^="assign-"]').exists()).toBe(false)
    expect(wrapper.find('[data-test^="edit-"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Иванов')
    expect(wrapper.text()).toContain('Петров')
  })

  it('в исторической смене неснятое назначение не отображается активным', async () => {
    shiftStaffingApi.item.mockResolvedValue({
      data: detail({ shift: summary({ state: 'Historical', canEditOperators: false }) })
    })
    wrapper = mountView()
    await flushPromises()

    const row = wrapper.get('[data-test="assignment-a1"]')
    expect(wrapper.text()).toContain('Литейщики: 0/2')
    expect(row.text()).toContain('До конца смены')
    expect(row.text()).not.toContain('место')
    expect(row.text()).not.toContain('Активен')
    expect(row.classes()).toContain('bg-gray-50')
    expect(row.classes()).not.toContain('bg-primary-50')
  })

  it('после 409 показывает сообщение, перезагружает item и применяет server read-only', async () => {
    shiftStaffingApi.unassignOperator.mockRejectedValue({
      response: { status: 409, data: { message: 'Состав литейщиков уже изменился' } }
    })
    shiftStaffingApi.item
      .mockResolvedValueOnce({ data: detail() })
      .mockResolvedValueOnce({
        data: detail({ shift: summary({ state: 'Historical', canEditOperators: false }) })
      })
    wrapper = mountView()
    await flushPromises()

    await confirmUnassign(wrapper, 'a1')

    expect(errorMessage).toHaveBeenCalledWith('Состав литейщиков уже изменился')
    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(2)
    expect(wrapper.find('[data-test^="unassign-"]').exists()).toBe(false)
    expect(wrapper.find('[data-test^="assign-"]').exists()).toBe(false)
  })

  it('pending mutation блокирует повторные mutations', async () => {
    const request = deferred()
    const first = assignment('a1', 'op1', 'Иванов')
    const second = assignment('a2', 'op2', 'Петров', { slot: 2 })
    shiftStaffingApi.item.mockResolvedValue({ data: detail({ assignments: [first, second] }) })
    shiftStaffingApi.unassignOperator.mockReturnValue(request.promise)
    wrapper = mountView()
    await flushPromises()

    await wrapper.find('[data-test="unassign-a1"]').trigger('click')
    await wrapper.find('[data-test="submit-interval"]').trigger('click')
    await wrapper.find('[data-test="submit-interval"]').trigger('click')
    expect(shiftStaffingApi.unassignOperator).toHaveBeenCalledTimes(1)

    request.resolve({ data: {} })
    await flushPromises()
  })

  it('всегда запускает собственный polling при переходе из Home', async () => {
    vi.useRealTimers()
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval', 'setTimeout', 'clearTimeout'] })
    vi.setSystemTime(new Date('2026-11-08T12:00:00Z'))
    const store = useShiftStaffingStore()
    store.nearest = summary()
    store.currentItem = detail()

    wrapper = mountView()
    await flushPromises()
    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(60_000)
    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(2)
  })

  it('forced navigation Item A отменяет in-flight Home nearest B', async () => {
    const store = useShiftStaffingStore()
    store.nearest = summary()
    store.currentItem = detail()
    const nearestRequest = deferred()
    shiftStaffingApi.nearest.mockReturnValue(nearestRequest.promise)
    const pendingNearest = store.loadNearest()
    shiftStaffingApi.item.mockImplementation(async itemId => ({
      data: detail({ shift: summary({ itemId }) })
    }))

    wrapper = mountView()
    await flushPromises()
    nearestRequest.resolve({ data: { item: summary({ itemId: 'item-b' }) } })
    await pendingNearest
    await flushPromises()

    expect(store.currentItem.shift.itemId).toBe('item-1')
    expect(shiftStaffingApi.item).not.toHaveBeenCalledWith('item-b')
  })

  it('ActiveOperatorCount=2 не является client gate: submit отправляется, занятость проверяет сервер', async () => {
    wrapper = mountView()
    await flushPromises()
    await wrapper.find('[data-test="assign-imm-1"]').trigger('click')
    await flushPromises()
    const select = wrapper.findComponent('[data-test="operator-select"]')
    await select.vm.$emit('update:modelValue', 'op3')

    const store = useShiftStaffingStore()
    store.currentItem = detail({
      assignments: [
        assignment('a1', 'op1', 'Иванов'),
        assignment('a2', 'op2', 'Петров', { slot: 2 })
      ]
    })
    await nextTick()
    shiftStaffingApi.assignOperator.mockResolvedValue({ data: {} })

    const confirm = wrapper.find('[data-test="submit-interval"]')
    expect(confirm.attributes('disabled')).toBeUndefined()
    await confirm.trigger('click')
    await flushPromises()
    expect(shiftStaffingApi.assignOperator).toHaveBeenCalledWith('item-1', {
      immId: 'imm-1', operatorId: 'op3', assignedAt: null, unassignedAt: null
    })
  })

  it('same-component itemId navigation сбрасывает форму, запросы и timers', async () => {
    vi.useRealTimers()
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval', 'setTimeout', 'clearTimeout'] })
    vi.setSystemTime(new Date('2026-11-08T12:00:00Z'))
    shiftStaffingApi.item.mockImplementation(async itemId => ({
      data: detail({
        shift: summary({ itemId }),
        tasks: [task({ id: `task-${itemId}` })]
      })
    }))
    wrapper = mountView()
    await flushPromises()
    await wrapper.find('[data-test="assign-imm-1"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-test="operator-select"]').exists()).toBe(true)

    useRoute().params.itemId = 'item-b'
    await nextTick()
    await flushPromises()

    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-b')
    expect(wrapper.find('[data-test="operator-select"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="task-task-item-b"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="task-task-1"]').exists()).toBe(false)
    expect(shiftStaffingApi.item.mock.calls.filter(([id]) => id === 'item-1')).toHaveLength(1)

    await vi.advanceTimersByTimeAsync(60_000)
    expect(shiftStaffingApi.item.mock.calls.filter(([id]) => id === 'item-b')).toHaveLength(2)
    expect(shiftStaffingApi.item.mock.calls.filter(([id]) => id === 'item-1')).toHaveLength(1)
  })

  it('ручное Обновить не пересекается с polling direct item', async () => {
    vi.useRealTimers()
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval', 'setTimeout', 'clearTimeout'] })
    vi.setSystemTime(new Date('2026-11-08T12:00:00Z'))
    wrapper = mountView()
    await flushPromises()
    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(1)

    const request = deferred()
    shiftStaffingApi.item.mockReturnValue(request.promise)
    await vi.advanceTimersByTimeAsync(60_000)
    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(2)

    const manualRefresh = wrapper.vm.refreshItem()
    await Promise.resolve()
    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(2)
    request.resolve({ data: detail() })
    await manualRefresh
    await flushPromises()
  })

  it('показывает server timeline и Назначить на сироте открывает форму с immId и границами', async () => {
    const orphan = {
      startAtUtc: '2026-11-08T10:00:17Z', endAtUtc: '2026-11-08T11:30:43Z',
      operators: [], automaticOutputQuantity: 4, hasOrphanOutput: true, taskOutputs: []
    }
    shiftClosuresApi.getByItem.mockResolvedValue({ data: closureReport({
      timelines: [{ immId: 'imm-1', immName: 'ТПА-1', intervals: [orphan] }]
    }) })
    shiftStaffingApi.assignOperator.mockResolvedValue({ data: {} })
    wrapper = mountView()
    await flushPromises()

    expect(wrapper.get('[data-test="timeline-interval-0"]').attributes('data-orphan')).toBe('true')
    await wrapper.get('[data-test="assign-orphan-0"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-test="assigned-at"]').element.value)
      .toContain(dayjs(orphan.startAtUtc).format('YYYY-MM-DDTHH:mm:ss'))
    expect(wrapper.get('[data-test="unassigned-at"]').element.value)
      .toContain(dayjs(orphan.endAtUtc).format('YYYY-MM-DDTHH:mm:ss'))
    await wrapper.findComponent('[data-test="operator-select"]').vm.$emit('update:modelValue', 'op3')
    await wrapper.get('[data-test="submit-interval"]').trigger('click')
    await flushPromises()

    expect(shiftStaffingApi.assignOperator).toHaveBeenCalledWith('item-1', {
      immId: 'imm-1', operatorId: 'op3',
      assignedAt: dayjs(orphan.startAtUtc).toISOString(),
      unassignedAt: dayjs(orphan.endAtUtc).toISOString()
    })
    expect(shiftClosuresApi.getByItem).toHaveBeenCalledTimes(2)
  })

  it('ended unclosed показывает mutations для открытого и завершённого интервалов, edit отправляет PUT', async () => {
    const ended = assignment('a2', 'op2', 'Петров', {
      assignedAt: '2026-11-08T08:30:00Z', unassignedAt: '2026-11-08T10:15:00Z'
    })
    shiftStaffingApi.item.mockResolvedValue({ data: detail({
      assignments: [assignment('a1', 'op1', 'Иванов'), ended],
      shift: summary({ state: 'Historical', canEditOperators: true })
    }) })
    shiftStaffingApi.updateOperatorInterval.mockResolvedValue({ data: ended })
    wrapper = mountView()
    await flushPromises()

    expect(wrapper.find('[data-test="assign-imm-1"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="unassign-a1"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="edit-a1"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="edit-a2"]').exists()).toBe(true)
    await wrapper.get('[data-test="edit-a2"]').trigger('click')
    const localEnd = dayjs('2026-11-08T11:00:00Z').format('YYYY-MM-DDTHH:mm')
    await wrapper.get('[data-test="unassigned-at"]').setValue(localEnd)
    await wrapper.get('[data-test="submit-interval"]').trigger('click')
    await flushPromises()

    expect(shiftStaffingApi.updateOperatorInterval).toHaveBeenCalledWith('a2', {
      assignedAt: dayjs(ended.assignedAt).toISOString(),
      unassignedAt: dayjs(localEnd).toISOString()
    })
    expect(shiftClosuresApi.getByItem).toHaveBeenCalledTimes(2)
  })

  it('400 shiftClosed перезагружает item/report и скрывает mutations', async () => {
    shiftStaffingApi.unassignOperator.mockRejectedValue({
      response: { status: 400, data: { code: 'shiftClosed', message: 'Сменный рапорт уже закрыт' } }
    })
    shiftStaffingApi.item
      .mockResolvedValueOnce({ data: detail() })
      .mockResolvedValueOnce({
        data: detail({ shift: summary({ state: 'Historical', canEditOperators: false }) })
      })
    shiftClosuresApi.getByItem
      .mockResolvedValueOnce({ data: closureReport() })
      .mockResolvedValueOnce({ data: closureReport({ state: 'Closed' }) })
    wrapper = mountView()
    await flushPromises()

    await confirmUnassign(wrapper, 'a1')

    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(2)
    expect(shiftClosuresApi.getByItem).toHaveBeenCalledTimes(2)
    expect(wrapper.find('[data-test^="assign-"]').exists()).toBe(false)
    expect(wrapper.find('[data-test^="edit-"]').exists()).toBe(false)
    expect(wrapper.find('[data-test^="unassign-"]').exists()).toBe(false)
  })

  it('click задания ведёт в read-only task маршрут', async () => {
    wrapper = mountView()
    await flushPromises()

    await wrapper.find('[data-test="task-task-1"]').trigger('click')
    expect(push).toHaveBeenCalledWith('/shift-lead/items/item-1/tasks/task-1')
  })
})
