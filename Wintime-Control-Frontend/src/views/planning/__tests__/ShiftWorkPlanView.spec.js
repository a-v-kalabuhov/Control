// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus, { ElMessage } from 'element-plus'
import ShiftWorkPlanView from '../ShiftWorkPlanView.vue'
import { tasksApi } from '@/api/tasks'
import { personnelApi } from '@/api/personnel'
import { shiftsApi } from '@/api/shifts'
import { archivedTaskRefs } from '@/utils/taskIssueCheck'

vi.mock('@/api/tasks', () => ({
  tasksApi: {
    getList: vi.fn(),
    setPersonnel: vi.fn(() => Promise.resolve()),
    issue: vi.fn(() => Promise.resolve())
  }
}))
vi.mock('@/api/personnel', () => ({
  personnelApi: { getList: vi.fn() }
}))
vi.mock('@/api/shifts', () => ({
  shiftsApi: { getShifts: vi.fn() }
}))
vi.mock('@/utils/taskIssueCheck', () => ({
  archivedTaskRefs: vi.fn(() => Promise.resolve([]))
}))

const planned = {
  id: 'planned-1', status: 'Planned', immId: 'imm-1', moldId: 'mold-1',
  immName: 'ТПА-1', moldName: 'ПФ-1', productFullArticle: 'ТИП.1', productName: 'Корпус',
  planQuantity: 100, personnelId: 'adjuster-1', personnelName: 'Иванов',
  plannedStartAt: new Date(2026, 10, 6, 9).toISOString()
}
const carried = {
  id: 'carried-1', status: 'InProgress', immId: 'imm-2', moldId: 'mold-2',
  immName: 'ТПА-2', moldName: 'ПФ-2', productFullArticle: 'ТИП.2', productName: 'Крышка',
  planQuantity: 200, personnelId: 'adjuster-2', personnelName: 'Петров',
  plannedStartAt: new Date(2026, 10, 5, 20).toISOString(),
  setupStartedAt: new Date(2026, 10, 5, 20).toISOString()
}
const otherShift = {
  ...planned,
  id: 'other-shift', immName: 'ТПА-ДРУГОЙ',
  plannedStartAt: new Date(2026, 10, 6, 21).toISOString()
}

function arrangeTasks(items = [planned, carried]) {
  tasksApi.getList.mockImplementation(params => Promise.resolve({
    data: items
      .filter(t => !params.activeFrom || ['Setup', 'InProgress', 'Completed'].includes(t.status))
      .map(t => ({ ...t }))
  }))
}

function mountView() {
  return mount(ShiftWorkPlanView, {
    global: {
      plugins: [ElementPlus],
      stubs: { TaskStatusBadge: true, Refresh: true }
    }
  })
}

async function changePersonnel(wrapper, taskId, personnelId) {
  const select = wrapper.findComponent(`[data-test="personnel-${taskId}"]`)
  await select.vm.$emit('update:modelValue', personnelId)
  await select.vm.$emit('change', personnelId)
  await flushPromises()
}

