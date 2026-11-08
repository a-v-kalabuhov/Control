// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import ShiftLeadHomeView from '../ShiftLeadHomeView.vue'
import { shiftStaffingApi } from '@/api/shiftStaffing'
import { tasksApi } from '@/api/tasks'
import { personnelApi } from '@/api/personnel'
import router from '@/router'
import { useShiftStaffingStore } from '@/stores/shiftStaffing'

const { push } = vi.hoisted(() => ({ push: vi.fn() }))
vi.mock('vue-router', async importOriginal => ({
  ...await importOriginal(),
  useRouter: () => ({ push })
}))

vi.mock('@/api/shiftStaffing', () => ({
  shiftStaffingApi: {
    nearest: vi.fn(), history: vi.fn(), item: vi.fn(), task: vi.fn(),
    operators: vi.fn(), assign: vi.fn(), unassign: vi.fn()
  }
}))
vi.mock('@/api/tasks', () => ({ tasksApi: { getResults: vi.fn(), getTasks: vi.fn() } }))
vi.mock('@/api/personnel', () => ({ personnelApi: { getAll: vi.fn(), getPersonnel: vi.fn() } }))

function summary(overrides = {}) {
  return {
    itemId: 'item-1', shiftDate: '2026-11-08', position: 1,
    startAtUtc: '2026-11-08T08:00:00Z', endAtUtc: '2026-11-08T20:00:00Z',
    timeZoneId: 'Asia/Kamchatka', state: 'Current', canEditOperators: true,
    ...overrides
  }
}

function task(overrides = {}) {
  return {
    id: 'task-1', immId: 'imm-1', productFullArticle: 'ТИП.001',
    productName: 'Корпус', moldName: 'ПФ-1', planQuantity: 100,
    actualQuantity: 25, status: 'InProgress', ...overrides
  }
}

function detail(overrides = {}) {
  return {
    shift: summary(), shiftLeadId: 'lead-1', shiftLeadName: 'Старший Смены',
    imms: [
      {
        immId: 'imm-1', name: 'ТПА-1', inventoryNumber: 'ИНВ-1', isActive: true,
        effectiveStatus: 'Production', tasks: [task()], activeOperatorCount: 1,
        assignments: [{ id: 'a1', operatorId: 'op1', operatorName: 'Иванов', unassignedAt: null }]
      },
      {
        immId: 'imm-2', name: 'ТПА-2', inventoryNumber: 'ИНВ-2', isActive: true,
        effectiveStatus: 'NoTask', tasks: [], activeOperatorCount: 2,
        assignments: [
          { id: 'a2', operatorId: 'op2', operatorName: 'Петров', unassigned: null },
          { id: 'a3', operatorId: 'op3', operatorName: 'Сидоров', unassignedAt: null }
        ]
      }
    ],
    ...overrides
  }
}

function mountView() {
  return mount(ShiftLeadHomeView, { global: { plugins: [ElementPlus] } })
}

describe('ShiftLeadHomeView', () => {
  let wrapper

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-11-08T12:00:00Z'))
    setActivePinia(createPinia())
    vi.clearAllMocks()
    shiftStaffingApi.nearest.mockResolvedValue({ data: { item: summary() } })
    shiftStaffingApi.item.mockResolvedValue({ data: detail() })
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    vi.useRealTimers()
  })

  it('показывает эффективные статусы, задания, занятость и кликабельный ТПА без задания', async () => {
    wrapper = mountView()
    await flushPromises()

    expect(shiftStaffingApi.nearest).toHaveBeenCalledTimes(1)
    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-1')
    expect(wrapper.text()).toContain('Работа')
    expect(wrapper.text()).toContain('Без задания')
    expect(wrapper.text()).toContain('ТИП.001')
    expect(wrapper.text()).toContain('25 / 100')
    expect(wrapper.text()).toContain('1/2')
    expect(wrapper.text()).toContain('2/2')
    expect(wrapper.text()).toContain('Задание отсутствует')

    await wrapper.find('[data-test="home-imm-imm-1"]').trigger('click')
    expect(push).toHaveBeenCalledWith('/shift-lead/items/item-1')
    await wrapper.find('[data-test="home-imm-imm-2"]').trigger('click')
    expect(push).toHaveBeenCalledTimes(2)
  })

  it('без назначенной смены показывает заглушку и не вызывает общие tasks/personnel APIs', async () => {
    shiftStaffingApi.nearest.mockResolvedValue({ data: { item: null } })
    wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Назначенная смена не найдена')
    expect(shiftStaffingApi.item).not.toHaveBeenCalled()
    expect(tasksApi.getTasks).not.toHaveBeenCalled()
    expect(tasksApi.getResults).not.toHaveBeenCalled()
    expect(personnelApi.getAll).not.toHaveBeenCalled()
    expect(personnelApi.getPersonnel).not.toHaveBeenCalled()
  })

  it('History→Home с nearest=null не показывает и не перезагружает unrelated cached item', async () => {
    const historical = detail({
      shift: summary({ itemId: 'history-1', state: 'Historical', canEditOperators: false })
    })
    shiftStaffingApi.item.mockResolvedValue({ data: historical })
    const store = useShiftStaffingStore()
    await store.loadItem('history-1')
    vi.clearAllMocks()
    shiftStaffingApi.nearest.mockResolvedValue({ data: { item: null } })
    shiftStaffingApi.item.mockResolvedValue({ data: historical })

    wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Назначенная смена не найдена')
    expect(wrapper.text()).not.toContain('Смена 1 · 08.11.2026')
    expect(store.currentItem).toBeNull()
    expect(shiftStaffingApi.item).not.toHaveBeenCalled()
  })

  it('маршрут Manager из плана ведёт в ShiftStaffingItemView, а не в мобильный контур наладчика', async () => {
    const resolved = router.resolve('/shift-lead/items/item-1')
    const loaded = await resolved.matched.at(-1).components.default()
    const expected = await import('../ShiftStaffingItemView.vue')

    expect(resolved.meta.roles).toEqual(['ShiftLead', 'Manager', 'Admin'])
    expect(resolved.matched.some(record => record.meta.isShiftLeadUi)).toBe(true)
    expect(loaded.default).toBe(expected.default)
  })
})
