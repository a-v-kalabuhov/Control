<template>
  <div ref="chartRef" class="w-full h-96"></div>
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

const initChart = () => {
  if (!chartRef.value || !props.data.length) return

  chart = echarts.init(chartRef.value)

  const seriesData = props.data.map((imm, idx) => ({
    name: imm.immName,
    type: 'custom',
    renderItem: (params, api) => {
      const categoryIndex = api.value(0)
      const start = api.coord([api.value(1), categoryIndex])
      const end = api.coord([api.value(2), categoryIndex])
      const height = api.size([0, 1])[1] * 0.6

      return {
        type: 'rect',
        shape: {
          x: start[0],
          y: start[1] - height / 2,
          width: end[0] - start[0],
          height
        },
        style: api.style({
          fill: api.visual('color')
        })
      }
    },
    encode: {
      x: [1, 2],
      y: 0
    },
    data: imm.timeline?.map(t => [
      imm.immName,
      dayjs(t.start).valueOf(),
      dayjs(t.end).valueOf(),
      t.type
    ]) || []
  }))

  const option = {
    title: {
      text: 'График работы ТПА',
      left: 'center'
    },
    tooltip: {
      formatter: (params) => {
        return `${params.name}<br/>${dayjs(params.value[1]).format('HH:mm')} - ${dayjs(params.value[2]).format('HH:mm')}`
      }
    },
    grid: {
      left: '3%',
      right: '4%',
      bottom: '3%',
      top: '15%',
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
       props.data.map(i => i.immName)
    },
    series: seriesData
  }

  chart.setOption(option)
}

onMounted(() => {
  initChart()
  window.addEventListener('resize', () => chart?.resize())
})

onUnmounted(() => {
  chart?.dispose()
})

watch(() => props.data, () => {
  initChart()
}, { deep: true })
</script>