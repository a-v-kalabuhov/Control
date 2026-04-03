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
  }
})

const chartRef = ref(null)
let chart = null

const initChart = () => {
  if (!chartRef.value) return

  chart = echarts.init(chartRef.value)

  const option = {
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' }
    },
    legend: {
       ['Работа', 'Простой'],
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
       props.data.map(d => d.immName)
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
         props.data.map(d => (d.totalWorkSeconds / 3600).toFixed(2)),
        itemStyle: { color: '#10b981' }
      },
      {
        name: 'Простой',
        type: 'bar',
        stack: 'total',
         props.data.map(d => (d.totalDowntimeSeconds / 3600).toFixed(2)),
        itemStyle: { color: '#ef4444' }
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
})

watch(() => props.data, () => {
  initChart()
}, { deep: true })
</script>