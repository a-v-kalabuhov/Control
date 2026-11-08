// @vitest-environment jsdom
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import router from '../index'
import { useAuthStore } from '@/stores/auth'
import { authApi } from '@/api/auth'

vi.mock('@/api/auth', () => ({
  authApi: {
    login: vi.fn(),
    logout: vi.fn(),
    getCurrentUser: vi.fn(),
    refreshToken: vi.fn()
  }
}))

function deferred() {
  let resolve
  const promise = new Promise(resolvePromise => { resolve = resolvePromise })
  return { promise, resolve }
}

describe('маршруты ShiftLead (PZP-32)', () => {
  beforeEach(async () => {
    localStorage.clear()
    vi.clearAllMocks()
    setActivePinia(createPinia())
    await router.push('/__route_test_start__')
  })

  afterEach(() => localStorage.clear())

  function authenticate(role) {
    localStorage.setItem('access_token', `${role}-token`)
    localStorage.setItem('user_role', role)
    setActivePinia(createPinia())
    useAuthStore().user = { role, fullName: `${role} Test` }
  }

  function authenticateForRestore(cachedRole) {
    localStorage.setItem('access_token', 'restore-token')
    if (cachedRole) localStorage.setItem('user_role', cachedRole)
    setActivePinia(createPinia())
    const request = deferred()
    authApi.getCurrentUser.mockReturnValue(request.promise)
    return request
  }

  function resolveRestoredUser(request, role) {
    request.resolve({ data: { id: `${role}-id`, role, fullName: `${role} Restored` } })
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

  it.each([
    [null, 'Manager', '/shift-lead', '/'],
    [null, 'ShiftLead', '/', '/shift-lead'],
    ['ShiftLead', 'Manager', '/shift-lead', '/'],
    ['Manager', 'ShiftLead', '/', '/shift-lead'],
    [null, 'Adjuster', '/', '/mobile/tasks'],
    [null, 'Operator', '/', '/']
  ])(
    'ждёт /auth/me: cached %s, restored %s, target %s → %s',
    async (cachedRole, restoredRole, target, expectedPath) => {
      const request = authenticateForRestore(cachedRole)

      const navigation = router.push(target)
      await vi.waitFor(() => expect(authApi.getCurrentUser).toHaveBeenCalledTimes(1))
      const pathBeforeRestore = router.currentRoute.value.path

      resolveRestoredUser(request, restoredRole)
      await navigation

      expect(pathBeforeRestore).toBe('/__route_test_start__')
      expect(router.currentRoute.value.path).toBe(expectedPath)
      expect(useAuthStore().user.role).toBe(restoredRole)
      expect(localStorage.getItem('user_role')).toBe(restoredRole)
      expect(authApi.getCurrentUser).toHaveBeenCalledTimes(1)
    }, 20_000
  )

  it('делит один restore promise между инициализациями без дублирования /auth/me', async () => {
    const request = authenticateForRestore(null)
    const auth = useAuthStore()

    const first = auth.ensureInitialized()
    const sharedPromise = auth.initializationPromise
    const second = auth.ensureInitialized()

    expect(sharedPromise).not.toBeNull()
    expect(auth.initializationPromise).toBe(sharedPromise)
    expect(authApi.getCurrentUser).toHaveBeenCalledTimes(1)
    resolveRestoredUser(request, 'ShiftLead')
    await Promise.all([first, second])
    expect(auth.user.role).toBe('ShiftLead')
    expect(authApi.getCurrentUser).toHaveBeenCalledTimes(1)
  })

  it('регистрирует home и history только для ShiftLead, item/task — для трёх ролей', () => {
    expect(router.resolve('/shift-lead').meta.roles).toEqual(['ShiftLead'])
    expect(router.resolve('/shift-lead/history').meta.roles).toEqual(['ShiftLead'])
    expect(router.resolve('/shift-lead/items/item-1').meta.roles)
      .toEqual(['ShiftLead', 'Manager', 'Admin'])
    expect(router.resolve('/shift-lead/items/item-1/tasks/task-1').meta.roles)
      .toEqual(['ShiftLead', 'Manager', 'Admin'])
  })
})
