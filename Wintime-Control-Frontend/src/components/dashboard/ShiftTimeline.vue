<template>
  <div class="shift-timeline">
    <!-- Временна́я шкала -->
    <div class="flex">
      <div class="row-label"></div>
      <div class="flex-1 relative time-axis">
        <div
          v-for="tick in timeTicks"
          :key="tick.label"
          class="tick"
          :style="{ left: tick.pct }"
        >{{ tick.label }}</div>
      </div>
    </div>

    <!-- Строка: Статус -->
    <div class="flex items-center mb-2">
      <div class="row-label">Статус</div>
      <div class="flex-1 relative">
        <div class="status-bar">
          <div
            v-for="(seg, i) in statusSegments"
            :key="i"
            class="seg-block"
            :style="segStyle(seg)"
            :title="segTitle(seg)"
          ></div>
          <!-- Ещё не прошедшее время смены -->
          <div
            v-if="futureStyle"
            class="seg-future"
            :style="futureStyle"
            :title="`Ещё не наступило\n${formatClock(nowClamped)} — ${formatClock(shiftEnd)}`"
          ></div>
        </div>
        <div class="now-line now-line--labeled" :style="{ left: nowPct }" :data-time="nowLabel"></div>
      </div>
    </div>

    <!-- Строка: План наладки (BL-40) -->
    <div class="flex items-center mb-2">
      <div class="row-label">План</div>
      <div class="flex-1 relative">
        <div class="plan-bar" :style="{ height: planRowHeight + 'px' }">
          <div
            v-for="bar in planBars"
            :key="'plan-' + bar.task.id"
            class="plan-block"
            :style="planStyle(bar)"
            :title="planTitle(bar.task)"
          >
            <template v-if="!bar.layout.isMarker">
              <div class="plan-name">{{ bar.task.moldName || 'Задание' }}</div>
              <div class="plan-marks">
                <span
                  v-for="(m, i) in planMarks(bar.task)"
                  :key="i"
                  :class="'mark--' + m.tone"
                >{{ i ? ' · ' : '' }}{{ m.text }}</span>
              </div>
            </template>
          </div>
        </div>
        <div class="now-line" :style="{ left: nowPct }"></div>
      </div>
    </div>

    <!-- Строка: Задания -->
    <div class="flex items-center">
      <div class="row-label">Задания</div>
      <div class="flex-1 relative">
        <div class="task-bar" :style="{ height: taskRowHeight + 'px' }">
          <div
            v-for="bar in taskBars"
            :key="bar.task.id"
            class="task-block"
            :style="taskStyle(bar)"
            :title="taskTitle(bar.task)"
          >
            <div class="task-name">{{ bar.task.moldName || 'Задание' }}</div>
            <div class="task-qty">{{ bar.task.actualQuantity }}&thinsp;/&thinsp;{{ bar.task.planQuantity }} шт</div>
          </div>
        </div>
        <div class="now-line" :style="{ left: nowPct }"></div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, onMounted, onUnmounted } from 'vue'
import { getEffectiveStatusMeta } from '@/constants/effectiveStatus'
import {
  taskBarLayout, planBarLayout, planMarks, planTooltipLines, assignLanes, formatDuration, formatClock
} from '@/utils/shiftTimeline'

const props = defineProps({
  statusSegments: { type: Array, default: () => [] }, // EffectiveStatusSegmentDto[]
  tasks:          { type: Array, default: () => [] }, // TaskDto[]; индекс = цвет задания в обоих рядах
  shiftStart:     { type: Date, required: true },
  shiftEnd:       { type: Date, required: true },
})

const TASK_COLORS = ['#3b82f6', '#7c3aed', '#0891b2', '#059669', '#d97706']

// Дорожки (BL-40): высота дорожки фиксирована, ряд растёт с числом дорожек.
const TASK_LANE_H = 44
const PLAN_LANE_H = 34
const LANE_GAP = 4
const TASK_PAD = 7
const PLAN_PAD = 4
// Узкие бары/маркеры при раскладке считаем не уже этой доли смены — чтобы маркеры не слипались.
const MIN_LANE_PCT = 0.5

const shiftDur = computed(() => props.shiftEnd - props.shiftStart)

const now = ref(new Date())
let _nowTimer = null
onMounted(() => { _nowTimer = setInterval(() => { now.value = new Date() }, 30_000) })
onUnmounted(() => clearInterval(_nowTimer))

const nowClamped = computed(() => {
  if (now.value < props.shiftStart) return props.shiftStart
  if (now.value > props.shiftEnd)   return props.shiftEnd
  return now.value
})

function taskColor(idx) {
  return TASK_COLORS[idx % TASK_COLORS.length]
}

function withLanes(bars) {
  const lanes = assignLanes(bars.map(b => ({
    start: b.layout.leftPct,
    end:   b.layout.leftPct + Math.max(b.layout.widthPct, MIN_LANE_PCT),
  })))
  return bars.map((b, i) => ({ ...b, lane: lanes[i] }))
}

