// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import ShiftClosureView from '../ShiftClosureView.vue'
import * as api from '@/api/shiftClosures'
import { tasksApi } from '@/api/tasks'
import { personnelApi } from '@/api/personnel'
import { shiftStaffingApi } from '@/api/shiftStaffing'

const { push, route } = vi.hoisted(() => ({
  push: vi.fn(),
  route: { params: { itemId: 'item-1' } }
}))

vi.mock('vue-router', async importOriginal => ({
  ...await importOriginal(),
  useRoute: () => route,
  useRouter: () => ({ push })
}))

vi.mock('@/api/shiftClosures', () => ({
  getByItem: vi.fn(), saveDraft: vi.fn(), close: vi.fn()
}))
vi.mock('@/api/tasks', () => ({ tasksApi: { getResults: vi.fn(), getTasks: vi.fn() } }))
vi.mock('@/api/personnel', () => ({ personnelApi: { getAll: vi.fn(), getPersonnel: vi.fn() } }))
vi.mock('@/api/shiftStaffing', () => ({
  shiftStaffingApi: { history: vi.fn(), nearest: vi.fn(), item: vi.fn() }
}))

function entry(overrides = {}) {
  return {
    taskId: 'task-1', immId: 'imm-1', immName: 'ТПА-1', productName: 'Корпус',
    remainingAtStart: 0, plasticIngredients: 'Пластик 100%', colorantIngredients: '',
    normPartWeightGrams: 4, actualPartWeightGrams: 4,
    plannedFullCycleSeconds: 60, automaticAverageFullCycleSeconds: 55, manualFullCycleSeconds: 58,
    taskPackagingUnit: 20, actualPackagingUnit: 24, packagingPlaces: 1.25,
    cavities: 2, shiftPlanQuantity: 100, plannedMaterialWeightGrams: 450,
    automaticOutputQuantity: 10, manualGoodQuantity: 8, manualDefectQuantity: 1,
    hasOperators: true, isManualFallbackRequired: false, operatorTotals: [], outputShares: [],
    ...overrides
  }
}

function report(overrides = {}) {
  return {
    id: 'closure-1', shiftPlanItemId: 'item-1', state: 'Draft', shiftDate: '2026-11-18',
    startAtUtc: '2026-11-18T05:00:00Z', endAtUtc: '2026-11-18T17:00:00Z',
    shiftLeadName: 'Старший Смены', entries: [entry()], ...overrides
  }
}

function response(value) {
  return { data: value }
}

function mountView() {
  return mount(ShiftClosureView, { global: { plugins: [ElementPlus] } })
}

function input(wrapper, field) {
  return wrapper.findComponent(`[data-manual="task-1:${field}"]`)
}

