// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import ShiftsDictionary from '../ShiftsDictionary.vue'
import ShiftDefaultLeadsDialog from '@/components/shifts/ShiftDefaultLeadsDialog.vue'
import { shiftsApi } from '@/api/shifts'
import { shiftPlansApi } from '@/api/shiftPlans'
import { useAuthStore } from '@/stores/auth'

vi.mock('@/api/shifts', () => ({
  shiftsApi: {
    getShifts: vi.fn(),
    saveShifts: vi.fn(),
    getDefaultLeads: vi.fn(),
    saveDefaultLeads: vi.fn()
  }
}))

vi.mock('@/api/shiftPlans', () => ({
  shiftPlansApi: { getLeadCandidates: vi.fn() }
}))

function mountDictionary(role) {
  setActivePinia(createPinia())
  const authStore = useAuthStore()
  authStore.user = { id: `${role.toLowerCase()}-1`, role }
  authStore.userRole = role
  authStore.isAuthenticated = true

  return mount(ShiftsDictionary, {
    global: {
      plugins: [ElementPlus],
      stubs: { Edit: true, Delete: true, Plus: true }
    }
  })
}

describe('ShiftsDictionary default leads', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    shiftsApi.getShifts.mockResolvedValue({ data: [] })
    shiftsApi.getDefaultLeads.mockResolvedValue({
      data: { items: [], fallbackManager: null }
    })
    shiftPlansApi.getLeadCandidates.mockResolvedValue({ data: [] })
  })

  it.each(['Manager', 'Admin'])('показывает %s отдельную настройку и загружает её только по кнопке', async role => {
    const wrapper = mountDictionary(role)
    await flushPromises()

    const button = wrapper.find('[data-test="open-default-leads"]')
    expect(button.exists()).toBe(true)
    expect(button.text()).toContain('Настройка')
    expect(shiftsApi.getDefaultLeads).not.toHaveBeenCalled()
    expect(shiftPlansApi.getLeadCandidates).not.toHaveBeenCalled()
    expect(wrapper.text()).not.toContain('Настройка графика смен')

    await button.trigger('click')
    await flushPromises()

    expect(wrapper.findComponent(ShiftDefaultLeadsDialog).props('modelValue')).toBe(true)
    expect(wrapper.text()).toContain('Настройка графика смен')
    expect(shiftsApi.getDefaultLeads).toHaveBeenCalledTimes(1)
    expect(shiftPlansApi.getLeadCandidates).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it.each(['Observer', 'ShiftLead', 'Operator', 'Adjuster'])('не показывает %s настройку и не вызывает её APIs', async role => {
    const wrapper = mountDictionary(role)
    await flushPromises()

    expect(wrapper.find('[data-test="open-default-leads"]').exists()).toBe(false)
    expect(wrapper.findComponent(ShiftDefaultLeadsDialog).exists()).toBe(false)
    expect(shiftsApi.getShifts).toHaveBeenCalledTimes(1)
    expect(shiftsApi.getDefaultLeads).not.toHaveBeenCalled()
    expect(shiftsApi.saveDefaultLeads).not.toHaveBeenCalled()
    expect(shiftPlansApi.getLeadCandidates).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
