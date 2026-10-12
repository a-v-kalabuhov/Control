// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useTasksStore } from '../tasks'

vi.mock('@/api/tasks', () => ({
  tasksApi: { getList: vi.fn(), issue: vi.fn(() => Promise.resolve({ data: {} })) }
}))

import { tasksApi } from '@/api/tasks'

describe('tasks store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-10-15T12:00:00Z'))
  })

  it('overdueTasks — не начатые задания с прошедшим плановым началом', () => {
    const store = useTasksStore()
    store.tasks = [
      { id: 'a', status: 'Issued', plannedStartAt: '2026-10-15T11:00:00Z' },
      { id: 'b', status: 'Draft', plannedStartAt: '2026-10-15T13:00:00Z' },
      { id: 'c', status: 'InProgress', plannedStartAt: '2026-10-14T05:00:00Z' }
    ]
    expect(store.overdueTasks.map(t => t.id)).toEqual(['a'])
  })

  it('issueTask передаёт наладчика в API', async () => {
    tasksApi.getList.mockResolvedValue({ data: [] })
    const store = useTasksStore()
    await store.issueTask('t1', 'adj-1')
    expect(tasksApi.issue).toHaveBeenCalledWith('t1', 'adj-1')
  })
})