describe('ShiftClosureView', () => {
  let wrapper

  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-18T12:00:00Z'))
    vi.clearAllMocks()
    route.params.itemId = 'item-1'
    api.getByItem.mockResolvedValue(response(report()))
    api.saveDraft.mockImplementation(async (_itemId, entries) => response(report({
      entries: [entry({ ...entries[0] })]
    })))
    api.close.mockResolvedValue(response(report({ state: 'Closed' })))
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    vi.useRealTimers()
    vi.restoreAllMocks()
    document.body.innerHTML = ''
  })

  it('загружает рапорт выбранного item через общий editor и возвращается к смене', async () => {
    wrapper = mountView()
    await flushPromises()

    expect(api.getByItem).toHaveBeenCalledWith('item-1')
    expect(wrapper.findComponent({ name: 'ShiftClosureCards' }).exists()).toBe(true)
    expect(wrapper.text()).toContain('Сменный рапорт')
    expect(wrapper.text()).toContain('ТПА-1')

    await wrapper.get('[data-test="back-to-item"]').trigger('click')
    expect(push).toHaveBeenCalledWith('/shift-lead/items/item-1')
    expect(tasksApi.getTasks).not.toHaveBeenCalled()
    expect(tasksApi.getResults).not.toHaveBeenCalled()
    expect(personnelApi.getAll).not.toHaveBeenCalled()
    expect(personnelApi.getPersonnel).not.toHaveBeenCalled()
    expect(shiftStaffingApi.history).not.toHaveBeenCalled()
  })

  it('60-second polling при dirty не вызывает load и не затирает ручной ввод', async () => {
    wrapper = mountView()
    await flushPromises()
    input(wrapper, 'manualGoodQuantity').vm.$emit('update:modelValue', 12)
    await wrapper.vm.$nextTick()

    await vi.advanceTimersByTimeAsync(60_000)
    await flushPromises()

    expect(api.getByItem).toHaveBeenCalledTimes(1)
    expect(input(wrapper, 'manualGoodQuantity').props('modelValue')).toBe(12)
    expect(wrapper.text()).toContain('Есть несохранённые изменения')
  })

  it('завершённая незакрытая смена допускает ручной ввод, save отправляет batch и обновляет computed', async () => {
    api.getByItem.mockResolvedValue(response(report({ endAtUtc: '2026-11-18T11:00:00Z' })))
    wrapper = mountView()
    await flushPromises()
    input(wrapper, 'manualGoodQuantity').vm.$emit('update:modelValue', 12)
    await wrapper.vm.$nextTick()
    api.getByItem.mockResolvedValueOnce(response(report({
      entries: [entry({ manualGoodQuantity: 12, automaticOutputQuantity: 15 })]
    })))

    await wrapper.get('[data-action="save"]').trigger('click')
    await flushPromises()

    expect(api.saveDraft).toHaveBeenCalledWith('item-1', [{
      shiftTaskId: 'task-1', actualPartWeightGrams: 4, manualFullCycleSeconds: 58,
      actualPackagingUnit: 24, packagingPlaces: 1.25, manualGoodQuantity: 12, manualDefectQuantity: 1
    }])
    expect(api.getByItem).toHaveBeenCalledTimes(2)
    expect(wrapper.get('[data-computed="task-1:automaticOutputQuantity"]').text()).toBe('15')
    expect(wrapper.text()).not.toContain('Есть несохранённые изменения')
  })

  it('показывает серверную ошибку поля и сохраняет введённое значение', async () => {
    wrapper = mountView()
    await flushPromises()
    input(wrapper, 'actualPartWeightGrams').vm.$emit('update:modelValue', null)
    await wrapper.vm.$nextTick()
    api.saveDraft.mockRejectedValueOnce({ response: { status: 400, data: {
      errors: [{ taskId: 'task-1', field: 'actualPartWeightGrams', code: 'required' }]
    } } })

    await wrapper.get('[data-action="save"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-error="task-1:actualPartWeightGrams"]').text()).toContain('обязатель')
    expect(input(wrapper, 'actualPartWeightGrams').props('modelValue')).toBeNull()
    expect(wrapper.text()).toContain('Есть несохранённые изменения')
  })

  it('earlyCloseConfirmationRequired ведёт через подтверждение к close(true) и Closed read-only', async () => {
    api.close
      .mockRejectedValueOnce({ response: { status: 400, data: {
        code: 'earlyCloseConfirmationRequired', message: 'Нужно подтверждение'
      } } })
      .mockResolvedValueOnce(response(report({ state: 'Closed' })))
    wrapper = mountView()
    await flushPromises()

    await wrapper.get('[data-action="close"]').trigger('click')
    await flushPromises()

    expect(api.close).toHaveBeenCalledWith('item-1', false)
    expect(api.close).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('Закрыть смену досрочно?')

    await wrapper.get('[data-action="confirm-early-close"]').trigger('click')
    await flushPromises()

    expect(api.close).toHaveBeenNthCalledWith(2, 'item-1', true)
    expect(wrapper.text()).toContain('Рапорт закрыт')
    expect(wrapper.find('[data-action="save"]').exists()).toBe(false)
    expect(wrapper.find('[data-action="close"]').exists()).toBe(false)
    for (const control of wrapper.findAllComponents({ name: 'ElInputNumber' })) {
      expect(control.props('disabled')).toBe(true)
    }
  })
})
