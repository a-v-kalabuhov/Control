// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import ShiftClosureCards from '../ShiftClosureCards.vue'

const manualFields = [
  'actualPartWeightGrams', 'manualFullCycleSeconds', 'actualPackagingUnit',
  'packagingPlaces', 'manualGoodQuantity', 'manualDefectQuantity'
]

function entry(overrides = {}) {
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

function mountCards({ report: value = report(), fieldErrors = {}, isDirty = false } = {}) {
  return mount(ShiftClosureCards, {
    props: { report: value, loading: false, saving: false, fieldErrors, isDirty },
    global: { plugins: [ElementPlus] }
  })
}

function input(wrapper, field, taskId = 'task-1') {
  return wrapper.findComponent(`[data-manual="${taskId}:${field}"]`)
}

describe('ShiftClosureCards', () => {
  let wrapper

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
  })

  it('показывает те же данные и проверки, что desktop, и шесть manual controls', async () => {
    wrapper = mountCards()

    for (const label of [
      'ТПА', 'Наименование', 'Остаток на начало смены', 'Сырьё', 'Цвет/краситель',
      'Вес', 'Норма', 'Изделие', 'Цикл', 'Автоматический', 'Ручной',
      'Тарная единица', 'Количество мест', 'Количество гнёзд', 'План', 'Выпуск',
      'Факт отлитых (годные)', 'Брак', 'Принято на склад', 'Номер', 'Литейщик',
      'Автовыпуск литейщика', 'Подпись', 'Расход'
    ]) expect(wrapper.text()).toContain(label)

    expect(wrapper.findAllComponents({ name: 'ElInputNumber' })).toHaveLength(6)
    for (const field of manualFields) expect(input(wrapper, field).exists()).toBe(true)
    expect(wrapper.text()).toContain('ТПА-1')
    expect(wrapper.text()).toContain('Пластик 100%')
    expect(wrapper.text()).toContain('Краситель 1%')
    expect(wrapper.get('[data-computed="task-1:automaticOutputQuantity"]').text()).toBe('10')
    expect(input(wrapper, 'packagingPlaces').props('precision')).toBe(2)
    expect(wrapper.get('[data-fallback="task-1"]').text()).toContain('Ручной ввод')
    expect(wrapper.text()).toContain('001')
    expect(wrapper.text()).toContain('Иванов Иван')
    expect(wrapper.get('[data-share="task-1:op-1"]').text()).toBe('6')
    expect(wrapper.get('[data-cell="task-1:warehouse"]').text()).toBe('')
    expect(wrapper.get('[data-cell="task-1:signature"]').text()).toBe('')
  })

  it('передаёт ручной ввод в общий editor без собственной валидации', async () => {
    wrapper = mountCards()

    for (const [index, field] of manualFields.entries()) {
      input(wrapper, field).vm.$emit('update:modelValue', index + 1)
      await wrapper.vm.$nextTick()
      expect(wrapper.emitted('update-manual').at(-1)).toEqual(['task-1', field, index + 1])
    }
    expect(wrapper.emitted('update-manual')).toHaveLength(6)
  })

  it('показывает server field error у соответствующего поля и dirty status', () => {
    wrapper = mountCards({
      isDirty: true,
      fieldErrors: { 'task-1:actualPartWeightGrams': 'Поле обязательно для заполнения' }
    })

    expect(wrapper.get('[data-error="task-1:actualPartWeightGrams"]').text()).toContain('обязатель')
    expect(wrapper.text()).toContain('Есть несохранённые изменения')
  })

  it('save и close делегируются родителю для пакетного сохранения через общий editor', async () => {
    wrapper = mountCards()

    await wrapper.get('[data-action="save"]').trigger('click')
    await wrapper.get('[data-action="close"]').trigger('click')

    expect(wrapper.emitted('save')).toHaveLength(1)
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('Closed read-only и скрывает save/close', () => {
    wrapper = mountCards({ report: report({ state: 'Closed' }) })

    for (const control of wrapper.findAllComponents({ name: 'ElInputNumber' })) {
      expect(control.props('disabled')).toBe(true)
    }
    expect(wrapper.find('[data-action="save"]').exists()).toBe(false)
    expect(wrapper.find('[data-action="close"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Рапорт закрыт')
  })
})
