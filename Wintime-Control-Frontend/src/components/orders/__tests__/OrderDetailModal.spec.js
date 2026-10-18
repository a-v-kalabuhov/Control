// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import OrderDetailModal from '../OrderDetailModal.vue'
import TaskFormModal from '@/views/tasks/TaskFormModal.vue'

vi.mock('@/api/orders', () => ({
  ordersApi: {
    getById: vi.fn(() => Promise.resolve({ data: {
      id: '1', number: 'ORD-1', quantity: 100, producedQuantity: 110,
      defectQuantity: 10, goodQuantity: 100, progressPercent: 100, status: 'Active',
      productId: 'p-1', productTypeId: 'pt-1', productFullArticle: 'A123.BLK', dueDate: '2026-08-01T00:00:00Z',
      tasks: [
        { taskId: 't1', immName: 'ТПА-1', moldName: 'ПФ-1', productFullArticle: 'A123.BLK', planQuantity: 60, actualQuantity: 60, defectQuantity: 0, status: 'Completed' }
      ]
    } })),
    detachTask: vi.fn(() => Promise.resolve({ data: {} })),
    attachTask: vi.fn(() => Promise.resolve({ data: {} })),
  }
}))

vi.mock('@/api/tasks', () => ({
  tasksApi: {
    getList: vi.fn(() => Promise.resolve({ data: [] }))
  }
}))

vi.mock('@/api/molds', () => ({
  moldsApi: {
    getList: vi.fn(() => Promise.resolve({ data: [] }))
  }
}))

import { ordersApi } from '@/api/orders'
import { tasksApi } from '@/api/tasks'
import { moldsApi } from '@/api/molds'

function mountModal() {
  return mount(OrderDetailModal, {
    props: { modelValue: true, orderId: '1' },
    global: {
      plugins: [ElementPlus],
      stubs: { 'el-progress': true, TaskFormModal: true }
    }
  })
}

function findButton(wrapper, text) {
  return wrapper.findAll('button').find(b => b.text() === text)
}

describe('OrderDetailModal', () => {
  beforeEach(() => vi.clearAllMocks())

  it('показывает разбивку прогресса и список заданий', async () => {
    const wrapper = mountModal()
    await flushPromises()
    expect(wrapper.text()).toContain('ТПА-1')
    expect(wrapper.text()).toContain('100') // годных
  })

  it('отвязывает задание по кнопке «Отвязать»', async () => {
    const wrapper = mountModal()
    await flushPromises()

    const detachButton = findButton(wrapper, 'Отвязать')
    expect(detachButton).toBeTruthy()
    await detachButton.trigger('click')
    await flushPromises()

    expect(ordersApi.detachTask).toHaveBeenCalledWith('1', 't1')
    expect(wrapper.emitted('updated')).toBeTruthy()
  })

  it('на активном заказе кнопки создания и привязки доступны', async () => {
    const wrapper = mountModal()
    await flushPromises()

    expect(findButton(wrapper, 'Создать задание').element.disabled).toBe(false)
    expect(findButton(wrapper, 'Привязать существующее').element.disabled).toBe(false)
  })

  it.each(['Completed', 'Cancelled'])(
    'на заказе в статусе %s обе кнопки заблокированы',
    async (status) => {
      ordersApi.getById.mockResolvedValueOnce({ data: {
        id: '1', number: 'ORD-1', quantity: 100, producedQuantity: 100,
        defectQuantity: 0, goodQuantity: 100, progressPercent: 100, status,
        productId: 'p-1', productTypeId: 'pt-1', productFullArticle: 'A123.BLK',
        dueDate: '2026-08-01T00:00:00Z', tasks: []
      } })

      const wrapper = mountModal()
      await flushPromises()

      expect(findButton(wrapper, 'Создать задание').element.disabled).toBe(true)
      expect(findButton(wrapper, 'Привязать существующее').element.disabled).toBe(true)
    }
  )

  it('по кнопке «Создать задание» открывает форму с заказом текущей карточки', async () => {
    const wrapper = mountModal()
    await flushPromises()

    await findButton(wrapper, 'Создать задание').trigger('click')
    await flushPromises()

    const form = wrapper.findComponent(TaskFormModal)
    expect(form.props('modelValue')).toBe(true)
    expect(form.props('lockedOrder')).toEqual({
      id: '1',
      number: 'ORD-1',
      productId: 'p-1',
      productTypeId: 'pt-1',
      productFullArticle: 'A123.BLK'
    })
  })

  it('кандидаты на привязку — задания без заказа с тем же изделием, без запроса ПФ', async () => {
    tasksApi.getList.mockResolvedValueOnce({ data: [
      { id: 'a', immName: 'ТПА-1', moldName: 'ПФ-1', productId: 'p-1', productFullArticle: 'A123.BLK' },
      { id: 'b', immName: 'ТПА-2', moldName: 'ПФ-2', productId: 'p-2', productFullArticle: 'A123.RED' },
      { id: 'c', immName: 'ТПА-3', moldName: 'ПФ-3', productId: 'p-1', productFullArticle: 'A123.BLK', orderId: 'x' }
    ] })
    const wrapper = mountModal()
    await flushPromises()
    await findButton(wrapper, 'Привязать существующее').trigger('click')
    await flushPromises()
    expect(wrapper.vm.attachableTasks.map(t => t.id)).toEqual(['a'])
    expect(moldsApi.getList).not.toHaveBeenCalled()
  })

  it('в таблице заданий колонка «Изделие» — полный артикул', async () => {
    const wrapper = mountModal()
    await flushPromises()
    const table = wrapper.find('.el-table')
    expect(table.text()).toContain('Изделие')
    expect(table.text()).toContain('A123.BLK')
  })

  it('после создания задания перечитывает заказ и эмитит updated', async () => {
    const wrapper = mountModal()
    await flushPromises()
    expect(ordersApi.getById).toHaveBeenCalledTimes(1)

    wrapper.findComponent(TaskFormModal).vm.$emit('success')
    await flushPromises()

    expect(ordersApi.getById).toHaveBeenCalledTimes(2)
    expect(wrapper.emitted('updated')).toBeTruthy()
  })
})
