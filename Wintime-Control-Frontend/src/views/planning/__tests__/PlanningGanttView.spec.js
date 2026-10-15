// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import PlanningGanttView from '../PlanningGanttView.vue'

vi.mock('@/api/planning', () => ({
  planningApi: {
    getBoard: vi.fn(() => Promise.resolve({ data: {
      from: '2026-10-12T00:00:00Z', to: '2026-10-19T00:00:00Z', now: '2026-10-15T10:00:00Z',
      rows: [{ imm: { id: 'i1', name: 'ТПА-1', isActive: true }, tasks: [] }]
    } }))
  }
}))
vi.mock('@/api/shifts', () => ({
  shiftsApi: { getShifts: vi.fn(() => Promise.resolve({ data: [{ startMinutes: 480, durationMinutes: 720 }] })) }
}))
import { planningApi } from '@/api/planning'

const mountView = () => mount(PlanningGanttView, {
  global: {
    plugins: [ElementPlus],
    stubs: { TaskDetailModal: true, TaskFormModal: true, IssueTaskDialog: true }
  }
})

const boardWith = (name) => ({ data: {
  from: '2026-10-12T00:00:00Z', to: '2026-10-19T00:00:00Z', now: '2026-10-15T10:00:00Z',
  rows: [{ imm: { id: name, name, isActive: true }, tasks: [] }]
} })


describe('PlanningGanttView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    vi.setSystemTime(new Date(2026, 9, 15, 10, 0))
    vi.clearAllMocks()
    planningApi.getBoard.mockImplementation(() => Promise.resolve(boardWith('ТПА-1')))
  })
  afterEach(() => vi.useRealTimers())

  it('по умолчанию — неделя с понедельника, строки ТПА', async () => {
    const w = mountView()
    await flushPromises()

    const { from, to } = planningApi.getBoard.mock.calls.at(-1)[0]
    expect(new Date(from)).toEqual(new Date(2026, 9, 12))
    expect(new Date(to)).toEqual(new Date(2026, 9, 19))
    expect(w.text()).toContain('ТПА-1')
    expect(w.text()).toContain('12.10.2026 — 18.10.2026')
  })

  it('«→» сдвигает на неделю вперёд', async () => {
    const w = mountView()
    await flushPromises()

    await w.find('[data-test="next"]').trigger('click')
    await flushPromises()

    expect(new Date(planningApi.getBoard.mock.calls.at(-1)[0].from)).toEqual(new Date(2026, 9, 19))
  })

  it('обновляется раз в 60 секунд', async () => {
    mountView()
    await flushPromises()
    const calls = planningApi.getBoard.mock.calls.length

    vi.advanceTimersByTime(60_000)
    await flushPromises()

    expect(planningApi.getBoard.mock.calls.length).toBe(calls + 1)
  })

  it('устаревший ответ не перезаписывает доску нового периода', async () => {
    const pending = []
    planningApi.getBoard.mockImplementation(() => new Promise(resolve => pending.push(resolve)))
    const w = mountView()
    await flushPromises()
    pending.splice(0).forEach(r => r(boardWith('ТПА-БАЗА')))
    await flushPromises()

    await w.find('[data-test="next"]').trigger('click')
    await w.find('[data-test="next"]').trigger('click')
    await flushPromises()
    expect(pending.length).toBeGreaterThanOrEqual(2)
    const older = pending[0]
    const newer = pending[pending.length - 1]

    newer(boardWith('ТПА-НОВЫЙ'))
    await flushPromises()
    older(boardWith('ТПА-СТАРЫЙ'))
    await flushPromises()

    expect(w.text()).toContain('ТПА-НОВЫЙ')
    expect(w.text()).not.toContain('ТПА-СТАРЫЙ')
  })

  it('размонтирование до окончания загрузки не оставляет таймер', async () => {
    const w = mountView()
    w.unmount()
    await flushPromises()
    const calls = planningApi.getBoard.mock.calls.length

    vi.advanceTimersByTime(60_000)
    await flushPromises()

    expect(planningApi.getBoard.mock.calls.length).toBe(calls)
  })
})
