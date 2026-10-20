<template>
  <div class="cushion-chart bg-white rounded-lg shadow-sm border">
    <div class="flex items-center justify-between px-3 py-1.5 border-b">
      <span class="text-sm font-medium text-gray-700">Подушка по циклам</span>
      <label class="text-xs text-gray-500 flex items-center gap-1 cursor-pointer">
        <input type="checkbox" v-model="fromZero" /> с нуля
      </label>
    </div>
    <div ref="chartRef" class="w-full h-44"></div>
  </div>
</template>

<script setup>
// Подушка — величина цикла, а не сигнал: точка на момент закрытия цикла (BL-45),
// без ступенчатого удержания между циклами, как у SignalChart.
import { ref, onMounted, onUnmounted, watch, nextTick } from 'vue'
import * as echarts from 'echarts'
import { buildCushionSeries } from '@/utils/telemetryChart'
import { getEffectiveStatusMeta } from '@/constants/effectiveStatus'

const props = defineProps({
  cycles:        { type: Array,  default: () => [] }, // [{ start, end, isSuccessful, cushion }]
  segments:      { type: Array,  default: () => [] }, // [{ effectiveStatus, changedAt, endedAt }]
  windowStartMs: { type: Number, required: true },
  windowEndMs:   { type: Number, required: true },
})

const GROUP = 'telemetry-signals' // тот же, что у SignalChart: общий crosshair
const chartRef = ref(null)
let chart = null
let resizeObserver = null
const fromZero = ref(false)

function buildOption() {
  const markArea = {
    silent: true,
    itemStyle: { opacity: 0.12 },
    data: props.segments.map(s => ([
      { xAxis: new Date(s.changedAt).getTime(), itemStyle: { color: getEffectiveStatusMeta(s.effectiveStatus).hex } },
      { xAxis: new Date(s.endedAt ?? props.windowEndMs).getTime() },
    ])),
  }

  return {
    animation: false,
    grid: { left: 52, right: 12, top: 8, bottom: 22 },
    tooltip: { trigger: 'axis', axisPointer: { type: 'line' } },
    axisPointer: { link: [{ xAxisIndex: 'all' }] },
    xAxis: { type: 'time', min: props.windowStartMs, max: props.windowEndMs, axisLabel: { fontSize: 10 } },
    yAxis: { type: 'value', scale: !fromZero.value, min: fromZero.value ? 0 : null, axisLabel: { fontSize: 10 } },
    series: [{
      name: 'Подушка',
      type: 'line',
      showSymbol: true,
      symbolSize: 5,
      data: buildCushionSeries(props.cycles),
      lineStyle: { width: 1, color: '#7c3aed' },
      itemStyle: { color: '#7c3aed' },
      markArea,
    }],
  }
}

function render() {
  chart?.setOption(buildOption(), true)
}

function onResize() { chart?.resize() }

onMounted(async () => {
  await nextTick()
  chart = echarts.init(chartRef.value)
  chart.group = GROUP
  echarts.connect(GROUP)
  render()
  resizeObserver = new ResizeObserver(onResize)
  resizeObserver.observe(chartRef.value)
})

onUnmounted(() => {
  resizeObserver?.disconnect()
  resizeObserver = null
  chart?.dispose()
})

watch(
  () => [props.cycles, props.segments, props.windowStartMs, props.windowEndMs, fromZero.value],
  render,
  { deep: true },
)
</script>
