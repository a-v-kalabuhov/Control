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
            :style="{ width: segWidth(seg.ChangedAt, seg.EndedAt), background: statusColor(seg.Status) }"
            :title="segTitle(seg)"
          ></div>
          <!-- Ещё не прошедшее время смены -->
          <div
            v-if="futureWidth"
            class="seg-future"
            :style="{ width: futureWidth }"
            :title="`Ещё не наступило\n${formatTime(nowClamped)} — ${formatTime(shiftEnd)}`"
          ></div>
        </div>
        <div class="now-line now-line--labeled" :style="{ left: nowPct }" :data-time="nowLabel"></div>
      </div>
    </div>

    <!-- Строка: Задания -->
    <div class="flex items-center">
      <div class="row-label">Задания</div>
      <div class="flex-1 relative">
        <div class="task-bar">
          <div
            v-for="task in tasks"
            :key="task.id"
            class="task-block"
            :style="taskStyle(task)"
            :title="taskTitle(task)"
          >
            <div class="task-name">{{ task.moldName || 'Задание' }}</div>
            <div class="task-qty">{{ task.actualQuantity }}&thinsp;/&thinsp;{{ task.planQuantity }} шт</div>
          </div>
        </div>
        <div class="now-line" :style="{ left: nowPct }"></div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  statusSegments: { type: Array, default: () => [] }, // ImmStatusSegmentDto[]
  tasks:          { type: Array, default: () => [] }, // TaskDto[]
  shiftStart:     { type: Date, required: true },
  shiftEnd:       { type: Date, required: true },
})

const STATUS_COLORS = {
  Auto:    '#22c55e',
  Manual:  '#eab308',
  Alarm:   '#ef4444',
  Offline: '#9ca3af',
  Idle:    '#9ca3af',
}

const TASK_COLORS = ['#3b82f6', '#7c3aed', '#0891b2', '#059669', '#d97706']

const shiftDur = computed(() => props.shiftEnd - props.shiftStart)

const now = new Date()
const nowClamped = computed(() => {
  if (now < props.shiftStart) return props.shiftStart
  if (now > props.shiftEnd)   return props.shiftEnd
  return now
})
const nowPct     = computed(() => toPct(nowClamped.value))
const nowLabel   = computed(() => formatTime(nowClamped.value))
const futureWidth = computed(() => {
  const lastEnd = props.statusSegments.length
    ? new Date(props.statusSegments[props.statusSegments.length - 1].EndedAt ?? nowClamped.value)
    : props.shiftStart
  const effectiveLast = lastEnd > nowClamped.value ? nowClamped.value : lastEnd
  if (effectiveLast >= props.shiftEnd) return null
  const w = ((props.shiftEnd - effectiveLast) / shiftDur.value * 100).toFixed(3)
  return w + '%'
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
    ticks.push({ label: formatTime(t), pct: toPct(t) })
    t = new Date(t.getTime() + hourMs)
  }
  // Всегда добавляем начало смены если оно ровное
  const startTick = { label: formatTime(props.shiftStart), pct: '0%' }
  if (!ticks.find(tk => tk.pct === '0%')) ticks.unshift(startTick)
  return ticks
})

function toPct(date) {
  const ms = (new Date(date) - props.shiftStart)
  return (Math.max(0, Math.min(1, ms / shiftDur.value)) * 100).toFixed(3) + '%'
}

function segWidth(changedAt, endedAt) {
  const start = new Date(changedAt)
  const end   = endedAt ? new Date(endedAt) : nowClamped.value
  const clampedStart = start < props.shiftStart ? props.shiftStart : start
  const clampedEnd   = end   > props.shiftEnd   ? props.shiftEnd   : end
  const w = ((clampedEnd - clampedStart) / shiftDur.value * 100).toFixed(3)
  return Math.max(0, w) + '%'
}

function taskStyle(task) {
  const start = new Date(task.startedAt ?? task.issuedAt)
  const end   = new Date(task.completedAt ?? task.closedAt ?? props.shiftEnd)
  const clampedStart = start < props.shiftStart ? props.shiftStart : start
  const clampedEnd   = end   > props.shiftEnd   ? props.shiftEnd   : end
  const left  = ((clampedStart - props.shiftStart) / shiftDur.value * 100).toFixed(3)
  const width = ((clampedEnd   - clampedStart)     / shiftDur.value * 100).toFixed(3)
  const idx   = props.tasks.indexOf(task) % TASK_COLORS.length
  return {
    left:       left + '%',
    width:      Math.max(0, width) + '%',
    background: TASK_COLORS[idx],
  }
}

function statusColor(status) {
  return STATUS_COLORS[status] ?? STATUS_COLORS.Offline
}

function formatTime(date) {
  const d = new Date(date)
  return d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
}

function durStr(ms) {
  const totalMin = Math.round(ms / 60_000)
  const h = Math.floor(totalMin / 60)
  const m = totalMin % 60
  if (h > 0 && m > 0) return `${h}ч ${m}м`
  if (h > 0) return `${h}ч`
  return `${m}м`
}

const STATUS_LABELS = { Auto: 'Авто', Manual: 'Наладка', Alarm: 'Авария', Offline: 'Оффлайн', Idle: 'Ожидание' }

function segTitle(seg) {
  const start = new Date(seg.ChangedAt)
  const end   = seg.EndedAt ? new Date(seg.EndedAt) : now
  return `${STATUS_LABELS[seg.Status] ?? seg.Status}\n${formatTime(start)} — ${formatTime(end)}\n${durStr(end - start)}`
}

function taskTitle(task) {
  const start = new Date(task.startedAt ?? task.issuedAt)
  const end   = new Date(task.completedAt ?? task.closedAt ?? props.shiftEnd)
  const progress = task.planQuantity ? Math.round(task.actualQuantity / task.planQuantity * 100) : 0
  return [
    task.moldName || 'Задание',
    `${formatTime(start)} — ${formatTime(end)} (${durStr(end - start)})`,
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
  height: 32px;
  background: #e5e7eb;
  border-radius: 6px;
  display: flex;
  overflow: hidden;
}

.seg-block {
  height: 100%;
  transition: filter 0.12s;
  cursor: default;
}
.seg-block:hover { filter: brightness(1.12); }

.seg-future {
  height: 100%;
  background: repeating-linear-gradient(
    45deg,
    #e5e7eb, #e5e7eb 4px,
    #f3f4f6 4px, #f3f4f6 8px
  );
  flex: 1;
  cursor: default;
}

/* Полоса заданий */
.task-bar {
  height: 58px;
  background: #f3f4f6;
  border-radius: 6px;
  border: 1px solid #e5e7eb;
  position: relative;
}

.task-block {
  position: absolute;
  top: 7px;
  bottom: 7px;
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
