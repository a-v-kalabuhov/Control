<template>
  <span 
    class="inline-flex items-center px-3 py-1.5 rounded-full text-sm font-bold"
    :class="statusClasses"
  >
    <span 
      class="w-2.5 h-2.5 mr-2 rounded-full"
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
    validator: (value) => ['Draft', 'Issued', 'InProgress', 'Completed', 'Closed'].includes(value)
  }
})

const statusConfig = {
  Draft: { 
    label: 'Черновик', 
    bg: 'bg-gray-100', 
    text: 'text-gray-700', 
    dot: 'bg-gray-500' 
  },
  Issued: { 
    label: 'Выдано', 
    bg: 'bg-blue-100', 
    text: 'text-blue-700', 
    dot: 'bg-blue-500' 
  },
  InProgress: { 
    label: 'В работе', 
    bg: 'bg-yellow-100', 
    text: 'text-yellow-700', 
    dot: 'bg-yellow-500 animate-pulse' 
  },
  Completed: { 
    label: 'Выполнено', 
    bg: 'bg-green-100', 
    text: 'text-green-700', 
    dot: 'bg-green-500' 
  },
  Closed: { 
    label: 'Закрыто', 
    bg: 'bg-gray-100', 
    text: 'text-gray-700', 
    dot: 'bg-gray-500' 
  }
}

const config = computed(() => statusConfig[props.status] || statusConfig.Draft)
const statusClasses = computed(() => `${config.value.bg} ${config.value.text}`)
const dotClasses = computed(() => config.value.dot)
const label = computed(() => config.value.label)
</script>