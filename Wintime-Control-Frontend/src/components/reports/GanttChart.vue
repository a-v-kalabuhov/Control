<template>
  <div>
    <div ref="chartRef" class="w-full h-96"></div>
    <div class="flex flex-wrap gap-5 justify-center mt-3 text-sm text-gray-600">
      <div v-for="item in legend" :key="item.type" data-test="legend-item" class="flex items-center gap-1.5">
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
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'

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

const emit = defineEmits(['segment-click'])

const chartRef = ref(null)
let chart = null

// Цвета и подписи — общая палитра эффективных состояний (как карточка ТПА и «Производительность»).
const statusMeta = (type) => REPORT_STATUS[type] ?? REPORT_STATUS.NoData

const legend = REPORT_STATUS_KEYS.map(type => ({ type, color: REPORT_STATUS[type].hex, label: REPORT_STATUS[type].label }))

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

  const shift = props.shiftId ? props.shifts.find(s => s.id === props.shiftId) : null

  // Смена не выбрана — бэкенд отдаёт таймлайн за сутки, показываем всю ось суток
  if (!shift) {
    const dayStart = dayjs(dateStr).startOf('day')
    return {
      min: dayStart.valueOf(),
      max: dayStart.add(1, 'day').valueOf()
    }
  }

  const { start, end } = parseShiftBounds(shift, dateStr)
  return {
    min: start.subtract(30, 'minute').valueOf(),
    max: end.add(30, 'minute').valueOf()
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

  // Клик по сегменту — показать его эпизод в таблице статусов (вкладка отчёта).
  chart.on('click', (params) => {
    if (params.seriesType !== 'custom') return
    const [, start, end, , immId] = params.value
    emit('segment-click', { immId, start, end })
  })

  const immNames = props.data.map(i => i.immName)

  const seriesData = []
  props.data.forEach((imm) => {
    (imm.timeline ?? []).forEach(t => {
      seriesData.push([
        imm.immName,
        dayjs(t.start).valueOf(),
        dayjs(t.end).valueOf(),
        t.type,
        imm.immId
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
        return `${name}<br/>${statusMeta(type).label}<br/>${dayjs(start).format('HH:mm:ss')} — ${dayjs(end).format('HH:mm:ss')} (${duration})<br/><span style="color:#9ca3af">Клик — показать в таблице</span>`
      }
    },
    grid: {
      left: '3%',
      right: '4%',
      bottom: 48, // место под ползунок масштаба
      top: '5%',
      containLabel: true
    },
    // Масштаб по времени: ползунок под осью + колесо мыши/перетаскивание по диаграмме.
    // filterMode 'none' — сегмент, частично попавший в окно, не пропадает (его обрезает renderItem).
    dataZoom: [
      {
        type: 'slider', xAxisIndex: 0, filterMode: 'none', minValueSpan: 60 * 1000,
        bottom: 8, height: 20, labelFormatter: (value) => dayjs(value).format('HH:mm:ss')
      },
      { type: 'inside', xAxisIndex: 0, filterMode: 'none', minValueSpan: 60 * 1000 }
    ],
    xAxis: {
      type: 'time',
      min: axisMin ?? undefined,
      max: axisMax ?? undefined,
      axisLabel: {
        // Подпись по уровню деления: при сильном приближении — до секунд, полночь — время, не дата.
        formatter: {
          year: '{HH}:{mm}', month: '{HH}:{mm}', day: '{HH}:{mm}',
          hour: '{HH}:{mm}', minute: '{HH}:{mm}', second: '{HH}:{mm}:{ss}', millisecond: '{HH}:{mm}:{ss}'
        }
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

          // custom-серия сама не обрезается по сетке: сегмент, вышедший за границы оси,
          // рисовался бы поверх осей — клипуем прямоугольник вручную.
          const shape = echarts.graphic.clipRectByRect(
            {
              x: start[0],
              y: start[1] - height / 2,
              width: Math.max(end[0] - start[0], 1),
              height
            },
            {
              x: params.coordSys.x,
              y: params.coordSys.y,
              width: params.coordSys.width,
              height: params.coordSys.height
            }
          )
          if (!shape) return null

          return {
            type: 'rect',
            cursor: 'pointer',
            shape,
            style: api.style({
              fill: statusMeta(type).hex
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

// Одна и та же функция для add/removeEventListener — иначе обработчик не снимается.
const onResize = () => chart?.resize()

// Сброс масштаба к полному периоду (кнопка «Сбросить масштаб» в отчёте).
const resetZoom = () => chart?.dispatchAction({ type: 'dataZoom', start: 0, end: 100 })

defineExpose({ resetZoom, resize: onResize })

onMounted(() => {
  initChart()
  window.addEventListener('resize', onResize)
})

onUnmounted(() => {
  chart?.dispose()
  window.removeEventListener('resize', onResize)
})

watch(() => props.data, () => {
  initChart()
}, { deep: true })
</script>
