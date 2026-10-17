<template>
  <el-collapse v-model="expanded">
    <el-collapse-item v-for="g in groups" :key="g.immId" :name="g.immId" data-test="imm-group">
      <template #title>
        <div class="flex flex-wrap items-center gap-x-4 gap-y-1 leading-5">
          <span class="font-semibold">{{ g.immName }}</span>
          <span
            v-for="s in g.summary"
            :key="s.type"
            data-test="summary-item"
            class="flex items-center gap-1 text-xs text-gray-600"
          >
            <span class="inline-block w-2.5 h-2.5 rounded-full" :style="{ background: meta(s.type).hex }"></span>
            {{ meta(s.type).label }} {{ formatDuration(s.seconds) }}
          </span>
        </div>
      </template>

      <el-table v-if="g.rows.length" :data="g.rows" stripe size="small" style="width: 100%">
        <el-table-column label="Состояние" min-width="200">
          <template #default="{ row }">
            <span class="flex items-center gap-2">
              <span class="inline-block w-3 h-3 rounded" :style="{ background: meta(row.type).hex }"></span>
              {{ meta(row.type).label }}
            </span>
          </template>
        </el-table-column>
        <el-table-column label="Начало" width="150">
          <template #default="{ row }">{{ formatPoint(row.start, periodStart) }}</template>
        </el-table-column>
        <el-table-column label="Конец" width="150">
          <template #default="{ row, $index }">
            <span data-test="row-end">
              {{ g.cutAtNow && $index === g.rows.length - 1 ? 'сейчас' : formatPoint(row.end, periodStart) }}
            </span>
          </template>
        </el-table-column>
        <el-table-column label="Длительность" width="130" align="right">
          <template #default="{ row }">{{ formatDuration(row.durationSeconds) }}</template>
        </el-table-column>
      </el-table>
      <div v-else data-test="no-data" class="text-gray-400 text-sm py-2">Нет данных за период</div>
    </el-collapse-item>
  </el-collapse>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { REPORT_STATUS } from '@/constants/effectiveStatus'
import { buildStatusRows, summarizeStatusRows, isCutAtNow, formatDuration, formatPoint } from '@/utils/statusRows'

// Таблица статусов «Картины рабочего дня»: по группе на ТПА, строки — за период отчёта
// (смена/сутки, без полей ±30 мин диаграммы), сводка в заголовке совпадает с часами сводной таблицы.
const props = defineProps({
  data: { type: Array, default: () => [] },
  periodStart: { type: String, required: true },
  periodEnd: { type: String, required: true }
})

const meta = (type) => REPORT_STATUS[type] ?? REPORT_STATUS.NoData

const groups = computed(() => props.data.map(imm => {
  const rows = buildStatusRows(imm.timeline, props.periodStart, props.periodEnd)
  return {
    immId: imm.immId,
    immName: imm.immName,
    rows,
    summary: summarizeStatusRows(rows),
    cutAtNow: isCutAtNow(rows, props.periodEnd)
  }
}))

// По умолчанию все группы раскрыты; новый отчёт снова раскрывает все.
const expanded = ref([])
watch(() => props.data, (data) => { expanded.value = data.map(i => i.immId) }, { immediate: true })
</script>
