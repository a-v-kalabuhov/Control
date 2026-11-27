// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import router from '../index'
import { useAuthStore } from '@/stores/auth'

vi.mock('@/api/auth', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), getCurrentUser: vi.fn(), refreshToken: vi.fn() }
}))

describe('маршрут администрирования телеметрии', () => {
  beforeEach(async () => {
    localStorage.clear()
    vi.clearAllMocks()
    setActivePinia(createPinia())
    await router.push('/__telemetry_route_test_start__')
  })

  afterEach(() => localStorage.clear())

  function authenticate(role) {
    localStorage.setItem('access_token', `${role}-token`)
    localStorage.setItem('user_role', role)
    setActivePinia(createPinia())
    useAuthStore().user = { role, fullName: `${role} Test` }
  }

  it('имеет Admin role metadata и заголовок', () => {
    const route = router.resolve('/admin/telemetry')
    expect(route.name).toBe('AdminTelemetry')
    expect(route.meta.roles).toEqual(['Admin'])
    expect(route.meta.title).toBe('Телеметрия')
  })

  it('пускает Admin по прямому URL', async () => {
    authenticate('Admin')
    await router.push('/admin/telemetry')
    expect(router.currentRoute.value.path).toBe('/admin/telemetry')
  }, 20_000)

  it.each([
    ['Manager', '/'], ['Observer', '/'], ['Operator', '/'],
    ['Adjuster', '/mobile/tasks'], ['ShiftLead', '/shift-lead']
  ])('перенаправляет %s на домашнюю страницу', async (role, home) => {
    authenticate(role)
    await router.push('/admin/telemetry')
    expect(router.currentRoute.value.path).toBe(home)
  }, 20_000)
})
