import dayjs from 'dayjs'
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'

// Метрики и представление отчёта «Производительность оборудования».

export function knownSeconds(seconds) {
  const total = REPORT_STATUS_KEYS.reduce((s, k) => s + (seconds?.[k] ?? 0), 0)
  return total - (seconds?.Offline ?? 0) - (seconds?.NoData ?? 0)
}

export function sumSeconds(immData) {
  const total = Object.fromEntries(REPORT_STATUS_KEYS.map(k => [k, 0]))
  for (const item of immData) {
    for (const k of REPORT_STATUS_KEYS) total[k] += item.seconds?.[k] ?? 0
  }
  return total
}

// Взвешенная эффективность парка: Σ Работа / Σ известного времени.
export function fleetEfficiency(immData) {
  const total = sumSeconds(immData)
  const known = knownSeconds(total)
  return known > 0 ? Math.round((total.Production / known) * 100) : null
}

export function countBelow(immData, threshold) {
  return immData.filter(i => i.efficiency != null && i.efficiency < threshold).length
}

// Список immIds для запроса: если выбраны все опции текущего режима архива — undefined
// (бэкенд трактует отсутствие immIds как «все ТПА режима»), чтобы не раздувать URL.
export function selectedIdsParam(selected, options) {
  const chosen = new Set(selected)
  return options.every(o => chosen.has(o.id)) ? undefined : selected
}

export function toHours(seconds) {
  return ((seconds ?? 0) / 3600).toFixed(1)
}

export function formatEfficiency(value) {
  return value == null ? '—' : `${Math.round(value)}%`
}

export function efficiencyClass(value) {
  if (value == null) return 'text-gray-400'
  if (value >= 85) return 'text-green-600'
  if (value >= 70) return 'text-yellow-600'
  return 'text-red-600'
}

const NO_DATA_DECAL = {
  symbol: 'rect', symbolSize: 1, dashArrayX: [1, 0], dashArrayY: [2, 4],
  rotation: Math.PI / 4, color: 'rgba(0, 0, 0, 0.15)'
}

// Опция ECharts для поднёвной диаграммы одного ТПА: столбцы с накоплением, шкала 0–24 ч.
export function buildDayChartOption(days) {
  return {
    legend: { show: false },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      formatter: (params) => {
        const rows = params
          .filter(p => p.value > 0)
          .map(p => `${p.marker}${p.seriesName}: ${p.value} ч`)
        return [params[0]?.axisValue, ...rows].join('<br/>')
      }
    },
    grid: { left: 36, right: 12, top: 16, bottom: 24 },
    // Дата суток завода приходит как 'YYYY-MM-DDT00:00:00Z' — берём дату без перевода в пояс браузера.
    xAxis: { type: 'category', data: days.map(d => dayjs(String(d.date).slice(0, 10)).format('DD.MM')) },
    yAxis: { type: 'value', min: 0, max: 24, interval: 6, name: 'ч' },
    series: REPORT_STATUS_KEYS.map(k => ({
      name: REPORT_STATUS[k].label,
      type: 'bar',
      stack: 'day',
      barMaxWidth: 40,
      data: days.map(d => +((d.seconds?.[k] ?? 0) / 3600).toFixed(2)),
      // decal серии — только объект: ECharts пишет в него dirty, строка 'none' роняет рендер.
      itemStyle: k === 'NoData'
        ? { color: REPORT_STATUS[k].hex, decal: NO_DATA_DECAL }
        : { color: REPORT_STATUS[k].hex }
    }))
  }
}
