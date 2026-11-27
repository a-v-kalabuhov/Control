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

  it('помечает окончание ровно в полночь в просмотре и редакторе', async () => {
    shiftsApi.getShifts.mockResolvedValue({ data: [{
      number: 1,
      startTime: '08:00',
      endTime: '00:00',
      startMinutes: 480,
      durationMinutes: 960,
      breakStartTime: '12:00',
      breakEndTime: '12:00',
      breakDurationMinutes: 0,
      timeZoneId: 'Europe/Moscow'
    }] })
    const wrapper = mountDictionary('Manager')
    await flushPromises()

    expect(wrapper.vm.isNightShift(wrapper.vm.shifts[0])).toBe(true)
    wrapper.vm.openEditDialog()
    expect(wrapper.vm.isNightShiftEdit(wrapper.vm.editShifts[0])).toBe(true)
    wrapper.unmount()
  })

  it.each(['Manager', 'Admin', 'Observer', 'ShiftLead', 'Operator', 'Adjuster'])('не показывает %s настройку старших по умолчанию', async role => {
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
