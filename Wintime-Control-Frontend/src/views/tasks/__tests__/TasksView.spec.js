// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import TasksView from '../TasksView.vue'
import TaskDetailModal from '../TaskDetailModal.vue'
import { useTasksStore } from '@/stores/tasks'

vi.mock('@/api/tasks', () => ({
  tasksApi: {
    getList: vi.fn(() => Promise.resolve({ data: [] })),
    getById: vi.fn()
  }
}))
vi.mock('@/api/shifts', () => ({
  shiftsApi: { getShifts: vi.fn(() => Promise.resolve({ data: [] })) }
}))

function mountView() {
  return mount(TasksView, {
    global: {
      plugins: [ElementPlus],
      stubs: {
        TaskFilters: true,
        TaskProgress: true,
        TaskStatusBadge: true,
        TaskFormModal: true,
        TaskDetailModal: true,
        IssueTaskDialog: true,
        Plus: true,
        Document: true,
        Edit: true,
        Clock: true,
        Warning: true,
        TrendCharts: true,
        WarningFilled: true
      }
    }
  })
}

describe('TasksView — обновление рецепта', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.spyOn(useTasksStore(), 'loadTasks').mockResolvedValue()
  })

  it('поздний ответ замены старого задания не переключает открытую карточку нового', async () => {
    const taskA = { id: 'task-a' }
    const taskB = { id: 'task-b' }
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.openDetail(taskA)
    wrapper.vm.openDetail(taskB)
    await flushPromises()

    wrapper.findComponent(TaskDetailModal).vm.$emit('recipe-changed', { id: 'task-a', recipeEdited: true })
    await flushPromises()

    expect(wrapper.findComponent(TaskDetailModal).props('task')).toEqual(taskB)
    expect(useTasksStore().loadTasks).toHaveBeenCalled()
  })
})
