<template>
  <div ref="chartRef" class="w-full" style="height: 220px"></div>
</template>

<script setup>
import { ref, onMounted, onUnmounted, watch } from 'vue'
import * as echarts from 'echarts'
import { buildDayChartOption } from '@/utils/equipmentReport'

defineOptions({ name: 'EffectiveStatusDayChart' })

const props = defineProps({
  // [{ date, seconds: { Production, …, NoData } }]
  days: { type: Array, default: () => [] }
})

const chartRef = ref(null)
let chart = null

const render = () => {
  if (!chart) return
  chart.setOption(buildDayChartOption(props.days), true)
}

const onResize = () => chart?.resize()

onMounted(() => {
  chart = echarts.init(chartRef.value)
  render()
  window.addEventListener('resize', onResize)
})

onUnmounted(() => {
  window.removeEventListener('resize', onResize)
  chart?.dispose()
  chart = null
})

watch(() => props.days, render, { deep: true })
</script>
