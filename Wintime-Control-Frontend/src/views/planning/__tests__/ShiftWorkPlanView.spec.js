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
import { shiftPlansApi } from '@/api/shiftPlans'
import * as shiftClosuresApi from '@/api/shiftClosures'

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
vi.mock('@/api/shiftPlans', () => ({
  shiftPlansApi: { getRange: vi.fn() }
}))
vi.mock('@/api/shiftClosures', () => ({
  getByItem: vi.fn(),
  saveDraft: vi.fn(),
  close: vi.fn()
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

function planItem(overrides = {}) {
  return {
    id: 'item-1', position: 1, shiftLeadId: 'lead-1', shiftLeadName: 'Старший Смены',
    startAtUtc: '2026-11-06T05:00:00Z', endAtUtc: '2026-11-06T17:00:00Z',
    timeZoneId: 'Europe/Moscow', state: 'Current', ...overrides
  }
}

function planDay(item = planItem()) {
  return { shiftDate: '2026-11-06', isSaved: !!item.id, items: [item] }
}

async function openClosureTab(wrapper) {
  const tab = wrapper.findAll('.el-tabs__item').find(node => node.text() === 'Сменный рапорт')
  expect(tab).toBeDefined()
  await tab.trigger('click')
  await flushPromises()
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
    shiftPlansApi.getRange.mockResolvedValue({ data: [planDay()] })
    shiftClosuresApi.getByItem.mockResolvedValue({ data: {
      id: null, shiftPlanItemId: 'item-1', state: 'Draft', entries: [],
      startAtUtc: '2026-11-06T05:00:00Z', endAtUtc: '2026-11-06T17:00:00Z'
    } })
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

  it('разрешает выбранные date+shift.number в Position и загружает closure только по item.id', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(shiftPlansApi.getRange).not.toHaveBeenCalled()
    expect(shiftClosuresApi.getByItem).not.toHaveBeenCalled()
    tasksApi.getList.mockClear()

    await openClosureTab(wrapper)

    expect(shiftPlansApi.getRange).toHaveBeenCalledWith('2026-11-06', '2026-11-06')
    expect(shiftClosuresApi.getByItem).toHaveBeenCalledWith('item-1')
    expect(tasksApi.getList).not.toHaveBeenCalled()
  })

  it.each([
    ['virtual item', planItem({ id: null })],
    ['без старшего', planItem({ shiftLeadId: null, shiftLeadName: null })]
  ])('показывает объяснение для %s без вызова closure API', async (_name, item) => {
    shiftPlansApi.getRange.mockResolvedValue({ data: [planDay(item)] })
    const wrapper = mountView()
    await flushPromises()

    await openClosureTab(wrapper)

    expect(wrapper.text()).toContain('Назначьте старшего смены')
    expect(shiftClosuresApi.getByItem).not.toHaveBeenCalled()
  })

  it('при снятии старшего на Refresh скрывает рапорт без повторного closure API', async () => {
    const wrapper = mountView()
    await flushPromises()
    await openClosureTab(wrapper)
    expect(shiftClosuresApi.getByItem).toHaveBeenCalledTimes(1)
    shiftPlansApi.getRange.mockResolvedValueOnce({
      data: [planDay(planItem({ shiftLeadId: null, shiftLeadName: null }))]
    })

    const refresh = wrapper.findAll('button').find(button => button.text() === 'Обновить')
    await refresh.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Назначьте старшего смены')
    expect(shiftClosuresApi.getByItem).toHaveBeenCalledTimes(1)
  })

  it('после материализации повторное разрешение включает рапорт', async () => {
    shiftPlansApi.getRange.mockResolvedValueOnce({ data: [planDay(planItem({ id: null, shiftLeadId: null }))] })
    const wrapper = mountView()
    await flushPromises()
    await openClosureTab(wrapper)
    expect(shiftClosuresApi.getByItem).not.toHaveBeenCalled()
    shiftPlansApi.getRange.mockResolvedValueOnce({ data: [planDay()] })

    const refresh = wrapper.findAll('button').find(button => button.text() === 'Обновить')
    await refresh.trigger('click')
    await flushPromises()

    expect(shiftPlansApi.getRange).toHaveBeenCalledTimes(2)
    expect(shiftClosuresApi.getByItem).toHaveBeenCalledWith('item-1')
  })

  it('Refresh сохраняет несохранённый ввод и не перемонтирует редактор той же смены', async () => {
    shiftClosuresApi.getByItem.mockResolvedValue({ data: {
      id: 'closure-1', shiftPlanItemId: 'item-1', state: 'Draft',
      startAtUtc: '2026-11-06T05:00:00Z', endAtUtc: '2026-11-06T17:00:00Z',
      entries: [{
        taskId: 'task-1', immName: 'ТПА-1', productName: 'Корпус', operatorTotals: [],
        actualPartWeightGrams: 4, manualFullCycleSeconds: 50, actualPackagingUnit: 20,
        packagingPlaces: 1, manualGoodQuantity: 8, manualDefectQuantity: 1,
        automaticOutputQuantity: 2, isManualFallbackRequired: false
      }]
    } })
    const wrapper = mountView()
    await flushPromises()
    await openClosureTab(wrapper)
    const input = wrapper.findComponent('[data-manual="task-1:manualGoodQuantity"]')
    const inputElement = input.element
    input.vm.$emit('update:modelValue', 9)
    await wrapper.vm.$nextTick()
    expect(input.props('modelValue')).toBe(9)

    const refresh = wrapper.findAll('button').find(button => button.text() === 'Обновить')
    await refresh.trigger('click')
    await flushPromises()

    const refreshedInput = wrapper.findComponent('[data-manual="task-1:manualGoodQuantity"]')
    expect(refreshedInput.element).toBe(inputElement)
    expect(refreshedInput.props('modelValue')).toBe(9)
    expect(shiftClosuresApi.getByItem).toHaveBeenCalledTimes(1)
  })

  it('повторно разрешает Position после переключения номера смены на вкладке рапорта', async () => {
    const second = planItem({ id: 'item-2', position: 2 })
    shiftPlansApi.getRange.mockResolvedValue({ data: [planDay(second)] })
    const wrapper = mountView()
    await flushPromises()
    await openClosureTab(wrapper)
    expect(shiftClosuresApi.getByItem).not.toHaveBeenCalled()

    const shiftSelect = wrapper.findAllComponents({ name: 'ElSelect' })[0]
    await shiftSelect.vm.$emit('update:modelValue', 2)
    await flushPromises()

    expect(shiftClosuresApi.getByItem).toHaveBeenCalledWith('item-2')
    expect(shiftClosuresApi.getByItem).toHaveBeenCalledTimes(1)
  })
})
