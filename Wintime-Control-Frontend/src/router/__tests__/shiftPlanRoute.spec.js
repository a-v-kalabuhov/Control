// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import router from '../index'
import { useAuthStore } from '@/stores/auth'

describe('маршрут PZP-32: план смен', () => {
  afterEach(() => localStorage.clear())

  it('регистрирует именованный десктопный маршрут только для Manager/Admin', () => {
    const route = router.resolve('/planning/shift-plan')

    expect(route.name).toBe('ShiftPlan')
    expect(route.meta.roles).toEqual(['Admin', 'Manager'])
    expect(route.meta.title).toBe('План смен')
  })

  it.each(['Observer', 'ShiftLead', 'Adjuster', 'Operator'])(
    'перенаправляет роль %s при прямом переходе по URL',
    async role => {
      localStorage.setItem('access_token', `${role}-token`)
      localStorage.setItem('user_role', role)
      setActivePinia(createPinia())
      useAuthStore().user = { role, fullName: `${role} Test` }

      await router.push('/planning/shift-plan')

      expect(router.currentRoute.value.path).not.toBe('/planning/shift-plan')
    }, 20_000)
})
