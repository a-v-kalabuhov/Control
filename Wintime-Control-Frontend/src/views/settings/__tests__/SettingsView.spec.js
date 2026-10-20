// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import SettingsView from '../SettingsView.vue'

vi.mock('@/api/settings', () => ({
  settingsApi: {
    getPlanningDefaults: vi.fn(() => Promise.resolve({ data: {
      setupMinutes: 90, releaseBufferFixedMinutes: 30, releaseBufferPercent: 10, conflictThresholdMinutes: 25, updatedAt: null
    } })),
    updatePlanningDefaults: vi.fn(() => Promise.resolve({ data: {} }))
  }
}))

vi.mock('@/api/planning', () => ({
  planningApi: { getConflicts: vi.fn(() => Promise.resolve({ data: { taskCount: 0, items: [] } })) }
}))

import { settingsApi } from '@/api/settings'
import { planningApi } from '@/api/planning'

function mountView() {
  return mount(SettingsView, { global: { plugins: [ElementPlus] } })
}

describe('SettingsView — умолчания планирования', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  it('загружает умолчания с сервера', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(settingsApi.getPlanningDefaults).toHaveBeenCalled()
    expect(wrapper.find('[data-test="setup-minutes"] input').element.value).toBe('90')
    expect(wrapper.find('[data-test="buffer-fixed-minutes"] input').element.value).toBe('30')
    expect(wrapper.find('[data-test="buffer-percent"] input').element.value).toBe('10')
  })

  it('«Сохранить» отправляет четыре значения', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.defaults.setupMinutes = 120

    await wrapper.find('[data-test="save"]').trigger('click')
    await flushPromises()

    expect(settingsApi.updatePlanningDefaults).toHaveBeenCalledWith({
      setupMinutes: 120, releaseBufferFixedMinutes: 30, releaseBufferPercent: 10,
      conflictThresholdMinutes: 25
    })
  })

  it('после сохранения обновляет конфликты — порог действует сразу', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(planningApi.getConflicts).not.toHaveBeenCalled()

    await wrapper.find('[data-test="save"]').trigger('click')
    await flushPromises()

    expect(planningApi.getConflicts).toHaveBeenCalledTimes(1)
  })

  it('поясняет, что уже созданные задания не меняются', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Уже созданные задания не меняются')
  })

  it('показывает и сохраняет порог конфликта', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.find('[data-test="conflict-threshold"] input').element.value).toBe('25')

    wrapper.vm.defaults.conflictThresholdMinutes = 40
    await wrapper.find('[data-test="save"]').trigger('click')
    await flushPromises()

    expect(settingsApi.updatePlanningDefaults).toHaveBeenCalledWith(
      expect.objectContaining({ conflictThresholdMinutes: 40 }))
  })
})
