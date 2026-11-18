// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import ShiftClosureTable from '../ShiftClosureTable.vue'
import * as api from '@/api/shiftClosures'

vi.mock('@/api/shiftClosures', () => ({
  getByItem: vi.fn(),
  saveDraft: vi.fn(),
  close: vi.fn()
}))

export function entry(overrides = {}) {
  return {
    taskId: 'task-1', immId: 'imm-1', immName: 'ТПА-1', productName: 'Корпус',
    remainingAtStart: 12, plasticIngredients: 'Пластик 100%', colorantIngredients: 'Краситель 1%',
    normPartWeightGrams: 4, actualPartWeightGrams: 4.2,
    plannedFullCycleSeconds: 60, automaticAverageFullCycleSeconds: 55.5, manualFullCycleSeconds: 58,
    taskPackagingUnit: 20, actualPackagingUnit: 24, packagingPlaces: 1.25,
    cavities: 2, shiftPlanQuantity: 100, plannedMaterialWeightGrams: 450,
    automaticOutputQuantity: 10, manualGoodQuantity: 8, manualDefectQuantity: 1,
    hasOperators: true, isManualFallbackRequired: true,
    operatorTotals: [{ operatorId: 'op-1', operatorFullName: 'Иванов Иван', operatorEmployeeId: '001', quantity: 6 }],
    outputShares: [{ operatorId: 'op-1', operatorFullName: 'Иванов Иван', operatorEmployeeId: '001', quantity: 6 }],
    ...overrides
  }
}

function report(overrides = {}) {
  return {
    id: 'closure-1', shiftPlanItemId: 'item-1', state: 'Draft',
    shiftDate: '2026-11-18', startAtUtc: '2026-11-18T05:00:00Z', endAtUtc: '2026-11-18T17:00:00Z',
    shiftLeadName: 'Старший Смены', entries: [entry()], ...overrides
  }
}

function response(value) {
  return { data: value }
}

function mountTable({ entries = [entry()], state = 'Draft' } = {}) {
  api.getByItem.mockResolvedValue(response(report({ entries, state })))
  return mount(ShiftClosureTable, {
    props: { itemId: 'item-1' },
    global: { plugins: [ElementPlus] }
  })
}

function input(wrapper, field, taskId = 'task-1') {
  return wrapper.findComponent(`[data-manual="${taskId}:${field}"]`)
}

