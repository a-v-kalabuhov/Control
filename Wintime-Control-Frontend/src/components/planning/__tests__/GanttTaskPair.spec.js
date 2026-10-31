// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import GanttTaskPair from '../GanttTaskPair.vue'

const MIN = 60_000
const T0 = new Date(2026, 9, 15, 10, 0).getTime()
const at = (m) => new Date(T0 + m * MIN).toISOString()

const overrunItem = {
  task: { id: 't1', status: 'InProgress', moldName: 'ПФ-1', immName: 'ТПА-1', planQuantity: 100,
          plannedSetupMinutes: 30, startedAt: at(32) },
  productArticle: 'ART-1',
  plan: { setupStart: at(0), setupEnd: at(30), releaseEnd: at(90), bufferEnd: at(100) },
  fact: { setupStart: at(5), setupEnd: at(32), releaseEnd: null, forecastEnd: at(110) },
  bufferRemainingSeconds: -600, overdueNotStartedSeconds: null, setupLagSeconds: null
}

const mountPair = (item) => mount(GanttTaskPair, {
  props: { item, from: new Date(T0 - 60 * MIN), to: new Date(T0 + 240 * MIN), now: new Date(T0 + 60 * MIN) }
})

describe('GanttTaskPair', () => {
  it('рисует план, прогноз штрихом и красный перерасход', () => {
    const w = mountPair(overrunItem)
    for (const k of ['plan-setup', 'plan-release', 'plan-buffer', 'fact-setup', 'fact-release', 'fact-forecast', 'fact-overrun'])
      expect(w.find(`[data-kind="${k}"]`).exists(), k).toBe(true)
    expect(w.text()).toContain('ART-1 · ПФ-1 · 100 шт')
    expect(w.text()).toContain('перерасход 10м')
  })

  it('метка просрочки на план-баре', () => {
    const w = mountPair({ ...overrunItem, task: { ...overrunItem.task, status: 'Planned', startedAt: null },
      fact: { setupStart: null, setupEnd: null, releaseEnd: null, forecastEnd: null },
      bufferRemainingSeconds: null, overdueNotStartedSeconds: 3600 })
    expect(w.find('.row--plan').text()).toContain('не начато, опоздание 1ч')
  })

  it('клик — select с заданием', async () => {
    const w = mountPair(overrunItem)
    await w.find('.pair').trigger('click')
    expect(w.emitted('select')[0][0]).toEqual(overrunItem.task)
  })

  it('вне окна — ничего не рисует', () => {
    const w = mount(GanttTaskPair, {
      props: { item: overrunItem, from: new Date(T0 + 500 * MIN), to: new Date(T0 + 600 * MIN), now: new Date(T0) }
    })
    expect(w.find('.pair').exists()).toBe(false)
  })

  it('рамка конфликта плана и отклонения', () => {
    const conflict = (kind) => ({ kind, resource: 'Imm', overlapSeconds: 600,
      task: { id: 't1', moldName: 'ПФ-1' }, other: { id: 't2', moldName: 'ПФ-2' } })
    const plan = mountPair({ ...overrunItem, conflicts: [conflict('Deviation'), conflict('Plan')] })
    expect(plan.find('.pair').attributes('data-conflict')).toBe('Plan')
    expect(plan.find('.pair').classes()).toContain('pair--conflict-plan')

    const dev = mountPair({ ...overrunItem, conflicts: [conflict('Deviation')] })
    expect(dev.find('.pair').classes()).toContain('pair--conflict-deviation')

    const none = mountPair(overrunItem)
    expect(none.find('.pair').attributes('data-conflict')).toBeUndefined()
  })
})
