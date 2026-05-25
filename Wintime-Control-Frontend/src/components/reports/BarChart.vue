<template>
  <div ref="chartRef" class="w-full h-80"></div>
</template>

<script setup>
import { ref, onMounted, onUnmounted, watch } from 'vue'
import * as echarts from 'echarts'

const props = defineProps({
  data: {
    type: Array,
    default: () => []
  },
  labelField: {
    type: String,
    default: 'immName'
  }
})

const chartRef = ref(null)
let chart = null

const initChart = () => {
  if (!chartRef.value) return

  if (!chart) chart = echarts.init(chartRef.value)

  const option = {
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' }
    },
    legend: {
      data: ['Работа', 'Наладка', 'Простой'],
      top: '10%'
    },
    grid: {
      left: '3%',
      right: '4%',
      bottom: '3%',
      top: '20%',
      containLabel: true
    },
    xAxis: {
      type: 'category',
      data: props.data.map(d => d[props.labelField])
    },
    yAxis: {
      type: 'value',
      name: 'Часы'
    },
    series: [
      {
        name: 'Работа',
        type: 'bar',
        stack: 'total',
        data: props.data.map(d => (d.totalWorkSeconds / 3600).toFixed(2)),
        itemStyle: { color: '#10b981' }
      },
      {
        name: 'Наладка',
        type: 'bar',
        stack: 'total',
        data: props.data.map(d => ((d.totalSetupSeconds ?? 0) / 3600).toFixed(2)),
        itemStyle: { color: '#f59e0b' }
      },
      {
        name: 'Простой',
        type: 'bar',
        stack: 'total',
        data: props.data.map(d => (d.totalDowntimeSeconds / 3600).toFixed(2)),
        itemStyle: { color: '#ef4444' }
      }
    ]
  }

  chart.setOption(option, true)
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