describe('ShiftClosureTable', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
    document.body.innerHTML = ''
  })

  it('показывает все колонки спецификации и grouped weight/cycle/output', async () => {
    const wrapper = mountTable()
    await flushPromises()

    for (const label of [
      'ТПА', 'Наименование', 'Остаток на начало смены', 'Сырьё', 'Цвет/краситель',
      'Вес', 'Цикл', 'Тарная единица', 'Количество мест', 'Количество гнёзд', 'План',
      'Выпуск', 'Принято на склад', 'Номер', 'Литейщик', 'Автовыпуск литейщика', 'Подпись', 'Расход',
      'Норма', 'Изделие', 'Автоматический', 'Ручной', 'Факт отлитых (годные)', 'Брак'
    ]) expect(wrapper.text()).toContain(label)
    expect(wrapper.find('[data-group="weight"]').exists()).toBe(true)
    expect(wrapper.find('[data-group="cycle"]').exists()).toBe(true)
    expect(wrapper.find('[data-group="output"]').exists()).toBe(true)
  })

  it('оставляет automatic/план/остаток/recipe/cavities read-only и редактирует шесть manual fields', async () => {
    const wrapper = mountTable()
    await flushPromises()

    expect(wrapper.findAllComponents({ name: 'ElInputNumber' })).toHaveLength(6)
    for (const field of [
      'actualPartWeightGrams', 'manualFullCycleSeconds', 'actualPackagingUnit',
      'packagingPlaces', 'manualGoodQuantity', 'manualDefectQuantity'
    ]) expect(input(wrapper, field).exists()).toBe(true)
    expect(wrapper.text()).toContain('ТПА-1')
    expect(wrapper.text()).toContain('Пластик 100%')
    expect(wrapper.text()).toContain('Краситель 1%')
    expect(wrapper.get('[data-computed="task-1:automaticOutputQuantity"]').text()).toBe('10')
    expect(input(wrapper, 'packagingPlaces').props('precision')).toBe(2)
  })

  it('помечает fallback, показывает Operators/доли и пустые Warehouse/Signature', async () => {
    const wrapper = mountTable()
    await flushPromises()

    expect(wrapper.get('[data-fallback="task-1"]').text()).toContain('Ручной ввод')
    expect(wrapper.text()).toContain('001')
    expect(wrapper.text()).toContain('Иванов Иван')
    expect(wrapper.get('[data-share="task-1:op-1"]').text()).toBe('6')
    expect(wrapper.get('[data-cell="task-1:warehouse"]').text()).toBe('')
    expect(wrapper.get('[data-cell="task-1:signature"]').text()).toBe('')
  })

  it('save отправляет только manual fields', async () => {
    const wrapper = mountTable()
    await flushPromises()
    api.saveDraft.mockImplementation(async (_itemId, entries) => response(report({
      entries: [entry({ ...entries[0] })]
    })))
    input(wrapper, 'manualGoodQuantity').vm.$emit('update:modelValue', 9)
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-action="save"]').trigger('click')
    await flushPromises()

    expect(api.saveDraft).toHaveBeenCalledWith('item-1', [{
      shiftTaskId: 'task-1', actualPartWeightGrams: 4.2, manualFullCycleSeconds: 58,
      actualPackagingUnit: 24, packagingPlaces: 1.25, manualGoodQuantity: 9, manualDefectQuantity: 1
    }])
    expect(Object.keys(api.saveDraft.mock.calls[0][1][0])).toHaveLength(7)
  })

  it('maps_server_field_error_to_the_matching_task_input', async () => {
    const wrapper = mountTable({ entries: [entry({ automaticOutputQuantity: 2 })] })
    await flushPromises()
    api.saveDraft.mockRejectedValue({ response: { data: {
      code: 'required', errors: [{ taskId: 'task-1', field: 'actualPartWeightGrams', code: 'required' }]
    } } })

    await wrapper.find('[data-action="save"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-error="task-1:actualPartWeightGrams"]').text()).toContain('обязатель')
  })

  it('Closed отключает все поля редактирования и close', async () => {
    const wrapper = mountTable({ state: 'Closed' })
    await flushPromises()

    for (const control of wrapper.findAllComponents({ name: 'ElInputNumber' })) {
      expect(control.props('disabled')).toBe(true)
    }
    expect(wrapper.get('[data-action="close"]').attributes('disabled')).toBeDefined()
    await wrapper.find('[data-action="close"]').trigger('click')
    expect(api.close).not.toHaveBeenCalled()
  })

  it('early close открывает dialog и повторяет запрос только после подтверждения', async () => {
    const wrapper = mountTable()
    await flushPromises()
    api.close
      .mockRejectedValueOnce({ response: { status: 400, data: {
        code: 'earlyCloseConfirmationRequired', message: 'Нужно подтверждение'
      } } })
      .mockResolvedValueOnce(response(report({ state: 'Closed' })))

    await wrapper.find('[data-action="close"]').trigger('click')
    await flushPromises()

    expect(api.close).toHaveBeenCalledWith('item-1', false)
    expect(api.close).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('Закрыть смену досрочно')

    await wrapper.find('[data-action="confirm-early-close"]').trigger('click')
    await flushPromises()

    expect(api.close).toHaveBeenNthCalledWith(2, 'item-1', true)
  })

  it('отмена early close не повторяет запрос', async () => {
    const wrapper = mountTable()
    await flushPromises()
    api.close.mockRejectedValueOnce({ response: { status: 400, data: {
      code: 'earlyCloseConfirmationRequired', message: 'Нужно подтверждение'
    } } })

    await wrapper.find('[data-action="close"]').trigger('click')
    await flushPromises()
    await wrapper.find('[data-action="cancel-early-close"]').trigger('click')
    await flushPromises()

    expect(api.close).toHaveBeenCalledTimes(1)
  })
})
