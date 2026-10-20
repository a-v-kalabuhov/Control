import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('@/api/planning', () => ({
  planningApi: { getConflicts: vi.fn() }
}))

import { planningApi } from '@/api/planning'
import { usePlanConflictsStore, PLAN_CONFLICTS_REFRESH_MS } from '../planConflicts'

const data = {
  taskCount: 2, planCount: 1, deviationCount: 0,
  items: [{ kind: 'Plan', resource: 'Imm', task: { id: 'a' }, other: { id: 'b' }, overlapSeconds: 600 }]
}

describe('planConflicts store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.useFakeTimers()
    planningApi.getConflicts.mockResolvedValue({ data })
  })
  afterEach(() => vi.useRealTimers())

  it('load — кладёт ответ; forTask — пары задания', async () => {
    const store = usePlanConflictsStore()
    expect(await store.load()).toBe(true)
    expect(store.taskCount).toBe(2)
    expect(store.forTask('b')).toHaveLength(1)
    expect(store.forTask('z')).toEqual([])
  })

  it('ошибка загрузки — остаётся прошлое состояние', async () => {
    const store = usePlanConflictsStore()
    await store.load()
    planningApi.getConflicts.mockRejectedValueOnce(new Error('net'))
    expect(await store.load()).toBe(false)
    expect(store.taskCount).toBe(2)
  })

  it('start опрашивает раз в минуту, повторный start не дублирует, stop останавливает', async () => {
    const store = usePlanConflictsStore()
    store.start()
    store.start()
    expect(planningApi.getConflicts).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(PLAN_CONFLICTS_REFRESH_MS)
    expect(planningApi.getConflicts).toHaveBeenCalledTimes(2)
    store.stop()
    await vi.advanceTimersByTimeAsync(PLAN_CONFLICTS_REFRESH_MS)
    expect(planningApi.getConflicts).toHaveBeenCalledTimes(2)
  })
})
