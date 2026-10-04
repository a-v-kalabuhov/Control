// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import EquipmentImmRow from '../EquipmentImmRow.vue'

const H = 3600
const item = (o = {}) => ({
  immId: '1', immName: 'ТПА-1', isActive: true,
  seconds: { Production: 20 * H, Setup: 0, Downtime: 0, Unplanned: 0, NoTask: 0, Offline: 0, NoData: 4 * H },
  totalCycles: 1200, avgCycleSeconds: 35.25, efficiency: 100,
  days: [], ...o
})

function mountRow(props) {
  return mount(EquipmentImmRow, {
    props: { item: props },
    global: { plugins: [ElementPlus], stubs: { EffectiveStatusDayChart: true } }
  })
}

describe('EquipmentImmRow', () => {
  it('показывает имя, эффективность, часы по статусам и циклы', () => {
    const w = mountRow(item())
    expect(w.text()).toContain('ТПА-1')
    expect(w.text()).toContain('100%')
    expect(w.text()).toContain('20.0 ч')
    expect(w.text()).toContain('Нет данных')
    expect(w.text()).toContain('1200')
    expect(w.text()).toContain('35.3 с')
    expect(w.text()).not.toContain('Архив')
  })

  it('архивный ТПА помечен бейджем, эффективность null → «—»', () => {
    const w = mountRow(item({ isActive: false, efficiency: null, avgCycleSeconds: 0 }))
    expect(w.text()).toContain('Архив')
    expect(w.text()).toContain('Эффективность: —')
  })

  it('передаёт days в диаграмму', () => {
    const days = [{ date: '2026-10-01T00:00:00Z', seconds: {} }]
    const w = mountRow(item({ days }))
    expect(w.findComponent({ name: 'EffectiveStatusDayChart' }).props('days')).toEqual(days)
  })
})
