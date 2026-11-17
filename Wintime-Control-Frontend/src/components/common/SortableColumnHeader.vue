<template>
  <span
    class="inline-flex items-center gap-1 cursor-pointer select-none whitespace-nowrap hover:text-blue-600"
    @click="$emit('sort', field)"
  >
    {{ label }}
    <span class="text-[10px] text-blue-600 min-w-2.5">{{ icon }}</span>
  </span>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  label: { type: String, required: true },
  field: { type: String, required: true },
  sort: { type: Object, required: true }
})

defineEmits(['sort'])

const icon = computed(() => {
  if (props.sort.field !== props.field) return ''
  return props.sort.dir === 'desc' ? '▼' : '▲'
})
</script>
