// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { usePermissions } from '../usePermissions'

describe('usePermissions — восстановление сессии', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('использует сохранённую userRole, пока профиль пользователя ещё не загружен', () => {
    const auth = useAuthStore()
    auth.isAuthenticated = true
    auth.user = null
    auth.userRole = 'Operator'

    const { canAccess, hasRole } = usePermissions()

    expect(hasRole('Operator')).toBe(true)
    expect(canAccess(['Operator'])).toBe(true)
  })
})
