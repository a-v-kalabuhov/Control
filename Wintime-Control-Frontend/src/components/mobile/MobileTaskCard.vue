<template>
  <div 
    class="mobile-task-card cursor-pointer active:scale-98 transition-transform"
    :class="statusBorderClass"
    @click="$emit('click', task)"
  >
    <!-- Статус -->
    <div class="flex items-center justify-between mb-3">
      <MobileStatusBadge :status="task.status" />
      <el-icon class="text-gray-400"><Right /></el-icon>
    </div>

    <!-- Оборудование -->
    <div class="mb-3">
      <div class="text-xs text-gray-500 mb-1">Оборудование</div>
      <div class="text-lg font-bold text-gray-800">{{ task.immName }}</div>
    </div>

    <!-- Пресс-форма -->
    <div class="mb-3">
      <div class="text-xs text-gray-500 mb-1">Пресс-форма</div>
      <div class="text-base text-gray-700">{{ task.moldName }}</div>
    </div>

    <!-- План / Факт -->
    <div class="mb-3 p-3 bg-gray-50 rounded-lg">
      <div class="flex justify-between items-center mb-2">
        <span class="text-sm text-gray-500">Прогресс</span>
        <span class="text-lg font-bold" :class="progressColor">{{ progressPercent }}%</span>
      </div>
      <el-progress 
        :percentage="progressPercent" 
        :stroke-width="12"
        :status="progressStatus"
        :show-text="false"
      />
      <div class="flex justify-between text-sm mt-2">
        <span class="text-gray-600">Факт: <strong>{{ actualQuantity }}</strong></span>
        <span class="text-gray-600">План: <strong>{{ planQuantity }}</strong></span>
      </div>
    </div>

    <!-- Наладчик -->
    <div class="flex items-center gap-2 text-sm text-gray-600">
      <el-icon><User /></el-icon>
      <span>{{ task.personnelName || 'Не назначен' }}</span>
    </div>

    <!-- Кнопка действия (если в работе) -->
    <el-button 
      v-if="task.status === 'InProgress'"
      type="primary" 
      size="large"
      class="w-full mt-4 h-14 text-lg"
      @click.stop="$emit('complete', task)"
    >
      Завершить
    </el-button>

    <el-button 
      v-else-if="task.status === 'Issued'"
      type="success" 
      size="large"
      class="w-full mt-4 h-14 text-lg"
      @click.stop="$emit('start', task)"
    >
      Начать
    </el-button>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import MobileStatusBadge from './MobileStatusBadge.vue'

const props = defineProps({
  task: {
    type: Object,
    required: true
  }
})

defineEmits(['click', 'start', 'complete'])

const progressPercent = computed(() => {
  if (!props.task.planQuantity || props.task.planQuantity === 0) return 0
  return Math.min(100, Math.round((props.task.actualQuantity / props.task.planQuantity) * 100))
})

const progressStatus = computed(() => {
  if (progressPercent.value >= 100) return 'success'
  if (progressPercent.value >= 75) return ''
  if (progressPercent.value >= 50) return 'warning'
  return 'exception'
})

const progressColor = computed(() => {
  if (progressPercent.value >= 100) return 'text-green-600'
  if (progressPercent.value >= 75) return 'text-blue-600'
  if (progressPercent.value >= 50) return 'text-yellow-600'
  return 'text-red-600'
})

const statusBorderClass = computed(() => {
  const classes = {
    Draft: 'border-l-4 border-gray-400',
    Issued: 'border-l-4 border-blue-500',
    InProgress: 'border-l-4 border-yellow-500',
    Completed: 'border-l-4 border-green-500',
    Closed: 'border-l-4 border-gray-400'
  }
  return classes[props.task.status] || 'border-l-4 border-gray-400'
})

const planQuantity = computed(() => props.task.planQuantity || 0)
const actualQuantity = computed(() => props.task.actualQuantity || 0)
</script>

<style scoped>
.mobile-task-card {
  @apply bg-white rounded-xl shadow-md p-4 min-h-[200px];
}

.mobile-task-card:active {
  @apply scale-98;
}
</style>