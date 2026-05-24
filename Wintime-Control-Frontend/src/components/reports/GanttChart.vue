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
      axisLabel: {
        formatter: (value) => dayjs(value).format('HH:mm')
      }
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
        data: seriesData
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
