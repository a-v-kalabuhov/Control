// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import ShiftPlanView from '../ShiftPlanView.vue'
import ShiftDefaultLeadsDialog from '@/components/shifts/ShiftDefaultLeadsDialog.vue'
import { shiftPlansApi } from '@/api/shiftPlans'
import { shiftsApi } from '@/api/shifts'

vi.mock('@/api/shiftPlans', () => ({
  shiftPlansApi: {
    getRange: vi.fn(),
    getLeadCandidates: vi.fn(),
    setLead: vi.fn()
  }
}))

vi.mock('@/api/shifts', () => ({
  shiftsApi: {
    getDefaultLeads: vi.fn(),
    saveDefaultLeads: vi.fn()
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
    shiftsApi.getDefaultLeads.mockResolvedValue({
      data: { items: [], fallbackManager: null }
    })
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('открывает настройку старших по умолчанию и загружает её только по кнопке', async () => {
    const wrapper = mountView()
    await flushPromises()

    const button = wrapper.find('[data-test="open-default-leads"]')
    expect(button.exists()).toBe(true)
    expect(button.text()).toContain('Настройка')
    expect(shiftsApi.getDefaultLeads).not.toHaveBeenCalled()
    expect(shiftPlansApi.getLeadCandidates).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).not.toContain('Настройка графика смен')

    await button.trigger('click')
    await flushPromises()

    expect(wrapper.findComponent(ShiftDefaultLeadsDialog).props('modelValue')).toBe(true)
    expect(wrapper.text()).toContain('Настройка графика смен')
    expect(shiftsApi.getDefaultLeads).toHaveBeenCalledTimes(1)
    expect(shiftPlansApi.getLeadCandidates).toHaveBeenCalledTimes(2)
  })

  it('перечитывает план после сохранения старших по умолчанию', async () => {
    const wrapper = mountView()
    await flushPromises()
    await wrapper.find('[data-test="open-default-leads"]').trigger('click')
    await flushPromises()

    wrapper.findComponent(ShiftDefaultLeadsDialog).vm.$emit('saved')
    await flushPromises()

    expect(shiftPlansApi.getRange).toHaveBeenCalledTimes(2)
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

  it('показывает source badges для Default и ManagerFallback, а warning только без старшего', async () => {
    const sourceDay = day('2026-11-09', [
      item(1, {
        id: 'explicit', shiftLeadId: 'lead-1', shiftLeadName: 'Явный Старший',
        shiftLeadSource: 'Explicit'
      }),
      item(2, {
        shiftLeadId: 'lead-1', shiftLeadName: 'Иванов Иван', shiftLeadSource: 'Default'
      }),
      item(3, {
        shiftLeadId: 'manager-1', shiftLeadName: 'Петров Пётр', shiftLeadSource: 'ManagerFallback'
      }),
      item(4, { shiftLeadSource: null, hasMissingLeadWarning: true })
    ])
    shiftPlansApi.getRange.mockResolvedValue({ data: [sourceDay] })
    const wrapper = mountView()
    await flushPromises()

    const cells = wrapper.findAll('.shift-plan-item')
    expect(cells[0].find('[data-test="lead-source"]').exists()).toBe(false)
    expect(cells[1].find('[data-test="lead-source"]').text()).toBe('По умолчанию')
    expect(cells[2].find('[data-test="lead-source"]').text()).toBe('Первый активный менеджер')
    expect(cells[3].find('[data-test="lead-source"]').exists()).toBe(false)
    expect(cells.slice(0, 3).every(cell => !cell.find('[data-test="missing-lead"]').exists())).toBe(true)
    expect(cells[3].find('[data-test="missing-lead"]').text()).toBe('Не назначен старший')
  })

  it('показывает закрытие только по серверному closureStatus без pending badge для Draft', async () => {
    const indicatorDay = day('2026-11-08', [
      item(1, { id: 'closed', state: 'Historical', closureStatus: 'Closed' }),
      item(2, { id: 'not-closed', state: 'Historical', closureStatus: 'NotClosed' }),
      item(3, { id: 'current-draft', state: 'Current', closureStatus: 'Draft' }),
      item(4, { id: 'future-draft', state: 'Future', closureStatus: 'Draft' }),
      item(5, {
        id: null, state: 'Current', closureStatus: null, hasMissingLeadWarning: true
      })
    ])
    shiftPlansApi.getRange.mockResolvedValue({ data: [indicatorDay] })
    const wrapper = mountView()
    await flushPromises()

    const cells = wrapper.findAll('.shift-plan-item')
    expect(cells[0].find('[data-test="closure-status"]').text()).toBe('Закрыта')
    expect(cells[1].find('[data-test="closure-status"]').text()).toBe('Не закрыта')
    expect(cells[2].find('[data-test="closure-status"]').exists()).toBe(false)
    expect(cells[3].find('[data-test="closure-status"]').exists()).toBe(false)
    expect(cells[4].find('[data-test="closure-status"]').exists()).toBe(false)
    expect(cells[4].find('[data-test="missing-lead"]').text()).toBe('Не назначен старший')
  })

  it('показывает выпуск без литейщика независимо от статуса закрытия', async () => {
    const orphanDay = day('2026-11-08', [
      item(1, {
        id: 'closed-orphan', state: 'Historical', closureStatus: 'Closed', hasOrphanOutput: true
      }),
      item(2, {
        id: 'draft-orphan', state: 'Current', closureStatus: 'Draft', hasOrphanOutput: true
      }),
      item(3, {
        id: 'not-closed', state: 'Historical', closureStatus: 'NotClosed', hasOrphanOutput: false
      })
    ])
    shiftPlansApi.getRange.mockResolvedValue({ data: [orphanDay] })
    const wrapper = mountView()
    await flushPromises()

    const cells = wrapper.findAll('.shift-plan-item')
    expect(cells[0].find('[data-test="orphan-output"]').text()).toBe('Есть выпуск без литейщика')
    expect(cells[1].find('[data-test="orphan-output"]').text()).toBe('Есть выпуск без литейщика')
    expect(cells[2].find('[data-test="orphan-output"]').exists()).toBe(false)
  })

  it('различает прошлую, текущую и будущую строки серверными и календарными признаками', async () => {
    const wrapper = mountView()
    await flushPromises()

    const rows = wrapper.findAll('.shift-plan-row')
    expect(rows[0].classes()).toContain('shift-past')
    expect(rows[1].classes()).toContain('shift-current-date')
    expect(rows[2].classes()).toContain('shift-future')
  })

  it('раскрашивает отдельные смены по state и наследует цвет текста старшего от ячейки', async () => {
    const wrapper = mountView()
    await flushPromises()

    const cells = wrapper.findAll('.shift-plan-item')
    expect(cells[0].classes()).toContain('shift-plan-item-historical')
    expect(cells[1].classes()).toContain('shift-plan-item-current')
    expect(cells[2].classes()).toContain('shift-plan-item-future')
    expect(cells[0].element.style.backgroundColor).toBe('rgb(31, 41, 55)')
    expect(cells[0].element.style.color).toBe('rgb(255, 255, 255)')
    expect(cells[1].element.style.backgroundColor).toBe('rgb(220, 252, 231)')
    expect(cells[2].element.style.backgroundColor).toBe('rgb(224, 242, 254)')

    const lead = cells[0].find('.shift-plan-lead')
    expect(lead.exists()).toBe(true)
    expect(lead.text()).toBe('Иванов Иван')
    expect(lead.classes()).not.toContain('text-gray-800')
    expect(lead.classes()).not.toContain('text-gray-500')
  })

  it('классифицирует прошлое и будущее по серверным state, а не по дате браузера', async () => {
    const historicalDespiteBrowserTomorrow = day('2026-11-09', [
      item(1, { state: 'Historical' })
    ], { isFactoryToday: false })
    const futureDespiteBrowserYesterday = day('2026-11-07', [
      item(1, { state: 'Future' })
    ], { isFactoryToday: false })
    shiftPlansApi.getRange.mockResolvedValue({
      data: [historicalDespiteBrowserTomorrow, futureDespiteBrowserYesterday]
    })
    const wrapper = mountView()
    await flushPromises()

    const rows = wrapper.findAll('.shift-plan-row')
    expect(rows[0].classes()).toContain('shift-past')
    expect(rows[0].classes()).not.toContain('shift-future')
    expect(rows[1].classes()).toContain('shift-future')
    expect(rows[1].classes()).not.toContain('shift-past')
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

  it('игнорирует saved старого диалога после закрытия A и сохранения B', async () => {
    const wrapper = mountView()
    await flushPromises()

    await wrapper.findAll('.shift-plan-item')[2].trigger('click')
    await flushPromises()
    const dialogA = wrapper.findComponent({ name: 'ShiftPlanItemDialog' })
    await dialogA.vm.$emit('update:modelValue', false)
    await flushPromises()

    await wrapper.findAll('.shift-plan-item')[4].trigger('click')
    await flushPromises()
    const dialogB = wrapper.findComponent({ name: 'ShiftPlanItemDialog' })
    const savedFuture = day('2026-11-09', futureDay.items.map(futureItem =>
      futureItem.position === 2
        ? { ...futureItem, id: 'future-2', shiftLeadId: 'lead-1', shiftLeadName: 'Новый B' }
        : futureItem), { isSaved: true })
    await dialogB.vm.$emit('saved', savedFuture, {
      date: '2026-11-09', position: 2, request: 2
    })
    await flushPromises()

    const staleCurrent = day('2026-11-08', currentDay.items.map(currentItem =>
      currentItem.position === 2
        ? { ...currentItem, shiftLeadId: 'manager-1', shiftLeadName: 'Поздний A' }
        : currentItem), { isSaved: true })
    wrapper.vm.onSaved(staleCurrent, {
      date: '2026-11-08', position: 2, request: 1
    })
    await flushPromises()

    const currentDialog = wrapper.findComponent({ name: 'ShiftPlanItemDialog' })
    expect(currentDialog.props('day')).toEqual(savedFuture)
    expect(currentDialog.props('item').shiftLeadName).toBe('Новый B')
    expect(wrapper.findAll('.shift-plan-row')[1].text()).not.toContain('Поздний A')
  })

  it('после 409 перечитывает открытую карточку по дате и позиции', async () => {
    const wrapper = mountView()
    await flushPromises()
    const freshDay = day('2026-11-08', [
      item(1, { id: 'current-1', state: 'Current', canEditLead: false }),
      item(2, {
        id: 'current-2',
        startAtUtc: '2026-11-08T17:00:00Z',
        endAtUtc: '2026-11-09T05:00:00Z',
        shiftLeadId: 'lead-1',
        shiftLeadName: 'Иванов Иван',
        state: 'Future',
        canEditLead: true,
        canEditOperators: true,
        operatorAssignmentCount: 3
      })
    ], { isSaved: true })
    shiftPlansApi.getRange.mockResolvedValue({ data: [freshDay] })

    await wrapper.findAll('.shift-plan-item')[2].trigger('click')
    await flushPromises()
    const dialog = wrapper.findComponent({ name: 'ShiftPlanItemDialog' })
    await dialog.vm.$emit('reload')
    await flushPromises()

    expect(shiftPlansApi.getRange).toHaveBeenCalledTimes(2)
    const refreshedDialog = wrapper.findComponent({ name: 'ShiftPlanItemDialog' })
    expect(refreshedDialog.props('day')).toEqual(freshDay)
    expect(refreshedDialog.props('item')).toEqual(freshDay.items[1])
    expect(refreshedDialog.props('item').shiftLeadName).toBe('Иванов Иван')
    expect(refreshedDialog.props('item').canEditLead).toBe(true)
    expect(refreshedDialog.props('item').canEditOperators).toBe(true)
    expect(refreshedDialog.props('item').operatorAssignmentCount).toBe(3)
    expect(refreshedDialog.props('item').id).toBe('current-2')
  })

  it('после перезагрузки закрывает карточку, если её позиция исчезла', async () => {
    const wrapper = mountView()
    await flushPromises()
    const freshDay = day('2026-11-08', [
      item(1, { id: 'current-1', state: 'Current', canEditLead: false })
    ], { isSaved: true })
    shiftPlansApi.getRange.mockResolvedValue({ data: [freshDay] })

    await wrapper.findAll('.shift-plan-item')[2].trigger('click')
    await flushPromises()
    await wrapper.findComponent({ name: 'ShiftPlanItemDialog' }).vm.$emit('reload')
    await flushPromises()

    expect(wrapper.findComponent({ name: 'ShiftPlanItemDialog' }).exists()).toBe(false)
  })
})
