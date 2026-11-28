// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import ShiftDefaultLeadsDialog from '../ShiftDefaultLeadsDialog.vue'
import { shiftsApi } from '@/api/shifts'
import { shiftPlansApi } from '@/api/shiftPlans'

vi.mock('@/api/shifts', () => ({
  shiftsApi: {
    getDefaultLeads: vi.fn(),
    saveDefaultLeads: vi.fn()
  }
}))

vi.mock('@/api/shiftPlans', () => ({
  shiftPlansApi: { getLeadCandidates: vi.fn() }
}))

const candidates = [
  { id: 'lead-1', fullName: 'Иванов Иван', employeeId: '001' },
  { id: 'manager-1', fullName: 'Петров Пётр', employeeId: '002' }
]

function defaultItems() {
  return [
    {
      position: 1,
      number: 1,
      startMinutes: 480,
      durationMinutes: 720,
      breakStartMinutes: 720,
      breakDurationMinutes: 30,
      timeZoneId: 'Europe/Moscow',
      defaultShiftLeadId: 'lead-1',
      defaultShiftLeadName: 'Иванов Иван',
      defaultShiftLeadEmployeeId: '001'
    },
    {
      position: 2,
      number: 2,
      startMinutes: 1200,
      durationMinutes: 720,
      breakStartMinutes: 0,
      breakDurationMinutes: 0,
      timeZoneId: 'Europe/Moscow',
      defaultShiftLeadId: null,
      defaultShiftLeadName: null,
      defaultShiftLeadEmployeeId: null
    },
    {
      position: 3,
      number: 3,
      startMinutes: 0,
      durationMinutes: 480,
      breakStartMinutes: 240,
      breakDurationMinutes: 30,
      timeZoneId: 'Europe/Moscow',
      defaultShiftLeadId: 'archived-1',
      defaultShiftLeadName: 'Архивный Старший',
      defaultShiftLeadEmployeeId: '009'
    }
  ]
}

function settings(overrides = {}) {
  return {
    items: defaultItems(),
    fallbackManager: {
      id: 'manager-1',
      fullName: 'Петров Пётр',
      employeeId: '002',
      createdAt: '2026-01-01T00:00:00Z'
    },
    ...overrides
  }
}

function mountDialog(modelValue = true) {
  return mount(ShiftDefaultLeadsDialog, {
    props: { modelValue },
    global: { plugins: [ElementPlus] }
  })
}

