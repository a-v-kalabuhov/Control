<template>
  <div 
    class="card cursor-pointer hover:shadow-lg transition-all duration-200 border-l-4"
    :class="borderColor"
    @click="$emit('click', imm)"
  >
    <!-- Заголовок карточки -->
    <div class="flex items-start justify-between mb-3">
      <div class="flex-1 min-w-0">
        <h3 class="font-semibold text-gray-800 truncate">{{ imm.name }}</h3>
        <p class="text-sm text-gray-500 truncate">{{ imm.manufacturer }} {{ imm.model }}</p>
      </div>
      <ImmStatusBadge :status="imm.status" />
    </div>

    <!-- Текущее задание -->
    <div class="space-y-2 mb-3">
      <div class="flex items-center gap-2 text-sm">
        <el-icon class="text-gray-400"><Document /></el-icon>
        <span class="text-gray-600">Задание:</span>
        <span class="text-gray-800 truncate flex-1">
          {{ imm.currentMoldName || 'Нет задания' }}
        </span>
      </div>

      <div class="flex items-center gap-2 text-sm" v-if="imm.currentMoldName">
        <el-icon class="text-gray-400"><User /></el-icon>
        <span class="text-gray-600">Наладчик:</span>
        <span class="text-gray-800">{{ imm.personnelName || '—' }}</span>
      </div>
    </div>

    <!-- Прогресс выполнения -->
    <div class="mb-3" v-if="imm.currentTaskId">
      <div class="flex justify-between text-sm mb-1">
        <span class="text-gray-500">Прогресс</span>
        <span class="text-gray-800 font-medium">{{ progressPercent }}%</span>
      </div>
      <el-progress 
        :percentage="progressPercent" 
        :stroke-width="8"
        :status="progressStatus"
        :show-text="false"
      />
      <div class="flex justify-between text-xs text-gray-500 mt-1">
        <span>{{ actualQuantity }} шт</span>
        <span>{{ planQuantity }} шт</span>
      </div>
    </div>

    <!-- Статистика -->
    <div class="grid grid-cols-2 gap-2 text-sm border-t pt-3">
      <div>
        <div class="text-gray-500 text-xs">Циклов</div>
        <div class="font-semibold text-gray-800">{{ cycleCount }}</div>
      </div>
      <div>
        <div class="text-gray-500 text-xs">Время цикла</div>
        <div class="font-semibold text-gray-800">{{ cycleTime }} сек</div>
      </div>
      <div>
        <div class="text-gray-500 text-xs">Эффективность</div>
        <div class="font-semibold" :class="efficiencyColor">{{ efficiency }}%</div>
      </div>
      <div>
        <div class="text-gray-500 text-xs">Обновлено</div>
        <div class="font-semibold text-gray-800">{{ lastUpdate }}</div>
      </div>
    </div>

    <!-- Индикатор аварии -->
    <el-alert
      v-if="imm.status === 'Alarm'"
      type="error"
      title="Активная авария"
      :description="imm.alarmMessage || 'Требуется внимание наладчика'"
      show-icon
      closable
      class="mt-3"
    />
  </div>
</template>

<script setup>
import { computed } from 'vue'
import ImmStatusBadge from './ImmStatusBadge.vue'

const props = defineProps({
  imm: {
    type: Object,
    required: true
  }
})

defineEmits(['click'])

// Прогресс выполнения задания
const progressPercent = computed(() => {
  if (!props.imm.planQuantity || props.imm.planQuantity === 0) return 0
  return Math.min(100, Math.round((props.imm.actualQuantity / props.imm.planQuantity) * 100))
})

const progressStatus = computed(() => {
  if (progressPercent.value >= 100) return 'success'
  if (progressPercent.value >= 75) return ''
  if (progressPercent.value >= 50) return 'warning'
  return 'exception'
})

const planQuantity = computed(() => props.imm.planQuantity || 0)
const actualQuantity = computed(() => props.imm.actualQuantity || 0)
const cycleCount = computed(() => props.imm.cycleCount || 0)
const cycleTime = computed(() => props.imm.currentCycleTime?.toFixed(1) || '0.0')
const efficiency = computed(() => props.imm.efficiency?.toFixed(1) || '0.0')

const efficiencyColor = computed(() => {
  const eff = parseFloat(efficiency.value)
  if (eff >= 85) return 'text-green-600'
  if (eff >= 70) return 'text-yellow-600'
  return 'text-red-600'
})

const borderColor = computed(() => {
  const colors = {
    Auto: 'border-green-500',
    Manual: 'border-yellow-500',
    Alarm: 'border-red-500',
    Offline: 'border-gray-400'
  }
  return colors[props.imm.status] || 'border-gray-400'
})

const lastUpdate = computed(() => {
  if (!props.imm.lastUpdate) return '—'
  const date = new Date(props.imm.lastUpdate)
  return date.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
})
</script>

<style scoped>
.card {
  @apply bg-white rounded-lg shadow-md p-4;
}

.card:hover {
  @apply shadow-xl transform -translate-y-0.5;
}
</style>