<template>
  <span 
    class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium"
    :class="statusClasses"
  >
    <span 
      class="w-2 h-2 mr-1.5 rounded-full animate-pulse"
      :class="dotClasses"
    ></span>
    {{ label }}
  </span>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  status: {
    type: String,
    required: true,
    validator: (value) => ['Auto', 'Manual', 'Alarm', 'Offline'].includes(value)
  }
})

const statusConfig = {
  Auto: { 
    label: 'Авто', 
    bg: 'bg-green-100', 
    text: 'text-green-800', 
    dot: 'bg-green-500' 
  },
  Manual: { 
    label: 'Наладка', 
    bg: 'bg-yellow-100', 
    text: 'text-yellow-800', 
    dot: 'bg-yellow-500' 
  },
  Alarm: { 
    label: 'Авария', 
    bg: 'bg-red-100', 
    text: 'text-red-800', 
    dot: 'bg-red-500' 
  },
  Offline: { 
    label: 'Оффлайн', 
    bg: 'bg-gray-100', 
    text: 'text-gray-800', 
    dot: 'bg-gray-500' 
  }
}

const config = computed(() => statusConfig[props.status] || statusConfig.Offline)

const statusClasses = computed(() => `${config.value.bg} ${config.value.text}`)
const dotClasses = computed(() => config.value.dot)
const label = computed(() => config.value.label)
</script>