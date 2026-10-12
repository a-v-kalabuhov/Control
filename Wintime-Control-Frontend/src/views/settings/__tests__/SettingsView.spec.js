// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import SettingsView from '../SettingsView.vue'

vi.mock('@/api/settings', () => ({
  settingsApi: {
    getPlanningDefaults: vi.fn(() => Promise.resolve({ data: {
      setupMinutes: 90, releaseBufferFixedMinutes: 30, releaseBufferPercent: 10, updatedAt: null
    } })),
    updatePlanningDefaults: vi.fn(() => Promise.resolve({ data: {} }))
  }
}))

import { settingsApi } from '@/api/settings'

function mountView() {
  return mount(SettingsView, { global: { plugins: [ElementPlus] } })
}

describe('SettingsView — умолчания планирования', () => {
  beforeEach(() => vi.clearAllMocks())

  it('загружает умолчания с сервера', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(settingsApi.getPlanningDefaults).toHaveBeenCalled()
    expect(wrapper.find('[data-test="setup-minutes"] input').element.value).toBe('90')
    expect(wrapper.find('[data-test="buffer-fixed-minutes"] input').element.value).toBe('30')
    expect(wrapper.find('[data-test="buffer-percent"] input').element.value).toBe('10')
  })

  it('«Сохранить» отправляет только три значения', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.defaults.setupMinutes = 120

    await wrapper.find('[data-test="save"]').trigger('click')
    await flushPromises()

    expect(settingsApi.updatePlanningDefaults).toHaveBeenCalledWith({
      setupMinutes: 120, releaseBufferFixedMinutes: 30, releaseBufferPercent: 10
    })
  })

  it('поясняет, что уже созданные задания не меняются', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Уже созданные задания не меняются')
  })
})
