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
import { EFFECTIVE_STATUS, EFFECTIVE_STATUS_KEYS, getEffectiveStatusMeta } from '@/constants/effectiveStatus'

const props = defineProps({
  status: {
    type: String,
    required: true,
    validator: (v) => EFFECTIVE_STATUS_KEYS.includes(v),
  },
})

const config = computed(() => getEffectiveStatusMeta(props.status))
const statusClasses = computed(() => `${config.value.bg} ${config.value.text}`)
const dotClasses = computed(() => config.value.dot)
const label = computed(() => config.value.label)
</script>