// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'

vi.mock('echarts', () => ({
  init: () => ({ setOption: vi.fn(), resize: vi.fn(), dispose: vi.fn() }),
  graphic: { clipRectByRect: vi.fn(() => ({})) }
}))

const { default: GanttChart } = await import('../GanttChart.vue')

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
