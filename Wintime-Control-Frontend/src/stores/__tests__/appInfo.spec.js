import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// Стор тянет apiClient → authStore → router (нужен window) — api-модуль мокаем.
const { get } = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock('@/api/appInfo', () => ({ appInfoApi: { get } }))

const { useAppInfoStore } = await import('@/stores/appInfo')

describe('appInfo store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    get.mockReset()
  })

  it('demoMode берётся из ответа сервера', async () => {
    get.mockResolvedValue({ data: { demoMode: true } })
    const store = useAppInfoStore()
    await store.load()
    expect(store.demoMode).toBe(true)
    expect(store.loaded).toBe(true)
  })

  it('ошибка запроса — не демо', async () => {
    get.mockRejectedValue(new Error('network'))
    const store = useAppInfoStore()
    await store.load()
    expect(store.demoMode).toBe(false)
    expect(store.loaded).toBe(true)
  })

  it('сведения грузятся один раз', async () => {
    get.mockResolvedValue({ data: { demoMode: false } })
    const store = useAppInfoStore()
    await store.load()
    await store.load()
    expect(get).toHaveBeenCalledTimes(1)
  })
})
