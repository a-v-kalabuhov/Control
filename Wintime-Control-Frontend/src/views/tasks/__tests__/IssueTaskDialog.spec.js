// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import IssueTaskDialog from '../IssueTaskDialog.vue'

vi.mock('@/api/personnel', () => ({
  personnelApi: { getList: vi.fn(() => Promise.resolve({ data: [
    { id: 'adj-1', fullName: 'Иванов', isActive: true },
    { id: 'adj-2', fullName: 'Петров', isActive: true }
  ] })) }
}))
vi.mock('@/api/tasks', () => ({
  tasksApi: { issue: vi.fn(() => Promise.resolve({ data: {} })), getList: vi.fn(() => Promise.resolve({ data: [] })) }
}))

import { tasksApi } from '@/api/tasks'

function mountDialog(task) {
  return mount(IssueTaskDialog, {
    props: { modelValue: true, task },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
}

describe('IssueTaskDialog', () => {
  beforeEach(() => { vi.clearAllMocks(); setActivePinia(createPinia()) })

  it('предзаполняет назначенного наладчика', async () => {
    const wrapper = mountDialog({ id: 't1', personnelId: 'adj-2', immName: 'ТПА-1', moldName: 'ПФ' })
    await flushPromises()
    expect(wrapper.vm.personnelId).toBe('adj-2')
  })

  it('без наладчика кнопка «Выдать» неактивна', async () => {
    const wrapper = mountDialog({ id: 't1', personnelId: null })
    await flushPromises()
    expect(wrapper.vm.canSubmit).toBe(false)
  })

  it('архивный наладчик не предзаполняется', async () => {
    const wrapper = mountDialog({ id: 't1', personnelId: 'adj-archived' })
    await flushPromises()
    expect(wrapper.vm.personnelId).toBe(null)
  })

  it('выдаёт с выбранным наладчиком и сообщает родителю', async () => {
    const wrapper = mountDialog({ id: 't1', personnelId: null })
    await flushPromises()
    wrapper.vm.personnelId = 'adj-1'
    await wrapper.vm.submit()
    await flushPromises()
    expect(tasksApi.issue).toHaveBeenCalledWith('t1', 'adj-1')
    expect(wrapper.emitted('issued')).toBeTruthy()
  })
})
