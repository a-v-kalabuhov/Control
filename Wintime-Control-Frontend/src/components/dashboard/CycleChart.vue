<template>
  <div ref="chartRef" class="w-full h-64"></div>
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
  parameterName: {
    type: String,
    default: 'cycle_time'
  },
  title: {
    type: String,
    default: 'Параметр'
  }
})

const chartRef = ref(null)
let chart = null

const initChart = () => {
  if (!chartRef.value) return

  chart = echarts.init(chartRef.value)

  const option = {
    title: {
      text: props.title,
      left: 'center',
      textStyle: {
        fontSize: 14,
        fontWeight: 'bold'
      }
    },
    tooltip: {
      trigger: 'axis',
      formatter: (params) => {
        const point = params[0]
        return `${point.name}<br/>${point.value}`
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
      type: 'category',
      boundaryGap: false,
      data: props.data.map(d => dayjs(d.timestamp).format('HH:mm:ss')),
      axisLabel: {
        rotate: 45,
        fontSize: 10
      }
    },
    yAxis: {
      type: 'value',
      scale: true,
      axisLabel: {
        formatter: (value) => {
          if (props.parameterName.includes('temp')) {
            return value + '°C'
          } else if (props.parameterName.includes('pressure')) {
            return value + ' бар'
          } else if (props.parameterName.includes('cycle')) {
            return value + ' сек'
          }
          return value
        }
      }
    },
    series: [
      {
        name: props.title,
        type: 'line',
        smooth: true,
        data: props.data.map(d => d.valueNumeric || 0),
        itemStyle: {
          color: '#3b82f6'
        },
        areaStyle: {
          color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
            { offset: 0, color: 'rgba(59, 130, 246, 0.3)' },
            { offset: 1, color: 'rgba(59, 130, 246, 0.05)' }
          ])
        }
      }
    ]
  }

  chart.setOption(option)
}

onMounted(() => {
  initChart()
  
  window.addEventListener('resize', () => {
    chart?.resize()
  })
})

onUnmounted(() => {
  chart?.dispose()
})

watch(() => props.data, () => {
  initChart()
}, { deep: true })
</script>