function rowHeight(bars, laneH, pad) {
  const n = bars.reduce((max, b) => Math.max(max, b.lane + 1), 1)
  return pad * 2 + n * laneH + (n - 1) * LANE_GAP
}

// Бары заданий: только задания с окном активности, пересекающим смену.
const taskBars = computed(() => withLanes(props.tasks
  .map((task, idx) => ({ task, idx, layout: taskBarLayout(task, props.shiftStart, props.shiftEnd, nowClamped.value) }))
  .filter(b => b.layout)))
// Плановые бары: только задания, чьё плановое окно наладки пересекает смену.
const planBars = computed(() => withLanes(props.tasks
  .map((task, idx) => ({ task, idx, layout: planBarLayout(task, props.shiftStart, props.shiftEnd) }))
  .filter(b => b.layout)))

const taskRowHeight = computed(() => rowHeight(taskBars.value, TASK_LANE_H, TASK_PAD))
const planRowHeight = computed(() => rowHeight(planBars.value, PLAN_LANE_H, PLAN_PAD))

const nowPct     = computed(() => toPct(nowClamped.value))
const nowLabel   = computed(() => formatClock(nowClamped.value))
// Заштрихованный «хвост» смены, который ещё не наступил (от now до конца смены).
// Позиционируется абсолютно, как и сегменты статусов.
const futureStyle = computed(() => {
  if (nowClamped.value >= props.shiftEnd) return null
  const left  = ((nowClamped.value - props.shiftStart) / shiftDur.value * 100).toFixed(3)
  const width = ((props.shiftEnd - nowClamped.value)   / shiftDur.value * 100).toFixed(3)
  return { left: left + '%', width: width + '%' }
})

const timeTicks = computed(() => {
  const ticks = []
  const start = props.shiftStart.getTime()
  const end   = props.shiftEnd.getTime()
  const hourMs = 3_600_000
  let t = new Date(start)
  t.setMinutes(0, 0, 0)
  if (t.getTime() < start) t = new Date(t.getTime() + hourMs)
  while (t.getTime() <= end) {
    ticks.push({ label: formatClock(t), pct: toPct(t) })
    t = new Date(t.getTime() + hourMs)
  }
  // Всегда добавляем начало смены если оно ровное
  const startTick = { label: formatClock(props.shiftStart), pct: '0%' }
  if (!ticks.find(tk => tk.pct === '0%')) ticks.unshift(startTick)
  return ticks
})

function toPct(date) {
  const ms = (new Date(date) - props.shiftStart)
  return (Math.max(0, Math.min(1, ms / shiftDur.value)) * 100).toFixed(3) + '%'
}

// Сегмент статуса позиционируется абсолютно по реальному времени (changedAt),
// а не упаковкой flex'ом — иначе блоки «уезжают» к левому краю, если история
// статусов начинается не с начала смены или в ней есть разрывы.
function segStyle(seg) {
  const start = new Date(seg.changedAt)
  const end   = seg.endedAt ? new Date(seg.endedAt) : nowClamped.value
  const clampedStart = start < props.shiftStart ? props.shiftStart : start
  const clampedEnd   = end   > props.shiftEnd   ? props.shiftEnd   : end
  const left  = ((clampedStart - props.shiftStart) / shiftDur.value * 100).toFixed(3)
  const width = ((clampedEnd   - clampedStart)     / shiftDur.value * 100).toFixed(3)
  return {
    left:       Math.max(0, left) + '%',
    width:      Math.max(0, width) + '%',
    background: statusColor(seg.effectiveStatus),
  }
}

// Наладка — светлым оттенком цвета задания, выпуск — основным цветом.
function taskStyle(bar) {
  const color = taskColor(bar.idx)
  const sp = bar.layout.setupPct.toFixed(3)
  return {
    left:       bar.layout.leftPct.toFixed(3) + '%',
    width:      bar.layout.widthPct.toFixed(3) + '%',
    top:        (TASK_PAD + bar.lane * (TASK_LANE_H + LANE_GAP)) + 'px',
    height:     TASK_LANE_H + 'px',
    background: `linear-gradient(to right, ${color}80 0 ${sp}%, ${color} ${sp}% 100%)`,
  }
}

// План — контур цвета задания; обрезанный сменой край без скругления.
function planStyle(bar) {
  const color = taskColor(bar.idx)
  const l = bar.layout
  const style = {
    left:        l.leftPct.toFixed(3) + '%',
    width:       l.isMarker ? '4px' : l.widthPct.toFixed(3) + '%',
    top:         (PLAN_PAD + bar.lane * (PLAN_LANE_H + LANE_GAP)) + 'px',
    height:      PLAN_LANE_H + 'px',
    borderColor: color,
    background:  color + '1f',
    color,
  }
  if (l.clippedLeft)  { style.borderTopLeftRadius = '0';  style.borderBottomLeftRadius = '0' }
  if (l.clippedRight) { style.borderTopRightRadius = '0'; style.borderBottomRightRadius = '0' }
  return style
}

