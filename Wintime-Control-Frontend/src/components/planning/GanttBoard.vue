<template>
  <div class="gantt">
    <div class="gantt__head">
      <div class="gantt__name"></div>
      <div class="gantt__axis">
        <span v-for="t in ticks" :key="t.at.getTime()" class="tick" :style="{ left: t.pct + '%' }">{{ t.label }}</span>
      </div>
    </div>
    <div v-for="row in board.rows" :key="row.imm.id" class="gantt__row">
      <div class="gantt__name">
        {{ row.imm.name }}
        <el-tag v-if="!row.imm.isActive" size="small" type="info">в архиве</el-tag>
      </div>
      <div class="gantt__track" :style="{ height: trackHeight(row) + 'px' }">
        <div v-for="t in ticks" :key="t.at.getTime()" class="grid" :style="{ left: t.pct + '%' }"></div>
        <div v-if="nowPct != null" class="now-line" :style="{ left: nowPct + '%' }"></div>
        <div
          v-for="(item, i) in row.tasks"
          :key="item.task.id"
          class="pair-lane"
          :style="{ top: lanes.get(row.imm.id)[i] * LANE_HEIGHT + 'px' }"
        >
          <GanttTaskPair :item="item" :from="from" :to="to" :now="now" @select="$emit('select', $event)" />
        </div>
      </div>
    </div>
    <el-empty v-if="board.rows.length === 0" description="Нет ТПА" />
  </div>
</template>

<script setup>
import { computed } from 'vue'
import GanttTaskPair from './GanttTaskPair.vue'
import { rowLanes } from '@/utils/planningBoard'

const LANE_HEIGHT = 44

const props = defineProps({
  board: { type: Object, required: true },
  from: { type: Date, required: true },
  to: { type: Date, required: true },
  now: { type: Date, required: true },
  ticks: { type: Array, default: () => [] }
})
defineEmits(['select'])

const lanes = computed(() => new Map(props.board.rows.map(r => [r.imm.id, rowLanes(r.tasks, props.now)])))

function trackHeight(row) {
  const used = lanes.value.get(row.imm.id)
  return (used.length ? Math.max(...used) + 1 : 1) * LANE_HEIGHT
}

const nowPct = computed(() => {
  const n = props.now.getTime()
  const f = props.from.getTime()
  const t = props.to.getTime()
  return n >= f && n < t ? (n - f) / (t - f) * 100 : null
})
</script>

<style scoped>
.gantt { min-width: 720px; }
.gantt__head, .gantt__row { display: flex; }
.gantt__row { border-top: 1px solid #ebeef5; }
.gantt__name {
  flex: 0 0 160px;
  padding: 6px 8px;
  font-size: 13px;
  font-weight: 500;
  color: #303133;
}
.gantt__axis { position: relative; flex: 1; height: 24px; }
.tick {
  position: absolute;
  transform: translateX(2px);
  font-size: 11px;
  color: #909399;
  white-space: nowrap;
}
.gantt__track { position: relative; flex: 1; }
.grid { position: absolute; top: 0; bottom: 0; border-left: 1px solid #ebeef5; }
.now-line { position: absolute; top: 0; bottom: 0; border-left: 2px solid #f56c6c; z-index: 2; pointer-events: none; }
.pair-lane { position: absolute; left: 0; right: 0; height: 40px; pointer-events: none; }
.pair-lane :deep(.pair) { pointer-events: auto; }
</style>
