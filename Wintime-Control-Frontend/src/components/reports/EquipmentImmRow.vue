<template>
  <el-card shadow="never" class="mb-3" data-test="imm-row">
    <template #header>
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-2">
          <span class="font-semibold">{{ item.immName }}</span>
          <el-tag v-if="!item.isActive" size="small" type="info">Архив</el-tag>
        </div>
        <span class="text-sm font-medium" :class="efficiencyClass(item.efficiency)">
          Эффективность: {{ formatEfficiency(item.efficiency) }}
        </span>
      </div>
    </template>

    <div class="flex flex-col lg:flex-row gap-4">
      <div class="flex-1 min-w-0">
        <EffectiveStatusDayChart :days="item.days" />
      </div>

      <div class="lg:w-60 text-sm">
        <div v-for="k in REPORT_STATUS_KEYS" :key="k" class="flex items-center justify-between py-0.5">
          <span class="flex items-center gap-2">
            <span class="inline-block w-2.5 h-2.5 rounded-sm" :style="{ background: REPORT_STATUS[k].hex }"></span>
            {{ REPORT_STATUS[k].label }}
          </span>
          <span class="tabular-nums">{{ toHours(item.seconds?.[k]) }} ч</span>
        </div>
        <div class="border-t mt-2 pt-2 flex justify-between">
          <span>Циклы</span>
          <span class="tabular-nums">{{ item.totalCycles }}</span>
        </div>
        <div class="flex justify-between">
          <span>Ср. цикл</span>
          <span class="tabular-nums">{{ item.avgCycleSeconds > 0 ? `${item.avgCycleSeconds.toFixed(1)} с` : '—' }}</span>
        </div>
      </div>
    </div>
  </el-card>
</template>

<script setup>
import EffectiveStatusDayChart from './EffectiveStatusDayChart.vue'
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'
import { toHours, formatEfficiency, efficiencyClass } from '@/utils/equipmentReport'

defineProps({
  // элемент immData отчёта «Производительность оборудования»
  item: { type: Object, required: true }
})
</script>
