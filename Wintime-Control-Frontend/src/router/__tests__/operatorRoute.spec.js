// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import router from '../index'

describe('маршруты Operator (PZP-30)', () => {
  afterEach(() => localStorage.clear())

  function authenticateOperator() {
    localStorage.setItem('access_token', 'operator-token')
    localStorage.setItem('user_role', 'Operator')
    setActivePinia(createPinia())
  }

  it('разрешает литейщику дашборд', async () => {
    authenticateOperator()

    await router.push('/')

    expect(router.currentRoute.value.path).toBe('/')
    expect(router.currentRoute.value.meta.roles).toContain('Operator')
  }, 20_000)

  it.each(['/tasks', '/orders', '/reports', '/dictionary/personnel', '/mobile/tasks'])(
    'запрещает литейщику прямой переход на %s', async path => {
      authenticateOperator()

      await router.push(path)

      expect(router.currentRoute.value.path).toBe('/')
    }
  )
})
