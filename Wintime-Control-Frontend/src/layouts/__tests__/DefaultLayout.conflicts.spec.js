// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

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
import { planningApi } from '@/api/planning'

const stubs = {
  'router-view': true,
  'el-menu': { template: '<div><slot /></div>' },
  'el-menu-item': { template: '<div><slot /></div>' },
  'el-sub-menu': { template: '<div><slot name="title" /><slot /></div>' },
  'el-icon': true,
  'el-avatar': true,
  'el-button': true,
  PlanConflictsBadge: true
}

describe('DefaultLayout: опрос конфликтов плана', () => {
  let wrapper
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })
  afterEach(() => {
    wrapper?.unmount()
    usePlanConflictsStore().stop()
  })

  const mountLayout = () => {
    wrapper = mount(DefaultLayout, { global: { stubs } })
  }

  it('стартует, когда пользователь появился после монтирования (перезагрузка страницы)', async () => {
    const auth = useAuthStore()
    auth.isAuthenticated = true // токен в localStorage, пользователь ещё не загружен
    auth.user = null
    mountLayout()
    await nextTick()
    expect(planningApi.getConflicts).not.toHaveBeenCalled()

    auth.user = { role: 'Manager', fullName: 'X' }
    await nextTick()
    expect(planningApi.getConflicts).toHaveBeenCalledTimes(1)
  })

  it('не стартует для наладчика', async () => {
    const auth = useAuthStore()
    auth.isAuthenticated = true // токен в localStorage, пользователь ещё не загружен
    auth.user = null
    mountLayout()
    auth.user = { role: 'Adjuster', fullName: 'X' }
    await nextTick()
    expect(planningApi.getConflicts).not.toHaveBeenCalled()
  })
})
