<template>
  <div
    v-if="layout"
    :class="['pair', conflictKind && `pair--conflict-${conflictKind.toLowerCase()}`]"
    :data-conflict="conflictKind || undefined"
    :style="{ left: layout.box.leftPct + '%', width: layout.box.widthPct + '%', '--c': color }"
    :title="title"
    @click="$emit('select', item.task)"
  >
    <div class="row row--plan">
      <div
        v-for="s in layout.plan"
        :key="s.kind"
        :class="['seg', `seg--plan-${s.kind}`]"
        :style="{ left: s.leftPct + '%', width: s.widthPct + '%' }"
        :data-kind="`plan-${s.kind}`"
      ></div>
      <span class="label">
        {{ label }}<span v-for="m in planMarks" :key="m.text" class="mark"> · {{ m.text }}</span>
      </span>
    </div>
    <div class="row row--fact">
      <div
        v-for="s in layout.fact"
        :key="s.kind"
        :class="['seg', `seg--fact-${s.kind}`]"
        :style="{ left: s.leftPct + '%', width: s.widthPct + '%' }"
        :data-kind="`fact-${s.kind}`"
      ></div>
      <span v-if="factMarks.length" class="label">
        <span v-for="m in factMarks" :key="m.text" class="mark">{{ m.text }} </span>
      </span>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { worstKind } from '@/utils/planConflicts'
import { pairLayout, boardMarks, planLabel, tooltipLines, GANTT_STATUS_COLORS } from '@/utils/planningBoard'

const props = defineProps({
  item: { type: Object, required: true },
  from: { type: Date, required: true },
  to: { type: Date, required: true },
  now: { type: Date, required: true }
})
defineEmits(['select'])

const layout = computed(() => pairLayout(props.item, props.from, props.to, props.now))
const color = computed(() => GANTT_STATUS_COLORS[props.item.task.status] ?? '#909399')
const conflictKind = computed(() => worstKind(props.item.conflicts))
const marks = computed(() => boardMarks(props.item))
const planMarks = computed(() => marks.value.filter(m => m.row === 'plan'))
const factMarks = computed(() => marks.value.filter(m => m.row === 'fact'))
const label = computed(() => planLabel(props.item))
const title = computed(() => tooltipLines(props.item).join('\n'))
</script>

<style scoped>
/* Конфликты плана (PZP-07b): красная рамка — конфликт плана, оранжевая — отклонения */
.pair--conflict-plan { outline: 2px solid #f56c6c; outline-offset: 1px; border-radius: 2px; }
.pair--conflict-deviation { outline: 2px solid #e6a23c; outline-offset: 1px; border-radius: 2px; }

.pair {
  position: absolute;
  min-width: 3px;
  height: 40px;
  cursor: pointer;
}
.row {
  position: relative;
  height: 18px;
  margin-bottom: 2px;
  overflow: hidden;
}
.seg {
  position: absolute;
  top: 0;
  bottom: 0;
}
/* План — контур цвета статуса; наладка светлее, буфер штрихом */
.seg--plan-setup { background: color-mix(in srgb, var(--c) 20%, white); border: 1px solid var(--c); }
.seg--plan-release { background: color-mix(in srgb, var(--c) 45%, white); border: 1px solid var(--c); }
.seg--plan-buffer {
  border: 1px dashed var(--c);
  background: repeating-linear-gradient(45deg, transparent 0 4px, color-mix(in srgb, var(--c) 25%, white) 4px 8px);
}
/* Факт — заливка; прогноз штрихом; остаток буфера — цвет буфера; перерасход — красный */
.seg--fact-setup { background: color-mix(in srgb, var(--c) 55%, white); }
.seg--fact-release { background: var(--c); }
.seg--fact-forecast {
  background: repeating-linear-gradient(45deg, var(--c) 0 4px, color-mix(in srgb, var(--c) 30%, white) 4px 8px);
}
.seg--fact-remaining { background: color-mix(in srgb, var(--c) 25%, white); border: 1px dashed var(--c); }
.seg--fact-overrun { background: #f56c6c; }
.label {
  position: absolute;
  left: 4px;
  right: 4px;
  top: 1px;
  font-size: 11px;
  line-height: 16px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: #303133;
  pointer-events: none;
}
.mark { color: #f56c6c; font-weight: 600; }
</style>
