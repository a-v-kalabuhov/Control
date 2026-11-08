// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal()),
  useRoute: () => ({ path: '/', meta: {} })
}))
vi.mock('@/router', () => ({ default: { push: vi.fn() } }))
vi.mock('@/api/planning', () => ({
  planningApi: { getConflicts: vi.fn().mockResolvedValue({ data: { items: [] } }) }
}))
vi.mock('@/stores/appInfo', () => ({
  useAppInfoStore: () => ({ demoMode: false, load: vi.fn() })
}))

import DefaultLayout from '../DefaultLayout.vue'
import { useAuthStore } from '@/stores/auth'
import { usePlanConflictsStore } from '@/stores/planConflicts'

const stubs = {
  'router-view': true,
  'el-menu': { template: '<div><slot /></div>' },
  'el-menu-item': { template: '<div><slot /></div>' },
  'el-sub-menu': { template: '<div><slot name="title" /><slot /></div>' },
  'el-icon': true,
  'el-avatar': true,
  'el-button': true,
  Monitor: true,
  TrendCharts: true,
  Calendar: true,
  Notebook: true,
  DataLine: true,
  Setting: true,
  Clock: true,
  Tools: true,
  PlanConflictsBadge: true
}

describe('DefaultLayout: план работ на смену', () => {
  let wrapper

  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  afterEach(() => {
    wrapper?.unmount()
    usePlanConflictsStore().stop()
  })

  function mountAs(role) {
    const auth = useAuthStore()
    auth.isAuthenticated = true
    auth.user = { role, fullName: 'Тест' }
    wrapper = mount(DefaultLayout, { global: { stubs } })
  }

  it.each(['Manager', 'Admin'])('показывает пункт меню роли %s', role => {
    mountAs(role)
    expect(wrapper.text()).toContain('План работ на смену')
  })

  it('не показывает пункт меню наблюдателю', () => {
    mountAs('Observer')
    expect(wrapper.text()).not.toContain('План работ на смену')
  })

  it.each(['Manager', 'Admin'])('показывает план смен роли %s, сохраняя план работ PZP-18', role => {
    mountAs(role)
    expect(wrapper.text()).toContain('План смен')
    expect(wrapper.text()).toContain('План работ на смену')
  })

  it('не показывает план смен наблюдателю', () => {
    mountAs('Observer')
    expect(wrapper.text()).not.toContain('План смен')
  })
})
