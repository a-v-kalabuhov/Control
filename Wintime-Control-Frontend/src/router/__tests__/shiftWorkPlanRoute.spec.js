// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import router from '../index'
import { useAuthStore } from '@/stores/auth'

describe('маршрут PZP-18', () => {
  afterEach(() => localStorage.clear())

  it('открывает план работ на смену только менеджеру и администратору', () => {
    const route = router.resolve('/planning/shift-work-plan')

    expect(route.name).toBe('ShiftWorkPlan')
    expect(route.meta.roles).toEqual(['Admin', 'Manager'])
    expect(route.meta.title).toBe('План работ на смену')
  })

  it('перенаправляет наблюдателя при прямом переходе по URL', async () => {
    localStorage.setItem('access_token', 'observer-token')
    localStorage.setItem('user_role', 'Observer')
    setActivePinia(createPinia())
    useAuthStore().user = { role: 'Observer', fullName: 'Observer Test' }

    await router.push('/planning/shift-work-plan')

    expect(router.currentRoute.value.path).toBe('/')
  }, 20_000)
})