describe('ShiftWorkPlanView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2026, 10, 6, 10, 0))
    vi.clearAllMocks()
    shiftsApi.getShifts.mockResolvedValue({ data: [
      { number: 1, startMinutes: 8 * 60, durationMinutes: 12 * 60 },
      { number: 2, startMinutes: 20 * 60, durationMinutes: 12 * 60 }
    ] })
    personnelApi.getList.mockResolvedValue({ data: [
      { id: 'adjuster-1', fullName: 'Иванов' },
      { id: 'adjuster-2', fullName: 'Петров' }
    ] })
    arrangeTasks()
    archivedTaskRefs.mockResolvedValue([])
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
    vi.spyOn(ElMessage, 'warning').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('показывает план выбранной смены и перенесённый выпуск', async () => {
    arrangeTasks([planned, carried, otherShift])
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('План работ на смену')
    expect(wrapper.text()).toContain('ТПА-1')
    expect(wrapper.text()).toContain('ТПА-2')
    expect(wrapper.text()).not.toContain('ТПА-ДРУГОЙ')
    expect(tasksApi.getList).toHaveBeenCalledWith({ plannedFrom: '2026-11-06', plannedTo: '2026-11-06' })
    expect(tasksApi.getList).toHaveBeenCalledWith({
      activeFrom: new Date(2026, 10, 6, 8).toISOString(),
      activeTo: new Date(2026, 10, 6, 20).toISOString()
    })
  })

  it('после полуночи открывает ночную смену с датой её начала', async () => {
    vi.setSystemTime(new Date('2026-11-06T23:00:00Z')) // 07.11.2026 02:00 в Москве
    shiftsApi.getShifts.mockResolvedValue({ data: [
      { number: 1, startMinutes: 8 * 60, durationMinutes: 12 * 60, timeZoneId: 'Europe/Moscow' },
      { number: 2, startMinutes: 20 * 60, durationMinutes: 12 * 60, timeZoneId: 'Europe/Moscow' }
    ] })
    arrangeTasks([])

    mountView()
    await flushPromises()

    expect(tasksApi.getList).toHaveBeenCalledWith({ plannedFrom: '2026-11-06', plannedTo: '2026-11-07' })
    expect(tasksApi.getList).toHaveBeenCalledWith({
      activeFrom: '2026-11-06T17:00:00.000Z',
      activeTo: '2026-11-07T05:00:00.000Z'
    })
  })

  it('для ночной смены запрашивает также задания следующих суток', async () => {
    const afterMidnight = {
      ...planned,
      id: 'after-midnight', immName: 'ТПА-НОЧЬ',
      plannedStartAt: new Date(2026, 10, 7, 2).toISOString()
    }
    arrangeTasks([afterMidnight])
    const wrapper = mountView()
    await flushPromises()

    const shiftSelect = wrapper.findAllComponents({ name: 'ElSelect' })[0]
    await shiftSelect.vm.$emit('update:modelValue', 2)
    await flushPromises()

    expect(tasksApi.getList).toHaveBeenCalledWith({ plannedFrom: '2026-11-06', plannedTo: '2026-11-07' })
    expect(wrapper.text()).toContain('ТПА-НОЧЬ')
  })

  it('вычисляет вторую дату ночной смены по часовому поясу завода', async () => {
    shiftsApi.getShifts.mockResolvedValue({ data: [
      { number: 1, startMinutes: 8 * 60, durationMinutes: 12 * 60, timeZoneId: 'Asia/Kamchatka' },
      { number: 2, startMinutes: 20 * 60, durationMinutes: 12 * 60, timeZoneId: 'Asia/Kamchatka' }
    ] })
    arrangeTasks([])
    const wrapper = mountView()
    await flushPromises()

    const shiftSelect = wrapper.findAllComponents({ name: 'ElSelect' })[0]
    await shiftSelect.vm.$emit('update:modelValue', 2)
    await flushPromises()

    expect(tasksApi.getList).toHaveBeenCalledWith({ plannedFrom: '2026-11-06', plannedTo: '2026-11-07' })
    expect(tasksApi.getList).toHaveBeenCalledWith({
      activeFrom: '2026-11-06T08:00:00.000Z',
      activeTo: '2026-11-06T20:00:00.000Z'
    })
  })

  it('при переключении смены блокирует выдачу прежних заданий до загрузки', async () => {
    const wrapper = mountView()
    await flushPromises()
    const pending = []
    tasksApi.getList.mockImplementation(() => new Promise(resolve => pending.push(resolve)))

    const shiftSelect = wrapper.findAllComponents({ name: 'ElSelect' })[0]
    await shiftSelect.vm.$emit('update:modelValue', 2)
    await flushPromises()

    expect(wrapper.text()).not.toContain('ТПА-1')
    expect(wrapper.find('[data-test="issue-all"]').attributes('disabled')).toBeDefined()
    pending.splice(0).forEach(resolve => resolve({ data: [] }))
    await flushPromises()
  })

  it('после ошибки смены не оставляет прежние задания доступными к выдаче', async () => {
    const wrapper = mountView()
    await flushPromises()
    tasksApi.getList.mockRejectedValue({ response: { data: { message: 'Смена недоступна' } } })

    const shiftSelect = wrapper.findAllComponents({ name: 'ElSelect' })[0]
    await shiftSelect.vm.$emit('update:modelValue', 2)
    await flushPromises()

    expect(wrapper.text()).not.toContain('ТПА-1')
    expect(wrapper.find('[data-test="issue-all"]').attributes('disabled')).toBeDefined()
  })

  it('назначает наладчика без открытия карточки задания', async () => {
    const wrapper = mountView()
    await flushPromises()

    await changePersonnel(wrapper, planned.id, 'adjuster-2')

    expect(tasksApi.setPersonnel).toHaveBeenCalledWith(planned.id, 'adjuster-2')
    expect(wrapper.text()).toContain('Петров')
  })

  it('блокирует выдачу до завершения сохранения наладчика', async () => {
    let save
    tasksApi.setPersonnel.mockImplementation(() => new Promise(resolve => { save = resolve }))
    const wrapper = mountView()
    await flushPromises()
    const select = wrapper.findComponent(`[data-test="personnel-${planned.id}"]`)

    await select.vm.$emit('change', 'adjuster-2')
    await wrapper.vm.$nextTick()

    expect(select.props('disabled')).toBe(true)
    expect(wrapper.find(`[data-test="issue-${planned.id}"]`).attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-test="issue-all"]').attributes('disabled')).toBeDefined()
    save()
    await flushPromises()
  })

  it('снятие наладчика с планового задания передаёт null', async () => {
    const wrapper = mountView()
    await flushPromises()

    await changePersonnel(wrapper, planned.id, '')

    expect(tasksApi.setPersonnel).toHaveBeenCalledWith(planned.id, null)
  })

  it('выдаёт отдельное готовое задание', async () => {
    const wrapper = mountView()
    await flushPromises()

    await wrapper.find(`[data-test="issue-${planned.id}"]`).trigger('click')
    await flushPromises()

    expect(archivedTaskRefs).toHaveBeenCalledWith(expect.objectContaining({ id: planned.id }))
    expect(tasksApi.issue).toHaveBeenCalledWith(planned.id, 'adjuster-1')
  })

  it('массовая выдача продолжается после ошибки и оставляет сообщение у строки', async () => {
    const second = {
      ...planned,
      id: 'planned-2', immName: 'ТПА-3', personnelId: 'adjuster-2', personnelName: 'Петров'
    }
    const withoutAdjuster = { ...planned, id: 'planned-3', personnelId: null, personnelName: null }
    arrangeTasks([planned, second, withoutAdjuster, carried])
    tasksApi.issue.mockImplementation(id => id === planned.id
      ? Promise.reject({ response: { data: { message: 'Наладчик недоступен' } } })
      : Promise.resolve())
    const wrapper = mountView()
    await flushPromises()

    await wrapper.find('[data-test="issue-all"]').trigger('click')
    await flushPromises()

    expect(tasksApi.issue).toHaveBeenCalledTimes(2)
    expect(tasksApi.issue).toHaveBeenNthCalledWith(1, planned.id, 'adjuster-1')
    expect(tasksApi.issue).toHaveBeenNthCalledWith(2, second.id, 'adjuster-2')
    expect(wrapper.text()).toContain('Наладчик недоступен')
    expect(ElMessage.warning).toHaveBeenCalled()
  })
})
