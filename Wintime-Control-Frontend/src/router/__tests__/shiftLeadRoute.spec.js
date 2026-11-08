// @vitest-environment jsdom
import { beforeEach, afterEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import router from '../index'
import { useAuthStore } from '@/stores/auth'

describe('маршруты ShiftLead (PZP-32)', () => {
  beforeEach(async () => {
    localStorage.clear()
    setActivePinia(createPinia())
    await router.push('/__route_test_start__')
  })

  afterEach(() => localStorage.clear())

  function authenticate(role) {
    localStorage.setItem('access_token', `${role}-token`)
    localStorage.setItem('user_role', role)
    setActivePinia(createPinia())
  }

  it.each(['/', '/tasks', '/mobile/tasks', '/planning/shift-plan'])(
    'перенаправляет ShiftLead с %s на собственный домашний маршрут', async path => {
      authenticate('ShiftLead')

      await router.push(path)

      expect(router.currentRoute.value.path).toBe('/shift-lead')
    }, 20_000
  )

  it('не использует правила Adjuster для ShiftLead', async () => {
    authenticate('ShiftLead')
    const auth = useAuthStore()

    expect(auth.isShiftLead).toBe(true)
    expect(auth.isAdjuster).toBe(false)
    expect(auth.homePath).toBe('/shift-lead')

    await router.push('/shift-lead/history')

    expect(router.currentRoute.value.path).toBe('/shift-lead/history')
  }, 20_000)

  it('пускает Manager на выбранный item, но не в персональную историю', async () => {
    authenticate('Manager')

    await router.push('/shift-lead/items/item-1')
    expect(router.currentRoute.value.path).toBe('/shift-lead/items/item-1')

    await router.push('/shift-lead/history')
    expect(router.currentRoute.value.path).toBe('/')
  }, 20_000)

  it('пускает Admin на выбранный item, но не в home и историю ShiftLead', async () => {
    authenticate('Admin')

    await router.push('/shift-lead/items/item-1')
    expect(router.currentRoute.value.path).toBe('/shift-lead/items/item-1')

    await router.push('/shift-lead')
    expect(router.currentRoute.value.path).toBe('/')

    await router.push('/shift-lead/history')
    expect(router.currentRoute.value.path).toBe('/')
  }, 20_000)

  it.each(['ShiftLead', 'Manager', 'Admin'])(
    'открывает прямой маршрут задания роли %s в контуре старшего смены', async role => {
      authenticate(role)

      await router.push('/shift-lead/items/item-1/tasks/task-1')

      expect(router.currentRoute.value.path).toBe('/shift-lead/items/item-1/tasks/task-1')
      expect(router.currentRoute.value.matched.some(record => record.meta.isShiftLeadUi)).toBe(true)
    }, 20_000
  )

  it.each([
    ['ShiftLead', '/shift-lead'],
    ['Adjuster', '/mobile/tasks'],
    ['Operator', '/']
  ])('перенаправляет аутентифицированную роль %s с /login на %s', async (role, homePath) => {
    authenticate(role)

    await router.push('/login')

    expect(router.currentRoute.value.path).toBe(homePath)
  }, 20_000)

  it.each(['Manager', 'Admin'])('не пускает %s в layout наладчика', async role => {
    authenticate(role)

    await router.push('/mobile/tasks')

    expect(router.currentRoute.value.path).toBe('/')
  }, 20_000)

  it('сохраняет прежний домашний маршрут Adjuster', async () => {
    authenticate('Adjuster')

    await router.push('/')
    expect(router.currentRoute.value.path).toBe('/mobile/tasks')

    await router.push('/mobile/tasks')
    expect(router.currentRoute.value.path).toBe('/mobile/tasks')
  }, 20_000)

  it.each([
    ['Adjuster', '/mobile/tasks'],
    ['Observer', '/'],
    ['Operator', '/']
  ])('перенаправляет роль %s с item старшего на домашний маршрут', async (role, homePath) => {
    authenticate(role)

    await router.push('/shift-lead/items/item-1')

    expect(router.currentRoute.value.path).toBe(homePath)
  }, 20_000)

  it.each([
    ['Adjuster', '/mobile/tasks'],
    ['Observer', '/'],
    ['Operator', '/']
  ])('перенаправляет роль %s с task старшего на домашний маршрут', async (role, homePath) => {
    authenticate(role)

    await router.push('/shift-lead/items/item-1/tasks/task-1')

    expect(router.currentRoute.value.path).toBe(homePath)
  }, 20_000)

  it('регистрирует home и history только для ShiftLead, item/task — для трёх ролей', () => {
    expect(router.resolve('/shift-lead').meta.roles).toEqual(['ShiftLead'])
    expect(router.resolve('/shift-lead/history').meta.roles).toEqual(['ShiftLead'])
    expect(router.resolve('/shift-lead/items/item-1').meta.roles)
      .toEqual(['ShiftLead', 'Manager', 'Admin'])
    expect(router.resolve('/shift-lead/items/item-1/tasks/task-1').meta.roles)
      .toEqual(['ShiftLead', 'Manager', 'Admin'])
  })
})
