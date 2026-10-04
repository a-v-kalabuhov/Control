import { describe, it, expect } from 'vitest'
import {
  knownSeconds, fleetEfficiency, countBelow, sumSeconds,
  toHours, formatEfficiency, buildDayChartOption, selectedIdsParam
} from '@/utils/equipmentReport'

const H = 3600
const secs = (o) => ({ Production: 0, Setup: 0, Downtime: 0, Unplanned: 0, NoTask: 0, Offline: 0, NoData: 0, ...o })

describe('equipmentReport utils', () => {
  it('knownSeconds исключает Нет связи и Нет данных', () => {
    expect(knownSeconds(secs({ Production: 2 * H, NoTask: H, Offline: 5 * H, NoData: 7 * H }))).toBe(3 * H)
  })

  it('fleetEfficiency взвешенная, а не среднее процентов', () => {
    const immData = [
      { seconds: secs({ Production: 24 * H }), efficiency: 100 },            // 24 ч известно
      { seconds: secs({ Production: 0, NoTask: 1 * H, NoData: 23 * H }), efficiency: 0 } // 1 ч известно
    ]
    // Σ Работа 24 / Σ известного 25 = 96 %, а не (100 + 0) / 2 = 50 %
    expect(fleetEfficiency(immData)).toBe(96)
  })

  it('fleetEfficiency = null, если известного времени нет', () => {
    expect(fleetEfficiency([{ seconds: secs({ NoData: 24 * H }), efficiency: null }])).toBeNull()
    expect(fleetEfficiency([])).toBeNull()
  })

  it('countBelow игнорирует null', () => {
    const immData = [{ efficiency: 40 }, { efficiency: 65 }, { efficiency: 90 }, { efficiency: null }]
    expect(countBelow(immData, 70)).toBe(2)
    expect(countBelow(immData, 50)).toBe(1)
  })

  it('sumSeconds суммирует по ключам', () => {
    const total = sumSeconds([{ seconds: secs({ Setup: H }) }, { seconds: secs({ Setup: 2 * H, Offline: H }) }])
    expect(total.Setup).toBe(3 * H)
    expect(total.Offline).toBe(H)
    expect(total.NoData).toBe(0)
  })

  it('форматирование', () => {
    expect(toHours(5400)).toBe('1.5')
    expect(toHours(undefined)).toBe('0.0')
    expect(formatEfficiency(null)).toBe('—')
    expect(formatEfficiency(84.6)).toBe('85%')
  })

  it('buildDayChartOption: шкала 0–24, 7 серий в стеке, NoData заштрихован', () => {
    const days = [{ date: '2026-10-01T00:00:00Z', seconds: secs({ Production: 20 * H, NoData: 4 * H }) }]
    const opt = buildDayChartOption(days)
    expect(opt.yAxis).toMatchObject({ min: 0, max: 24, interval: 6 })
    expect(opt.xAxis.data).toEqual(['01.10'])
    expect(opt.series).toHaveLength(7)
    expect(opt.series.every(s => s.stack === 'day')).toBe(true)
    expect(opt.series[0].data).toEqual([20])
    const noData = opt.series.find(s => s.name === 'Нет данных')
    expect(noData.itemStyle.decal).toBeTypeOf('object')
    expect(opt.legend.show).toBe(false)
  })

  it('selectedIdsParam: выбраны все опции режима — undefined (бэкенд сам берёт все ТПА режима)', () => {
    const options = [{ id: '1' }, { id: '2' }]
    expect(selectedIdsParam(['2', '1'], options)).toBeUndefined()
  })

  it('selectedIdsParam: частичный выбор отправляется как есть', () => {
    const options = [{ id: '1' }, { id: '2' }]
    expect(selectedIdsParam(['1'], options)).toEqual(['1'])
  })
})
