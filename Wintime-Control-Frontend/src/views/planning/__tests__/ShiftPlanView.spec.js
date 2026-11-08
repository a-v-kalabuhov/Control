// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import ShiftPlanView from '../ShiftPlanView.vue'
import { shiftPlansApi } from '@/api/shiftPlans'

vi.mock('@/api/shiftPlans', () => ({
  shiftPlansApi: {
    getRange: vi.fn(),
    getLeadCandidates: vi.fn(),
    setLead: vi.fn()
  }
}))

const candidates = [
  { id: 'lead-1', fullName: 'Иванов Иван', employeeId: '001' },
  { id: 'manager-1', fullName: 'Петров Пётр', employeeId: '002' }
]

function item(position, overrides = {}) {
  return {
    id: null,
    position,
    startAtUtc: '2026-11-08T05:00:00Z',
    endAtUtc: '2026-11-08T17:00:00Z',
    timeZoneId: 'Europe/Moscow',
    shiftLeadId: null,
    shiftLeadName: null,
    state: 'Future',
    canEditLead: true,
    canEditOperators: false,
    operatorAssignmentCount: 0,
    ...overrides
  }
}

function day(shiftDate, items, overrides = {}) {
  return {
    shiftDate,
    isSaved: false,
    isFactoryToday: shiftDate === '2026-11-08',
    items,
    ...overrides
  }
}

const pastDay = day('2026-11-07', [
  item(1, {
    id: 'past-item',
    startAtUtc: '2026-11-07T05:00:00Z',
    endAtUtc: '2026-11-07T17:00:00Z',
    shiftLeadId: 'lead-1',
    shiftLeadName: 'Иванов Иван',
    state: 'Historical',
    canEditLead: false
  })
], { isSaved: true })

const currentDay = day('2026-11-08', [
  item(1, { id: 'current-1', state: 'Current', canEditLead: false }),
  item(2, {
    id: 'current-2',
    startAtUtc: '2026-11-08T17:00:00Z',
    endAtUtc: '2026-11-09T05:00:00Z',
    shiftLeadId: 'manager-1',
    shiftLeadName: 'Петров Пётр',
    canEditLead: false
  })
])

const futureDay = day('2026-11-09', [
  item(1, { startAtUtc: '2026-11-09T05:00:00Z', endAtUtc: '2026-11-09T17:00:00Z' }),
  item(2, {
    startAtUtc: '2026-11-09T17:00:00Z',
    endAtUtc: '2026-11-10T05:00:00Z',
    timeZoneId: 'Asia/Kamchatka'
  }),
  item(3, { startAtUtc: '2026-11-10T05:00:00Z', endAtUtc: '2026-11-10T11:00:00Z' })
])

function mountView() {
  return mount(ShiftPlanView, {
    global: {
      plugins: [ElementPlus],
      stubs: { ShiftPlanItemDialog: true }
    }
  })
}

