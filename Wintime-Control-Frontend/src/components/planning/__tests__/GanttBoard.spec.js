// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import GanttBoard from '../GanttBoard.vue'

const MIN = 60_000
const T0 = new Date(2026, 9, 15, 10, 0).getTime()
const at = (m) => new Date(T0 + m * MIN).toISOString()

function item(id, startMin) {
  return {
    task: { id, status: 'Planned', moldName: 'ПФ', immName: 'ТПА-1', planQuantity: 10, plannedSetupMinutes: 30 },
    productArticle: null,
    plan: { setupStart: at(startMin), setupEnd: at(startMin + 30), releaseEnd: at(startMin + 90), bufferEnd: at(startMin + 100) },
    fact: { setupStart: null, setupEnd: null, releaseEnd: null, forecastEnd: null },
    bufferRemainingSeconds: null, overdueNotStartedSeconds: null, setupLagSeconds: null
  }
}

const board = {
  rows: [
    { imm: { id: 'i1', name: 'ТПА-1', isActive: true }, tasks: [item('a', 0), item('b', 50)] },
    { imm: { id: 'i2', name: 'ТПА-2', isActive: false }, tasks: [item('c', 0)] }
  ]
}

const mountBoard = () => mount(GanttBoard, {
  props: { board, from: new Date(T0 - 60 * MIN), to: new Date(T0 + 300 * MIN), now: new Date(T0 + 20 * MIN), ticks: [] },
  global: { plugins: [ElementPlus] }
})

describe('GanttBoard', () => {
  it('строки ТПА, архивный — с пометкой', () => {
    const w = mountBoard()
    expect(w.findAll('.gantt__row')).toHaveLength(2)
    expect(w.findAll('.gantt__row')[1].text()).toContain('в архиве')
  })

  it('пересекающиеся задания — на разных дорожках', () => {
    const pairs = mountBoard().findAll('.gantt__row')[0].findAll('.pair-lane')
    expect(pairs[0].attributes('style')).toContain('top: 0px')
    expect(pairs[1].attributes('style')).toContain('top: 44px')
  })

  it('линия «сейчас» внутри окна', () => {
    expect(mountBoard().find('.now-line').exists()).toBe(true)
  })

  it('select из пары пробрасывается наверх', async () => {
    const w = mountBoard()
    await w.find('.pair').trigger('click')
    expect(w.emitted('select')[0][0].id).toBe('a')
  })
})
