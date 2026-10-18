// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import ImmCard from '../ImmCard.vue'

const imm = (o = {}) => ({
  id: '1', name: 'ТПА-1', manufacturer: 'Haitian', model: 'MA', status: 'Production',
  currentTaskId: 't1', currentMoldName: 'ПФ-1', personnelName: 'Иванов',
  planQuantity: 100, actualQuantity: 40, cycleCount: 3, currentCycleTime: 20,
  taskStartedAt: new Date(Date.now() - 3_600_000).toISOString(),
  releaseRatePerHour: 240.4, releaseForecastAt: new Date(Date.now() + 900_000).toISOString(),
  lastUpdate: new Date().toISOString(),
  ...o
})

const mountCard = (props) => mount(ImmCard, {
  props: { imm: props },
  global: { plugins: [ElementPlus], stubs: { ImmStatusBadge: true, Document: true, User: true } }
})

describe('ImmCard — темп и прогноз (BL-32)', () => {
  it('темп и прогноз берёт из полей API', () => {
    const w = mountCard(imm())
    expect(w.text()).toContain('240 шт/ч')
    expect(w.text()).toContain('Прогноз окончания')
    expect(w.text()).toMatch(/в \d\d:\d\d/)
  })

  it('нет полей прогноза — темп «—», строка прогноза скрыта', () => {
    const w = mountCard(imm({ releaseRatePerHour: null, releaseForecastAt: null }))
    expect(w.text()).not.toContain('шт/ч')
    expect(w.text()).not.toContain('Прогноз окончания')
  })
})

describe('ImmCard — изделие (PZP-19)', () => {
  it('полный артикул виден под названием ПФ', () => {
    const w = mountCard(imm({ currentProductArticle: 'A123.BLK' }))
    expect(w.text()).toContain('A123.BLK')
  })
})
