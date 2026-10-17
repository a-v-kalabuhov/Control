// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'

const chartMock = vi.hoisted(() => ({
  setOption: vi.fn(), resize: vi.fn(), dispose: vi.fn(), dispatchAction: vi.fn(), on: vi.fn()
}))

vi.mock('echarts', () => ({
  init: () => chartMock,
  graphic: { clipRectByRect: vi.fn(() => ({})) }
}))

const { default: GanttChart } = await import('../GanttChart.vue')

const data = [{
  immId: 'imm-1',
  immName: 'ТПА-1',
  timeline: [{ type: 'Production', start: '2026-10-01T05:00:00Z', end: '2026-10-01T06:00:00Z' }]
}]
const lastOption = () => chartMock.setOption.mock.calls.at(-1)[0]

describe('GanttChart — палитра эффективных состояний', () => {
  it('легенда — все состояния отчёта в цветах и подписях общей палитры', () => {
    const w = mount(GanttChart, { props: { data: [], shifts: [], date: '2026-10-01' } })

    const items = w.findAll('[data-test="legend-item"]')
    expect(items.map(i => i.text())).toEqual(REPORT_STATUS_KEYS.map(k => REPORT_STATUS[k].label))
    items.forEach((item, i) => {
      const swatch = item.find('span').element.style.background
      const expected = REPORT_STATUS[REPORT_STATUS_KEYS[i]].hex
      // jsdom нормализует цвет в rgb(...)
      const hex = expected.replace('#', '')
      const rgb = `rgb(${parseInt(hex.slice(0, 2), 16)}, ${parseInt(hex.slice(2, 4), 16)}, ${parseInt(hex.slice(4, 6), 16)})`
      expect(swatch).toBe(rgb)
    })
    expect(w.text()).not.toContain('Авария')
  })
})

describe('GanttChart — масштаб', () => {
  beforeEach(() => vi.clearAllMocks())

  it('ось времени масштабируется ползунком и колесом, минимум — минута', () => {
    mount(GanttChart, { props: { data, shifts: [], date: '2026-10-01' } })

    const { dataZoom, xAxis } = lastOption()
    expect(dataZoom.map(z => z.type)).toEqual(['slider', 'inside'])
    dataZoom.forEach(z => {
      expect(z.xAxisIndex).toBe(0)
      expect(z.filterMode).toBe('none')
      expect(z.minValueSpan).toBe(60 * 1000)
    })
    // Иначе при приближении не появятся мелкие деления
    expect(xAxis.minInterval).toBeUndefined()
    expect(xAxis.axisLabel.formatter.second).toBe('{HH}:{mm}:{ss}')
    expect(xAxis.axisLabel.formatter.minute).toBe('{HH}:{mm}')
    expect(xAxis.axisLabel.formatter.day).toBe('{HH}:{mm}')
  })

  it('resetZoom возвращает весь период', () => {
    const w = mount(GanttChart, { props: { data, shifts: [], date: '2026-10-01' } })

    w.vm.resetZoom()

    expect(chartMock.dispatchAction).toHaveBeenCalledWith({ type: 'dataZoom', start: 0, end: 100 })
  })

  it('снимает тот же обработчик resize, что повесил', () => {
    const add = vi.spyOn(window, 'addEventListener')
    const remove = vi.spyOn(window, 'removeEventListener')
    const w = mount(GanttChart, { props: { data, shifts: [], date: '2026-10-01' } })
    const handler = add.mock.calls.find(c => c[0] === 'resize')[1]

    w.unmount()

    expect(remove).toHaveBeenCalledWith('resize', handler)
    add.mockRestore(); remove.mockRestore()
  })
})

describe('GanttChart — клик по сегменту', () => {
  beforeEach(() => vi.clearAllMocks())

  it('клик по сегменту → segment-click с ТПА и границами сегмента', () => {
    const w = mount(GanttChart, { props: { data, shifts: [], date: '2026-10-01' } })
    const onClick = chartMock.on.mock.calls.find(c => c[0] === 'click')[1]
    const s = Date.parse('2026-10-01T05:00:00Z'), e = Date.parse('2026-10-01T06:00:00Z')

    onClick({ seriesType: 'custom', value: ['ТПА-1', s, e, 'Production', 'imm-1'] })

    expect(w.emitted('segment-click')).toEqual([[{ immId: 'imm-1', start: s, end: e }]])
  })

  it('данные серии несут immId пятым значением', () => {
    mount(GanttChart, { props: { data, shifts: [], date: '2026-10-01' } })
    expect(lastOption().series[0].data[0][4]).toBe('imm-1')
  })
})
