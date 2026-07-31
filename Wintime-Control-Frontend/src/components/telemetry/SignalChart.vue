<template>
  <div class="signal-chart bg-white rounded-lg shadow-sm border">
    <div class="flex items-center justify-between px-3 py-1.5 border-b">
      <span class="text-sm font-medium text-gray-700">{{ signal.parameterName }}</span>
      <label v-if="axisKind === 'numeric'" class="text-xs text-gray-500 flex items-center gap-1 cursor-pointer">
        <input type="checkbox" v-model="fromZero" /> с нуля
      </label>
    </div>
    <div ref="chartRef" class="w-full h-44"></div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import * as echarts from 'echarts'
import { buildStepSeries, resolveAxisKind } from '@/utils/telemetryChart'
import { getEffectiveStatusMeta } from '@/constants/effectiveStatus'

const props = defineProps({
  signal:        { type: Object, required: true },   // { parameterName, type, points }
  cycles:        { type: Array,  default: () => [] }, // [{ start, end, isSuccessful }]
  segments:      { type: Array,  default: () => [] }, // [{ effectiveStatus, changedAt, endedAt }]
  windowStartMs: { type: Number, required: true },
  windowEndMs:   { type: Number, required: true },
})

const GROUP = 'telemetry-signals'
const chartRef = ref(null)
let chart = null
let resizeObserver = null
const fromZero = ref(false)
const axisKind = computed(() => resolveAxisKind(props.signal.type))

function buildOption() {
  const offlineStarts = props.segments
    .filter(s => s.effectiveStatus === 'Offline')
    .map(s => new Date(s.changedAt).getTime())
  const seriesData = buildStepSeries(props.signal.points, props.windowEndMs, offlineStarts)

  // Фон эффективного статуса.
  const markArea = {
    silent: true,
    itemStyle: { opacity: 0.12 },
    data: props.segments.map(s => ([
      { xAxis: new Date(s.changedAt).getTime(), itemStyle: { color: getEffectiveStatusMeta(s.effectiveStatus).hex } },
      { xAxis: new Date(s.endedAt ?? props.windowEndMs).getTime() },
    ])),
  }

  // Вертикали границ циклов (начало — по годности, конец — серый).
  const markLine = {
    silent: true,
    symbol: 'none',
    label: { show: false },
    data: props.cycles.flatMap(c => ([
      { xAxis: new Date(c.start).getTime(), lineStyle: { color: c.isSuccessful ? '#16a34a' : '#dc2626', type: 'solid', width: 1 } },
      { xAxis: new Date(c.end).getTime(),   lineStyle: { color: '#9ca3af', type: 'dashed', width: 1 } },
    ])),
  }

  const yAxis = axisKind.value === 'numeric'
    ? { type: 'value', scale: !fromZero.value, min: fromZero.value ? 0 : null, axisLabel: { fontSize: 10 } }
    : { type: 'value', min: -0.1, max: 1.1, interval: 1,
        axisLabel: { fontSize: 10, formatter: v => (v === 1 ? '1' : v === 0 ? '0' : '') } }

  return {
    animation: false,
    grid: { left: 52, right: 12, top: 8, bottom: 22 },
    tooltip: { trigger: 'axis', axisPointer: { type: 'line' } },
    axisPointer: { link: [{ xAxisIndex: 'all' }] },
    xAxis: { type: 'time', min: props.windowStartMs, max: props.windowEndMs, axisLabel: { fontSize: 10 } },
    yAxis,
    series: [{
      name: props.signal.parameterName,
      type: 'line',
      step: 'end',
      showSymbol: false,
      connectNulls: false,
      data: seriesData,
      lineStyle: { width: axisKind.value === 'numeric' ? 1.5 : 2, color: '#2563eb' },
      itemStyle: { color: '#2563eb' },
      markArea,
      markLine,
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
  echarts.connect(GROUP) // общий crosshair/zoom по всем диаграммам столбца
  render()
  // ECharts фиксирует размер канваса при init и сам его не пересчитывает. Ширина контейнера
  // меняется без window.resize (появился вертикальный скроллбар, свернулось меню) — тогда
  // диаграммы столбца расходятся по правому краю. Следим за контейнером, а не за окном.
  resizeObserver = new ResizeObserver(onResize)
  resizeObserver.observe(chartRef.value)
})

onUnmounted(() => {
  resizeObserver?.disconnect()
  resizeObserver = null
  chart?.dispose()
})

watch(
  () => [props.signal, props.cycles, props.segments, props.windowStartMs, props.windowEndMs, fromZero.value],
  render,
  { deep: true },
)
</script>
