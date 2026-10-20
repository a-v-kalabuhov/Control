// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal()),
  useRouter: () => ({ push: vi.fn() })
}))
vi.mock('@/api/dashboard', () => ({
  dashboardApi: { getImmEffectiveStatusHistory: vi.fn(() => Promise.resolve({ data: [] })) }
}))
vi.mock('@/api/tasks', () => ({ tasksApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) } }))
vi.mock('@/api/shifts', () => ({ shiftsApi: { getShifts: vi.fn(() => Promise.resolve({ data: [] })) } }))

const { default: ImmDetailModal } = await import('../ImmDetailModal.vue')

const task = { id: 't1', status: 'InProgress', moldName: 'ПФ-12', planQuantity: 500, actualQuantity: 120 }

describe('ImmDetailModal — задание из хронологии', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('клик по бару задания открывает карточку задания только для чтения', async () => {
    const w = mount(ImmDetailModal, {
      props: { immId: 'imm-1', modelValue: true },
      global: {
        plugins: [ElementPlus],
        stubs: {
          ImmCard: true,
          ShiftTimeline: { name: 'ShiftTimeline', template: '<div />', emits: ['task-click'] },
          TaskDetailModal: {
            name: 'TaskDetailModal',
            props: { modelValue: Boolean, task: Object, readonly: Boolean, appendToBody: Boolean },
            template: '<div />'
          }
        }
      },
      attachTo: document.body
    })
    await flushPromises()

    const card = () => w.findComponent({ name: 'TaskDetailModal' })
    expect(card().exists() && card().props('modelValue')).toBeFalsy()

    w.findComponent({ name: 'ShiftTimeline' }).vm.$emit('task-click', task)
    await flushPromises()

    expect(card().props('modelValue')).toBe(true)
    expect(card().props('task')).toEqual(task)
    expect(card().props('readonly')).toBe(true)
    expect(card().props('appendToBody')).toBe(true)
    w.unmount()
  })
})
