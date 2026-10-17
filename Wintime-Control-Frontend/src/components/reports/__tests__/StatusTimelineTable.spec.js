// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import dayjs from 'dayjs'
import StatusTimelineTable from '../StatusTimelineTable.vue'

const iso = (s) => dayjs(`2026-10-01 ${s}`).toISOString()
const seg = (type, from, to) => ({ type, start: iso(from), end: iso(to) })

async function mountTable(data, periodStart = iso('08:00'), periodEnd = iso('16:00')) {
  const w = mount(StatusTimelineTable, {
    props: { data, periodStart, periodEnd },
    global: { plugins: [ElementPlus] }
  })
  await flushPromises()
  return w
}

describe('StatusTimelineTable', () => {
  afterEach(() => vi.useRealTimers())

  it('группа на каждый ТПА со сводкой ненулевых состояний', async () => {
    const w = await mountTable([
      { immId: 'a', immName: 'ТПА-1', timeline: [seg('Setup', '07:30', '08:30'), seg('Production', '08:30', '16:30')] },
      { immId: 'b', immName: 'ТПА-2', timeline: [seg('NoTask', '08:00', '16:00')] }
    ])

    const groups = w.findAll('[data-test="imm-group"]')
    expect(groups).toHaveLength(2)
    expect(groups[0].text()).toContain('ТПА-1')
    const summary = groups[0].findAll('[data-test="summary-item"]').map(s => s.text())
    expect(summary).toEqual(['Работа 7:30:00', 'Наладка 0:30:00'])
  })

  it('строки: состояние, начало, конец, длительность — в пределах периода', async () => {
    const w = await mountTable([
      { immId: 'a', immName: 'ТПА-1', timeline: [seg('Setup', '07:30', '08:30'), seg('Production', '08:30', '16:30')] }
    ])

    const text = w.find('[data-test="imm-group"] .el-table').text()
    expect(text).toContain('Наладка')
    expect(text).toContain('08:00:00')
    expect(text).toContain('08:30:00')
    expect(text).toContain('0:30:00')
    expect(text).toContain('16:00:00')
    // по ячейкам: в сплошном тексте «16:00:00» + «7:30:00» дают ложное «07:30:00»
    const cells = w.findAll('[data-test="imm-group"] .el-table td').map(c => c.text())
    expect(cells).not.toContain('07:30:00')
  })

  it('идущий период: конец последнего сегмента — «сейчас»', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(dayjs('2026-10-01 10:00').toDate())
    const w = await mountTable([
      { immId: 'a', immName: 'ТПА-1', timeline: [seg('Production', '08:00', '10:00')] }
    ])

    expect(w.findAll('[data-test="row-end"]').at(-1).text()).toBe('сейчас')
  })

  it('ТПА без сегментов в периоде — «Нет данных за период»', async () => {
    const w = await mountTable([{ immId: 'a', immName: 'ТПА-1', timeline: [] }])

    expect(w.find('[data-test="no-data"]').text()).toBe('Нет данных за период')
  })
})
