<template>
  <div class="task-progress">
    <div class="flex items-center justify-between mb-1">
      <span class="text-sm text-gray-500">Прогресс</span>
      <span class="text-sm font-medium" :class="progressColor">{{ progressPercent }}%</span>
    </div>
    
    <el-progress 
      :percentage="progressPercent" 
      :stroke-width="10"
      :status="progressStatus"
      :show-text="false"
      class="mb-1"
    />
    
    <div class="flex justify-between text-xs text-gray-500">
      <span>Факт: {{ actualQuantity }} шт</span>
      <span>План: {{ planQuantity }} шт</span>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  planQuantity: {
    type: Number,
    required: true
  },
  actualQuantity: {
    type: Number,
    default: 0
  }
})

const progressPercent = computed(() => {
  if (!props.planQuantity || props.planQuantity === 0) return 0
  return Math.min(100, Math.round((props.actualQuantity / props.planQuantity) * 100))
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
</script>

<style scoped>
.task-progress {
  @apply w-full;
}
</style>