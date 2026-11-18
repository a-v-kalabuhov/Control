// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import ShiftClosureCorrectionsDialog from '../ShiftClosureCorrectionsDialog.vue'
import * as api from '@/api/shiftClosures'
import { useAuthStore } from '@/stores/auth'

vi.mock('@/api/shiftClosures', () => ({
  correct: vi.fn(),
  getCorrections: vi.fn()
}))

const historyRow = {
  id: 'correction-1', changedByName: 'Менеджер Иванов',
  changedAt: '2026-11-18T12:30:00Z', reason: 'Уточнение по акту',
  changes: [{ taskId: 'task-1', field: 'manualGoodQuantity', oldValue: 7, newValue: 8 }]
}

function closedReport(overrides = {}) {
  return {
    id: 'closure-1', shiftPlanItemId: 'item-1', state: 'Closed', entries: [{
      taskId: 'task-1', immName: 'ТПА-1', productName: 'Корпус',
      actualPartWeightGrams: 4.2, manualFullCycleSeconds: 58, actualPackagingUnit: 24,
      packagingPlaces: 1.25, manualGoodQuantity: 8, manualDefectQuantity: 1,
      automaticOutputQuantity: 10
    }], ...overrides
  }
}

function mountDialog(role = 'Manager', report = closedReport()) {
  const auth = useAuthStore()
  auth.isAuthenticated = true
  auth.userRole = role
  const reloadReport = vi.fn().mockResolvedValue(true)
  const wrapper = mount(ShiftClosureCorrectionsDialog, {
    props: { report, reloadReport },
    global: { plugins: [ElementPlus] }
  })
  return { wrapper, reloadReport }
}

async function open(wrapper, selector = '[data-test="open-corrections"]') {
  await wrapper.get(selector).trigger('click')
  await flushPromises()
}

describe('ShiftClosureCorrectionsDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    api.correct.mockResolvedValue({})
    api.getCorrections.mockResolvedValue({ data: [historyRow] })
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
    document.body.innerHTML = ''
  })

  it.each(['Manager', 'Admin'])('разрешает %s открыть исправление закрытого рапорта', async role => {
    const { wrapper } = mountDialog(role)
    await open(wrapper)
    expect(api.getCorrections).toHaveBeenCalledWith('closure-1')
    expect(wrapper.text()).toContain('Исправить показатели')
  })

  it('не даёт ShiftLead действие исправления, но показывает историю его закрытой смены', async () => {
    const { wrapper } = mountDialog('ShiftLead')
    expect(wrapper.find('[data-test="open-corrections"]').exists()).toBe(false)

    await open(wrapper, '[data-test="open-correction-history"]')

    expect(api.getCorrections).toHaveBeenCalledWith('closure-1')
    expect(wrapper.text()).toContain('Менеджер Иванов')
    expect(wrapper.text()).toContain('Уточнение по акту')
    expect(wrapper.text()).toContain('7')
    expect(wrapper.text()).toContain('8')
    expect(wrapper.find('[data-test="correction-reason"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="submit-correction"]').exists()).toBe(false)
    expect(api.correct).not.toHaveBeenCalled()
  })

  it('не открывает исправление для динамического Draft', () => {
    const { wrapper } = mountDialog('Manager', closedReport({ id: null, state: 'Draft' }))
    expect(wrapper.find('[data-test="open-corrections"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="open-correction-history"]').exists()).toBe(false)
  })

  it.each(['', '   '])('блокирует submit с пустой причиной %j', async reason => {
    const { wrapper } = mountDialog()
    await open(wrapper)
    await wrapper.get('[data-test="correction-reason"]').setValue(reason)

    await wrapper.get('[data-test="submit-correction"]').trigger('click')
    await flushPromises()

    expect(api.correct).not.toHaveBeenCalled()
  })

  it('отправляет Reason и только shiftTaskId с шестью manual fields, затем обновляет report и history', async () => {
    const { wrapper, reloadReport } = mountDialog()
    await open(wrapper)
    wrapper.getComponent('[data-correction-manual="task-1:manualGoodQuantity"]').vm.$emit('update:modelValue', 9)
    await wrapper.vm.$nextTick()
    await wrapper.get('[data-test="correction-reason"]').setValue('  Исправление по акту  ')

    await wrapper.get('[data-test="submit-correction"]').trigger('click')
    await flushPromises()

    expect(api.correct).toHaveBeenCalledWith('closure-1', 'Исправление по акту', [{
      shiftTaskId: 'task-1', actualPartWeightGrams: 4.2, manualFullCycleSeconds: 58,
      actualPackagingUnit: 24, packagingPlaces: 1.25, manualGoodQuantity: 9, manualDefectQuantity: 1
    }])
    expect(Object.keys(api.correct.mock.calls[0][2][0])).toHaveLength(7)
    expect(reloadReport).toHaveBeenCalledTimes(1)
    expect(api.getCorrections).toHaveBeenCalledTimes(2)
  })

  it('показывает серверную ошибку у соответствующего поля', async () => {
    api.correct.mockRejectedValueOnce({ response: { status: 400, data: {
      errors: [{ taskId: 'task-1', field: 'actualPartWeightGrams', code: 'required' }]
    } } })
    const { wrapper } = mountDialog()
    await open(wrapper)
    await wrapper.get('[data-test="correction-reason"]').setValue('Правка')

    await wrapper.get('[data-test="submit-correction"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-correction-error="task-1:actualPartWeightGrams"]').text()).toContain('обязатель')
  })

  it('при 409 перезагружает report и history без повторного PATCH', async () => {
    api.correct.mockRejectedValueOnce({ response: { status: 409, data: { message: 'Рапорт изменён' } } })
    const { wrapper, reloadReport } = mountDialog()
    await open(wrapper)
    await wrapper.get('[data-test="correction-reason"]').setValue('Правка')

    await wrapper.get('[data-test="submit-correction"]').trigger('click')
    await flushPromises()

    expect(reloadReport).toHaveBeenCalledTimes(1)
    expect(api.getCorrections).toHaveBeenCalledTimes(2)
    expect(api.correct).toHaveBeenCalledTimes(1)
    expect(ElMessage.error).toHaveBeenCalled()
  })
})
