<template>
  <div>
    <div ref="chartRef" class="w-full h-96"></div>
    <div class="flex gap-5 justify-center mt-3 text-sm text-gray-600">
      <div v-for="item in legend" :key="item.type" class="flex items-center gap-1.5">
        <span class="inline-block w-4 h-4 rounded" :style="{ background: item.color }"></span>
        {{ item.label }}
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted, watch } from 'vue'
import * as echarts from 'echarts'
import dayjs from 'dayjs'

const props = defineProps({
  data: {
    type: Array,
    default: () => []
  },
  shifts: {
    type: Array,
    default: () => []
  },
  date: {
    type: String,
    default: ''
  },
  shiftId: {
    type: [Number, String],
    default: null
  }
})

const chartRef = ref(null)
let chart = null

const colorMap = {
  work:    '#67c23a',
  setup:   '#409eff',
  alarm:   '#f56c6c',
  idle:    '#e6a23c',
  offline: '#909399'
}

const typeLabels = {
  work:    'Работа',
  setup:   'Наладка',
  alarm:   'Авария',
  idle:    'Простой',
  offline: 'Нет связи'
}

const legend = Object.entries(colorMap).map(([type, color]) => ({
  type,
  color,
  label: typeLabels[type]
}))

// Разбираем время смены (HH:mm) в timestamp для заданной даты.
// Если endTime <= startTime — смена переходит через полночь, сдвигаем на +1 день.
const parseShiftBounds = (shift, dateStr) => {
  const start = dayjs(`${dateStr} ${shift.startTime}`)
  let end = dayjs(`${dateStr} ${shift.endTime}`)
  if (end.valueOf() <= start.valueOf()) {
    end = end.add(1, 'day')
  }
  return { start, end }
}

const computeAxisBounds = () => {
  const dateStr = props.date || dayjs().format('YYYY-MM-DD')

  if (!props.shifts.length) return { min: null, max: null }

  let targetShifts = props.shifts

  if (props.shiftId) {
    const found = props.shifts.find(s => s.id === props.shiftId)
    if (found) targetShifts = [found]
  }

  const parsed = targetShifts.map(s => parseShiftBounds(s, dateStr))
  const minStart = parsed.reduce((a, b) => (a.start.valueOf() < b.start.valueOf() ? a : b)).start
  const maxEnd   = parsed.reduce((a, b) => (a.end.valueOf() > b.end.valueOf() ? a : b)).end

  return {
    min: minStart.subtract(30, 'minute').valueOf(),
    max: maxEnd.add(30, 'minute').valueOf()
  }
}

// Строим список markLine: для каждой целой часовой отметки между min и max —
// либо тонкая серая линия сетки, либо чёткая линия границы смены.
const buildMarkLines = (axisMin, axisMax) => {
  const dateStr = props.date || dayjs().format('YYYY-MM-DD')

  // Собираем timestamp'ы всех границ смен (start + end)
  const shiftBoundaryMap = new Map() // ts -> label
  props.shifts.forEach(s => {
    const { start, end } = parseShiftBounds(s, dateStr)
    const startTs = start.valueOf()
    const endTs   = end.valueOf()
    // Метку ставим только для начала смены
    shiftBoundaryMap.set(startTs, `Смена ${s.number ?? ''} ${s.startTime}`.trim())
    if (!shiftBoundaryMap.has(endTs)) {
      shiftBoundaryMap.set(endTs, '')
    }
  })

  const markLines = []

  // Первая целая часовая отметка >= axisMin
  let t = dayjs(axisMin).startOf('hour')
  if (t.valueOf() < axisMin) t = t.add(1, 'hour')

  while (t.valueOf() <= axisMax) {
    const ts = t.valueOf()

    if (shiftBoundaryMap.has(ts)) {
      const label = shiftBoundaryMap.get(ts)
      markLines.push({
        name: label,
        xAxis: ts,
        lineStyle: { color: '#374151', type: 'dashed', width: 1.5 },
        label: label
          ? { show: true, position: 'insideStartTop', formatter: label, color: '#374151', fontSize: 11 }
          : { show: false }
      })
    } else {
      markLines.push({
        name: '',
        xAxis: ts,
        lineStyle: { color: '#d1d5db', type: 'solid', width: 0.8 },
        label: { show: false }
      })
    }

    t = t.add(1, 'hour')
  }

  return markLines
}

const initChart = () => {
  if (!chartRef.value || !props.data.length) return

  chart?.dispose()
  chart = echarts.init(chartRef.value)

  const immNames = props.data.map(i => i.immName)

  const seriesData = []
  props.data.forEach((imm) => {
    (imm.timeline ?? []).forEach(t => {
      seriesData.push([
        imm.immName,
        dayjs(t.start).valueOf(),
        dayjs(t.end).valueOf(),
        t.type
      ])
    })
  })

  const { min: axisMin, max: axisMax } = computeAxisBounds()
  const markLines = (axisMin !== null) ? buildMarkLines(axisMin, axisMax) : []

  const option = {
    tooltip: {
      formatter: (params) => {
        const [name, start, end, type] = params.value
        const totalSec = Math.round((end - start) / 1000)
        const h = Math.floor(totalSec / 3600)
        const m = Math.floor((totalSec % 3600) / 60)
        const s = totalSec % 60
        const duration = [h && `${h} ч.`, m && `${m} мин.`, (!h && !m) || s ? `${s} сек.` : ''].filter(Boolean).join(' ')
        return `${name}<br/>${typeLabels[type] ?? type}<br/>${dayjs(start).format('HH:mm:ss')} — ${dayjs(end).format('HH:mm:ss')} (${duration})`
      }
    },
    grid: {
      left: '3%',
      right: '4%',
      bottom: '3%',
      top: '5%',
      containLabel: true
    },
    xAxis: {
      type: 'time',
      min: axisMin ?? undefined,
      max: axisMax ?? undefined,
      minInterval: 3600 * 1000,
      axisLabel: {
        formatter: (value) => dayjs(value).format('HH:mm')
      },
      splitLine: { show: false }
    },
    yAxis: {
      type: 'category',
      data: immNames
    },
    series: [
      {
        type: 'custom',
        renderItem: (params, api) => {
          const categoryIndex = api.value(0)
          const start = api.coord([api.value(1), categoryIndex])
          const end   = api.coord([api.value(2), categoryIndex])
          const height = api.size([0, 1])[1] * 0.6
          const type = api.value(3)

          return {
            type: 'rect',
            shape: {
              x: start[0],
              y: start[1] - height / 2,
              width: Math.max(end[0] - start[0], 1),
              height
            },
            style: api.style({
              fill: colorMap[type] ?? colorMap.offline
            })
          }
        },
        encode: { x: [1, 2], y: 0 },
        data: seriesData,
        markLine: markLines.length ? {
          silent: true,
          symbol: ['none', 'none'],
          data: markLines
        } : undefined
      }
    ]
  }

  chart.setOption(option)
}

onMounted(() => {
  initChart()
  window.addEventListener('resize', () => chart?.resize())
})

onUnmounted(() => {
  chart?.dispose()
  window.removeEventListener('resize', () => chart?.resize())
})

watch(() => props.data, () => {
  initChart()
}, { deep: true })
</script>