function statusColor(seg) {
  return getEffectiveStatusMeta(seg).hex
}

function segTitle(seg) {
  const start = new Date(seg.changedAt)
  const end   = seg.endedAt ? new Date(seg.endedAt) : now.value
  return `${getEffectiveStatusMeta(seg.effectiveStatus).label}\n${formatClock(start)} — ${formatClock(end)}\n${formatDuration(end - start)}`
}

function planTitle(task) {
  return [task.moldName || 'Задание', ...planTooltipLines(task)].join('\n')
}

function taskTitle(task) {
  const start = new Date(task.setupStartedAt)
  const end   = new Date(task.completedAt ?? task.closedAt ?? now.value)
  const progress = task.planQuantity ? Math.round(task.actualQuantity / task.planQuantity * 100) : 0
  return [
    task.moldName || 'Задание',
    `${formatClock(start)} — ${formatClock(end)} (${formatDuration(end - start)})`,
    task.startedAt ? `Наладка: ${formatClock(task.setupStartedAt)} — ${formatClock(task.startedAt)}` : (task.closedAt || task.completedAt) ? 'Закрыто во время наладки' : 'Идёт наладка',
    ...planTooltipLines(task),
    `Факт: ${task.actualQuantity} / План: ${task.planQuantity} шт (${progress}%)`,
    task.personnelName ? `Наладчик: ${task.personnelName}` : null,
  ].filter(Boolean).join('\n')
}
</script>

<style scoped>
.shift-timeline {
  font-family: inherit;
  user-select: none;
}

.row-label {
  width: 5rem;
  flex-shrink: 0;
  text-align: right;
  padding-right: 0.75rem;
  font-size: 0.7rem;
  color: #9ca3af;
  display: flex;
  align-items: center;
  justify-content: flex-end;
}

/* Временная шкала */
.time-axis {
  height: 22px;
  margin-bottom: 4px;
}

.tick {
  position: absolute;
  bottom: 0;
  transform: translateX(-50%);
  font-size: 10px;
  color: #9ca3af;
  white-space: nowrap;
}
.tick::before {
  content: '';
  position: absolute;
  bottom: 100%;
  left: 50%;
  width: 1px;
  height: 4px;
  background: #d1d5db;
  margin-bottom: 1px;
}

/* Полоса статусов */
.status-bar {
  position: relative;
  height: 32px;
  background: #e5e7eb;
  border-radius: 6px;
  overflow: hidden;
}

.seg-block {
  position: absolute;
  top: 0;
  height: 100%;
  transition: filter 0.12s;
  cursor: default;
}
.seg-block:hover { filter: brightness(1.12); }

.seg-future {
  position: absolute;
  top: 0;
  height: 100%;
  background: repeating-linear-gradient(
    45deg,
    #e5e7eb, #e5e7eb 4px,
    #f3f4f6 4px, #f3f4f6 8px
  );
  cursor: default;
}

/* Полоса плана наладки */
.plan-bar {
  background: #fafafa;
  border-radius: 6px;
  border: 1px dashed #e5e7eb;
  position: relative;
}

.plan-block {
  position: absolute;
  box-sizing: border-box;
  border: 1.5px dashed;
  border-radius: 5px;
  padding: 2px 6px;
  overflow: hidden;
  cursor: default;
  display: flex;
  flex-direction: column;
  justify-content: center;
}

.plan-name {
  font-size: 11px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.plan-marks {
  font-size: 10px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: #374151;
}
.mark--late   { color: #dc2626; font-weight: 600; }
.mark--muted  { color: #6b7280; }
.mark--normal { color: #374151; }

/* Полоса заданий */
.task-bar {
  background: #f3f4f6;
  border-radius: 6px;
  border: 1px solid #e5e7eb;
  position: relative;
}

.task-block {
  position: absolute;
  box-sizing: border-box;
  border-radius: 5px;
  padding: 3px 8px;
  overflow: hidden;
  cursor: default;
  display: flex;
  flex-direction: column;
  justify-content: center;
  color: #fff;
  border: 1px solid rgba(0, 0, 0, 0.08);
  transition: filter 0.12s;
}
.task-block:hover { filter: brightness(1.08); }

.task-name {
  font-size: 11px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.task-qty {
  font-size: 10px;
  opacity: 0.82;
  white-space: nowrap;
}

/* Линия текущего времени */
.now-line {
  position: absolute;
  top: -5px;
  bottom: -5px;
  width: 2px;
  background: #ef4444;
  z-index: 10;
  pointer-events: none;
}
.now-line::before {
  content: '';
  position: absolute;
  top: 3px;
  left: -4px;
  width: 10px;
  height: 10px;
  background: #ef4444;
  border-radius: 50%;
}
.now-line--labeled::after {
  content: attr(data-time);
  position: absolute;
  top: -18px;
  left: 50%;
  transform: translateX(-50%);
  background: #ef4444;
  color: #fff;
  font-size: 10px;
  font-weight: 600;
  padding: 1px 5px;
  border-radius: 3px;
  white-space: nowrap;
}
</style>