describe('ShiftPlanView', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-11-08T10:00:00Z'))
    vi.clearAllMocks()
    shiftPlansApi.getRange.mockResolvedValue({ data: [pastDay, currentDay, futureDay] })
    shiftPlansApi.getLeadCandidates.mockResolvedValue({ data: candidates })
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('показывает независимое число смен в строках и точные заголовки без пустых заглушек', async () => {
    const wrapper = mountView()
    await flushPromises()

    const rows = wrapper.findAll('.shift-plan-row')
    expect(rows).toHaveLength(3)
    expect(rows.map(row => row.findAll('.shift-plan-item').length)).toEqual([1, 2, 3])
    expect(wrapper.findAll('.el-table__header-wrapper th').map(th => th.text())).toEqual([
      'Дата', 'Смены и старшие'
    ])
    expect(wrapper.text()).not.toContain('Нет в графике')
  })

  it('показывает позицию, окно в часовом поясе завода и старшего каждой смены', async () => {
    const wrapper = mountView()
    await flushPromises()

    const cells = wrapper.findAll('.shift-plan-item')
    expect(cells[0].text()).toContain('Смена 1')
    expect(cells[0].text()).toContain('08:00–20:00')
    expect(cells[0].text()).toContain('Иванов Иван')
    expect(cells[1].text()).toContain('Не назначен')
    expect(cells[2].text()).toContain('Смена 2')
    expect(cells[2].text()).toContain('20:00–08:00')
    expect(cells[4].text()).toContain('Смена 2')
    expect(cells[4].text()).toContain('05:00–17:00')
  })

  it('различает прошлую, текущую и будущую строки серверными и календарными признаками', async () => {
    const wrapper = mountView()
    await flushPromises()

    const rows = wrapper.findAll('.shift-plan-row')
    expect(rows[0].classes()).toContain('shift-past')
    expect(rows[1].classes()).toContain('shift-current-date')
    expect(rows[2].classes()).toContain('shift-future')
  })

  it('повторно загружает диапазон и одновременно обновляет кандидатов', async () => {
    const wrapper = mountView()
    await flushPromises()
    shiftPlansApi.getRange.mockResolvedValue({ data: [] })

    const picker = wrapper.findComponent({ name: 'ElDatePicker' })
    await picker.vm.$emit('update:modelValue', ['2026-11-10', '2026-11-12'])
    await flushPromises()

    expect(shiftPlansApi.getRange).toHaveBeenLastCalledWith('2026-11-10', '2026-11-12')
    expect(shiftPlansApi.getLeadCandidates).toHaveBeenCalledTimes(2)
  })

  it('игнорирует устаревший ответ диапазона', async () => {
    let resolveFirst
    shiftPlansApi.getRange
      .mockImplementationOnce(() => new Promise(resolve => { resolveFirst = resolve }))
      .mockResolvedValueOnce({ data: [futureDay] })
    const wrapper = mountView()
    await flushPromises()

    const picker = wrapper.findComponent({ name: 'ElDatePicker' })
    await picker.vm.$emit('update:modelValue', ['2026-11-09', '2026-11-09'])
    await flushPromises()
    expect(wrapper.findAll('.shift-plan-row')).toHaveLength(1)

    resolveFirst({ data: [pastDay, currentDay, futureDay] })
    await flushPromises()
    expect(wrapper.findAll('.shift-plan-row')).toHaveLength(1)
    expect(wrapper.text()).toContain('09.11.2026')
  })

  it('после сохранения заменяет день и выбранную смену по позиции', async () => {
    const wrapper = mountView()
    await flushPromises()

    await wrapper.findAll('.shift-plan-item')[2].trigger('click')
    await flushPromises()
    const dialog = wrapper.findComponent({ name: 'ShiftPlanItemDialog' })
    expect(dialog.exists()).toBe(true)

    const savedDay = day('2026-11-08', [
      item(1, { id: 'current-1', state: 'Current', canEditLead: false }),
      item(2, {
        id: 'current-2', shiftLeadId: 'lead-1', shiftLeadName: 'Иванов Иван', canEditLead: false
      })
    ], { isSaved: true })
    await dialog.vm.$emit('saved', savedDay)
    await flushPromises()

    expect(wrapper.findAll('.shift-plan-row')[1].text()).toContain('Иванов Иван')
    expect(wrapper.findComponent({ name: 'ShiftPlanItemDialog' }).props('item')).toEqual(savedDay.items[1])
  })

  it('после 409 полностью перезагружает план', async () => {
    const wrapper = mountView()
    await flushPromises()
    shiftPlansApi.getRange.mockResolvedValue({ data: [futureDay] })

    await wrapper.findAll('.shift-plan-item')[2].trigger('click')
    await flushPromises()
    const dialog = wrapper.findComponent({ name: 'ShiftPlanItemDialog' })
    await dialog.vm.$emit('reload')
    await flushPromises()

    expect(shiftPlansApi.getRange).toHaveBeenCalledTimes(2)
    expect(wrapper.findAll('.shift-plan-row')).toHaveLength(1)
  })
})
