// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import { nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { createPinia, setActivePinia } from 'pinia'
import ShiftStaffingTaskView from '../ShiftStaffingTaskView.vue'
import { shiftStaffingApi } from '@/api/shiftStaffing'
import { tasksApi } from '@/api/tasks'
import { dashboardApi } from '@/api/dashboard'
import { personnelApi } from '@/api/personnel'

const { route, push } = vi.hoisted(() => ({
  route: { params: { itemId: 'item-1', taskId: 'task-1' } },
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
    operators: vi.fn(), assign: vi.fn(), unassign: vi.fn()
  }
}))
vi.mock('@/api/tasks', () => ({ tasksApi: { getResults: vi.fn(), getTask: vi.fn() } }))
vi.mock('@/api/dashboard', () => ({
  dashboardApi: { getTaskResults: vi.fn(), getImmHistory: vi.fn() }
}))
vi.mock('@/api/personnel', () => ({ personnelApi: { getAll: vi.fn(), getPersonnel: vi.fn() } }))

const task = {
  id: 'task-1', immId: 'imm-1', immName: 'ТПА-1', moldName: 'ПФ-1',
  productFullArticle: 'ТИП.001', productName: 'Корпус', productTypeName: 'Тип',
  planQuantity: 100, actualQuantity: 25, defectQuantity: 2,
  status: 'InProgress', personnelName: 'Наладчик Иванов',
  plannedFullCycleSeconds: 60, plannedInjectionCycleSeconds: 12
}

const detail = {
  shift: {
    itemId: 'item-1', shiftDate: '2026-11-08', position: 1,
    startAtUtc: '2026-11-08T08:00:00Z', endAtUtc: '2026-11-08T20:00:00Z',
    timeZoneId: 'Europe/Moscow', state: 'Current', canEditOperators: true
  },
  shiftLeadId: 'lead-1', shiftLeadName: 'Старший Смены',
  imms: [{
    immId: 'imm-1', name: 'ТПА-1', effectiveStatus: 'Production',
    tasks: [task], assignments: [], activeOperatorCount: 0
  }]
}

describe('ShiftStaffingTaskView', () => {
  let wrapper

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    useRoute().params.itemId = 'item-1'
    useRoute().params.taskId = 'task-1'
    shiftStaffingApi.item.mockResolvedValue({ data: detail })
    shiftStaffingApi.task.mockResolvedValue({
      data: { task, results: { releaseStarted: false, inProgress: false } }
    })
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('читает detail и результаты только через shiftStaffingApi.item/task без mutation controls', async () => {
    wrapper = mount(ShiftStaffingTaskView, { global: { plugins: [ElementPlus] } })
    await flushPromises()

    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-1')
    expect(shiftStaffingApi.task).toHaveBeenCalledWith('item-1', 'task-1')
    expect(wrapper.text()).toContain('ТИП.001')
    expect(wrapper.text()).toContain('ПФ-1')
    expect(wrapper.text()).toContain('100')
    expect(wrapper.text()).toContain('25')
    expect(wrapper.text()).toContain('Выпуск ещё не начат')

    expect(tasksApi.getResults).not.toHaveBeenCalled()
    expect(tasksApi.getTask).not.toHaveBeenCalled()
    expect(dashboardApi.getTaskResults).not.toHaveBeenCalled()
    expect(dashboardApi.getImmHistory).not.toHaveBeenCalled()
    expect(personnelApi.getAll).not.toHaveBeenCalled()
    expect(shiftStaffingApi.history).not.toHaveBeenCalled()
    expect(shiftStaffingApi.assign).not.toHaveBeenCalled()
    expect(shiftStaffingApi.unassign).not.toHaveBeenCalled()
    expect(wrapper.text()).not.toContain('Продолжить выпуск')
    expect(wrapper.text()).not.toContain('Завершить наладку')
    expect(wrapper.find('[data-test^="mutation-"]').exists()).toBe(false)
  })

  it('ошибка initial item loading показывает on-page retry вместо false not-found', async () => {
    shiftStaffingApi.item.mockRejectedValueOnce({
      response: { status: 500, data: { message: 'Смена временно недоступна' } }
    })
    wrapper = mount(ShiftStaffingTaskView, { global: { plugins: [ElementPlus] } })
    await flushPromises()

    expect(wrapper.text()).toContain('Смена временно недоступна')
    expect(wrapper.text()).not.toContain('Задание не найдено в этой смене')
    expect(shiftStaffingApi.task).not.toHaveBeenCalled()

    shiftStaffingApi.item.mockResolvedValueOnce({ data: detail })
    await wrapper.find('[data-test="retry-item-load"]').trigger('click')
    await flushPromises()

    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(2)
    expect(shiftStaffingApi.task).toHaveBeenCalledWith('item-1', 'task-1')
    expect(wrapper.text()).toContain('Выпуск ещё не начат')
  })

  it('same-component itemId/taskId navigation загружает новый item и новые results', async () => {
    const taskB = { ...task, id: 'task-b', productFullArticle: 'ТИП.B' }
    const detailB = {
      ...detail,
      shift: { ...detail.shift, itemId: 'item-b' },
      imms: [{ ...detail.imms[0], tasks: [taskB] }]
    }
    shiftStaffingApi.item.mockImplementation(async itemId => ({
      data: itemId === 'item-b' ? detailB : detail
    }))
    wrapper = mount(ShiftStaffingTaskView, { global: { plugins: [ElementPlus] } })
    await flushPromises()

    useRoute().params.itemId = 'item-b'
    useRoute().params.taskId = 'task-b'
    await nextTick()
    await flushPromises()

    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-b')
    expect(shiftStaffingApi.task).toHaveBeenCalledWith('item-b', 'task-b')
    expect(wrapper.text()).toContain('ТИП.B')
    expect(wrapper.text()).not.toContain('ТИП.001')
  })

  it('same-item taskId navigation пересоздаёт results без перезагрузки item', async () => {
    const task2 = { ...task, id: 'task-2', productFullArticle: 'ТИП.002' }
    const twoTasks = {
      ...detail,
      imms: [{ ...detail.imms[0], tasks: [task, task2] }]
    }
    shiftStaffingApi.item.mockResolvedValue({ data: twoTasks })
    wrapper = mount(ShiftStaffingTaskView, { global: { plugins: [ElementPlus] } })
    await flushPromises()

    useRoute().params.taskId = 'task-2'
    await nextTick()
    await flushPromises()

    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(1)
    expect(shiftStaffingApi.task).toHaveBeenCalledWith('item-1', 'task-2')
    expect(wrapper.text()).toContain('ТИП.002')
  })
})