function deferred() {
  let resolve
  let reject
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

describe('ShiftDefaultLeadsDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    shiftsApi.getDefaultLeads.mockResolvedValue({ data: settings() })
    shiftPlansApi.getLeadCandidates.mockResolvedValue({ data: candidates })
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
  })

  afterEach(() => vi.restoreAllMocks())

  it('загружает настройки и кандидатов только при открытии и показывает read-only график', async () => {
    const wrapper = mountDialog(false)
    await flushPromises()
    expect(shiftsApi.getDefaultLeads).not.toHaveBeenCalled()
    expect(shiftPlansApi.getLeadCandidates).not.toHaveBeenCalled()

    await wrapper.setProps({ modelValue: true })
    await flushPromises()

    expect(shiftsApi.getDefaultLeads).toHaveBeenCalledTimes(1)
    expect(shiftPlansApi.getLeadCandidates).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('Настройка графика смен')
    expect(wrapper.text()).toContain('Начало')
    expect(wrapper.text()).toContain('Конец')
    expect(wrapper.text()).toContain('Длительность')
    expect(wrapper.text()).toContain('Перерыв')
    expect(wrapper.text()).toContain('Старший смены')
    expect(wrapper.text()).toContain('08:00')
    expect(wrapper.text()).toContain('20:00')
    expect(wrapper.text()).toContain('12 ч')
    expect(wrapper.text()).toContain('12:00 – 12:30')
    expect(wrapper.text()).toContain('Если старший не задан, используется первый активный менеджер: Петров Пётр')
    expect(wrapper.findAllComponents({ name: 'ElTimePicker' })).toHaveLength(0)
    expect(wrapper.findAllComponents({ name: 'ElInputNumber' })).toHaveLength(0)

    const selects = wrapper.findAllComponents({ name: 'ElSelect' })
    expect(selects).toHaveLength(3)
    expect(selects[0].props('filterable')).toBe(true)
    expect(selects[0].props('clearable')).toBe(true)
    expect(selects[1].props('placeholder')).toBe('Не задан')
  })

  it('считает окончание ровно в полночь переходом на следующие сутки', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    expect(wrapper.vm.isNightShift({ startMinutes: 480, durationMinutes: 960 })).toBe(true)
  })

  it('не исключает одного кандидата из остальных строк', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    wrapper.vm.items[0].selectedShiftLeadId = 'lead-1'
    wrapper.vm.items[1].selectedShiftLeadId = 'lead-1'
    await wrapper.vm.$nextTick()

    expect(wrapper.vm.optionsFor(wrapper.vm.items[0]).map(option => option.id)).toContain('lead-1')
    expect(wrapper.vm.optionsFor(wrapper.vm.items[1]).map(option => option.id)).toContain('lead-1')
  })

  it('показывает недоступное сохранённое значение с нейтральной пометкой только в его строке', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    const archivedRow = wrapper.vm.items[2]
    const archivedOption = wrapper.vm.optionsFor(archivedRow)
      .find(option => option.id === 'archived-1')
    expect(archivedOption).toBeDefined()
    expect(wrapper.vm.candidateLabel(archivedOption)).toContain('Архивный Старший')
    expect(wrapper.vm.candidateLabel(archivedOption)).toContain('009')
    expect(wrapper.vm.candidateLabel(archivedOption)).toContain('недоступен')
    expect(wrapper.vm.optionsFor(wrapper.vm.items[0]).map(option => option.id)).not.toContain('archived-1')

    archivedRow.selectedShiftLeadId = null
    expect(wrapper.vm.optionsFor(archivedRow).map(option => option.id)).not.toContain('archived-1')
  })

  it('сохраняет все позиции с null и применяет серверный ответ', async () => {
    const serverSettings = settings({
      items: defaultItems().map(item => ({
        ...item,
        defaultShiftLeadId: item.position < 3 ? 'manager-1' : null,
        defaultShiftLeadName: item.position < 3 ? 'Петров Пётр' : null,
        defaultShiftLeadEmployeeId: item.position < 3 ? '002' : null
      })),
      fallbackManager: null
    })
    shiftsApi.saveDefaultLeads.mockResolvedValue({ data: serverSettings })
    const wrapper = mountDialog()
    await flushPromises()
    wrapper.vm.items[0].selectedShiftLeadId = 'manager-1'
    wrapper.vm.items[1].selectedShiftLeadId = 'manager-1'
    wrapper.vm.items[2].selectedShiftLeadId = null

    await wrapper.vm.save()
    await flushPromises()

    expect(shiftsApi.saveDefaultLeads).toHaveBeenCalledWith([
      { position: 1, shiftLeadId: 'manager-1' },
      { position: 2, shiftLeadId: 'manager-1' },
      { position: 3, shiftLeadId: null }
    ])
    expect(wrapper.vm.items.map(item => item.selectedShiftLeadId)).toEqual(['manager-1', 'manager-1', null])
    expect(wrapper.vm.items[0].defaultShiftLeadName).toBe('Петров Пётр')
    expect(wrapper.vm.fallbackManager).toBeNull()
    expect(wrapper.emitted('saved')[0]).toEqual([serverSettings])
    expect(wrapper.emitted('update:modelValue').at(-1)).toEqual([false])
  })

  it('блокирует закрытие крестиком и Escape во время сохранения', async () => {
    const pendingSave = deferred()
    shiftsApi.saveDefaultLeads.mockReturnValue(pendingSave.promise)
    const wrapper = mountDialog()
    await flushPromises()

    const savePromise = wrapper.vm.save()
    await wrapper.vm.$nextTick()

    const dialog = wrapper.findComponent({ name: 'ElDialog' })
    expect(dialog.props('showClose')).toBe(false)
    expect(dialog.props('closeOnPressEscape')).toBe(false)
    expect(wrapper.find('[data-test="cancel-default-leads"]').attributes('disabled')).toBeDefined()

    pendingSave.resolve({ data: settings() })
    await savePromise
    await flushPromises()
    expect(wrapper.emitted('saved')).toHaveLength(1)
  })

  it('предупреждает, если активных менеджеров для fallback нет', async () => {
    shiftsApi.getDefaultLeads.mockResolvedValue({ data: settings({ fallbackManager: null }) })
    const wrapper = mountDialog()
    await flushPromises()

    expect(wrapper.text()).toContain('Нет активных менеджеров')
  })

  it('при 400 показывает серверное сообщение и сохраняет введённые значения', async () => {
    shiftsApi.saveDefaultLeads.mockRejectedValue({
      response: { status: 400, data: { message: 'Недопустимый кандидат' } }
    })
    const wrapper = mountDialog()
    await flushPromises()
    wrapper.vm.items[0].selectedShiftLeadId = null
    wrapper.vm.items[1].selectedShiftLeadId = 'manager-1'

    await wrapper.vm.save()
    await flushPromises()

    expect(wrapper.vm.items[0].selectedShiftLeadId).toBeNull()
    expect(wrapper.vm.items[1].selectedShiftLeadId).toBe('manager-1')
    expect(wrapper.find('[data-test="default-leads-error"]').text()).toContain('Недопустимый кандидат')
    expect(shiftsApi.getDefaultLeads).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })

  it('при 409 сохраняет ввод и предлагает перезагрузить актуальные настройки', async () => {
    shiftsApi.saveDefaultLeads.mockRejectedValue({
      response: { status: 409, data: { message: 'График смен изменился' } }
    })
    const wrapper = mountDialog()
    await flushPromises()
    wrapper.vm.items[0].selectedShiftLeadId = 'manager-1'

    await wrapper.vm.save()
    await flushPromises()

    expect(wrapper.vm.items[0].selectedShiftLeadId).toBe('manager-1')
    expect(wrapper.find('[data-test="default-leads-error"]').text()).toContain('График смен изменился')
    const reload = wrapper.find('[data-test="reload-default-leads"]')
    expect(reload.exists()).toBe(true)

    await reload.trigger('click')
    await flushPromises()

    expect(shiftsApi.getDefaultLeads).toHaveBeenCalledTimes(2)
    expect(shiftPlansApi.getLeadCandidates).toHaveBeenCalledTimes(2)
    expect(wrapper.vm.items[0].selectedShiftLeadId).toBe('lead-1')
  })

  it('отмена не сохраняет ввод и следующее открытие загружает серверные значения заново', async () => {
    const wrapper = mountDialog()
    await flushPromises()
    wrapper.vm.items[0].selectedShiftLeadId = 'manager-1'

    await wrapper.find('[data-test="cancel-default-leads"]').trigger('click')

    expect(shiftsApi.saveDefaultLeads).not.toHaveBeenCalled()
    expect(wrapper.emitted('update:modelValue').at(-1)).toEqual([false])

    await wrapper.setProps({ modelValue: false })
    await wrapper.setProps({ modelValue: true })
    await flushPromises()

    expect(shiftsApi.getDefaultLeads).toHaveBeenCalledTimes(2)
    expect(wrapper.vm.items[0].selectedShiftLeadId).toBe('lead-1')
  })

  it('игнорирует поздний успешный save после закрытия и повторного открытия', async () => {
    const pendingSave = deferred()
    shiftsApi.saveDefaultLeads.mockReturnValue(pendingSave.promise)
    const wrapper = mountDialog()
    await flushPromises()
    wrapper.vm.items[0].selectedShiftLeadId = null
    const oldSave = wrapper.vm.save()

    const reopenedSettings = settings({
      items: defaultItems().map(item => item.position === 1
        ? {
            ...item,
            defaultShiftLeadId: 'manager-1',
            defaultShiftLeadName: 'Петров Пётр',
            defaultShiftLeadEmployeeId: '002'
          }
        : item),
      fallbackManager: null
    })
    shiftsApi.getDefaultLeads.mockResolvedValueOnce({ data: reopenedSettings })
    await wrapper.setProps({ modelValue: false })
    await wrapper.setProps({ modelValue: true })
    await flushPromises()
    wrapper.vm.items[1].selectedShiftLeadId = 'lead-1'

    pendingSave.resolve({ data: settings() })
    await oldSave
    await flushPromises()

    expect(wrapper.vm.items[0].selectedShiftLeadId).toBe('manager-1')
    expect(wrapper.vm.items[1].selectedShiftLeadId).toBe('lead-1')
    expect(wrapper.vm.fallbackManager).toBeNull()
    expect(wrapper.emitted('saved')).toBeUndefined()
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    expect(ElMessage.success).not.toHaveBeenCalled()
  })

  it('игнорирует позднюю ошибку и finally старого save в новом контексте', async () => {
    const oldRequest = deferred()
    const currentRequest = deferred()
    shiftsApi.saveDefaultLeads
      .mockReturnValueOnce(oldRequest.promise)
      .mockReturnValueOnce(currentRequest.promise)
    const wrapper = mountDialog()
    await flushPromises()
    const oldSave = wrapper.vm.save()

    const reopenedSettings = settings({
      items: defaultItems().map(item => item.position === 1
        ? {
            ...item,
            defaultShiftLeadId: 'manager-1',
            defaultShiftLeadName: 'Петров Пётр',
            defaultShiftLeadEmployeeId: '002'
          }
        : item)
    })
    shiftsApi.getDefaultLeads.mockResolvedValueOnce({ data: reopenedSettings })
    await wrapper.setProps({ modelValue: false })
    await wrapper.setProps({ modelValue: true })
    await flushPromises()
    const currentSave = wrapper.vm.save()
    expect(wrapper.vm.saving).toBe(true)

    oldRequest.reject({ response: { status: 409, data: { message: 'Старый конфликт' } } })
    await oldSave
    await flushPromises()

    expect(wrapper.vm.saving).toBe(true)
    expect(wrapper.vm.errorMessage).toBe('')
    expect(wrapper.vm.scheduleConflict).toBe(false)
    expect(wrapper.vm.items[0].selectedShiftLeadId).toBe('manager-1')
    expect(wrapper.emitted('saved')).toBeUndefined()

    currentRequest.resolve({ data: reopenedSettings })
    await currentSave
    await flushPromises()

    expect(wrapper.emitted('saved')).toHaveLength(1)
    expect(wrapper.emitted('update:modelValue').at(-1)).toEqual([false])
  })

  it('не применяет завершившийся save после unmount', async () => {
    const pendingSave = deferred()
    shiftsApi.saveDefaultLeads.mockReturnValue(pendingSave.promise)
    const wrapper = mountDialog()
    await flushPromises()
    const oldSave = wrapper.vm.save()

    wrapper.unmount()
    pendingSave.resolve({ data: settings() })
    await oldSave
    await flushPromises()

    expect(wrapper.emitted('saved')).toBeUndefined()
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    expect(ElMessage.success).not.toHaveBeenCalled()
  })

  it('при initial load failure блокирует save и не показывает ложный fallback', async () => {
    const pendingLoad = deferred()
    shiftsApi.getDefaultLeads.mockReturnValueOnce(pendingLoad.promise)
    const wrapper = mountDialog()
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-test="save-default-leads"]').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).not.toContain('Нет активных менеджеров')

    pendingLoad.reject({ response: { status: 500, data: { message: 'Ошибка загрузки' } } })
    await flushPromises()

    expect(wrapper.vm.loaded).toBe(false)
    expect(wrapper.vm.items).toEqual([])
    expect(wrapper.find('[data-test="save-default-leads"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-test="default-leads-error"]').text()).toContain('Ошибка загрузки')
    expect(wrapper.text()).not.toContain('Нет активных менеджеров')

    await wrapper.vm.save()
    expect(shiftsApi.saveDefaultLeads).not.toHaveBeenCalled()
  })

  it('при reload failure очищает stale значения и блокирует select и save', async () => {
    const wrapper = mountDialog()
    await flushPromises()
    expect(wrapper.vm.items).toHaveLength(3)

    shiftsApi.getDefaultLeads.mockRejectedValueOnce({
      response: { status: 500, data: { message: 'Не удалось перезагрузить' } }
    })
    await wrapper.vm.load()
    await flushPromises()

    expect(wrapper.vm.loaded).toBe(false)
    expect(wrapper.vm.items).toEqual([])
    expect(wrapper.vm.candidates).toEqual([])
    expect(wrapper.findAllComponents({ name: 'ElSelect' })).toHaveLength(0)
    expect(wrapper.find('[data-test="save-default-leads"]').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).not.toContain('первый активный менеджер:')
    expect(wrapper.text()).not.toContain('Нет активных менеджеров')

    await wrapper.vm.save()
    expect(shiftsApi.saveDefaultLeads).not.toHaveBeenCalled()
  })
})
