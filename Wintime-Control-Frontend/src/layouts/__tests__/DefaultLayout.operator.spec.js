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

describe('DefaultLayout: Operator', () => {
  let wrapper

  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    const auth = useAuthStore()
    auth.isAuthenticated = true
    auth.user = { role: 'Operator', fullName: 'Литейщик Тестовый' }
    wrapper = mount(DefaultLayout, { global: { stubs } })
  })

  afterEach(() => {
    wrapper?.unmount()
    usePlanConflictsStore().stop()
  })

  it('оставляет только дашборд и показывает название роли', () => {
    expect(wrapper.text()).toContain('Дашборд')
    expect(wrapper.text()).toContain('Литейщик')
    for (const item of ['Планирование', 'Мониторинг', 'Отчёты', 'Справочники', 'Настройки']) {
      expect(wrapper.text()).not.toContain(item)
    }
  })
})
